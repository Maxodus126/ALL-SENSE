# Build + publish single-file self-contained x64, lalu zip ke AllSense.App\dist
# Pemakaian:  powershell -ExecutionPolicy Bypass -File .\publish-and-zip.ps1 [-Version 1.2.0]
# Signing opsional: set env ALLSENSE_CERT_BASE64 / ALLSENSE_CERT_PASSWORD dulu.
param(
  [string]$Version = "1.2.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "AllSense.App\AllSense.App.csproj"
$dir  = Join-Path $root "AllSense.App\dist\AllSense-v$Version-x64"
$zip  = Join-Path $root "AllSense.App\dist\AllSense-v$Version-x64.zip"
$sign = Join-Path $root ".github\scripts\sign.ps1"

Write-Host "Publish -> $dir"
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:Version=$Version -o $dir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal ($LASTEXITCODE)" }

if (Test-Path $sign) { & $sign -Files (Join-Path $dir "AllSense.App.exe") }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dir "*") -DestinationPath $zip -Force

Write-Host "OK. Folder: $dir"
Write-Host "OK. ZIP  : $zip  ($([math]::Round((Get-Item $zip).Length/1MB,1)) MB)"
Write-Host "Upload zip ini sebagai asset release GitHub, tag v$Version"
