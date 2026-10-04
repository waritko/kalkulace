param([switch]$SkipInstall, [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot '.output'
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE" }
    } finally { Pop-Location }
}

function Escape-TeamCity([string]$Value) {
    return $Value.Replace('|', '||').Replace("'", "|'").Replace("`r", '|r').Replace("`n", '|n').Replace('[', '|[').Replace(']', '|]')
}

function Invoke-Smoke([string]$Name, [scriptblock]$Run) {
    Write-Host "##teamcity[testStarted name='$(Escape-TeamCity $Name)']"
    try {
        & $Run | Out-Host
        Write-Host "##teamcity[testFinished name='$(Escape-TeamCity $Name)']"
        return $true
    } catch {
        $message = Escape-TeamCity $_.Exception.Message
        $details = Escape-TeamCity ($_ | Out-String)
        Write-Host "##teamcity[testFailed name='$(Escape-TeamCity $Name)' message='$message' details='$details']"
        Write-Host "##teamcity[testFinished name='$(Escape-TeamCity $Name)']"
        return $false
    }
}

$frontend = Join-Path $root 'frontend'
$backend = Join-Path $root 'backend/Kalkulace.Api.csproj'
$backendDirectory = Join-Path $root 'backend'
$backendDll = Join-Path $backendDirectory 'bin/Release/net9.0/Kalkulace.Api.dll'
if (-not $SkipInstall) { Invoke-Checked 'npm.cmd' @('ci') $frontend }
if (-not $SkipBuild) {
    Invoke-Checked 'npm.cmd' @('run', 'build') $frontend
    Invoke-Checked 'dotnet' @('build', $backend, '--configuration', 'Release') $root
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$database = Join-Path $output ("ci-{0}.db" -f [Guid]::NewGuid().ToString('N'))
$stdout = Join-Path $output 'api.stdout.log'
$stderr = Join-Path $output 'api.stderr.log'
$previousProvider = [Environment]::GetEnvironmentVariable('Database__Provider', 'Process')
$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Sqlite', 'Process')
$env:Database__Provider = 'Sqlite'
$env:ConnectionStrings__Sqlite = "Data Source=$database"
$process = $null
try {
    $process = Start-Process dotnet -ArgumentList "`"$backendDll`" --urls $baseUrl" -WorkingDirectory $backendDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { break }
        try {
            $null = Invoke-RestMethod "$baseUrl/api/catalog" -TimeoutSec 2
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "API did not start at $baseUrl. See $stdout and $stderr." }

    $failed = 0
    if (-not (Invoke-Smoke 'API calculation and invoice' { & (Join-Path $PSScriptRoot 'api-smoke.ps1') -BaseUrl $baseUrl })) { $failed++ }
    if (-not (Invoke-Smoke 'Daily entries' { & (Join-Path $PSScriptRoot 'daily-smoke.ps1') -BaseUrl $baseUrl })) { $failed++ }
    if (-not (Invoke-Smoke 'Finish calculation' {
        & node (Join-Path $PSScriptRoot 'finish-smoke.cjs') $baseUrl
        if ($LASTEXITCODE -ne 0) { throw "finish-smoke.cjs exited with code $LASTEXITCODE" }
    })) { $failed++ }
    if ($failed -gt 0) { throw "$failed smoke test(s) failed." }
} finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    [Environment]::SetEnvironmentVariable('Database__Provider', $previousProvider, 'Process')
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Sqlite', $previousConnection, 'Process')
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        foreach ($suffix in @('', '-shm', '-wal')) {
            Remove-Item -LiteralPath "$database$suffix" -Force -ErrorAction SilentlyContinue
        }
        if (-not (Test-Path "$database-shm") -and -not (Test-Path "$database-wal") -and -not (Test-Path $database)) { break }
        Start-Sleep -Milliseconds 200
    }
}
