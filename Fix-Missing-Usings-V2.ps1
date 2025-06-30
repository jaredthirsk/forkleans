# Fix missing using statements in C# and F# files after Orleans to Forkleans conversion
# This version adds using statements at the file level, before any namespace declarations

param(
    [string]$Path = ".",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

function Add-MissingUsings {
    param(
        [string]$Content,
        [string]$FileExtension
    )
    
    # Check if this is a C# or F# file
    $isSharp = $FileExtension -eq '.cs'
    $isFSharp = $FileExtension -match '\.fs$|\.fsi$|\.fsx$'
    
    if (-not $isSharp -and -not $isFSharp) {
        return $Content
    }
    
    # Check if Forkleans namespace is already imported
    if ($isSharp) {
        if ($Content -match '^\s*using\s+Forkleans\s*;' -or $Content -match '^\s*global\s+using\s+Forkleans\s*;') {
            return $Content
        }
    } elseif ($isFSharp) {
        if ($Content -match '^\s*open\s+Forkleans\s*$') {
            return $Content
        }
    }
    
    # Check if the file contains Orleans/Forkleans types that need the using statement
    $needsUsing = $false
    
    # Common Orleans/Forkleans types
    $commonTypes = @(
        'Grain', 'IGrain', 'IGrainFactory', 'GrainReference',
        'SiloAddress', 'IGrainObserver', 'GrainId',
        'StorageProvider', 'Serializable', 'GenerateSerializer',
        'Id', 'Immutable', 'Alias', 'Reentrant',
        'IGrainWithGuidKey', 'IGrainWithIntegerKey', 'IGrainWithStringKey',
        'IGrainWithIntegerCompoundKey', 'IGrainWithGuidCompoundKey',
        'ISiloBuilder', 'IClusterClient', 'IClientBuilder',
        'GrainCancellationToken', 'ImplicitStreamSubscription',
        'RequestContext', 'IManagementGrain', 'OutsideRuntimeClient',
        'IInternalGrainFactory', 'IInternalClusterClient',
        'GrainType', 'IGrainBase', 'DeactivationReason',
        'AlwaysInterleave', 'MayInterleave'
    )
    
    foreach ($type in $commonTypes) {
        if ($Content -match "\b$type\b" -and $Content -notmatch "\bForkleans\.$type\b") {
            $needsUsing = $true
            break
        }
    }
    
    # Also check for specific attributes and base classes
    if ($Content -match '\[\s*(StorageProvider|Serializable|GenerateSerializer|Id|Immutable|Alias|Reentrant|ImplicitStreamSubscription|AlwaysInterleave|MayInterleave|GrainType)\s*[\(\]]' -or
        $Content -match ':\s*Grain[\s,<\[]' -or
        $Content -match ':\s*Grain<' -or
        $Content -match 'Task<IGrain' -or
        $Content -match 'interface\s+\w+\s*:\s*IGrain') {
        $needsUsing = $true
    }
    
    if (-not $needsUsing) {
        return $Content
    }
    
    if ($isSharp) {
        # For C# files, we need to add the using statement at the file level,
        # before any namespace declarations
        
        # Find the position to insert the using statement
        # Look for the last using statement or the first namespace/class/interface
        $lines = $Content -split "`n"
        $insertIndex = -1
        $lastUsingIndex = -1
        $firstCodeIndex = -1
        
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            
            # Skip empty lines and comments at the beginning
            if ($line -match '^\s*$' -or $line -match '^\s*//' -or $line -match '^\s*/\*') {
                continue
            }
            
            # Check for using statements
            if ($line -match '^\s*using\s+' -and $line -notmatch '^\s*using\s*\(') {
                $lastUsingIndex = $i
            }
            # Check for namespace, class, interface, or other code
            elseif ($line -match '^\s*(namespace|class|interface|struct|enum|delegate|public|private|protected|internal|\[)') {
                $firstCodeIndex = $i
                break
            }
        }
        
        # Determine where to insert
        if ($lastUsingIndex -ge 0) {
            # Insert after the last using statement
            $insertIndex = $lastUsingIndex + 1
        } elseif ($firstCodeIndex -ge 0) {
            # Insert before the first code element
            $insertIndex = $firstCodeIndex
        } else {
            # Insert at the beginning of the file
            $insertIndex = 0
        }
        
        # Insert the using statement
        $newLines = @()
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($i -eq $insertIndex) {
                # Add the using statement
                if ($lastUsingIndex -ge 0) {
                    # Add after other using statements
                    $newLines += $lines[$i]
                    $newLines += "using Forkleans;"
                } else {
                    # Add before first code element
                    $newLines += "using Forkleans;"
                    $newLines += ""  # Add a blank line after
                    $newLines += $lines[$i]
                }
            } else {
                $newLines += $lines[$i]
            }
        }
        
        # Check if we need additional using statements
        $additionalUsings = @()
        if ($Content -match '\bISiloBuilder\b' -and $Content -notmatch 'using\s+Forkleans\.Hosting') {
            $additionalUsings += "using Forkleans.Hosting;"
        }
        if ($Content -match '\b(RequestContext|IManagementGrain)\b' -and $Content -notmatch 'using\s+Forkleans\.Runtime') {
            $additionalUsings += "using Forkleans.Runtime;"
        }
        
        # Add additional using statements after the main one
        if ($additionalUsings.Count -gt 0) {
            $orleansUsingIndex = -1
            for ($i = 0; $i -lt $newLines.Count; $i++) {
                if ($newLines[$i] -eq "using Forkleans;") {
                    $orleansUsingIndex = $i
                    break
                }
            }
            
            if ($orleansUsingIndex -ge 0) {
                $finalLines = @()
                for ($i = 0; $i -lt $newLines.Count; $i++) {
                    $finalLines += $newLines[$i]
                    if ($i -eq $orleansUsingIndex) {
                        foreach ($using in $additionalUsings) {
                            $finalLines += $using
                        }
                    }
                }
                $newLines = $finalLines
            }
        }
        
        return $newLines -join "`n"
    }
    elseif ($isFSharp) {
        # For F# files, add open statement after module/namespace declaration
        $lines = $Content -split "`n"
        $insertIndex = -1
        
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*(namespace|module)\s+') {
                # Found namespace or module declaration
                # Look for the next line that's not empty or a comment
                for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                    if ($lines[$j] -notmatch '^\s*$' -and $lines[$j] -notmatch '^\s*//') {
                        if ($lines[$j] -match '^\s*open\s+') {
                            # Insert after the last open statement
                            while ($j + 1 -lt $lines.Count -and $lines[$j + 1] -match '^\s*open\s+') {
                                $j++
                            }
                            $insertIndex = $j + 1
                        } else {
                            # No open statements, insert here
                            $insertIndex = $j
                        }
                        break
                    }
                }
                break
            }
        }
        
        if ($insertIndex -lt 0) {
            # No namespace/module found, add at the beginning
            $insertIndex = 0
        }
        
        # Insert the open statement
        $newLines = @()
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($i -eq $insertIndex) {
                $newLines += "open Forkleans"
                if ($lines[$i] -notmatch '^\s*$') {
                    $newLines += ""  # Add blank line if needed
                }
            }
            $newLines += $lines[$i]
        }
        
        return $newLines -join "`n"
    }
    
    return $Content
}

Write-Host "Scanning for files with missing using statements..." -ForegroundColor Cyan

# Get all C# and F# files
$files = Get-ChildItem -Path $Path -Recurse -Include "*.cs", "*.fs", "*.fsi", "*.fsx" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\' -and
    $_.Name -ne "AssemblyInfo.cs" -and
    $_.Name -ne "AssemblyInfo.fs"
}

$fixedFiles = 0
$checkedFiles = 0

foreach ($file in $files) {
    $checkedFiles++
    
    try {
        $content = Get-Content $file.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }
        
        $originalContent = $content
        $content = Add-MissingUsings -Content $content -FileExtension $file.Extension
        
        if ($content -ne $originalContent) {
            $fixedFiles++
            Write-Host "Fixing: $($file.FullName)" -ForegroundColor Yellow
            
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
Write-Host "Files checked: $checkedFiles"
Write-Host "Files fixed: $fixedFiles"

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Cyan
}