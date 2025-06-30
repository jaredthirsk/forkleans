#!/bin/bash

# Build RPC projects in order
echo "Building Orleans RPC projects..."

# 1. Build Orleans.Rpc.Client
echo "Building Orleans.Rpc.Client..."
dotnet build /mnt/g/forks/orleans/src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj -c Debug

# 2. Build Orleans.Rpc.Server
echo "Building Orleans.Rpc.Server..."
dotnet build /mnt/g/forks/orleans/src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj -c Debug

# 3. Pack RPC libraries
echo "Packing RPC libraries..."
dotnet pack /mnt/g/forks/orleans/src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj -c Debug -o /mnt/g/forks/orleans/Artifacts/Debug --no-build
dotnet pack /mnt/g/forks/orleans/src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj -c Debug -o /mnt/g/forks/orleans/Artifacts/Debug --no-build

# 4. Build Shooter.Client
echo "Building Shooter.Client..."
dotnet build /mnt/g/forks/orleans/samples/Rpc/Shooter.Client/Shooter.Client.csproj -c Debug

# 5. Build Shooter.ActionServer
echo "Building Shooter.ActionServer..."
dotnet build /mnt/g/forks/orleans/samples/Rpc/Shooter.ActionServer/Shooter.ActionServer.csproj -c Debug

echo "Build complete!"