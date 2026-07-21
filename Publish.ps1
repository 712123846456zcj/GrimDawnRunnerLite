param([ValidateSet("win-x64", "win-x86")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Wpf_gdRunnerLite.csproj"
$output = Join-Path $PSScriptRoot "bin\publish\$Runtime"

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

Write-Host ""
Write-Host "发布完成：$output" -ForegroundColor Green
Write-Host "将 GrimDawnLauncher.exe 放到 Grim Dawn 游戏根目录即可。" -ForegroundColor Cyan
