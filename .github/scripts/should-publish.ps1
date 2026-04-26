param(
    [Parameter(Mandatory = $true)]
    [string]$ChangedFilesFile
)

$rawPaths = if (Test-Path -LiteralPath $ChangedFilesFile) {
    Get-Content -LiteralPath $ChangedFilesFile
}
else {
    @()
}

$normalizedPaths = $rawPaths |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_.Trim() -replace '\\', '/' }

$relevantPaths = $normalizedPaths |
    Where-Object { $_ -notmatch '^(docs/|\.github/)' }

$shouldPublish = $relevantPaths.Count -gt 0

if ($shouldPublish) {
    $reason = 'Relevant changes detected outside docs/ and .github/.'
}
elseif ($normalizedPaths.Count -eq 0) {
    $reason = 'No changed files were detected.'
}
else {
    $reason = 'Only docs/ and .github/ changes were detected.'
}

"should_publish=$($shouldPublish.ToString().ToLowerInvariant())"
"reason=$reason"
