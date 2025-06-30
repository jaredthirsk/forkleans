# Orleans to Forkleans Namespace Converter - Fixed Version
# This script helps maintain a fork of Microsoft Orleans with renamed namespaces
# This version properly handles project references to avoid breaking them

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

function Write-Error($message) {
    Write-Host "[ERROR] $message" -ForegroundColor Red
}

# Create backup if requested
if ($BackupFirst -and -not $DryRun) {
    $backupPath = "$RootPath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Write-Info "Creating backup at: $backupPath"
    Copy-Item -Path $RootPath -Destination $backupPath -Recurse -Force
    Write-Success "Backup created"
}

# Function to check if path should be excluded
function Should-Exclude($path) {
    foreach ($exclude in $excludeDirs) {
        if ($path -like "*\$exclude\*" -or $path -like "*/$exclude/*" -or $path -like "*\$exclude" -or $path -like "*/$exclude") {
            return $true
        }
    }
    # Also check if any parent directory is in the exclude list
    $directory = Split-Path -Path $path -Parent
    if ($directory) {
        $dirName = Split-Path -Path $directory -Leaf
        if ($dirName -in $excludeDirs) {
            return $true
        }
    }
    return $false
}

# Function to process file content
function Process-FileContent($filePath, $content) {
    $changes = @()
    $newContent = $content
    
    # Define replacement patterns based on file type
    $patterns = @()
    
    if ($filePath -match '\.(cs|fs|fsx)$') {
        # C# and F# source files
        $patterns += @(
            @{ Pattern = "namespace\s+$OldName"; Replace = "namespace $NewName" },
            @{ Pattern = "using\s+$OldName"; Replace = "using $NewName" },
            @{ Pattern = "(\s|^|[^\.>])$OldName\."; Replace = "`$1$NewName." },  # More specific pattern to avoid file paths
            @{ Pattern = "`"$OldName`""; Replace = "`"$NewName`"" },
            @{ Pattern = "'$OldName'"; Replace = "'$NewName'" }
        )
    }
    
    if ($filePath -match '\.(csproj|fsproj|props|targets)$') {
        # Project and build files - be more careful
        $patterns += @(
            @{ Pattern = "<RootNamespace>$OldName"; Replace = "<RootNamespace>$NewName" },
            @{ Pattern = "<AssemblyName>$OldName"; Replace = "<AssemblyName>$NewName" },
            @{ Pattern = "<PackageId>$OldName"; Replace = "<PackageId>$NewName" },
            @{ Pattern = "<Product>$OldName"; Replace = "<Product>$NewName" },
            @{ Pattern = "namespace\s+$OldName"; Replace = "namespace $NewName" },
            @{ Pattern = "using\s+$OldName"; Replace = "using $NewName" },
            # For XML elements that aren't file paths
            @{ Pattern = "<$OldName([>\s])"; Replace = "<$NewName`$1" },
            @{ Pattern = "</$OldName>"; Replace = "</$NewName>" }
        )
        
        # Special handling for PackageReference - these should be converted
        $newContent = $newContent -replace "(<PackageReference\s+Include=`")$OldName([^`"]*`")", "`$1$NewName`$2"
        
        # Special handling for Using directives in project files
        $newContent = $newContent -replace "(<Using\s+Include=`")$OldName(`")", "`$1$NewName`$2"
    }
    
    if ($filePath -match '\.(json|config)$') {
        # JSON and config files
        $patterns += @(
            @{ Pattern = "`"$OldName\."; Replace = "`"$NewName." },
            @{ Pattern = "`"$OldName`""; Replace = "`"$NewName`"" }
        )
    }
    
    if ($filePath -match '\.xml$') {
        # XML files (but not project files)
        $patterns += @(
            @{ Pattern = "<$OldName([>\s])"; Replace = "<$NewName`$1" },
            @{ Pattern = "</$OldName>"; Replace = "</$NewName>" },
            @{ Pattern = "(\s)$OldName\."; Replace = "`$1$NewName." }
        )
    }
    
    # Apply replacements
    foreach ($pattern in $patterns) {
        $regex = [regex]$pattern.Pattern
        $matches = $regex.Matches($newContent)
        if ($matches.Count -gt 0) {
            $newContent = $regex.Replace($newContent, $pattern.Replace)
            $changes += "$($matches.Count) occurrences of '$($pattern.Pattern)'"
        }
    }
    
    # Special handling for assembly attributes
    $assemblyPatterns = @(
        @{ Pattern = '\[assembly:\s*AssemblyTitle\("([^"]*Orleans[^"]*)"\)\]'; Replace = '[assembly: AssemblyTitle("$1")]' },
        @{ Pattern = '\[assembly:\s*AssemblyProduct\("([^"]*Orleans[^"]*)"\)\]'; Replace = '[assembly: AssemblyProduct("$1")]' }
    )
    
    foreach ($pattern in $assemblyPatterns) {
        $newContent = $newContent -replace $pattern.Pattern, ($pattern.Replace -replace 'Orleans', $NewName)
    }
    
    return @{
        Content = $newContent
        Changes = $changes
        HasChanges = $changes.Count -gt 0
    }
}

# Process all files
$processedFiles = 0
$modifiedFiles = 0
$skippedFiles = 0

Write-Info "Starting namespace conversion from $OldName to $NewName"
Write-Info "Root path: $RootPath"
Write-Info "Dry run: $DryRun"

# First, close any Visual Studio instances that might have files locked
Write-Info "Note: Please close Visual Studio if it's open to avoid file locking issues"

Get-ChildItem -Path $RootPath -Recurse -File -Include $codeExtensions | ForEach-Object {
    $file = $_
    
    # Skip excluded directories
    if (Should-Exclude $file.FullName) {
        $skippedFiles++
        return
    }
    
    $processedFiles++
    $relativePath = $file.FullName.Substring($RootPath.Length + 1)
    
    try {
        # Skip if file is locked (common for .vs folder files)
        try {
            $fileStream = [System.IO.File]::Open($file.FullName, 'Open', 'Read', 'Read')
            $fileStream.Close()
        }
        catch {
            Write-Warning "Skipping locked file: $relativePath"
            $skippedFiles++
            return
        }
        
        $content = Get-Content -Path $file.FullName -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($content)) {
            return
        }
        
        $result = Process-FileContent $file.FullName $content
        
        if ($result.HasChanges) {
            $modifiedFiles++
            Write-Change "File: $relativePath"
            foreach ($change in $result.Changes) {
                Write-Host "  - $change" -ForegroundColor Gray
            }
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $result.Content -NoNewline -Force
            }
        }
    }
    catch {
        Write-Error "Failed to process $relativePath : $_"
    }
}

Write-Success "Processing complete!"
Write-Info "Files processed: $processedFiles"
Write-Info "Files modified: $modifiedFiles"
Write-Info "Files skipped: $skippedFiles"

# Generate summary report
if (-not $DryRun) {
    $reportPath = Join-Path $RootPath "namespace_conversion_report.txt"
    @"
Namespace Conversion Report
==========================
Date: $(Get-Date)
From: $OldName
To: $NewName
Files Processed: $processedFiles
Files Modified: $modifiedFiles

IMPORTANT: Project references to Orleans.*.csproj files are preserved.
This is intentional as the physical file names remain unchanged.
"@ | Set-Content -Path $reportPath
    Write-Info "Report saved to: $reportPath"
}

# Additional recommendations
Write-Host "`nRecommendations for maintaining your fork:" -ForegroundColor Magenta
Write-Host "1. Project references to Orleans.*.csproj files are intentionally preserved"
Write-Host "2. Physical file and directory names remain as Orleans.* for easier merging"
Write-Host "3. Only namespaces and package references are converted to Forkleans"
Write-Host "4. Run 'dotnet restore' after conversion to update package references"
Write-Host "5. Set up automated testing to verify the conversion"