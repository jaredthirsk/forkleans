# Orleans to Forkleans Namespace Converter
# This script helps maintain a fork of Microsoft Orleans with renamed namespaces
# Updated to also rename Orleans-prefixed types for consistency

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
                # Code files - apply comprehensive replacements
                
                # 1. Convert using statements
                $content = $content -replace "using\s+$OldName([\s;.])", "using $NewName`$1"
                $content = $content -replace "using\s+$OldName\.", "using $NewName."
                
                # 2. Convert namespace declarations
                $content = $content -replace "namespace\s+$OldName([\s\r\n{])", "namespace $NewName`$1"
                $content = $content -replace "namespace\s+$OldName\.", "namespace $NewName."
                
                # 3. Convert Orleans. to Forkleans. in qualified names
                $content = $content -replace "\b$OldName\.", "$NewName."
                
                # 4. Convert Orleans-prefixed types to Forkleans-prefixed
                # This includes class names, exception types, attributes, etc.
                # Match whole words that start with Orleans followed by an uppercase letter
                $content = $content -replace "\b${OldName}([A-Z][a-zA-Z0-9]*)\b", "${NewName}`$1"
                
                # 5. Special case: IOrleansX interfaces should become IForkleansX
                $content = $content -replace "\bI${OldName}([A-Z][a-zA-Z0-9]*)\b", "I${NewName}`$1"
            }
            elseif ($extension -in "*.csproj", "*.fsproj") {
                # Project files - be careful but still convert type names
                
                # Convert Using statements
                $content = $content -replace "<Using\s+Include=`"$OldName`"", "<Using Include=`"$NewName`""
                
                # Convert Alias attributes
                $content = $content -replace "Alias=`"$OldName`"", "Alias=`"$NewName`""
                
                # DO NOT convert:
                # - ProjectReference Include paths (file names remain Orleans.*.csproj)
                # - File paths
                # But DO convert type names in other contexts
            }
            elseif ($extension -in "*.props", "*.targets") {
                # MSBuild files - convert namespaces but not property names
                
                # Convert namespace usage but keep MSBuild properties like OrleansBuildTimeCodeGen unchanged
                # This is tricky - we want to change Orleans types but not MSBuild properties
                
                # Convert namespace imports
                $content = $content -replace "<Using\s+Include=`"$OldName`"", "<Using Include=`"$NewName`""
                
                # Don't convert property names or file paths
            }
            elseif ($extension -eq "*.sln") {
                # Solution files - no changes needed
            }
            else {
                # Other files (json, xml, config) - convert namespaces and type names
                $content = $content -replace "\b$OldName\.", "$NewName."
                $content = $content -replace "\b${OldName}([A-Z][a-zA-Z0-9]*)\b", "${NewName}`$1"
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

Conversion Summary:
- Namespace declarations: Orleans -> Forkleans
- Using statements: Orleans -> Forkleans  
- Type names: Orleans* -> Forkleans*
- Interfaces: IOrleans* -> IForkleans*
- Qualified names: Orleans. -> Forkleans.

Important Notes:
- Project file names remain unchanged (Orleans.*.csproj)
- MSBuild property names (like OrleansBuildTimeCodeGen) remain unchanged
- Microsoft.Orleans package references remain unchanged
- All Orleans-prefixed types are now Forkleans-prefixed for consistency
"@ | Set-Content -Path $reportPath

Write-Info "Report saved to: $reportPath"