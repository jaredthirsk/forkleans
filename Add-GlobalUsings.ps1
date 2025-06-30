# Add-GlobalUsings.ps1
# This script adds GlobalUsings.cs files to each project for Forkleans compatibility

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,

    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

function Write-Info($message) {
    Write-Host "[INFO] $message" -ForegroundColor Cyan
}

function Write-Change($message) {
    Write-Host "[CHANGE] $message" -ForegroundColor Yellow
}

function Write-Success($message) {
    Write-Host "[SUCCESS] $message" -ForegroundColor Green
}

Write-Info "Adding global usings to all projects..."

# Define the global usings we want to add
# We'll map Orleans namespaces to Forkleans equivalents
$globalUsingsContent = @'
// Global usings for Forkleans compatibility
global using Forkleans;
global using Forkleans.Runtime;
global using Forkleans.Core;
global using Forkleans.Core.Abstractions;
global using Forkleans.Serialization;
global using Forkleans.Serialization.Abstractions;
global using Forkleans.Storage;
global using Forkleans.Streams;
global using Forkleans.Timers;
global using Forkleans.Transactions;
global using Forkleans.Transactions.Abstractions;
global using Forkleans.Configuration;
global using Forkleans.Hosting;

// Type aliases to maintain compatibility with Orleans type names
global using GrainId = Forkleans.Runtime.GrainId;
global using GrainReference = Forkleans.Runtime.GrainReference;
global using IGrainFactory = Forkleans.IGrainFactory;
global using IClusterClient = Forkleans.IClusterClient;
global using ISiloHost = Forkleans.Hosting.ISiloHost;

// Add more aliases as needed for commonly used types
'@

# Find all project directories (containing .csproj files)
$projectDirs = Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | ForEach-Object {
    [PSCustomObject]@{
        ProjectFile = $_
        Directory = $_.DirectoryName
        ProjectName = $_.BaseName
    }
}

Write-Info "Found $($projectDirs.Count) projects"

$filesCreated = 0
$filesUpdated = 0

foreach ($project in $projectDirs) {
    # Skip test projects if desired
    # if ($project.ProjectName -match "Test") { continue }

    $globalUsingsFile = Join-Path $project.Directory "GlobalUsings.cs"
    $relativePath = $globalUsingsFile.Substring($RootPath.Length + 1)

    # Check if GlobalUsings.cs already exists
    if (Test-Path $globalUsingsFile) {
        Write-Info "GlobalUsings.cs already exists in: $($project.ProjectName)"

        # Read existing content
        $existingContent = Get-Content $globalUsingsFile -Raw

        # Check if it already has Forkleans usings
        if ($existingContent -match "global using Forkleans") {
            Write-Info "  Already has Forkleans global usings"
            continue
        }

        # Append our usings to existing file
        if (-not $DryRun) {
            $newContent = $existingContent.TrimEnd() + "`r`n`r`n" + $globalUsingsContent
            Set-Content -Path $globalUsingsFile -Value $newContent -NoNewline
            Write-Change "  Updated existing GlobalUsings.cs"
            $filesUpdated++
        } else {
            Write-Info "  Would update existing GlobalUsings.cs (dry run)"
        }
    }
    else {
        # Create new GlobalUsings.cs
        Write-Change "Creating GlobalUsings.cs for: $($project.ProjectName)"

        if (-not $DryRun) {
            # Create a project-specific version with only relevant namespaces
            $projectSpecificContent = "// Global usings for $($project.ProjectName)`r`n"

            # Determine which namespaces this project likely needs
            $projectContent = Get-ChildItem -Path $project.Directory -Filter "*.cs" -Recurse |
                ForEach-Object { Get-Content $_.FullName -Raw } | Out-String

            # Add only the namespaces that seem to be used
            $namespacesToAdd = @()

            if ($projectContent -match "Orleans\.Runtime|GrainId|GrainReference|IGrain") {
                $namespacesToAdd += "global using Forkleans.Runtime;"
            }
            if ($projectContent -match "Orleans\.Core") {
                $namespacesToAdd += "global using Forkleans.Core;"
            }
            if ($projectContent -match "Orleans\.Serialization|ISerializer") {
                $namespacesToAdd += "global using Forkleans.Serialization;"
            }
            if ($projectContent -match "Orleans\.Storage|IStorage") {
                $namespacesToAdd += "global using Forkleans.Storage;"
            }
            if ($projectContent -match "Orleans\.Streams|IStream") {
                $namespacesToAdd += "global using Forkleans.Streams;"
            }
            if ($projectContent -match "Orleans\.Hosting|ISiloHost|IHost") {
                $namespacesToAdd += "global using Forkleans.Hosting;"
            }
            if ($projectContent -match "Orleans\.Configuration|IOptions|IConfiguration") {
                $namespacesToAdd += "global using Forkleans.Configuration;"
            }

            # Always add the base namespace
            if ($namespacesToAdd.Count -eq 0) {
                $namespacesToAdd += "global using Forkleans;"
            }

            $projectSpecificContent += $namespacesToAdd -join "`r`n"

            # Add type aliases if needed
            if ($projectContent -match "IPersistentState") {
                $projectSpecificContent += "`r`n`r`n// Type aliases for compatibility"
                $projectSpecificContent += "`r`nglobal using IPersistentState = Forkleans.Runtime.IPersistentState;"
            }

            Set-Content -Path $globalUsingsFile -Value $projectSpecificContent -NoNewline
            Write-Success "  Created GlobalUsings.cs"
            $filesCreated++
        } else {
            Write-Info "  Would create GlobalUsings.cs (dry run)"
        }
    }
}

