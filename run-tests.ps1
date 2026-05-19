# Run pytest from backend/Python so paths like tests/test_protocol.py work.
$Root = $PSScriptRoot
$Py = Join-Path $Root "backend\Python"
if (-not (Test-Path $Py)) { Write-Error "Not found: $Py"; exit 1 }
Set-Location -LiteralPath $Py
python -m pytest @args
