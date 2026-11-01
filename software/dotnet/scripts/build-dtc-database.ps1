<#
.SYNOPSIS
    Builds and aggregates the comprehensive DTC database for Neomotive ScanTool.
.DESCRIPTION
    Aggregates diagnostic trouble code definitions from:
      - SAE J2012 / J2012DA via foerbsnavi/OBDex (standard generic codes)
      - Wal33D/dtc-database (comprehensive generic + 33 OEM makes including Ford)
      - OBDb (Open On-Board Diagnostics Database)
    Outputs to software/dotnet/Apps/Shared/Neomotive.Obd2/Resources/dtc-descriptions.csv.
#>
[CmdletBinding()]
param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PyScript = Join-Path $ScriptDir "aggregate_dtc_database.py"

Write-Host "Running DTC database aggregation..." -ForegroundColor Cyan
python $PyScript
if ($LASTEXITCODE -ne 0) {
    Write-Error "DTC database aggregation failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "Rebuilding Neomotive.Obd2..." -ForegroundColor Cyan
$Proj = Join-Path $ScriptDir "..\Apps\Shared\Neomotive.Obd2\Neomotive.Obd2.csproj"
dotnet build $Proj --no-incremental
Write-Host "DTC database built and verified successfully!" -ForegroundColor Green
