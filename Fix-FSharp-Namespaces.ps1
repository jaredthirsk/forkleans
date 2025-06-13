# Fix F# specific namespace issues after Orleans to Forkleans conversion
# F# uses 'open' instead of 'using' and has different syntax

param(
    [string]$Path = ".",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

function Fix-FSharpNamespaces {
    param(
        [string]$Content
    )
    
    # Replace Orleans namespace references in F# files
    $patterns = @(
        # Open statements
        @{ Pattern = '\bopen\s+Orleans\b'; Replace = 'open Forkleans' },
        @{ Pattern = '\bopen\s+Orleans\.'; Replace = 'open Forkleans.' },
        
        # Fully qualified names
        @{ Pattern = '\bOrleans\.'; Replace = 'Forkleans.' },
        
        # Attributes (F# attribute syntax)
        @{ Pattern = '\[<Orleans\.'; Replace = '[<Forkleans.' },
        
        # Type annotations
        @{ Pattern = ':\s*Orleans\.'; Replace = ': Forkleans.' },
        
        # Module declarations
        @{ Pattern = 'module\s+Orleans\b'; Replace = 'module Forkleans' },
        
        # Namespace declarations
        @{ Pattern = 'namespace\s+Orleans\b'; Replace = 'namespace Forkleans' }
    )
    
    $newContent = $Content
    foreach ($pattern in $patterns) {
        $newContent = $newContent -replace $pattern.Pattern, $pattern.Replace
    }
    
    return $newContent
}

function Fix-FSharpProjectFiles {
    param(
        [string]$Content
    )
    
    # Fix F# project file references
    $patterns = @(
        # Package references
        @{ Pattern = '<PackageReference\s+Include="Orleans'; Replace = '<PackageReference Include="Forkleans' },
        
        # Assembly references
        @{ Pattern = '<Reference\s+Include="Orleans'; Replace = '<Reference Include="Forkleans' },
        
        # Import statements
        @{ Pattern = '<Import\s+Project="[^"]*Orleans'; Replace = '<Import Project="$1Forkleans' }
    )
    
    $newContent = $Content
    foreach ($pattern in $patterns) {
        $newContent = $newContent -replace $pattern.Pattern, $pattern.Replace
    }
    
    return $newContent
}

Write-Host "Scanning for F# files to fix..." -ForegroundColor Cyan

# Process F# source files
$fsharpFiles = Get-ChildItem -Path $Path -Recurse -Include "*.fs", "*.fsi", "*.fsx" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\' -and
    $_.Name -ne "AssemblyInfo.fs"
}

$fixedSourceFiles = 0
foreach ($file in $fsharpFiles) {
    try {
        $content = Get-Content $file.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }
        
        $originalContent = $content
        $content = Fix-FSharpNamespaces -Content $content
        
        if ($content -ne $originalContent) {
            $fixedSourceFiles++
            Write-Host "Fixed F# source: $($file.FullName)" -ForegroundColor Yellow
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline
            }
        }
    }
    catch {
        Write-Error "Error processing $($file.FullName): $_"
    }
}

# Process F# project files
$fsprojFiles = Get-ChildItem -Path $Path -Recurse -Include "*.fsproj" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\'
}

$fixedProjectFiles = 0
foreach ($file in $fsprojFiles) {
    try {
        $content = Get-Content $file.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }
        
        $originalContent = $content
        $content = Fix-FSharpProjectFiles -Content $content
        
        if ($content -ne $originalContent) {
            $fixedProjectFiles++
            Write-Host "Fixed F# project: $($file.FullName)" -ForegroundColor Yellow
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline
            }
        }
    }
    catch {
        Write-Error "Error processing $($file.FullName): $_"
    }
}

Write-Host "`nSummary:" -ForegroundColor Green
Write-Host "F# source files fixed: $fixedSourceFiles"
Write-Host "F# project files fixed: $fixedProjectFiles"

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Cyan
}