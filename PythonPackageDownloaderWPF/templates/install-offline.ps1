$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot
python -m pip install --no-index --find-links .\packages -r .\requirements.lock.txt
