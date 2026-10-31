# Script to sequentially push each batch commit to GitHub to stay within the 2 GB per-push limit.
$commits = @(
    "3a3dc76", # Core Unity settings, packages, scenes, and scripts (~150 MB)
    "7bdcb2d", # VRBN shared textures part 1 (0-3) (~1.3 GB)
    "dd754ac", # VRBN shared textures part 2 (4-5) (~0.98 GB)
    "7bd8e7b", # VRBN shared textures part 3 (6-9) (~1.24 GB)
    "b4f8ede", # VRBN shared textures part 4 (a-m) (~1.16 GB)
    "a0acc69", # VRBN shared textures part 5 (n-z) (~1.00 GB)
    "e3ed960", # VRBN USA buildings group 1 (~1.56 GB)
    "c6ea9b5", # VRBN USA buildings group 2 (~1.42 GB)
    "37739c6", # VRBN USA buildings group 3 (~1.36 GB)
    "main"     # Final commit: InputSystem_Actions & Readme
)

$gitExe = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $gitExe)) {
    $gitExe = (Get-Command git).Source
}

Write-Host "Starting sequential push of $($commits.Count) commits to stay under GitHub's 2GB push limit..." -ForegroundColor Cyan

foreach ($commit in $commits) {
    Write-Host "`n--> Pushing commit: $commit to origin main..." -ForegroundColor Yellow
    & $gitExe push origin "${commit}:refs/heads/main"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Push failed at commit $commit. Please check network connection and credentials."
        exit $LASTEXITCODE
    }
    Write-Host "--> Success: $commit pushed!" -ForegroundColor Green
}

Write-Host "`nAll commits successfully pushed to GitHub!" -ForegroundColor Cyan
