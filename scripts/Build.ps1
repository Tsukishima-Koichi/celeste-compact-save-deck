param([string]$CelestePath = 'E:\Public\SteamLibrary\steamapps\common\Celeste')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $projectRoot 'CompactSaveDeck.csproj') -c Release "-p:CelestePath=$CelestePath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$package = Join-Path $projectRoot 'dist\CompactSaveDeck'
New-Item -ItemType Directory -Force -Path "$package\bin","$package\Dialog" | Out-Null
Copy-Item -LiteralPath "$projectRoot\bin\Release\net8.0\CompactSaveDeck.dll" -Destination "$package\bin"
Copy-Item -LiteralPath "$projectRoot\bin\Release\net8.0\CompactSaveDeck.pdb" -Destination "$package\bin"
Copy-Item -LiteralPath "$projectRoot\everest.yaml" -Destination $package
Get-ChildItem -LiteralPath "$projectRoot\Dialog" -Filter '*.txt' | Copy-Item -Destination "$package\Dialog"
foreach ($document in @('README.md', 'README.zh-CN.md', 'LICENSE', 'NOTICE')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $package
}
New-Item -ItemType Directory -Force -Path "$package\docs" | Out-Null
Copy-Item -LiteralPath "$projectRoot\docs\更新记录.md" -Destination "$package\docs"
Compress-Archive -Path "$package\*" -DestinationPath "$projectRoot\dist\CompactSaveDeck.zip" -Force
Write-Output "Package: $projectRoot\dist\CompactSaveDeck.zip"
