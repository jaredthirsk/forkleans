# Fix Directory.Build.targets and Directory.Build.props files
# These files reference code generator paths that should not be converted

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

Write-Info "Fixing Directory.Build files in: $RootPath"
Write-Info "Dry run: $DryRun"

$filesToFix = @("Directory.Build.targets", "Directory.Build.props")
$fixedFiles = 0

foreach ($fileName in $filesToFix) {
    $filePath = Join-Path $RootPath $fileName
    
    if (Test-Path $filePath) {
        Write-Info "Processing: $fileName"
        
        try {
            $content = Get-Content -Path $filePath -Raw
            $originalContent = $content
            
            # Fix references to Forkleans.CodeGenerator back to Orleans.CodeGenerator
            $content = $content -replace 'src/Forkleans\.CodeGenerator', 'src/Orleans.CodeGenerator'
            $content = $content -replace 'src\\Forkleans\.CodeGenerator', 'src\Orleans.CodeGenerator'
            
            # Fix references to Forkleans.Sdk back to Orleans.Sdk
            $content = $content -replace 'src/Forkleans\.Sdk', 'src/Orleans.Sdk'
            $content = $content -replace 'src\\Forkleans\.Sdk', 'src\Orleans.Sdk'
            
            # Fix references to Forkleans.Analyzers back to Orleans.Analyzers
            $content = $content -replace 'src/Forkleans\.Analyzers', 'src/Orleans.Analyzers'
            $content = $content -replace 'src\\Forkleans\.Analyzers', 'src\Orleans.Analyzers'
            
            # Fix build paths
            $content = $content -replace 'build/Microsoft\.Forkleans\.', 'build/Microsoft.Orleans.'
            $content = $content -replace 'build\\Microsoft\.Forkleans\.', 'build\Microsoft.Orleans.'
            
            # Fix project file names
            $content = $content -replace 'Forkleans\.(CodeGenerator|Analyzers|Sdk)\.csproj', 'Orleans.$1.csproj'
            
            if ($content -ne $originalContent) {
                $fixedFiles++
                Write-Change "Fixed references in $fileName"
                
                if (-not $DryRun) {
                    Set-Content -Path $filePath -Value $content -NoNewline -Force
                }
            }
        }
        catch {
            Write-Error "Failed to process $fileName : $_"
        }
    }
}

# Also check for Directory.Build.targets in subdirectories
Get-ChildItem -Path $RootPath -Recurse -Include "Directory.Build.targets", "Directory.Build.props" | ForEach-Object {
    $file = $_
    $relativePath = $file.FullName.Substring($RootPath.Length + 1)
    
    if ($relativePath -eq "Directory.Build.targets" -or $relativePath -eq "Directory.Build.props") {
        # Already processed
        return
    }
    
    try {
        $content = Get-Content -Path $file.FullName -Raw
        $originalContent = $content
        
        # Same fixes as above
        $content = $content -replace 'Forkleans\.(CodeGenerator|Analyzers|Sdk)', 'Orleans.$1'
        $content = $content -replace 'Microsoft\.Forkleans\.', 'Microsoft.Orleans.'
        
        if ($content -ne $originalContent) {
            $fixedFiles++
            Write-Change "Fixed references in $relativePath"
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline -Force
            }
        }
    }
    catch {
        Write-Error "Failed to process $relativePath : $_"
    }
}

Write-Success "Fixed $fixedFiles Directory.Build file(s)"