# Run Camo pipeline from repo root (paths with spaces are OK).
$Root = $PSScriptRoot
$Py = Join-Path $Root "backend\Python"
if (-not (Test-Path $Py)) { Write-Error "Not found: $Py"; exit 1 }
Set-Location -LiteralPath $Py
$env:PYTHONPATH = $Py
$python = if (Get-Command py -ErrorAction SilentlyContinue) { "py" } elseif (Get-Command python -ErrorAction SilentlyContinue) { "python" } else { Write-Error "Neither 'py' nor 'python' on PATH."; exit 1 }
& $python main.py @args
