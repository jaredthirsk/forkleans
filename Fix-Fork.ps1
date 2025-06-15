# Fix-Fork.ps1
# Master script to fix the Orleans fork after merging from upstream
# Updated for complete namespace and type name conversion

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,

    [Parameter()]
    [switch]$DryRun = $false,

    [Parameter()]
    [switch]$BackupFirst = $true
)

$ErrorActionPreference = "Stop"

Write-Host "Starting fork maintenance process..." -ForegroundColor Cyan

# Step 1: Convert namespaces from Orleans to Forkleans
Write-Host "`nStep 1: Converting namespaces and type names..." -ForegroundColor Green

# Check for improved converter first, then fixed version, then fall back to original
if (Test-Path ".\Convert-OrleansNamespace-Improved.ps1") {
    Write-Host "  Using improved conversion script..." -ForegroundColor Gray
    .\Convert-OrleansNamespace-Improved.ps1 -RootPath $RootPath -DryRun:$DryRun -BackupFirst:$BackupFirst
} elseif (Test-Path ".\Convert-OrleansNamespace-Fixed.ps1") {
    Write-Host "  Using fixed conversion script..." -ForegroundColor Gray
    .\Convert-OrleansNamespace-Fixed.ps1 -RootPath $RootPath -DryRun:$DryRun -BackupFirst:$BackupFirst
} else {
    .\Convert-OrleansNamespace.ps1 -RootPath $RootPath -DryRun:$DryRun -BackupFirst:$BackupFirst
}

# Step 1.5: Fix ALL project reference issues comprehensively
Write-Host "`nStep 1.5: Fixing all project reference issues..." -ForegroundColor Green
.\Fix-AllProjectReferences.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 1.6: Fix Directory.Build files
Write-Host "`nStep 1.6: Fixing Directory.Build files..." -ForegroundColor Green
.\Fix-DirectoryBuildFiles.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 1.7: Fix SDK build file names
Write-Host "`nStep 1.7: Fixing SDK build file names..." -ForegroundColor Green
.\Fix-SdkBuildFiles.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 1.8: Fix build props/targets file names
Write-Host "`nStep 1.8: Fixing build props/targets file names..." -ForegroundColor Green
.\Fix-BuildPropFileNames.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 1.9: Ensure analyzer references are correct in Directory.Build.targets
Write-Host "`nStep 1.9: Verifying analyzer references..." -ForegroundColor Green

