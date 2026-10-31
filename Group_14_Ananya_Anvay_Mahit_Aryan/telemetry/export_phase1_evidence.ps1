$ErrorActionPreference = 'Stop'
$Group = Split-Path -Parent $PSScriptRoot
$Python = Join-Path $Group 'Phase1\Python'
$Dest = Join-Path $PSScriptRoot 'phase1_evidence'
New-Item -ItemType Directory -Force -Path $Dest | Out-Null

$Items = @(
    @{ From='checkpoints\sac_v15f\seed_71\eval_history.csv'; To='eval_history.csv' },
    @{ From='checkpoints\sac_v15f\seed_71\stage_history.csv'; To='stage_history.csv' },
    @{ From='data\phase2\manifest.csv'; To='terrain_manifest.csv' },
    @{ From='results\sac_v15f_seed_71\benchmark_summary.csv'; To='benchmark_summary.csv' }
)

Write-Host "Reading genuine output files from: $Python" -ForegroundColor Cyan
foreach ($item in $Items) {
    $src = Join-Path $Python $item.From
    if (Test-Path -LiteralPath $src -PathType Leaf) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $Dest $item.To) -Force
        Write-Host "COPIED REAL FILE: $($item.To)" -ForegroundColor Green
    } else {
        Write-Warning "NOT AVAILABLE: $($item.From). Not fabricating data."
    }
}
Write-Host "Finished. Inspect results under $Dest" -ForegroundColor Cyan
