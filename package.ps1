# Builds the release zip in Thunderstore layout (manifest.json, icon.png, README.md, CHANGELOG.md and the DLL
# at the zip root). Usage: powershell -ExecutionPolicy Bypass -File package.ps1 [-OutDir <dir>]
param([string]$OutDir = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'

$manifest = Get-Content (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$pluginVersion = (Select-String -Path (Join-Path $PSScriptRoot 'src\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($manifest.version_number -ne $pluginVersion) {
    throw "manifest.json version $($manifest.version_number) does not match Plugin.Version $pluginVersion"
}

dotnet build (Join-Path $PSScriptRoot 'ValheimBetterStakewall.csproj') -c Release -nologo -p:DeployToGame=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$stage = Join-Path $OutDir 'stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage -Confirm:$false }
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($f in 'manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md', 'bin\Release\ValheimBetterStakewall.dll') {
    Copy-Item (Join-Path $PSScriptRoot $f) $stage
}

$zip = Join-Path $OutDir ("{0}-{1}.zip" -f $manifest.name, $manifest.version_number)
if (Test-Path $zip) { Remove-Item -Force $zip -Confirm:$false }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item -Recurse -Force $stage -Confirm:$false
Write-Host "Packaged $zip"
