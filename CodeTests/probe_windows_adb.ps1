param([Parameter(Mandatory=$true)][string]$Adb)
$ErrorActionPreference = 'Stop'
$Adb = (Resolve-Path $Adb).Path
$folder = 'outputs/windows-adb-probe'
New-Item -ItemType Directory -Force $folder | Out-Null
$results = @()
foreach ($kind in @('numeric', 'localhost', 'port')) {
    $reservation = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $reservation.Start(); $port = $reservation.LocalEndpoint.Port; $reservation.Stop()
    $spec = switch ($kind) { numeric { "tcp:127.0.0.1:$port" } localhost { "tcp:localhost:$port" } port { "tcp:$port" } }
    $info = [Diagnostics.ProcessStartInfo]::new($Adb)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.WorkingDirectory = Split-Path $Adb
    foreach ($arg in @('-L', $spec, 'nodaemon', 'server', '--one-device', 'startup-studio-offline-no-device')) { $info.ArgumentList.Add($arg) }
    foreach ($name in @('ADB_SERVER_SOCKET','ANDROID_ADB_SERVER_PORT','ANDROID_SERIAL','ADB_TRACE')) { $info.Environment.Remove($name) | Out-Null }
    $info.Environment['ADB_MDNS_AUTO_CONNECT'] = '0'
    $p = [Diagnostics.Process]::new(); $p.StartInfo = $info
    $ready = $false; $exitCode = $null
    try {
        if (-not $p.Start()) { throw 'Could not start bundled ADB' }
        $p.StandardInput.Close()
        $stdout = $p.StandardOutput.ReadToEndAsync(); $stderr = $p.StandardError.ReadToEndAsync()
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.ElapsedMilliseconds -lt 10000 -and -not $p.HasExited) {
            $client = [Net.Sockets.TcpClient]::new()
            try {
                $client.Connect('127.0.0.1', $port)
                $stream = $client.GetStream(); $stream.ReadTimeout = 1000
                $request = [Text.Encoding]::ASCII.GetBytes('000chost:version'); $stream.Write($request)
                $reply = [byte[]]::new(12); $stream.ReadExactly($reply)
                $ready = [Text.Encoding]::ASCII.GetString($reply) -match '^OKAY0004[0-9a-fA-F]{4}$'
                if ($ready) { break }
            } catch { } finally { $client.Dispose() }
            Start-Sleep -Milliseconds 100
        }
        if ($p.HasExited) { $exitCode = $p.ExitCode } else { $p.Kill(); if (-not $p.WaitForExit(5000)) { throw 'Owned child did not exit' } }
        $stdout.GetAwaiter().GetResult() | Set-Content "$folder/$kind-stdout.txt"
        $errorText = $stderr.GetAwaiter().GetResult(); $errorText | Set-Content "$folder/$kind-stderr.txt"
        $results += @{kind=$kind; protocolReady=$ready; exitCode=$exitCode; stderr=$errorText; realDeviceSelected=$false}
    } finally { $p.Dispose() }
}
$results | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$folder/result.json"
if (-not ($results | Where-Object protocolReady)) { throw 'No ADB listener spelling worked' }
