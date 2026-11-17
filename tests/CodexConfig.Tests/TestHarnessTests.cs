using System.Diagnostics;

internal static class TestHarnessTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("test setup failures exit with diagnostics instead of an unhandled Windows crash", async () =>
        {
            var path = Path.Combine(root, "harness-file-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, "This file cannot be used as an output directory.");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--application-unit"); start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new IOException("Unable to start test harness fixture");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
                var diagnostics = await stderr;
                if (process.ExitCode != 1 || !diagnostics.Contains("FAIL test execution") || !diagnostics.Contains("IOException") || diagnostics.Contains("Unhandled exception"))
                    throw new InvalidOperationException("Setup failures must exit normally with code 1 and the original diagnostic.\n" + await stdout + diagnostics);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
        }));
    }
}
