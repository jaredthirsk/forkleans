#!/bin/bash

# Kill any existing test processes
pkill -f Orleans.Rpc.IntegrationTest || true

# Start server in background
echo "Starting server..."
dotnet run --project Orleans.Rpc.IntegrationTest.Server/Orleans.Rpc.IntegrationTest.Server.csproj > server.log 2>&1 &
SERVER_PID=$!

# Wait for server to start
sleep 3

# Run client
echo "Running client..."
timeout 10 dotnet run --project Orleans.Rpc.IntegrationTest.Client/Orleans.Rpc.IntegrationTest.Client.csproj > client.log 2>&1

# Kill server
kill $SERVER_PID 2>/dev/null || true

# Show logs
echo -e "\n=== SERVER LOG ==="
cat server.log
echo -e "\n=== CLIENT LOG ==="
cat client.log