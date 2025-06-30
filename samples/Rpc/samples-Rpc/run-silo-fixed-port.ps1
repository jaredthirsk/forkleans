# Run the Orleans silo with fixed ports
cd Shooter.Silo
$env:ASPNETCORE_URLS="http://localhost:7071"
dotnet run --urls "http://localhost:7071"