# Setup local NuGet feed for Granville.Rpc packages
param(
    [string]$FeedPath = "$HOME\.nuget\local-feed\Granville.Rpc"
)

Write-Host "Setting up local NuGet feed for Granville.Rpc packages..." -ForegroundColor Green

# Create feed directory
if (!(Test-Path $FeedPath)) {
    New-Item -ItemType Directory -Path $FeedPath -Force | Out-Null
}

# Copy packages
$packages = Get-ChildItem "Artifacts\Granville.Rpc\*.nupkg"
foreach ($package in $packages) {
    Write-Host "Copying $($package.Name)..." -ForegroundColor Cyan
    Copy-Item $package.FullName -Destination $FeedPath -Force
}

Write-Host "`nPackages copied to: $FeedPath" -ForegroundColor Yellow
Write-Host "`nTo use these packages, add the following to your NuGet.config:" -ForegroundColor Green
Write-Host "<add key=`"LocalGranville`" value=`"$FeedPath`" />" -ForegroundColor Gray