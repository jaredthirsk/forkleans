# Fix-Fork.ps1
# Master script to fix the Orleans fork after merging from upstream

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,

    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Starting fork maintenance process..." -ForegroundColor Cyan

# Step 1: Convert namespaces from Orleans to Forkleans
Write-Host "`nStep 1: Converting namespaces..." -ForegroundColor Green
.\Convert-OrleansNamespace.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 2: Fix project references
Write-Host "`nStep 2: Fixing project references..." -ForegroundColor Green
.\Smart-Fix-References.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 3: Fix assembly names
Write-Host "`nStep 3: Fixing assembly names..." -ForegroundColor Green
.\Fix-AssemblyNames.ps1 -RootPath $RootPath -CheckOnly:$DryRun

# Step 4: Fix F# specific namespace issues
Write-Host "`nStep 4: Fixing F# namespaces..." -ForegroundColor Green
.\Fix-FSharp-Namespaces.ps1 -Path $RootPath -DryRun:$DryRun

# Step 5: Fix SDK targets files
Write-Host "`nStep 5: Fixing SDK targets files..." -ForegroundColor Green
.\Fix-SDK-Targets.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 6: Add missing using statements (disabled - using implicit usings instead)
#Write-Host "`nStep 6: Adding missing using statements..." -ForegroundColor Green
#.\Fix-Missing-Usings.ps1 -Path $RootPath -DryRun:$DryRun

# Step 7: Fix any syntax errors that might have been introduced (disabled - avoiding syntax errors)
#Write-Host "`nStep 7: Checking and fixing syntax errors..." -ForegroundColor Green
#.\Fix-Syntax-Errors.ps1 -Path $RootPath -DryRun:$DryRun

# Step 8: Build and report results
if (-not $DryRun) {
    Write-Host "`nStep 8: Building solution to verify fixes..." -ForegroundColor Green
    try {
        $buildOutput = & dotnet build "$RootPath\Orleans.sln" -c Debug --no-restore 2>&1
        $successfulProjects = ($buildOutput | Select-String " -> ").Count
        $totalProjects = & dotnet sln "$RootPath\Orleans.sln" list | Select-String "\.csproj|\.fsproj" | Measure-Object | Select-Object -ExpandProperty Count

        Write-Host "`nBuild Results:" -ForegroundColor Cyan
        Write-Host "Successful projects: $successfulProjects / $totalProjects" -ForegroundColor $(if ($successfulProjects -eq $totalProjects) { "Green" } else { "Yellow" })

        if ($successfulProjects -lt $totalProjects) {
            Write-Host "`nTo see build errors, run:" -ForegroundColor Yellow
            Write-Host "  dotnet build `"$RootPath\Orleans.sln`" -c Debug --no-restore" -ForegroundColor White
        }
    }
    catch {
        Write-Warning "Build failed. Run 'dotnet build' manually to see errors."
    }
}

Write-Host "`nFork maintenance complete!" -ForegroundColor Green

if ($DryRun) {
    Write-Host "`nThis was a dry run. To apply changes, run without -DryRun flag." -ForegroundColor Cyan
}
