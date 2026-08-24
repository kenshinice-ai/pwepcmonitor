param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$PackageVersion = "dev"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Pwe.PcMonitor/Pwe.PcMonitor.csproj"
$output = Join-Path $root "artifacts/$Runtime"
$safePackageVersion = $PackageVersion -replace "[^A-Za-z0-9._-]", "-"
$packageStem = "pwe-pc-monitor-$Runtime-$safePackageVersion"
$zip = Join-Path $root "artifacts/$packageStem.zip"
$checksum = Join-Path $root "artifacts/$packageStem.sha256"

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
if (Test-Path -LiteralPath $checksum) {
    Remove-Item -LiteralPath $checksum -Force
}

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

Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $zip)" | Set-Content -LiteralPath $checksum -Encoding ascii -NoNewline

Write-Host "Published PWE PC MONITOR to $output"
Write-Host "Packaged $zip"
Write-Host "SHA-256 $hash"
