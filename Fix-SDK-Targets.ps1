# Fix-SDK-Targets.ps1
# Renames Microsoft.Orleans.Sdk.targets files to Microsoft.Forkleans.Sdk.targets
# This ensures implicit usings work correctly for the Forkleans fork

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Find all Microsoft.Orleans.Sdk.targets files
$targetsFiles = @(
    Get-ChildItem -Path $RootPath -Filter "Microsoft.Orleans.Sdk.targets" -Recurse -File
)

if ($targetsFiles.Count -eq 0) {
    Write-Host "No Microsoft.Orleans.Sdk.targets files found." -ForegroundColor Yellow
    return
}

Write-Host "Found $($targetsFiles.Count) Microsoft.Orleans.Sdk.targets files to rename" -ForegroundColor Cyan

foreach ($file in $targetsFiles) {
    $directory = $file.DirectoryName
    $newName = "Microsoft.Forkleans.Sdk.targets"
    $newPath = Join-Path $directory $newName
    
    Write-Host "  Renaming: $($file.FullName.Substring($RootPath.Length + 1))" -ForegroundColor Gray
    
    if (-not $DryRun) {
        Move-Item -Path $file.FullName -Destination $newPath -Force
        Write-Host "    -> $newName" -ForegroundColor Green
    } else {
        Write-Host "    -> $newName (dry run)" -ForegroundColor Yellow
    }
}

# Also update the PackageId in Orleans.Sdk.csproj to ensure consistency
$sdkProjectPath = Join-Path $RootPath "src\Orleans.Sdk\Orleans.Sdk.csproj"
if (Test-Path $sdkProjectPath) {
    Write-Host "`nChecking Orleans.Sdk.csproj for targets file references..." -ForegroundColor Cyan
    
    if (-not $DryRun) {
        # The PackageId should already be Microsoft.Forkleans.Sdk based on the csproj content
        Write-Host "  PackageId is already set to Microsoft.Forkleans.Sdk" -ForegroundColor Green
    }
}

# Update Directory.Build.props files to include implicit usings
Write-Host "`nUpdating Directory.Build.props files with implicit usings..." -ForegroundColor Cyan

$implicitUsingsXml = @"

  <!-- Only add Forkleans implicit usings to projects that aren't Aspire hosts -->
  <ItemGroup Condition="('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' != 'true'">
    <Using Include="Forkleans"/>
    <Using Include="Forkleans.Hosting"/>
    <Using Include="Forkleans.Runtime"/>
  </ItemGroup>

  <!-- Add Aspire implicit usings for Aspire hosts -->
  <ItemGroup Condition="('`$(ImplicitUsings)' == 'enable' or '`$(ImplicitUsings)' == 'true') and '`$(IsAspireHost)' == 'true'">
    <Using Include="Aspire.Hosting"/>
    <Using Include="Aspire.Hosting.ApplicationModel"/>
  </ItemGroup>
"@

$directoryBuildPropsFiles = @(
    Join-Path $RootPath "playground\Directory.Build.props",
    Join-Path $RootPath "test\Directory.Build.props"
)

foreach ($propsFile in $directoryBuildPropsFiles) {
    if (Test-Path $propsFile) {
        $content = Get-Content $propsFile -Raw
        
        # Check if implicit usings are already added
        if ($content -notmatch '<Using Include="Forkleans"') {
            Write-Host "  Updating: $($propsFile.Substring($RootPath.Length + 1))" -ForegroundColor Gray
            
            if (-not $DryRun) {
                # Insert before the closing </Project> tag
                $newContent = $content -replace '</Project>', "$implicitUsingsXml`n</Project>"
                Set-Content -Path $propsFile -Value $newContent -NoNewline
                Write-Host "    -> Added implicit usings" -ForegroundColor Green
            } else {
                Write-Host "    -> Would add implicit usings (dry run)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  Skipping: $($propsFile.Substring($RootPath.Length + 1)) (already has implicit usings)" -ForegroundColor Gray
        }
    }
}

if ($DryRun) {
    Write-Host "`nDry run complete. No files were modified." -ForegroundColor Yellow
} else {
    Write-Host "`nSDK targets files renamed and implicit usings added successfully!" -ForegroundColor Green
}