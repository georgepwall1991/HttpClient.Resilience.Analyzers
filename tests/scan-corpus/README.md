# Scan regression corpus

`corpus.json` pins the public repositories scanned by
[`scripts/Invoke-ScanRegression.ps1`](../../scripts/Invoke-ScanRegression.ps1). `baselines/`
holds the generated finding lists; regenerate them with `-UpdateBaselines`, never by hand.

See [Scan regression corpus](../../docs/scan-regression.md) for the run commands, manifest
fields, drift triage, and coverage limits.
