param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$npm = if ($env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' }
foreach ($command in @('dotnet', $npm)) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found. Install .NET SDK 9 or newer (including .NET 10) and Node.js 20 or newer."
    }
}

$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$staging = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $artifacts 'kalkulace-release.zip'
$release = Join-Path $artifacts 'kalkulace-release'
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    Push-Location (Join-Path $root 'frontend')
    try {
        & $npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
        & $npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }

    dotnet publish (Join-Path $root 'backend/Kalkulace.Api.csproj') --configuration Release --output $staging --self-contained false -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }

    $wwwroot = Join-Path $staging 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $root 'frontend/dist/*') -Destination $wwwroot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'run.bat'), (Join-Path $root 'run.sh') -Destination $staging

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryArchive = "$staging.zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $temporaryArchive)
    $resolvedRelease = [System.IO.Path]::GetFullPath($release)
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRelease.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace release directory outside artifacts: $resolvedRelease"
    }
    if (Test-Path -LiteralPath $resolvedRelease) { Remove-Item -LiteralPath $resolvedRelease -Recurse -Force }
    Move-Item -LiteralPath $staging -Destination $resolvedRelease
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
    Write-Host "Release package: $archive"
    Write-Host "Unpacked release: $release"
} finally {
    # Only remove this invocation's generated staging directory.
    $resolvedStaging = [System.IO.Path]::GetFullPath($staging)
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStaging.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove staging directory outside artifacts: $resolvedStaging"
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
    if (Test-Path -LiteralPath "$staging.zip") { Remove-Item -LiteralPath "$staging.zip" -Force }
}
