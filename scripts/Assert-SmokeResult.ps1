function Assert-SmokeResult {
    param([bool]$Exited, [int]$ExitCode, [string]$LogText)
    if (-not $Exited) { throw "Sampler smoke test timed out" }
    if ($ExitCode -ne 0) { throw "Sampler smoke test exited with code $ExitCode" }
    foreach ($milestone in @("SMOKE initialized", "SMOKE sampled", "SMOKE stopped")) {
        if (-not $LogText.Contains($milestone)) { throw "Missing milestone: $milestone" }
    }
}
