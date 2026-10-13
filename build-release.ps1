param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot "GlobalDeformationLab\GlobalDeformationLab.csproj"
$output = Join-Path $projectRoot "artifacts\$Runtime"

dotnet test (Join-Path $projectRoot "GlobalDeformationLab.sln") -c Release
dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $output

Write-Host "Release ready: $output"
