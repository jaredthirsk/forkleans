# Find-ObserverGrainId.ps1
# Quick script to locate ObserverGrainId definition and usage

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath
)

Write-Host "Searching for ObserverGrainId definition and usages..." -ForegroundColor Cyan

# Find where ObserverGrainId is defined
$definitions = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -match '(internal|public|private|protected)\s+(sealed\s+)?(class|struct)\s+ObserverGrainId') {
        [PSCustomObject]@{
            File = $_.FullName.Substring($RootPath.Length + 1)
            Line = ($content | Select-String -Pattern 'ObserverGrainId' | Select-Object -First 1).LineNumber
            Context = $matches[0]
            Accessibility = $matches[1]
        }
    }
}

if ($definitions) {
    Write-Host "`nObserverGrainId definition found:" -ForegroundColor Green
    foreach ($def in $definitions) {
        Write-Host "  File: $($def.File)" -ForegroundColor Yellow
        Write-Host "  Line: $($def.Line)"
        Write-Host "  Declaration: $($def.Context)"
        Write-Host "  Current accessibility: $($def.Accessibility)" -ForegroundColor Magenta
    }
}

# Find usages
$usages = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -match 'ObserverGrainId' -and $_ -notmatch 'class\s+ObserverGrainId') {
        $matches = ($content | Select-String -Pattern 'ObserverGrainId' -AllMatches).Matches.Count
        [PSCustomObject]@{
            File = $_.FullName.Substring($RootPath.Length + 1)
            UsageCount = $matches
        }
    }
} | Sort-Object UsageCount -Descending

if ($usages) {
    Write-Host "`nObserverGrainId usages found in $($usages.Count) files:" -ForegroundColor Green
    $usages | Select-Object -First 10 | ForEach-Object {
        Write-Host "  $($_.File) - $($_.UsageCount) usage(s)"
    }
}

# Look for the specific error location
$clientObserverFile = Get-ChildItem -Path $RootPath -Recurse -Filter "ClientObserver.cs" | Select-Object -First 1
if ($clientObserverFile) {
    Write-Host "`nChecking ClientObserver.cs specifically..." -ForegroundColor Cyan
    $content = Get-Content $clientObserverFile.FullName
    $lineNum = 0
    foreach ($line in $content) {
        $lineNum++
        if ($line -match 'ObserverGrainId' -and $lineNum -ge 10 -and $lineNum -le 20) {
            Write-Host "  Line $lineNum : $($line.Trim())" -ForegroundColor Yellow
        }
    }
}

# Suggest fixes
Write-Host "`n=== SUGGESTED FIXES ===" -ForegroundColor Magenta
Write-Host "1. Quick fix - Make ObserverGrainId public:" -ForegroundColor Green
if ($definitions) {
    foreach ($def in $definitions) {
        Write-Host "   In file: $($def.File)"
        Write-Host "   Change: '$($def.Accessibility) class ObserverGrainId'"
        Write-Host "   To:     'public class ObserverGrainId'"
    }
}

Write-Host "`n2. Better fix - Add InternalsVisibleTo:" -ForegroundColor Green
Write-Host "   Find the AssemblyInfo.cs or .csproj file for the assembly containing ObserverGrainId"
Write-Host "   Add: [assembly: InternalsVisibleTo(`"Forkleans.Core`")]"
Write-Host "   Also add any other Forkleans assemblies that need access"

Write-Host "`n3. Alternative - Check if it's a namespace issue:" -ForegroundColor Green
Write-Host "   The error might be because ClientObserver is now in 'Forkleans' namespace"
Write-Host "   but trying to access 'Orleans.ObserverGrainId'"
Write-Host "   Make sure the using statements are correct"
