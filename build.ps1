$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\PrivacyScreen\PrivacyScreen.csproj'
$output = Join-Path $PSScriptRoot 'dist'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Privacy Screen publish failed.' }
Write-Host "Executable: $(Join-Path $output 'PrivacyScreen.exe')"
