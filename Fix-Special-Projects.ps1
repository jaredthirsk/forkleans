# Fix-Special-Projects.ps1
# Fixes special projects that have unique requirements in the Forkleans fork

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Fixing special projects that have unique requirements..." -ForegroundColor Cyan

# 1. Fix xunit.runner.json file references
Write-Host "`nFixing xunit.runner.json file references..." -ForegroundColor Green
$xunitFiles = Get-ChildItem -Path "$RootPath\test" -Filter "*.xunit.runner.json" -Recurse -File

foreach ($file in $xunitFiles) {
    # Check if the file name starts with "Forkleans."
    if ($file.Name.StartsWith("Forkleans.")) {
        $directory = $file.DirectoryName
        $expectedName = $file.Name.Replace("Forkleans.", "Orleans.")
        $expectedPath = Join-Path $directory $expectedName
        
        # Check if Orleans version exists
        if (Test-Path $expectedPath) {
            Write-Host "  Found duplicate: $($file.Name) and $expectedName" -ForegroundColor Yellow
            if (-not $DryRun) {
                # Keep the Orleans version, remove the Forkleans version
                Remove-Item $file.FullName -Force
                Write-Host "    Removed $($file.Name)" -ForegroundColor Green
            }
        } else {
            # Rename Forkleans to Orleans
            Write-Host "  Renaming: $($file.Name) -> $expectedName" -ForegroundColor Gray
            if (-not $DryRun) {
                Rename-Item -Path $file.FullName -NewName $expectedName -Force
                Write-Host "    Renamed successfully" -ForegroundColor Green
            }
        }
    }
}

# Also fix project file references to xunit.runner.json files
$projectFiles = Get-ChildItem -Path "$RootPath\test" -Filter "*.csproj" -Recurse -File
foreach ($projectFile in $projectFiles) {
    $content = Get-Content $projectFile -Raw
    if ($content -match 'Forkleans\..*\.xunit\.runner\.json') {
        Write-Host "  Fixing xunit.runner.json reference in: $($projectFile.Name)" -ForegroundColor Gray
        if (-not $DryRun) {
            $newContent = $content -replace 'Forkleans\.(.*?)\.xunit\.runner\.json', 'Orleans.$1.xunit.runner.json'
            Set-Content -Path $projectFile.FullName -Value $newContent -NoNewline
            Write-Host "    Fixed reference" -ForegroundColor Green
        }
    }
}

# 2. Add Directory.Build.props for test/Misc to exclude implicit usings for certain projects
$miscPropsPath = Join-Path $RootPath "test\Misc\Directory.Build.props"
Write-Host "`nCreating Directory.Build.props for test\Misc projects..." -ForegroundColor Green

$miscPropsContent = @'
<Project>
  <PropertyGroup>
    <_ParentDirectoryBuildPropsPath Condition="'$(_DirectoryBuildPropsFile)' != ''">$([System.IO.Path]::Combine('..', '$(_DirectoryBuildPropsFile)'))</_ParentDirectoryBuildPropsPath>
  </PropertyGroup>

  <Import Project="$(_ParentDirectoryBuildPropsPath)" Condition="Exists('$(_ParentDirectoryBuildPropsPath)')"/>

  <!-- Some Misc test projects don't actually use Orleans/Forkleans or only use serialization abstractions -->
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'TestInterfaces' or '$(MSBuildProjectName)' == 'TestFSharpInterfaces' or '$(MSBuildProjectName)' == 'TestSerializerExternalModels'">
    <DisableForkleanseImplicitUsings>true</DisableForkleanseImplicitUsings>
  </PropertyGroup>

</Project>
'@

if (-not $DryRun) {
    $miscDir = Split-Path $miscPropsPath -Parent
    if (-not (Test-Path $miscDir)) {
        New-Item -ItemType Directory -Path $miscDir -Force | Out-Null
    }
    Set-Content -Path $miscPropsPath -Value $miscPropsContent -NoNewline
    Write-Host "  Created test\Misc\Directory.Build.props" -ForegroundColor Green
} else {
    Write-Host "  Would create test\Misc\Directory.Build.props (dry run)" -ForegroundColor Yellow
}

# 3. Update the main test Directory.Build.props to respect DisableForkleanseImplicitUsings
$testPropsPath = Join-Path $RootPath "test\Directory.Build.props"
Write-Host "`nUpdating test\Directory.Build.props to support DisableForkleanseImplicitUsings..." -ForegroundColor Green

if (Test-Path $testPropsPath) {
    $content = Get-Content $testPropsPath -Raw
    
    # Update the condition to check for DisableForkleanseImplicitUsings
    $oldCondition = "('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' != 'true'"
    $newCondition = "('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' != 'true' and '`$(DisableForkleanseImplicitUsings)' != 'true'"
    
    if ($content -match [regex]::Escape($oldCondition)) {
        Write-Host "  Updating condition in test\Directory.Build.props" -ForegroundColor Gray
        if (-not $DryRun) {
            $newContent = $content.Replace($oldCondition, $newCondition)
            Set-Content -Path $testPropsPath -Value $newContent -NoNewline
            Write-Host "    Updated successfully" -ForegroundColor Green
        }
    }
}

# 4. Update playground Directory.Build.props for ChaoticCluster.ServiceDefaults
$playgroundPropsPath = Join-Path $RootPath "playground\Directory.Build.props"
Write-Host "`nUpdating playground\Directory.Build.props for Aspire shared projects..." -ForegroundColor Green

if (Test-Path $playgroundPropsPath) {
    $content = Get-Content $playgroundPropsPath -Raw
    
    # Update the condition to also check for IsAspireSharedProject
    $oldCondition = "('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' != 'true'"
    $newCondition = "('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' != 'true' and '`$(IsAspireSharedProject)' != 'true'"
    
    if ($content -match [regex]::Escape($oldCondition)) {
        Write-Host "  Updating condition in playground\Directory.Build.props" -ForegroundColor Gray
        if (-not $DryRun) {
            $newContent = $content.Replace($oldCondition, $newCondition)
            Set-Content -Path $playgroundPropsPath -Value $newContent -NoNewline
            Write-Host "    Updated successfully" -ForegroundColor Green
        }
    }
}

# 5. Clean obj directories for affected projects
Write-Host "`nCleaning obj directories for affected projects..." -ForegroundColor Green
$projectsToClean = @(
    "test\Misc\TestInterfaces",
    "test\Misc\TestSerializerExternalModels",
    "playground\ChaoticCluster\ChaoticCluster.ServiceDefaults",
    "test\TestInfrastructure\Orleans.TestingHost.Tests"
)

foreach ($project in $projectsToClean) {
    $objPath = Join-Path $RootPath $project "obj"
    if (Test-Path $objPath) {
        Write-Host "  Cleaning: $project\obj" -ForegroundColor Gray
        if (-not $DryRun) {
            Remove-Item -Path $objPath -Recurse -Force
            Write-Host "    Cleaned successfully" -ForegroundColor Green
        }
    }
}

if ($DryRun) {
    Write-Host "`nDry run complete. No files were modified." -ForegroundColor Yellow
} else {
    Write-Host "`nSpecial projects fixed successfully!" -ForegroundColor Green
}