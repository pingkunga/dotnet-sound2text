param(
    [string]$Name = "ggml-large-v3-q5_0.bin",
    [string]$Dir = (Join-Path $PSScriptRoot "..\models")
)
New-Item -ItemType Directory -Force $Dir | Out-Null
$out = Join-Path $Dir $Name
if (Test-Path $out) { Write-Host "exists: $out"; return }
Invoke-WebRequest "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$Name" -OutFile $out
Write-Host "saved: $out"
