# Orleans to Forkleans Namespace Converter
# This script helps maintain a fork of Microsoft Orleans with renamed namespaces
# Updated with lessons learned from debugging conversion issues

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [string]$OldName = "Orleans",
    
    [Parameter()]
    [string]$NewName = "Forkleans",
    
    [Parameter()]
    [switch]$DryRun = $false,
    
    [Parameter()]
    [switch]$BackupFirst = $true
)

$ErrorActionPreference = "Stop"

# File extensions to process
$codeExtensions = @("*.cs", "*.fs", "*.fsx", "*.fsproj", "*.csproj", "*.props", "*.targets", "*.json", "*.xml", "*.config", "*.sln")
$excludeDirs = @(".git", "bin", "obj", "packages", ".vs", "artifacts", "node_modules")

# Types that should NOT be renamed (Orleans-prefixed exception and serializer types)
$preservedTypes = @(
    "OrleansException",
    "OrleansConfigurationException",
    "OrleansTransactionAbortedException", 
    "OrleansJsonSerializer",
    "OrleansJsonSerializerOptions",
    "OrleansGrainStorageSerializer",
    "OrleansLifecycleAttribute",
    "OrleansGrainReferenceAttribute",
    "OrleansCopierNotFoundException",
    "OrleansMessageRejectionException",
    "OrleansClusterDisconnectedException",
    "OrleansGatewayTimeoutException",
    "OrleansLifecycleParticipantAttribute"
)

function Write-Info($message) {
    Write-Host "[INFO] $message" -ForegroundColor Cyan
}

function Write-Change($message) {
    Write-Host "[CHANGE] $message" -ForegroundColor Yellow
}

function Write-Success($message) {
    Write-Host "[SUCCESS] $message" -ForegroundColor Green
}

# Create backup if requested
if ($BackupFirst -and -not $DryRun) {
    $backupPath = "$RootPath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Write-Info "Creating backup at: $backupPath"
    Copy-Item -Path $RootPath -Destination $backupPath -Recurse -Force
    Write-Success "Backup created"
}

Write-Info "Converting namespaces from $OldName to $NewName"
Write-Info "Excluding directories: $($excludeDirs -join ', ')"
Write-Info "Processing extensions: $($codeExtensions -join ', ')"
Write-Info "Preserving types: $($preservedTypes.Count) Orleans-prefixed types"

$totalFiles = 0
$modifiedFiles = 0

# Process each file type
foreach ($extension in $codeExtensions) {
    Write-Info "Processing $extension files..."
    
    Get-ChildItem -Path $RootPath -Recurse -Filter $extension | Where-Object {
        $dir = $_.Directory.Name
        $excludeDirs -notcontains $dir
    } | ForEach-Object {
        $file = $_
        $totalFiles++
        
        try {
            $content = Get-Content -Path $file.FullName -Raw
            $originalContent = $content
            
            # Apply conversions based on file type
            if ($extension -in "*.cs", "*.fs", "*.fsx") {
                # Code files - apply smart replacements
                
                # 1. Convert using statements
                $content = $content -replace "using\s+$OldName([\s;.])", "using $NewName`$1"
                $content = $content -replace "using\s+$OldName\.", "using $NewName."
                
                # 2. Convert namespace declarations
                $content = $content -replace "namespace\s+$OldName([\s\r\n{])", "namespace $NewName`$1"
                $content = $content -replace "namespace\s+$OldName\.", "namespace $NewName."
                
                # 3. Convert Orleans. to Forkleans. carefully
                # Split into lines for more careful processing
                $lines = $content -split "`n"
                $newLines = @()
                
                foreach ($line in $lines) {
                    $newLine = $line
                    
                    # Skip if line is a comment or contains a string literal with Orleans.
                    if ($line -notmatch "^\s*//|^\s*\*|`"[^`"]*Orleans\.[^`"]*`"") {
                        # Check if line contains any preserved type - if so, skip the line
                        $skipLine = $false
                        foreach ($preservedType in $preservedTypes) {
                            if ($line -match "\b$preservedType\b") {
                                $skipLine = $true
                                break
                            }
                        }
                        
                        if (-not $skipLine) {
                            # Replace Orleans. with Forkleans. in code
                            $newLine = $newLine -replace "\b$OldName\.", "$NewName."
                        }
                    }
                    
                    $newLines += $newLine
                }
                
                $content = $newLines -join "`n"
                
                # 4. Final pass: Restore any preserved types that might have been converted
                foreach ($preservedType in $preservedTypes) {
                    $incorrectName = $preservedType -replace "^Orleans", $NewName
                    if ($content -match "\b$incorrectName\b") {
                        $content = $content -replace "\b$incorrectName\b", $preservedType
                    }
                }
            }
            elseif ($extension -in "*.csproj", "*.fsproj") {
                # Project files - be very careful
                
                # Convert Using statements
                $content = $content -replace "<Using\s+Include=`"$OldName`"", "<Using Include=`"$NewName`""
                
                # Convert Alias attributes
                $content = $content -replace "Alias=`"$OldName`"", "Alias=`"$NewName`""
                
                # DO NOT convert:
                # - ProjectReference Include paths
                # - PackageReference names
                # - Assembly names
                # - File paths
            }
            elseif ($extension -in "*.props", "*.targets") {
                # MSBuild files - minimal changes
                
                # Only convert specific namespace imports in code sections
                # DO NOT convert:
                # - File paths
                # - Property names like OrleansBuildTimeCodeGen
                # - Import paths
            }
            elseif ($extension -eq "*.sln") {
                # Solution files - no changes needed
            }
            else {
                # Other files (json, xml, config) - simple namespace replacement
                # But avoid changing file paths
                if ($content -notmatch "[\\/]Orleans[\\/]") {
                    $content = $content -replace "\b$OldName\.", "$NewName."
                }
            }
            
            # Only write if changed
            if ($content -ne $originalContent) {
                $modifiedFiles++
                
                if (-not $DryRun) {
                    Set-Content -Path $file.FullName -Value $content -NoNewline
                    Write-Change "Modified: $($file.FullName)"
                }
                else {
                    Write-Change "Would modify: $($file.FullName)"
                }
            }
        }
        catch {
            Write-Error "Error processing $($file.FullName): $_"
        }
    }
}

Write-Success "`nConversion complete!"
Write-Info "Total files scanned: $totalFiles"
Write-Info "Files modified: $modifiedFiles"

if ($DryRun) {
    Write-Info "`nThis was a dry run. No files were actually modified."
    Write-Info "Run without -DryRun to apply changes."
}

# Create a summary report
$reportPath = Join-Path $RootPath "namespace-conversion-report.txt"
@"
Namespace Conversion Report
==========================
Date: $(Get-Date)
From: $OldName
To: $NewName
Total Files: $totalFiles  
Modified Files: $modifiedFiles
Dry Run: $DryRun

Preserved Types:
$($preservedTypes | ForEach-Object { "  - $_" } | Out-String)

Important Notes:
- Project references remain unchanged (Orleans.*.csproj)
- Microsoft.Orleans package references remain unchanged  
- Orleans-prefixed exception and serializer types are preserved
- MSBuild property names (like OrleansBuildTimeCodeGen) remain unchanged
- Build file names remain unchanged
"@ | Set-Content -Path $reportPath

Write-Info "Report saved to: $reportPath"