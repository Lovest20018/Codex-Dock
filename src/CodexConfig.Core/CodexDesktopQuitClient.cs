using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

// Reuse the pinned mobile runtime's native menu helper. A receipt means only
// that Quit was requested; CodexDesktop still observes every owned process.
internal sealed class CodexDesktopQuitClient
{
    private readonly string executable;
    private readonly IReadOnlyList<string> arguments;
    private readonly TimeSpan timeout;
    internal CodexDesktopQuitClient() : this(Path.Combine(AppContext.BaseDirectory,
        "mobile", "runtime", "client-helpers", "codex-quit-helper.exe")) { }
    internal CodexDesktopQuitClient(string executable, IReadOnlyList<string>? arguments = null, TimeSpan? timeout = null)
    { this.executable = executable; this.arguments = arguments ?? []; this.timeout = timeout ?? TimeSpan.FromSeconds(22); }

    internal async Task RequestAsync(CodexDesktopProcess expected, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!File.Exists(executable)) throw new IOException("缺少 Codex 正常退出组件，请重新安装完整的 Codex Dock 后重试。");
        var folder = Path.Combine(Path.GetTempPath(), "CodexDock", "desktop-quit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var cancelFile = Path.Combine(folder, "cancel");
        using var helper = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        } };
        foreach (var argument in arguments) helper.StartInfo.ArgumentList.Add(argument);
        foreach (var argument in new[] { expected.Id.ToString(CultureInfo.InvariantCulture), "--quit", expected.Executable,
                     expected.Started.ToString(CultureInfo.InvariantCulture), cancelFile }) helper.StartInfo.ArgumentList.Add(argument);
        var stopped = true;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (!helper.Start()) throw new IOException("无法启动 Codex 正常退出组件，请检查 Dock 安装后重试。");
            }
            catch (Win32Exception error) { throw new IOException("无法启动 Codex 正常退出组件，请检查 Dock 安装后重试。", error); }
            stopped = false;
            helper.StandardInput.Close();
            var output = ReadBoundedAsync(helper.StandardOutput);
            var errors = ReadBoundedAsync(helper.StandardError);
            var exited = helper.WaitForExitAsync();
            var interrupted = false;
            try { await exited.WaitAsync(timeout, cancellation).ConfigureAwait(false); }
            catch (Exception error) when (error is TimeoutException or OperationCanceledException)
            {
                interrupted = true;
                // Cancel pending UIA dispatch first. Terminate only our helper
                // if its native call remains blocked, never the selected app.
                File.WriteAllText(cancelFile, "cancel");
                try { await exited.WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    try { helper.Kill(); }
                    catch (InvalidOperationException) when (helper.HasExited) { }
                    await exited.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
            }
            stopped = true;
            cancellation.ThrowIfCancellationRequested();
            var stdout = await output.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            _ = await errors.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            var receipt = ReadReceipt(stdout.Text, expected.Id);
            if (!stdout.Truncated && !interrupted && receipt.State == "pending")
                throw new IOException(string.IsNullOrWhiteSpace(receipt.Reason) ? "Codex 正在等待退出确认，请先在电脑端处理。" : receipt.Reason);
            // A flushed dispatch marker is enough to require observation even
            // after UIA times out or loses its result. Never replay the request.
            if (receipt.Dispatched) return;
            if (!stdout.Truncated && !interrupted && helper.ExitCode == 0 && receipt.State is "submitted" or "exited") return;
            var reason = !stdout.Truncated && !interrupted && receipt.State is "failed" or "pending" ? receipt.Reason : null;
            throw new IOException(string.IsNullOrWhiteSpace(reason)
                ? "尚未确认 Codex 正常退出命令，请在 Codex 菜单中选择退出后重试；本次未打开新实例。" : reason);
        }
        finally
        {
            // If cleanup itself failed, retain cancellation evidence so a
            // lingering helper can never dispatch after this operation ends.
            if (!stopped)
            {
                try { File.WriteAllText(cancelFile, "cancel"); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                try { if (!helper.HasExited) helper.Kill(); stopped = helper.WaitForExit(3000); }
                catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException) { }
            }
            if (stopped)
                try { File.Delete(cancelFile); Directory.Delete(folder); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(TextReader reader)
    {
        const int limit = 16384;
        var text = new StringBuilder(); var buffer = new char[1024]; var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var remaining = limit - text.Length;
            text.Append(buffer, 0, Math.Min(remaining, count));
            if (count > remaining) truncated = true;
        }
        return (text.ToString(), truncated);
    }

    private static (bool Dispatched, string? State, string? Reason) ReadReceipt(string output, int pid)
    {
        var dispatched = false; string? state = null, reason = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            state = reason = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var value = document.RootElement;
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("pid", out var identity) || identity.ValueKind != JsonValueKind.Number ||
                    !identity.TryGetInt32(out var actual) || actual != pid) continue;
                if (value.TryGetProperty("quitPhase", out var phase) && phase.ValueKind == JsonValueKind.String && phase.GetString() == "dispatching") dispatched = true;
                if (value.TryGetProperty("quitState", out var status) && status.ValueKind == JsonValueKind.String &&
                    status.GetString() is "submitted" or "exited" or "failed" or "pending" &&
                    value.TryGetProperty("reason", out var message) && message.ValueKind == JsonValueKind.String)
                { state = status.GetString(); var detail = message.GetString()!; reason = detail[..Math.Min(detail.Length, 1000)]; }
            }
            catch (JsonException) { }
        }
        return (dispatched, state, reason);
    }
}
