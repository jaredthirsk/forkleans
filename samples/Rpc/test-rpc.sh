#!/bin/bash

# Clean up any existing processes
./kill-shooter-processes.sh

# Clean up log files
rm -f Shooter.Silo/logs/*.log
rm -f Shooter.ActionServer/logs/*.log
rm -f Shooter.Bot/logs/*.log

echo "Starting Silo..."
cd Shooter.Silo
dotnet run -c Release > ../silo-test.log 2>&1 &
SILO_PID=$!
cd ..

echo "Waiting for Silo to start..."
sleep 5

echo "Starting ActionServer..."
cd Shooter.ActionServer
dotnet run -c Release > ../actionserver-test.log 2>&1 &
AS_PID=$!
cd ..

echo "Waiting for ActionServer to start..."
sleep 5

echo "Starting Bot..."
cd Shooter.Bot
TEST_MODE=true dotnet run -c Release > ../bot-test.log 2>&1 &
BOT_PID=$!
cd ..

echo "Waiting for Bot to connect..."
sleep 10

echo "Bot log:"
tail -50 bot-test.log | grep -E "Error|Exception|manifest|interfaces|grains"

echo ""
echo "ActionServer log:"
tail -50 actionserver-test.log | grep -E "handshake|manifest|grains"

echo ""
echo "Cleaning up..."
kill $BOT_PID $AS_PID $SILO_PID 2>/dev/null

echo "Done"