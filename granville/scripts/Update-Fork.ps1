param(
    [string]$Branch = "main"
)

# Fetch upstream
git fetch upstream

# Create update branch
$updateBranch = "update-$(Get-Date -Format 'yyyyMMdd')"
git checkout -b $updateBranch

# Merge upstream
$mergeResult = git merge upstream/$Branch

if ($LASTEXITCODE -ne 0) {
    Write-Host "Merge conflicts detected. Running namespace converter on conflicted files..."

    # Get conflicted files
    $conflicts = git diff --name-only --diff-filter=U

    foreach ($file in $conflicts) {
        if ($file -match '\.(cs|csproj)$') {
            # Accept upstream version
            git checkout --theirs $file

            # Re-apply namespace changes
            .\Convert-OrleansNamespace.ps1 -RootPath $file -DryRun:$true
        }
    }
}
