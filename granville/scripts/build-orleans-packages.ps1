# Build Orleans packages needed by RPC projects
$projects = @(
    "src\Orleans.Core.Abstractions\Orleans.Core.Abstractions.csproj",
    "src\Orleans.Serialization.Abstractions\Orleans.Serialization.Abstractions.csproj",
    "src\Orleans.CodeGenerator\Orleans.CodeGenerator.csproj",
    "src\Orleans.Sdk\Orleans.Sdk.csproj",
    "src\Orleans.Core\Orleans.Core.csproj",
    "src\Orleans.Serialization\Orleans.Serialization.csproj",
    "src\Orleans.Runtime\Orleans.Runtime.csproj",
    "src\Orleans.Server\Orleans.Server.csproj",
    "src\Orleans.Client\Orleans.Client.csproj",
    "src\Orleans.Persistence.Memory\Orleans.Persistence.Memory.csproj"
)

foreach ($project in $projects) {
    Write-Host "Building and packing $project..." -ForegroundColor Cyan
    dotnet pack $project -c Release -o local-packages
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to build $project" -ForegroundColor Red
        exit 1
    }
}

Write-Host "All Orleans packages built successfully!" -ForegroundColor Green