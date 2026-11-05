# compress-payload.ps1 — kompres payload AllSense.App.exe -> AllSense.App.pkg.gz
# dipakai setup (embed) biar ukuran installer turun drastis.
param(
  [string]$Source = "C:\Users\aflin\Music\All Sense\AllSense.App\dist\AllSense-x64.exe",
  [string]$Out = "$PSScriptRoot\AllSense.App.pkg.gz"
)
$ErrorActionPreference = "Stop"
$inStream = [System.IO.File]::OpenRead($Source)
try {
  $outStream = [System.IO.File]::Create($Out)
  try {
    $gz = New-Object System.IO.Compression.GZipStream($outStream, [System.IO.Compression.CompressionLevel]::Optimal)
    try { $inStream.CopyTo($gz) } finally { $gz.Dispose() }
  } finally { $outStream.Dispose() }
} finally { $inStream.Dispose() }
Write-Output ("compressed: " + $Out)
Write-Output ("  original = " + (Get-Item $Source).Length)
Write-Output ("  package  = " + (Get-Item $Out).Length)
