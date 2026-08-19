[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $projectRoot 'modinfo.json') -Raw | ConvertFrom-Json
$stageRoot = Join-Path $projectRoot 'build\package'
$stageMod = Join-Path $stageRoot $manifest.modid
$artifactRoot = Join-Path $projectRoot 'artifacts'
$archive = Join-Path $artifactRoot ("{0}_{1}.zip" -f $manifest.modid, $manifest.version)

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration

if (Test-Path $stageRoot) {
    Remove-Item $stageRoot -Recurse -Force
}
New-Item $stageMod -ItemType Directory -Force | Out-Null
New-Item $artifactRoot -ItemType Directory -Force | Out-Null

Copy-Item (Join-Path $projectRoot 'modinfo.json') $stageMod
Copy-Item (Join-Path $projectRoot "bin\$Configuration\net10.0\TanningRack.dll") $stageMod

$assets = Join-Path $projectRoot 'assets'
if (Test-Path $assets) {
    Get-ChildItem $assets -Recurse -File |
        Where-Object { $_.Name -ne '.gitkeep' } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($assets.Length).TrimStart([char[]]@('\', '/'))
            $destination = Join-Path (Join-Path $stageMod 'assets') $relativePath
            New-Item (Split-Path -Parent $destination) -ItemType Directory -Force | Out-Null
            Copy-Item $_.FullName $destination
        }
}

if (Test-Path $archive) {
    Remove-Item $archive -Force
}
Compress-Archive -Path (Join-Path $stageMod '*') -DestinationPath $archive
Write-Output "Created $archive"
