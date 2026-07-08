param(
    [string]$Python = "python"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    Write-Host "==> Checking Python command"
    $versionOutput = (& $Python --version 2>&1 | Out-String).Trim()
    Write-Host $versionOutput
    if ($versionOutput -notmatch '^Python\s+\d+\.\d+\.\d+') {
        throw "Python command did not report a real CPython version. Check PATH or pass -Python with a full python.exe path."
    }

    $executable = (& $Python -c "import sys; print(sys.executable)" 2>&1 | Out-String).Trim()
    if (-not $executable -or $executable -match 'WindowsApps') {
        throw "Python command appears to be a Microsoft Store stub or invalid runtime: $executable"
    }
    Write-Host "Python executable: $executable"

    Write-Host "==> Checking required files"
    foreach ($path in @("main.py", "profile.template.xml", "requirements.txt")) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Missing required file: $path"
        }
    }

    $xlsxInput = "Sat_List.xlsx"
    $csvInput = "Sat_List.csv"
    if (Test-Path -LiteralPath $xlsxInput) {
        Write-Host "Default input file: $xlsxInput"
    }
    elseif (Test-Path -LiteralPath $csvInput) {
        Write-Host "Default input file: $csvInput"
    }
    else {
        throw "Missing required input file: Sat_List.xlsx or Sat_List.csv"
    }

    Write-Host "==> Compiling source files"
    & $Python -m compileall main.py src
    if ($LASTEXITCODE -ne 0) {
        throw "Source compile check failed."
    }

    Write-Host "==> Smoke check complete"
}
finally {
    Pop-Location
}
