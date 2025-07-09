#!/usr/bin/env pwsh
# Repackage Microsoft.Orleans analyzer packages as Granville.Orleans packages

param(
    [string]$Version = "9.1.2.65"
)

Write-Host "Repackaging analyzer packages as Granville.Orleans..." -ForegroundColor Green

# Create temp directory
$tempDir = Join-Path $PSScriptRoot "temp-repackage"
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

$analyzerPackages = @(
    @{
        Original = "Orleans.Analyzers"
        New = "Granville.Orleans.Analyzers"
    },
    @{
        Original = "Orleans.CodeGenerator"
        New = "Granville.Orleans.CodeGenerator"
    }
)

foreach ($package in $analyzerPackages) {
    $projectPath = "src/$($package.Original)/$($package.Original).csproj"
    
    Write-Host "`nRepackaging $($package.Original) as $($package.New)..." -ForegroundColor Cyan
    
    # Pack with the new name
    dotnet pack $projectPath -c Release -o $tempDir --no-build `
        -p:PackageId="$($package.New)" `
        -p:PackageVersion="$Version" `
        -p:BuildAsGranville=true
        
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to repackage $($package.Original)"
        continue
    }
    
    # Extract, fix metadata, and repack
    $nupkgPath = Join-Path $tempDir "$($package.New).$Version.nupkg"
    if (Test-Path $nupkgPath) {
        $extractPath = Join-Path $tempDir "$($package.New)-extract"
        
        # Extract package
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkgPath, $extractPath)
        
        # Fix nuspec file
        $nuspecPath = Get-ChildItem -Path $extractPath -Filter "*.nuspec" | Select-Object -First 1
        if ($nuspecPath) {
            $content = Get-Content $nuspecPath.FullName -Raw
            
            # Fix dependencies from Microsoft.Orleans to Granville.Orleans
            $content = $content -replace 'id="Microsoft\.Orleans\.([^"]+)"', 'id="Granville.Orleans.$1"'
            
            # Ensure the package ID is correct
            $content = $content -replace "<id>$($package.Original)</id>", "<id>$($package.New)</id>"
            
            Set-Content -Path $nuspecPath.FullName -Value $content -NoNewline
        }
        
        # Repack
        Remove-Item $nupkgPath -Force
        $files = Get-ChildItem -Path $extractPath -Recurse | Where-Object { !$_.PSIsContainer }
        $archive = [System.IO.Compression.ZipFile]::Open($nupkgPath, 'Create')
        foreach ($file in $files) {
            $relativePath = $file.FullName.Substring($extractPath.Length + 1)
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relativePath) | Out-Null
        }
        $archive.Dispose()
        
        # Copy to final location
        Copy-Item $nupkgPath "Artifacts/Release/" -Force
        Write-Host "  Created: $($package.New).$Version.nupkg" -ForegroundColor Green
        
        # Clean up
        Remove-Item $extractPath -Recurse -Force
    }
}

# Clean up temp directory
Remove-Item $tempDir -Recurse -Force

Write-Host "`nAnalyzer packages repackaged successfully!" -ForegroundColor Green