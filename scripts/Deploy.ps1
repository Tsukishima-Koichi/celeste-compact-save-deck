param([string]$CelestePath = 'E:\Public\SteamLibrary\steamapps\common\Celeste')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runningGame = Get-CimInstance Win32_Process -Filter "Name = 'Celeste.exe' OR Name = 'dotnet.exe'" |
    Where-Object { $_.Name -eq 'Celeste.exe' -or $_.CommandLine -match 'Celeste\.dll' }
if ($runningGame) { throw 'Close Celeste before deploying. The package can still be built using Build.ps1.' }
& "$PSScriptRoot\Build.ps1" -CelestePath $CelestePath
$target = Join-Path $CelestePath 'Mods\CompactSaveDeck'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Get-ChildItem -LiteralPath "$projectRoot\dist\CompactSaveDeck" | Copy-Item -Destination $target -Recurse -Force
Write-Output "Deployed: $target"

