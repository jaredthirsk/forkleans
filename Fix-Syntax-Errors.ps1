# Fix common syntax errors in C# files that may have been introduced by automated scripts
# Specifically fixes missing braces for common control structures

param(
    [string]$Path = ".",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

function Fix-MissingBraces {
    param(
        [string]$Content
    )
    
    $lines = $Content -split "`n"
    $fixedLines = @()
    $i = 0
    
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        $trimmedLine = $line.Trim()
        
        # Check for control structures without opening brace on the same line
        if ($trimmedLine -match '^(public|private|protected|internal|static|async|override|virtual|sealed).*\s+(class|interface|struct|enum)\s+\w+.*[^{]$' -or
            $trimmedLine -match '^(public|private|protected|internal|static|async|override|virtual|void|Task|ValueTask|\w+)\s+\w+\s*\([^)]*\)\s*$' -or
            $trimmedLine -match '^(if|while|for|foreach)\s*\(.+\)\s*$' -or
            $trimmedLine -match '^(else)\s*$' -or
            $trimmedLine -match '^(try|finally)\s*$' -or
            $trimmedLine -match '^(catch)\s*(\([^)]+\))?\s*$' -or
            $trimmedLine -match '^namespace\s+[\w.]+\s*$' -or
            $trimmedLine -match '^using\s+\([^)]+\)\s*=>\s*$') {
            
            # Check if the next line is indented (indicating a statement block without braces)
            if ($i + 1 -lt $lines.Count) {
                $nextLine = $lines[$i + 1]
                $nextTrimmed = $nextLine.Trim()
                
                # If next line is indented and not a brace, add braces
                if ($nextLine -match '^\s+' -and $nextTrimmed -ne '{' -and $nextTrimmed -ne '' -and 
                    -not ($nextTrimmed -match '^(public|private|protected|internal|static|async|override|virtual)')) {
                    
                    # Add opening brace
                    $fixedLines += $line
                    $fixedLines += $line -replace '\S.*', '{'
                    
                    # Process the block
                    $blockIndent = $nextLine -match '^(\s+)' | Out-Null
                    $blockIndentLevel = $Matches[1]
                    $j = $i + 1
                    
                    while ($j -lt $lines.Count) {
                        $blockLine = $lines[$j]
                        
                        # Check if we've left the block
                        if ($blockLine -notmatch "^$blockIndentLevel" -and $blockLine.Trim() -ne '') {
                            break
                        }
                        
                        $fixedLines += $blockLine
                        $j++
                    }
                    
                    # Add closing brace
                    $fixedLines += $line -replace '\S.*', '}'
                    $i = $j - 1
                } else {
                    $fixedLines += $line
                }
            } else {
                $fixedLines += $line
            }
        } else {
            $fixedLines += $line
        }
        
        $i++
    }
    
    return $fixedLines -join "`n"
}

function Fix-DoubleAwait {
    param(
        [string]$Content
    )
    
    # Fix "await await" pattern
    $Content = $Content -replace '\bawait\s+await\s+', 'await '
    
    return $Content
}

function Validate-BraceBalance {
    param(
        [string]$Content
    )
    
    $openBraces = ([regex]::Matches($Content, '{')).Count
    $closeBraces = ([regex]::Matches($Content, '}')).Count
    
    if ($openBraces -ne $closeBraces) {
        Write-Warning "Brace mismatch detected: $openBraces open braces, $closeBraces close braces"
        return $false
    }
    
    return $true
}

Write-Host "Scanning for files with syntax errors..." -ForegroundColor Cyan

$files = Get-ChildItem -Path $Path -Recurse -Include "*.cs" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\' -and
    $_.Name -ne "AssemblyInfo.cs"
}

$fixedFiles = 0
$checkedFiles = 0
$filesWithIssues = @()

foreach ($file in $files) {
    $checkedFiles++
    
    try {
        $content = Get-Content $file.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }
        
        $originalContent = $content
        
        # Apply fixes
        $content = Fix-MissingBraces -Content $content
        $content = Fix-DoubleAwait -Content $content
        
        # Validate
        $isValid = Validate-BraceBalance -Content $content
        
        if ($content -ne $originalContent) {
            $fixedFiles++
            Write-Host "Fixed: $($file.FullName)" -ForegroundColor Yellow
            
            if (-not $isValid) {
                $filesWithIssues += $file.FullName
            }
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline
            }
        } elseif (-not $isValid) {
            Write-Warning "Brace imbalance in: $($file.FullName)"
            $filesWithIssues += $file.FullName
        }
    }
    catch {
        Write-Error "Error processing $($file.FullName): $_"
    }
}

Write-Host "`nSummary:" -ForegroundColor Green
Write-Host "Files checked: $checkedFiles"
Write-Host "Files fixed: $fixedFiles"

if ($filesWithIssues.Count -gt 0) {
    Write-Host "`nFiles that may still have issues:" -ForegroundColor Yellow
    foreach ($file in $filesWithIssues) {
        Write-Host "  - $file" -ForegroundColor Yellow
    }
}

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Cyan
}