# Also create a Directory.Build.props file to ensure global usings are used
$buildPropsFile = Join-Path $RootPath "Directory.Build.props"
$buildPropsContent = @'
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Ensure GlobalUsings.cs is included if it exists -->
    <Compile Include="GlobalUsings.cs" Condition="Exists('GlobalUsings.cs')" />
  </ItemGroup>

  <!-- Map Orleans namespaces to Forkleans -->
  <PropertyGroup>
    <RootNamespace Condition="$(RootNamespace.StartsWith('Orleans'))">$(RootNamespace.Replace('Orleans', 'Forkleans'))</RootNamespace>
  </PropertyGroup>
</Project>
'@

if (-not (Test-Path $buildPropsFile)) {
    if (-not $DryRun) {
        Set-Content -Path $buildPropsFile -Value $buildPropsContent -NoNewline
        Write-Success "`nCreated Directory.Build.props for global configuration"
    } else {
        Write-Info "`nWould create Directory.Build.props (dry run)"
    }
}

# Summary
Write-Host "`n=== SUMMARY ===" -ForegroundColor Magenta
Write-Info "Projects processed: $($projectDirs.Count)"
Write-Info "GlobalUsings.cs files created: $filesCreated"
Write-Info "GlobalUsings.cs files updated: $filesUpdated"

if ($DryRun) {
    Write-Warning "`nThis was a dry run. No files were created."
    Write-Info "Run without -DryRun to create the files."
}

# Additional recommendations
Write-Host "`nAdditional recommendations:" -ForegroundColor Magenta
Write-Host "1. You may need to remove conflicting 'using Orleans.*' statements from individual files"
Write-Host "2. Consider adding more type aliases for commonly used Orleans types"
Write-Host "3. Some projects may need project-specific global usings"
Write-Host "4. Run 'dotnet build' to see if any namespace conflicts remain"

# Create a cleanup script
$cleanupScriptPath = Join-Path $RootPath "Remove-OrleansUsings.ps1"
$cleanupScript = @'
# Remove-OrleansUsings.ps1
# Removes old Orleans using statements now that we have global usings

param([string]$RootPath)

Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | ForEach-Object {
    if ($_.Name -eq "GlobalUsings.cs") { return }

    $content = Get-Content $_.FullName -Raw
    $originalContent = $content

    # Remove Orleans using statements that are now global
    $content = $content -replace 'using Orleans\.Runtime;\s*\r?\n', ''
    $content = $content -replace 'using Orleans\.Core;\s*\r?\n', ''
    $content = $content -replace 'using Orleans;\s*\r?\n', ''

    if ($content -ne $originalContent) {
        Set-Content -Path $_.FullName -Value $content -NoNewline
        Write-Host "Cleaned: $($_.Name)"
    }
}
'@

if (-not $DryRun) {
    Set-Content -Path $cleanupScriptPath -Value $cleanupScript
    Write-Info "`nCreated Remove-OrleansUsings.ps1 to clean up old using statements"
}
