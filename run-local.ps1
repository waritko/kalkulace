$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$frontend = Join-Path $root 'frontend'
$backend = Join-Path $root 'backend/Kalkulace.Api.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 9 or newer is required.'
}
if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw 'Node.js 20 or newer (with npm) is required.'
}

Push-Location $frontend
try {
    if (-not (Test-Path 'node_modules')) {
        npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }

    npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
}
finally {
    Pop-Location
}

Write-Host 'Opening app at http://localhost:5080 (press Ctrl+C to stop).'
dotnet run --project $backend --no-launch-profile --urls http://localhost:5080
if ($LASTEXITCODE -ne 0) { throw 'Backend exited with an error.' }
