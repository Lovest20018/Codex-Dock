function New-InstallerMobileFixture([string]$ApplicationDirectory, [string]$StateRoot) {
    $taskWorkerSource = Join-Path $taskRoot 'artifacts\mobile-worker\CodexDock.Mobile'
    New-Item -ItemType Directory -Path $ApplicationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $taskWorkerSource -Destination (Join-Path $ApplicationDirectory 'mobile') -Recurse -Force
    $taskFixtureHome = Join-Path $ApplicationDirectory 'fixture-home'
    New-Item -ItemType Directory -Path $taskFixtureHome -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskFixtureHome 'config.toml'), '# Isolated installer fixture; no external requests.')
    $taskIdentity = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskFixtureHome.ToUpperInvariant()))).ToLowerInvariant()
    $taskData = Join-Path $StateRoot $taskIdentity
    New-Item -ItemType Directory -Path $taskData -Force | Out-Null
    function Get-FixturePort {
        $taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        try { $taskListener.Start(); return $taskListener.LocalEndpoint.Port } finally { $taskListener.Stop() }
    }
    $taskPort = Get-FixturePort; $taskControlPort = Get-FixturePort
    while ($taskControlPort -eq $taskPort) { $taskControlPort = Get-FixturePort }
    $taskConfig = [ordered]@{
        protocol=4; publicUrl='https://installer-fixture.example.com'; port=$taskPort; controlPort=$taskControlPort
        controlToken=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
        instanceId=[Guid]::NewGuid().ToString('N'); codexHome=$taskFixtureHome; dataDir=$taskData
        auth=@{mode='password'; username='installer-fixture'; salt=('a' * 32); hash=('b' * 64); iterations=600000; sessionHours=12}
    }
    $taskJson = $taskConfig | ConvertTo-Json -Depth 4 -Compress
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
    $taskSettings = Join-Path $taskData 'mobile-settings.dat'
    [IO.File]::WriteAllBytes($taskSettings, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($taskJson),
        [Text.Encoding]::UTF8.GetBytes('CodexDock.mobile.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser))
    $taskStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationDirectory 'mobile\CodexDock.Mobile.exe'))
    $taskStart.UseShellExecute = $false; $taskStart.CreateNoWindow = $true; $taskStart.RedirectStandardInput = $true
    $taskStart.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    try {
        $taskProcess.StandardInput.Write($taskJson); $taskProcess.StandardInput.Close()
        $taskFixture = [pscustomobject]@{ Process=$taskProcess; Config=$taskConfig; SettingsPath=$taskSettings; SettingsHash=(Get-FileHash -LiteralPath $taskSettings).Hash }
        for ($taskAttempt = 0; $taskAttempt -lt 100; $taskAttempt++) {
            if ($taskProcess.HasExited) { throw 'Isolated mobile fixture exited before health check.' }
            try { if ((Invoke-InstallerMobileFixture $taskFixture 'health').instanceId -eq $taskConfig.instanceId) { return $taskFixture } } catch { }
            Start-Sleep -Milliseconds 100
        }
        throw 'Isolated mobile fixture failed to start.'
    } catch {
        if (-not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
        $taskProcess.Dispose(); throw
    }
}

function Invoke-InstallerMobileFixture($Fixture, [string]$Action) {
    $taskHandler = [Net.Http.HttpClientHandler]::new(); $taskHandler.UseProxy = $false; $taskHandler.AllowAutoRedirect = $false
    $taskClient = [Net.Http.HttpClient]::new($taskHandler); $taskClient.Timeout = [TimeSpan]::FromSeconds(2)
    $taskMethod = if ($Action -eq 'health') { [Net.Http.HttpMethod]::Get } else { [Net.Http.HttpMethod]::Post }
    $taskRequest = [Net.Http.HttpRequestMessage]::new($taskMethod, "http://127.0.0.1:$($Fixture.Config.controlPort)/$Action")
    $taskRequest.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Fixture.Config.controlToken)
    if ($Action -ne 'health') { $taskRequest.Content = [Net.Http.StringContent]::new('{}', [Text.Encoding]::UTF8, 'application/json') }
    try {
        $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
        try { $taskResponse.EnsureSuccessStatusCode() | Out-Null; return ($taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json) }
        finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose(); $taskClient.Dispose() }
}

function Stop-InstallerMobileFixture($Fixture) {
    if ($null -eq $Fixture) { return }
    try {
        if (-not $Fixture.Process.HasExited) {
            Invoke-InstallerMobileFixture $Fixture 'stop' | Out-Null
            if (-not $Fixture.Process.WaitForExit(15000)) { throw 'Isolated fixture failed to stop.' }
        }
    } finally {
        # Only the handle created by this fixture may be terminated on failure.
        if (-not $Fixture.Process.HasExited) { $Fixture.Process.Kill(); $Fixture.Process.WaitForExit() }
        $Fixture.Process.Dispose()
    }
}
