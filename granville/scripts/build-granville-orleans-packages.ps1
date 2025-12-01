#!/usr/bin/env pwsh
# Build and package essential Granville.Orleans packages

param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [switch]$SkipClean = $false
)

Write-Host "Building and packaging Granville.Orleans packages..." -ForegroundColor Green

# Detect if we're running in a container (Docker/devcontainer)
$isContainer = Test-Path "/.dockerenv"

# Determine if we're running in WSL2 (but not in a container)
$isWSL = $false
if (-not $isContainer -and (Test-Path "/proc/version")) {
    $procVersion = Get-Content "/proc/version" -ErrorAction SilentlyContinue
    if ($procVersion -match "(WSL|Microsoft)") {
        $isWSL = $true
    }
}

# Choose appropriate dotnet command
# In containers, always use native dotnet; in WSL2 (not container), use dotnet-win
$dotnetCmd = if ($isContainer) { "dotnet" } elseif ($isWSL) { "dotnet-win" } else { "dotnet" }

# Get version dynamically if not provided
if ([string]::IsNullOrEmpty($Version)) {
    # Try to read from current-revision.txt first
    $revisionFile = Join-Path $PSScriptRoot "../current-revision.txt"
    if (Test-Path $revisionFile) {
        $revision = Get-Content $revisionFile -Raw
        $revision = $revision.Trim()
        $Version = "9.1.2.$revision"
        Write-Host "Using revision from current-revision.txt: $Version" -ForegroundColor Cyan
    }
    else {
        # Fall back to reading from Directory.Build.props
        $buildPropsPath = Join-Path $PSScriptRoot "../../Directory.Build.props"
        if (Test-Path $buildPropsPath) {
            $content = Get-Content $buildPropsPath -Raw
            if ($content -match '<GranvilleRevision[^>]*>([0-9]+)</GranvilleRevision>') {
                $revision = $matches[1]
                $Version = "9.1.2.$revision"
                Write-Host "Using revision from Directory.Build.props: $Version" -ForegroundColor Cyan
            }
        }
    }
    
    if ([string]::IsNullOrEmpty($Version)) {
        Write-Error "Could not determine version. Please specify -Version parameter."
        exit 1
    }
}

$projects = @(
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    "src/Orleans.Sdk/Orleans.Sdk.csproj",
    "src/Orleans.Runtime/Orleans.Runtime.csproj",
    "src/Orleans.Server/Orleans.Server.csproj",
    "src/Orleans.Client/Orleans.Client.csproj",
    "src/Orleans.Reminders/Orleans.Reminders.csproj"
)

# First build all dependencies
# Note: We disable NuGet audit to avoid failing on vulnerable packages in upstream Orleans
# that aren't part of the Granville RPC packages we're building
Write-Host "Building Orleans.sln to ensure all dependencies are built..." -ForegroundColor Yellow
& $dotnetCmd build Orleans.sln -c $Configuration -p:BuildAsGranville=true -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build Orleans.sln"
    exit 1
}

# Then pack each project with proper naming
foreach ($project in $projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "`nPacking $projectName..." -ForegroundColor Cyan
    
    # Pack with Granville naming
    & $dotnetCmd pack $project -c $Configuration -o Artifacts/Release --no-build `
        -p:BuildAsGranville=true `
        -p:PackageVersion="$Version"
        
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to pack $project"
    }
}

Write-Host "`nGranville.Orleans packages created successfully!" -ForegroundColor Green

# Clean up any Microsoft.Orleans packages that shouldn't exist
if (-not $SkipClean) {
    Write-Host "`nCleaning up Microsoft.Orleans packages..." -ForegroundColor Yellow
    $microsoftPackages = Get-ChildItem "Artifacts/Release/Microsoft.Orleans.*.nupkg" -ErrorAction SilentlyContinue
    if ($microsoftPackages) {
        foreach ($package in $microsoftPackages) {
            if ($package.Name -notmatch "-granville-shim") {
                Write-Host "  Removing: $($package.Name)" -ForegroundColor Red
                Remove-Item $package.FullName -Force
            }
        }
    }
} else {
    Write-Host "`nSkipping Microsoft.Orleans packages cleanup" -ForegroundColor Cyan
}

Write-Host "Packages are in: Artifacts/Release" -ForegroundColor Yellow