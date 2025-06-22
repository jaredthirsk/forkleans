#!/bin/bash

echo "Starting Shooter test with new packages (9.2.0.7-preview3)..."
echo

# Kill any existing processes
echo "Cleaning up any existing processes..."
pkill -f "Shooter.Silo" || true
pkill -f "Shooter.ActionServer" || true
sleep 2

# Start the Silo
echo "Starting Silo..."
cd /mnt/g/forks/orleans/samples/Rpc
dotnet run --project Shooter.Silo/Shooter.Silo.csproj > silo.log 2>&1 &
SILO_PID=$!
echo "Silo started with PID $SILO_PID"

# Wait for Silo to be ready
echo "Waiting for Silo to be ready..."
sleep 10

# Start the ActionServer
echo "Starting ActionServer..."
dotnet run --project Shooter.ActionServer/Shooter.ActionServer.csproj > actionserver.log 2>&1 &
ACTIONSERVER_PID=$!
echo "ActionServer started with PID $ACTIONSERVER_PID"

# Wait a bit for connection attempt
echo "Waiting for ActionServer to connect to Silo..."
sleep 10

# Check if ActionServer successfully registered
echo
echo "=== Checking ActionServer log for errors ==="
if grep -q "No active nodes are compatible with grain proxy_iworldmanager" actionserver.log; then
    echo "❌ ERROR: Still getting 'No active nodes are compatible' error!"
    echo
    echo "=== ActionServer Error Details ==="
    grep -A5 -B5 "No active nodes are compatible" actionserver.log
else
    if grep -q "Registering ActionServer" actionserver.log; then
        echo "✅ ActionServer started and attempting registration"
        if grep -q "Failed to register ActionServer" actionserver.log; then
            echo "❌ Registration failed with different error"
            grep -A10 "Failed to register" actionserver.log
        else
            echo "✅ No manifest provider errors detected!"
        fi
    fi
fi

echo
echo "=== Diagnostic Service Output ==="
grep -A20 "=== SERVICE DIAGNOSTICS ===" actionserver.log || echo "No diagnostic output found"

# Clean up
echo
echo "Cleaning up processes..."
kill $SILO_PID $ACTIONSERVER_PID 2>/dev/null || true

echo
echo "Test complete. Check silo.log and actionserver.log for details."
