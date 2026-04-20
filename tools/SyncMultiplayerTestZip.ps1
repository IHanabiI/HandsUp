param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [Parameter(Mandatory = $true)]
    [string]$ModDir
)

$ErrorActionPreference = 'Stop'

$workspaceDir = Split-Path -Parent $ProjectDir
$releaseDir = Join-Path $workspaceDir 'release'
$packageDir = Join-Path $releaseDir 'HandsUp_multiplayer_test_package'
$zipPath = Join-Path $releaseDir 'HandsUp_multiplayer_test_package.zip'
$modsRootDir = Split-Path -Parent $ModDir
$baseLibDir = Join-Path $modsRootDir 'BaseLib'
$packageModDir = Join-Path $packageDir 'HandsUp'
$packageBaseLibDir = Join-Path $packageDir 'BaseLib'
$installNotePath = Join-Path $releaseDir 'HandsUp_安装说明.md'
$packageInstallNotePath = Join-Path $packageDir 'HandsUp_安装说明.md'

if (-not (Test-Path -LiteralPath $ModDir)) {
    throw "Mod folder not found: $ModDir"
}

if (-not (Test-Path -LiteralPath $baseLibDir)) {
    throw "BaseLib folder not found: $baseLibDir"
}

if (-not (Test-Path -LiteralPath $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageModDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageBaseLibDir -Force | Out-Null
Copy-Item -Path (Join-Path $ModDir '*') -Destination $packageModDir -Recurse -Force
Copy-Item -Path (Join-Path $baseLibDir '*') -Destination $packageBaseLibDir -Recurse -Force

if (Test-Path -LiteralPath $installNotePath) {
    Copy-Item -LiteralPath $installNotePath -Destination $packageInstallNotePath -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -Force
Write-Host "Updated multiplayer test package: $zipPath"
