using System.Text.RegularExpressions;

namespace CodexConfig.Core;

internal static class DesktopConnectionPatch
{
    internal const string Marker = "__codexDockConnection";
    private const string Id = @"[A-Za-z_$][\w$]*";
    private const string NativeRequest = @"this\.requestClient\.sendRequest\(\k<method>,\k<parameters>,\k<options>";
    private static readonly Regex Method = new(
        $@"async sendRequest\((?<method>{Id}),(?<parameters>{Id}),(?<options>{Id})(?:,(?<signal>{Id}))?\)\{{this\.assertActive\(\);let " +
        $@"(?(signal)(?<pending>{Id})=(?<call>null==\k<signal>\?{NativeRequest}\):{NativeRequest},\k<signal>\)),{Id}=await \k<pending>|" +
        $@"{Id}=await (?<call>{NativeRequest}\)))\.catch\(",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(3));
    internal static int Count(string source) => Method.Matches(source).Count;
    internal static string Rewrite(string source)
    {
        if (source.Contains(Marker, StringComparison.Ordinal)) return source;
        var matches = Method.Matches(source);
        if (matches.Count != 1) throw new InvalidDataException("此 Codex 聊天请求入口尚未匹配，未启用接续。");
        var match = matches[0]; var call = match.Groups["call"];
        var method = match.Groups["method"].Value;
        var signal = match.Groups["signal"];
        const string send = "this.requestClient.sendRequest";
        const string arguments = "__dockMethod,__dockParameters,__dockOptions";
        var nativeCall = $"{send}({arguments})";
        // Auxiliary requests use the native three-argument form.
        var forward = signal.Success
            ? $"(null=={signal.Value}||__dockMethod!=={method}?{nativeCall}:{send}({arguments},{signal.Value}))"
            : nativeCall;
        var cancellation = signal.Success ? "," + signal.Value : "";
        var replacement = "(globalThis." + Marker + "?.enabled===true?globalThis." + Marker + ".dispatch(this," +
            method + "," + match.Groups["parameters"].Value + "," + match.Groups["options"].Value +
            ",(" + arguments + ")=>" + forward + cancellation + "):" + call.Value + ")";
        return source[..call.Index] + replacement + source[(call.Index + call.Length)..];
    }
    internal static string Controller(string binding, bool resumeOnly = false)
    {
        using var input = typeof(DesktopConnectionPatch).Assembly.GetManifestResourceStream("CodexConfig.Core.desktop-connections.js")!;
        using var reader = new StreamReader(input);
        return "(" + reader.ReadToEnd() + ")(" + System.Text.Json.JsonSerializer.Serialize(binding) + "," + (resumeOnly ? "true" : "false") + ")";
    }
}