$targetFile = Join-Path $RootPath "Directory.Build.targets"
if (Test-Path $targetFile) {
    $content = Get-Content $targetFile -Raw
    
    # Check if analyzer references have the required attributes
    if ($content -match "Orleans\.CodeGenerator.*csproj" -and $content -notmatch "ReferenceOutputAssembly=`"false`"") {
        Write-Host "  Adding proper analyzer reference attributes..." -ForegroundColor Yellow
        
        if (-not $DryRun) {
            # This is a simplified fix - in production you'd want more sophisticated XML manipulation
            $content = $content -replace '(<ProjectReference[^>]*Orleans\.CodeGenerator[^>]*)(>)', '$1 ReferenceOutputAssembly="false" PrivateAssets="All"$2'
            $content = $content -replace '(<ProjectReference[^>]*Orleans\.Analyzers[^>]*)(>)', '$1 ReferenceOutputAssembly="false" PrivateAssets="All"$2'
            Set-Content -Path $targetFile -Value $content -NoNewline
            Write-Host "  Updated Directory.Build.targets" -ForegroundColor Green
        }
    }
}

# Step 2: Fix project references
Write-Host "`nStep 2: Fixing project references..." -ForegroundColor Green
.\Smart-Fix-References.ps1 -RootPath $RootPath -DryRun:$DryRun -BackupFirst:$BackupFirst

# Step 3: Fix PackageId values
Write-Host "`nStep 3: Fixing PackageId values..." -ForegroundColor Green
.\Fix-PackageIds.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 4: Fix assembly names
Write-Host "`nStep 4: Fixing assembly names..." -ForegroundColor Green
.\Fix-AssemblyNames.ps1 -RootPath $RootPath -CheckOnly:$DryRun

# Step 5: Fix F# specific namespace issues
Write-Host "`nStep 5: Fixing F# namespaces..." -ForegroundColor Green
.\Fix-FSharp-Namespaces.ps1 -Path $RootPath -DryRun:$DryRun

# Step 6: Fix SDK targets files
Write-Host "`nStep 6: Fixing SDK targets files..." -ForegroundColor Green
.\Fix-SDK-Targets.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 7: Fix special projects with unique requirements
Write-Host "`nStep 7: Fixing special projects..." -ForegroundColor Green
.\Fix-Special-Projects.ps1 -RootPath $RootPath -DryRun:$DryRun

# Step 8: Clean NuGet cache artifacts if not dry run
if (-not $DryRun) {
    Write-Host "`nStep 8: Cleaning build artifacts..." -ForegroundColor Green
    Write-Host "  Removing project.assets.json files..." -ForegroundColor Gray
    Get-ChildItem -Path $RootPath -Recurse -Filter "project.assets.json" -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "  Removing .nuget.* files..." -ForegroundColor Gray
    Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj.nuget.*" -ErrorAction SilentlyContinue | Remove-Item -Force
}

# Step 9: Restore and build to verify
if (-not $DryRun) {
    Write-Host "`nStep 9: Restoring NuGet packages..." -ForegroundColor Green
    try {
        & dotnet restore "$RootPath\Orleans.sln" --force
        Write-Host "  Restore completed successfully" -ForegroundColor Green
    }
    catch {
        Write-Warning "Restore failed. You may need to run 'dotnet restore' manually."
    }

    Write-Host "`nStep 10: Building solution to verify fixes..." -ForegroundColor Green
    try {
        # Clear any remaining bin/obj folders that might have cached data
        Write-Host "  Cleaning bin/obj folders..." -ForegroundColor Gray
        Get-ChildItem -Path $RootPath -Directory -Recurse | Where-Object { $_.Name -in @("bin", "obj") } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        # Build the solution
        $buildOutput = & dotnet build "$RootPath\Orleans.sln" -c Debug --no-restore 2>&1
        $successfulProjects = ($buildOutput | Select-String " -> ").Count
        $totalProjects = & dotnet sln "$RootPath\Orleans.sln" list | Select-String "\.csproj|\.fsproj" | Measure-Object | Select-Object -ExpandProperty Count
        $buildErrors = ($buildOutput | Select-String "error").Count

        Write-Host "`nBuild Results:" -ForegroundColor Cyan
        Write-Host "Successful projects: $successfulProjects / $totalProjects" -ForegroundColor $(if ($successfulProjects -eq $totalProjects) { "Green" } else { "Yellow" })
        
        if ($buildErrors -gt 0) {
            Write-Host "Build errors: $buildErrors" -ForegroundColor Red
            
            # Show first few errors
            Write-Host "`nFirst few errors:" -ForegroundColor Yellow
            $buildOutput | Select-String "error" | Select-Object -First 5 | ForEach-Object {
                Write-Host "  $_" -ForegroundColor Red
            }
        }

        if ($successfulProjects -lt $totalProjects) {
            Write-Host "`nTo see all build errors, run:" -ForegroundColor Yellow
            Write-Host "  dotnet build `"$RootPath\Orleans.sln`" -c Debug --no-restore" -ForegroundColor White
            
            Write-Host "`nCommon issues to check:" -ForegroundColor Yellow
            Write-Host "  - Analyzer project references in Directory.Build.targets" -ForegroundColor Gray
            Write-Host "  - Windows paths in project.assets.json files" -ForegroundColor Gray
            Write-Host "  - Missing type conversions (Orleans* -> Forkleans*)" -ForegroundColor Gray
        } else {
            Write-Host "`nBuild completed successfully!" -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Build failed. Run 'dotnet build' manually to see errors."
    }
}

# Create a summary report
$reportPath = Join-Path $RootPath "fork-maintenance-report.txt"
@"
Fork Maintenance Report
======================
Date: $(Get-Date)
Dry Run: $DryRun

Steps Completed:
1. Namespace conversion (Orleans -> Forkleans)
2. Type name conversion (Orleans* -> Forkleans*)
3. Project reference fixes
4. Directory.Build file fixes
5. SDK build file name fixes
6. Build props/targets file renames (Microsoft.Orleans.* -> Forkleans.*)
7. PackageId fixes (Microsoft.Orleans.* -> Forkleans.*)
8. Assembly name updates
9. F# namespace fixes
10. SDK targets fixes
11. Special project fixes

Conversion Summary:
- All Orleans namespaces -> Forkleans
- All Orleans-prefixed types -> Forkleans-prefixed
- Assembly names use Forkleans prefix
- Package names use Forkleans prefix

Known Issues to Watch:
- Analyzer projects need ReferenceOutputAssembly="false"
- Project reference paths should not change (Orleans.*.csproj)
- MSBuild properties (OrleansBuildTimeCodeGen) remain unchanged
- Build props/targets files are renamed to Forkleans.*

Next Steps:
1. Run 'dotnet build' to verify all projects compile
2. Run a few unit tests to ensure functionality
3. Check git status for any unexpected changes
4. Commit the changes with a descriptive message
"@ | Set-Content -Path $reportPath

Write-Host "`nReport saved to: $reportPath" -ForegroundColor Cyan

Write-Host "`nFork maintenance complete!" -ForegroundColor Green

if ($DryRun) {
    Write-Host "`nThis was a dry run. To apply changes, run without -DryRun flag." -ForegroundColor Cyan
} else {
    Write-Host "`nRecommended next steps:" -ForegroundColor Cyan
    Write-Host "  1. Review the changes: git status" -ForegroundColor Gray
    Write-Host "  2. Run tests: dotnet test" -ForegroundColor Gray
    Write-Host "  3. Commit if satisfied: git commit -am 'Applied fork namespace conversion'" -ForegroundColor Gray
}