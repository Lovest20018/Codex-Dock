function Wait-InstallerFixture($Process, [scriptblock]$Ready) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 15 -and -not $Process.HasExited) {
        if (& $Ready) { return }
        Start-Sleep -Milliseconds 100
    }
    if ($Process.HasExited) { throw "Installer fixture exited before reporting ready (exit code $($Process.ExitCode))." }
    throw 'Installer fixture did not report ready within 15 seconds.'
}

function Stop-InstallerProcessFixture($Process) {
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            $Process.WaitForExit()
        }
    } finally { $Process.Dispose() }
}

function Start-InstallerDockFixture([string]$ApplicationDirectory, [string]$Output, [switch]$LegacySmokeHost) {
    if (Test-Path -LiteralPath $Output) { throw "Dock verification directory already exists: $Output" }
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationDirectory 'CodexDock.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $fixtureHome = $Output
    if ($LegacySmokeHost) {
        $start.Environment['CODEX_DOCK_WINUI_TEST_FILTER'] = 'installer-host'
        $start.ArgumentList.Add('--smoke-test')
        $marker = Join-Path $Output 'installer-host.ready'
    } else {
        New-Item -ItemType Directory -Path $Output | Out-Null
        [IO.File]::WriteAllText((Join-Path $Output 'config.toml'), "model='fixture-model'`nopenai_base_url='https://fixture.invalid/v1'`n")
        [IO.File]::WriteAllText((Join-Path $Output 'auth.json'), '{"auth_mode":"apikey","OPENAI_API_KEY":"fake-installer-fixture-key"}')
        $configHash = (Get-FileHash -LiteralPath (Join-Path $Output 'config.toml')).Hash
        $authHash = (Get-FileHash -LiteralPath (Join-Path $Output 'auth.json')).Hash
        $start.ArgumentList.Add('--verify-startup')
        $marker = Join-Path $Output 'startup.ready'
    }
    $start.ArgumentList.Add($Output)
    $process = [Diagnostics.Process]::Start($start)
    try {
        Wait-InstallerFixture $process { (Test-Path -LiteralPath $marker) -and (Get-Item -LiteralPath $marker).Length -gt 0 }
        if ($LegacySmokeHost) { $fixtureHome = [IO.File]::ReadAllText($marker) }
        $window = if ($LegacySmokeHost) { [IntPtr]::Zero } else { [IntPtr][long][IO.File]::ReadAllText($marker) }
        $config = Join-Path $fixtureHome 'config.toml'
        $auth = Join-Path $fixtureHome 'auth.json'
        if ($LegacySmokeHost) {
            $configHash = (Get-FileHash -LiteralPath $config).Hash
            $authHash = (Get-FileHash -LiteralPath $auth).Hash
        }
        return [pscustomobject]@{ Process=$process; Window=$window; Config=$config; ConfigHash=$configHash; Auth=$auth; AuthHash=$authHash }
    } catch {
        Stop-InstallerProcessFixture $process
        $failure = Join-Path $Output 'startup-failure.txt'
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
        throw
    }
}

function Test-InstallerDockFixture($Fixture) {
    return (Get-FileHash -LiteralPath $Fixture.Config).Hash -eq $Fixture.ConfigHash -and
        (Get-FileHash -LiteralPath $Fixture.Auth).Hash -eq $Fixture.AuthHash
}

function Invoke-DockStartupVerification([string]$ApplicationDirectory, [string]$Output) {
    if (-not ('DockStartupVerification' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DockStartupVerification
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static bool RequestExit(IntPtr window, uint expectedProcess)
    {
        uint message = RegisterWindowMessage("CodexDock.ExitForSetup.1");
        return message != 0 && GetWindowThreadProcessId(window, out uint process) != 0 &&
            process == expectedProcess && !IsWindowVisible(window) && PostMessage(window, message, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
    }
    $executable = Join-Path $ApplicationDirectory 'CodexDock.exe'
    $assembly = Join-Path $ApplicationDirectory 'CodexDock.dll'
    $exeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
    $fixture = Start-InstallerDockFixture $ApplicationDirectory $Output
    try {
        if (-not [DockStartupVerification]::RequestExit($fixture.Window, $fixture.Process.Id) -or
            -not $fixture.Process.WaitForExit(15000) -or $fixture.Process.ExitCode -ne 0) {
            throw 'The tray-hidden WinUI application did not exit normally on the installer message.'
        }
        if (-not (Test-InstallerDockFixture $fixture)) { throw 'Startup or exit changed the verification configuration or login.' }
        if ($exeHash -ne (Get-FileHash -LiteralPath $executable).Hash.ToLowerInvariant() -or
            $assemblyHash -ne (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant()) { throw 'The payload changed during startup verification.' }
        $report = [ordered]@{ status='PASS'; kind='production-startup'; uiFramework='WinUI 3';
            applicationVersion=[Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString(3);
            executableName='CodexDock.exe'; executableSha256=$exeHash; applicationAssemblySha256=$assemblyHash;
            tests=@('Loaded a WinUI workspace from isolated configuration', 'Registered the tray icon and hid the main window',
                'Exited normally after the native installer message', 'Preserved the fixture configuration and authentication') }
        $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'report.json') -Encoding utf8
        return $report
    } finally { Stop-InstallerProcessFixture $fixture.Process }
}

function Start-InstallerModelFixture([string]$ApplicationDirectory, [string]$Output) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); $port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
    $settings = Join-Path $Output 'codex-config-connection.dat'
    $configuration = [ordered]@{ BaseUrl='https://installer-model.invalid/v1'; ApiKey='fake-installer-model-key'; Kind=1;
        LocalToken=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)); Port=$port; NativeRoute=$true }
    [IO.File]::WriteAllText((Join-Path $Output 'config.toml'), "model='fixture-model'`nopenai_base_url='http://127.0.0.1:$port/backend-api/codex'`n")
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
    [IO.File]::WriteAllBytes($settings, [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes(($configuration | ConvertTo-Json -Compress)),
        [Text.Encoding]::UTF8.GetBytes('CodexConfig.mixed.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser))
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationDirectory 'CodexDock.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add('--model-bridge')
    $start.ArgumentList.Add($Output)
    $process = [Diagnostics.Process]::Start($start)
    try {
        $fixture = [pscustomobject]@{Process=$process; Port=$port; LocalToken=$configuration.LocalToken; Settings=$settings;
            SettingsHash=(Get-FileHash -LiteralPath $settings).Hash}
        Wait-InstallerFixture $process { Test-InstallerModelFixture $fixture }
        return $fixture
    } catch { Stop-InstallerProcessFixture $process; throw }
}

function Test-InstallerModelFixture($Fixture) {
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.UseProxy = $false; $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMilliseconds(750)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "http://127.0.0.1:$($Fixture.Port)/codex-config/health")
    $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Fixture.LocalToken)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try { return $response.IsSuccessStatusCode } finally { $response.Dispose() }
    } catch { return $false }
    finally { $request.Dispose(); $client.Dispose() }
}
