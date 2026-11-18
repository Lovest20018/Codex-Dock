function Wait-InstallerFixture($Process, [scriptblock]$Ready) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 15 -and -not $Process.HasExited) {
        if (& $Ready) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Installer fixture failed to start.'
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

function Start-InstallerDockFixture([string]$ApplicationDirectory, [string]$Output) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationDirectory 'CodexDock.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Environment['CODEX_DOCK_WINUI_TEST_FILTER'] = 'installer-host'
    $start.ArgumentList.Add('--smoke-test')
    $start.ArgumentList.Add($Output)
    $process = [Diagnostics.Process]::Start($start)
    try {
        $marker = Join-Path $Output 'installer-host.ready'
        Wait-InstallerFixture $process { Test-Path -LiteralPath $marker }
        $fixtureHome = [IO.File]::ReadAllText($marker)
        $config = Join-Path $fixtureHome 'config.toml'
        $auth = Join-Path $fixtureHome 'auth.json'
        return [pscustomobject]@{ Process=$process; Config=$config; ConfigHash=(Get-FileHash -LiteralPath $config).Hash;
            Auth=$auth; AuthHash=(Get-FileHash -LiteralPath $auth).Hash }
    } catch { Stop-InstallerProcessFixture $process; throw }
}

function Test-InstallerDockFixture($Fixture) {
    return (Get-FileHash -LiteralPath $Fixture.Config).Hash -eq $Fixture.ConfigHash -and
        (Get-FileHash -LiteralPath $Fixture.Auth).Hash -eq $Fixture.AuthHash
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
