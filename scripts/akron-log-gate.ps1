param(
    [string] $LogPath = ".\Saves\modlog.txt"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Host "FAIL: log not found '$LogPath'"
    exit 1
}

$log = Get-Content -LiteralPath $LogPath -Raw

$failPatterns = @(
    "ENCOUNTERED A CRITICAL ERROR",
    "Created critical error handler",
    "Crash Exception:",
    "System.NullReferenceException",
    "AkronModule.GameplayRendererOnRender",
    "AkronModule.LevelOnRender",
    "Akron overlay render failed",
    "Graphics state leak after Akron overlay render",
    "Encountered an additional critical error",
    "THIS IS NOT THE MAIN CRASH"
)

$failed = $false

foreach ($pattern in $failPatterns) {
    if ($log.Contains($pattern)) {
        Write-Host "FAIL: found '$pattern'"
        $failed = $true
    }
}

if ($failed) {
    exit 1
}

Write-Host "PASS: no Akron crash signatures found"
exit 0
