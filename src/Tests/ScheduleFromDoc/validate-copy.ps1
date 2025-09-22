Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "[validate-copy] Starting clean rebuild and data check..."

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir
try {
    if (Test-Path ./bin) {
        Write-Host "[validate-copy] Removing bin..."
        Remove-Item -Recurse -Force ./bin
    }

    Write-Host "[validate-copy] Building project..."
    dotnet build -c Debug | Out-Host

    $outDir = Join-Path $scriptDir 'bin/Debug/net9.0-windows'
    $dataDir = Join-Path $outDir 'data'

    if (-not (Test-Path $dataDir)) {
        throw "Output data directory not found: $dataDir"
    }

    $subdirs = Get-ChildItem $dataDir -Directory -ErrorAction Stop | Select-Object -ExpandProperty Name
    $unexpected = @()
    foreach ($d in $subdirs) {
        if ($d -ne '2024_sem2') { $unexpected += $d }
    }

    if ($unexpected.Count -gt 0) {
        Write-Error "Unexpected data subdirectories present: $($unexpected -join ', ')"
        exit 1
    }

    if (-not (Test-Path (Join-Path $dataDir '2024_sem2'))) {
        Write-Error "Expected directory '2024_sem2' not found under data";
        exit 1
    }

    Write-Host "[validate-copy] Success: only '2024_sem2' is present under data." -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}


