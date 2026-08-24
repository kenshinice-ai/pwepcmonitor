param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Pwe.PcMonitor/Pwe.PcMonitor.csproj"
$output = Join-Path $root "artifacts/$Runtime"

dotnet restore $project --runtime $Runtime
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $output `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "Published PWE PC MONITOR to $output"
