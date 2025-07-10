#!/usr/bin/env pwsh
# Fix the Granville.Orleans.CodeGenerator and Analyzers packages to have correct structure

param(
    [string]$Version
)

# Read version from Directory.Build.props if not provided
if (!$Version) {
    $directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
    if (Test-Path $directoryBuildProps) {
        $xml = [xml](Get-Content $directoryBuildProps)
        $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
        $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
        $Version = "$versionPrefix.$granvilleRevision"
    } else {
        Write-Error "Version parameter is required or Directory.Build.props must exist with VersionPrefix and GranvilleRevision"
        exit 1
    }
}

Write-Host "Fixing Granville.Orleans analyzer packages..." -ForegroundColor Green

# Fix CodeGenerator package
$codegenPackage = "Artifacts/Release/Granville.Orleans.CodeGenerator.$Version.nupkg"
if (Test-Path $codegenPackage) {
    Write-Host "`nFixing CodeGenerator package..." -ForegroundColor Cyan
    $tempDir = New-Item -ItemType Directory -Force -Path "temp-codegen-fix"
    
    Push-Location $tempDir
    try {
        Expand-Archive -Path "../$codegenPackage" -DestinationPath "." -Force
        
        # Check if targets files exist and rename them
        Get-ChildItem -Recurse -Filter "Microsoft.Orleans.CodeGenerator.*" | ForEach-Object {
            $newName = $_.Name -replace "Microsoft\.Orleans", "Granville.Orleans"
            $newPath = Join-Path $_.DirectoryName $newName
            Move-Item $_.FullName $newPath -Force
            Write-Host "  Renamed $($_.Name) to $newName" -ForegroundColor Gray
        }
        
        # Update nuspec
        $nuspecFile = Get-ChildItem -Filter "*.nuspec" | Select-Object -First 1
        if ($nuspecFile) {
            $content = Get-Content $nuspecFile.FullName -Raw
            $content = $content -replace 'Microsoft\.Orleans\.CodeGenerator', 'Granville.Orleans.CodeGenerator'
            $content | Set-Content $nuspecFile.FullName -NoNewline
        }
        
        # Recreate package
        Remove-Item "../$codegenPackage" -Force
        Compress-Archive -Path * -DestinationPath "../$codegenPackage" -Force
        Move-Item "../$codegenPackage.zip" "../$codegenPackage" -Force -ErrorAction SilentlyContinue
        
        Write-Host "  CodeGenerator package fixed!" -ForegroundColor Green
    }
    finally {
        Pop-Location
        Remove-Item -Recurse -Force $tempDir
    }
}

# Fix Analyzers package
$analyzersPackage = "Artifacts/Release/Granville.Orleans.Analyzers.$Version.nupkg"
if (Test-Path $analyzersPackage) {
    Write-Host "`nFixing Analyzers package..." -ForegroundColor Cyan
    $tempDir = New-Item -ItemType Directory -Force -Path "temp-analyzers-fix"
    
    Push-Location $tempDir
    try {
        Expand-Archive -Path "../$analyzersPackage" -DestinationPath "." -Force
        
        # Check if targets/props files exist and rename them
        Get-ChildItem -Recurse -Filter "Microsoft.Orleans.Analyzers.*" | ForEach-Object {
            $newName = $_.Name -replace "Microsoft\.Orleans", "Granville.Orleans"
            $newPath = Join-Path $_.DirectoryName $newName
            Move-Item $_.FullName $newPath -Force
            Write-Host "  Renamed $($_.Name) to $newName" -ForegroundColor Gray
        }
        
        # Update nuspec
        $nuspecFile = Get-ChildItem -Filter "*.nuspec" | Select-Object -First 1
        if ($nuspecFile) {
            $content = Get-Content $nuspecFile.FullName -Raw
            $content = $content -replace 'Microsoft\.Orleans\.Analyzers', 'Granville.Orleans.Analyzers'
            $content | Set-Content $nuspecFile.FullName -NoNewline
        }
        
        # Recreate package
        Remove-Item "../$analyzersPackage" -Force
        Compress-Archive -Path * -DestinationPath "../$analyzersPackage" -Force
        Move-Item "../$analyzersPackage.zip" "../$analyzersPackage" -Force -ErrorAction SilentlyContinue
        
        Write-Host "  Analyzers package fixed!" -ForegroundColor Green
    }
    finally {
        Pop-Location
        Remove-Item -Recurse -Force $tempDir
    }
}

Write-Host "`nAll analyzer packages fixed!" -ForegroundColor Green