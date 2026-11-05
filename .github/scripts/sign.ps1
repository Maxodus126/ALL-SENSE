# sign.ps1 — tandatangani exe dengan signtool (Windows SDK).
# Bila ALLSENSE_CERT_BASE64 di-set (base64 dari .pfx) → tandatangani.
# Kosong → lewati (skip) dengan peringatan.
param(
  [Parameter(Mandatory = $true)][string[]]$Files
)
$ErrorActionPreference = "Stop"
$CertBase64 = $env:ALLSENSE_CERT_BASE64
$CertPassword = $env:ALLSENSE_CERT_PASSWORD

if ([string]::IsNullOrWhiteSpace($CertBase64)) {
  Write-Host "sign: SKIP (ALLSENSE_CERT_BASE64 kosong — build tanpa signature)"
  exit 0
}

# Cari signtool dari Windows SDK.
$st = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
      Sort-Object FullName -Descending | Select-Object -First 1
if (-not $st) { throw "sign: signtool tidak ditemukan (pasang Windows SDK)" }

$pfx = Join-Path $env:TEMP "allsense-cert.pfx"
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($CertBase64))
try {
  foreach ($f in $Files) {
    if (-not (Test-Path $f)) { Write-Warning "sign: file tak ada, lewati: $f"; continue }
    Write-Host "sign: $f"
    & $st.FullName sign /f $pfx /p $CertPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $f
    if ($LASTEXITCODE -ne 0) { throw "sign gagal: $f (exit $LASTEXITCODE)" }
  }
}
finally {
  Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
