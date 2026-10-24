# Prints a summary of a Unity test run: the totals, then each failed test with its message and stack trace.
# Usage: powershell -File tools/test-summary.ps1 [path-to-results.xml] [-Slowest N]
param(
    [string]$Path = "$PSScriptRoot/../test-results.xml",
    [int]$Slowest = 0
)

if (-not (Test-Path $Path)) { Write-Error "No results file at $Path"; exit 1 }
$xml = [xml](Get-Content $Path -Raw)
$run = $xml.'test-run'
$color = if ($run.failed -ne '0') { 'Red' } else { 'Green' }
Write-Host ("{0}: {1} total, {2} passed, {3} failed, {4} skipped ({5:N0}s)" -f `
    $run.result, $run.total, $run.passed, $run.failed, $run.skipped, [double]$run.duration) -ForegroundColor $color

foreach ($test in $xml.SelectNodes("//test-case[@result='Failed']")) {
    Write-Host "`nFAILED $($test.fullname)" -ForegroundColor Red
    Write-Host $test.failure.message.InnerText.Trim()
    $stack = $test.failure.'stack-trace'
    if ($stack) { Write-Host $stack.InnerText.Trim() -ForegroundColor DarkGray }
}

$skipped = $xml.SelectNodes("//test-case[@result='Skipped']")
if ($skipped.Count -gt 0) {
    Write-Host "`nSkipped:" -ForegroundColor Yellow
    foreach ($test in $skipped) { Write-Host "  $($test.name) - $($test.reason.message.InnerText.Trim())" }
}

if ($Slowest -gt 0) {
    Write-Host "`nSlowest:"
    $xml.SelectNodes("//test-case") | Sort-Object { [double]$_.duration } -Descending | Select-Object -First $Slowest |
        ForEach-Object { Write-Host ("  {0,7:N1}s  {1}" -f [double]$_.duration, $_.name) }
}
