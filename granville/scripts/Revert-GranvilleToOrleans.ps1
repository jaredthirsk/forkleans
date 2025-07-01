# Granville to Orleans Namespace Reverter
# This script reverts Granville namespaces back to Orleans for non-RPC projects
# to enable compatibility with third-party Orleans packages

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [switch]$DryRun = $false,
    
    [Parameter()]
    [switch]$BackupFirst = $true
)

$ErrorActionPreference = "Stop"

# File extensions to process
$codeExtensions = @("*.cs", "*.fs", "*.fsx", "*.fsproj", "*.csproj", "*.props", "*.targets", "*.json", "*.xml", "*.config", "*.sln")
$excludeDirs = @(".git", "bin", "obj", "packages", ".vs", "artifacts", "node_modules", "Rpc", "samples")

function Write-Info($message) {
    Write-Host "[INFO] $message" -ForegroundColor Cyan
}

function Write-Change($message) {
    Write-Host "[CHANGE] $message" -ForegroundColor Yellow
}

function Write-Success($message) {
    Write-Host "[SUCCESS] $message" -ForegroundColor Green
}

function Write-Skip($message) {
    Write-Host "[SKIP] $message" -ForegroundColor Gray
}

# Create backup if requested
if ($BackupFirst -and -not $DryRun) {
    $backupPath = "$RootPath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Write-Info "Creating backup at: $backupPath"
    Copy-Item -Path $RootPath -Destination $backupPath -Recurse -Force
    Write-Success "Backup created"
}

Write-Info "Reverting namespaces from Granville to Orleans (excluding RPC projects)"
Write-Info "Excluding directories: $($excludeDirs -join ', ')"
Write-Info "Processing extensions: $($codeExtensions -join ', ')"

if ($DryRun) {
    Write-Info "DRY RUN MODE - No files will be modified"
}

# Namespace replacements
$namespaceReplacements = @{
    # Basic namespace replacements
    'namespace Granville' = 'namespace Orleans'
    'using Granville' = 'using Orleans'
    'global::Granville' = 'global::Orleans'
    'Granville::' = 'Orleans::'
    '<Granville\.' = '<Orleans.'
    '\.Granville\.' = '.Orleans.'
    ' Granville\.' = ' Orleans.'
    '"Granville\.' = '"Orleans.'
    "'Granville\." = "'Orleans."
    '\(Granville\.' = '(Orleans.'
}

# Project file specific replacements
$projectReplacements = @{
    '<AssemblyName>Granville\.' = '<AssemblyName>Orleans.'
    '<PackageId>Granville\.' = '<PackageId>Orleans.'
    '<RootNamespace>Granville' = '<RootNamespace>Orleans'
    'Include="Granville\.' = 'Include="Orleans.'
}

# Type name replacements (common Orleans-prefixed types)
$typeReplacements = @{
    'GranvilleClient' = 'OrleansClient'
    'GranvilleConfiguration' = 'OrleansConfiguration'
    'GranvilleException' = 'OrleansException'
    'GranvilleSilo' = 'OrleansSilo'
    'GranvilleReminder' = 'OrleansReminder'
    'GranvilleStream' = 'OrleansStream'
    'GranvilleCodeGen' = 'OrleansCodeGen'
    'GranvilleGeneratedCode' = 'OrleansGeneratedCode'
}

function Should-SkipFile($filePath) {
    # Skip if file is in Rpc directory
    if ($filePath -match '[\\/]Rpc[\\/]') {
        return $true
    }
    
    # Skip if file is in samples directory (they use the fork)
    if ($filePath -match '[\\/]samples[\\/]') {
        return $true
    }
    
    # Skip auto-generated files
    if ($filePath -match '[\\/]obj[\\/]|[\\/]bin[\\/]') {
        return $true
    }
    
    return $false
}

function Process-File($filePath) {
    if (Should-SkipFile $filePath) {
        Write-Skip "Skipping RPC/samples file: $filePath"
        return
    }
    
    $content = Get-Content -Path $filePath -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return }
    
    $originalContent = $content
    $fileChanged = $false
    
    # Apply namespace replacements
    foreach ($pattern in $namespaceReplacements.Keys) {
        $replacement = $namespaceReplacements[$pattern]
        if ($content -match $pattern) {
            $content = $content -replace $pattern, $replacement
            $fileChanged = $true
        }
    }
    
    # Apply project file replacements if it's a project file
    if ($filePath -match '\.(cs|fs|vb)proj$|\.props$|\.targets$') {
        foreach ($pattern in $projectReplacements.Keys) {
            $replacement = $projectReplacements[$pattern]
            if ($content -match $pattern) {
                $content = $content -replace $pattern, $replacement
                $fileChanged = $true
            }
        }
    }
    
    # Apply type replacements
    foreach ($pattern in $typeReplacements.Keys) {
        $replacement = $typeReplacements[$pattern]
        if ($content -match $pattern) {
            $content = $content -replace $pattern, $replacement
            $fileChanged = $true
        }
    }
    
    # Special handling for InternalsVisibleTo
    if ($content -match 'InternalsVisibleTo') {
        # Keep Granville.Rpc references but convert other Granville to Orleans
        $tempContent = $content
        
        # First, protect Granville.Rpc references by temporarily replacing them
        $tempContent = $tempContent -replace 'InternalsVisibleTo\(\s*"Granville\.Rpc', 'InternalsVisibleTo("__KEEP_GRANVILLE_RPC__'
        
        # Then convert all other Granville references to Orleans
        $tempContent = $tempContent -replace 'InternalsVisibleTo\(\s*"Granville\.', 'InternalsVisibleTo("Orleans.'
        
        # Finally, restore the Granville.Rpc references
        $tempContent = $tempContent -replace '__KEEP_GRANVILLE_RPC__', 'Granville.Rpc'
        
        if ($tempContent -ne $content) {
            $content = $tempContent
            $fileChanged = $true
        }
    }
    
    if ($fileChanged) {
        if (-not $DryRun) {
            Set-Content -Path $filePath -Value $content -NoNewline -Encoding UTF8
        }
        Write-Change "Modified: $filePath"
    }
}

# Get all files to process
$files = @()
foreach ($ext in $codeExtensions) {
    $files += Get-ChildItem -Path $RootPath -Filter $ext -Recurse -File | 
        Where-Object { 
            $dir = $_.DirectoryName
            $exclude = $false
            foreach ($excludeDir in $excludeDirs) {
                if ($dir -match "[\\/]$excludeDir([\\/]|$)") {
                    $exclude = $true
                    break
                }
            }
            -not $exclude
        }
}

Write-Info "Found $($files.Count) files to process"

$processedCount = 0
foreach ($file in $files) {
    Process-File $file.FullName
    $processedCount++
    if ($processedCount % 100 -eq 0) {
        Write-Info "Processed $processedCount/$($files.Count) files..."
    }
}

Write-Success "Conversion complete! Processed $($files.Count) files"

if ($DryRun) {
    Write-Info "This was a DRY RUN - no files were actually modified"
}