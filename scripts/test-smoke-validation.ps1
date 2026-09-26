$ErrorActionPreference = "Stop"
. "$PSScriptRoot/Assert-SmokeResult.ps1"
$complete = "SMOKE initialized`nSMOKE sampled`nSMOKE stopped"
Assert-SmokeResult -Exited $true -ExitCode 0 -LogText $complete
$cases = @(
    @{ Exited = $false; ExitCode = 0; LogText = $complete },
    @{ Exited = $true; ExitCode = 1; LogText = $complete },
    @{ Exited = $true; ExitCode = 0; LogText = "SMOKE initialized" }
)
foreach ($case in $cases) {
    $rejected = $false
    try { Assert-SmokeResult @case } catch { $rejected = $true }
    if (-not $rejected) { throw "Smoke validation accepted a failure case" }
}
Write-Host "4 smoke-result validation checks passed"
