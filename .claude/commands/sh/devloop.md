---
description: Restart AppHost and monitor for errors in dev loop
---

# Development Loop

Restart the AppHost and start monitoring for errors. When an error is detected, the monitoring will stop so you can investigate.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

# First restart everything
./scripts/kill-shooter-processes.sh
sleep 2
rm -f logs/*.log 2>/dev/null

# Start AppHost
cd Shooter.AppHost && nohup dotnet run > ../logs/apphost.log 2>&1 &
echo "AppHost restarted. Dashboard will be available shortly."
sleep 5

# Get dashboard URL
grep "Login to the dashboard" ../logs/apphost.log | tail -1

# Start monitoring for errors
echo "Starting error monitoring..."
cd ..
./scripts/monitor-for-errors.sh &
MONITOR_PID=$!
echo "Dev loop started. Monitoring PID: $MONITOR_PID"
echo "Monitor will block until an error is detected."

# Wait for monitor to complete
wait $MONITOR_PID
EXIT_CODE=$?
if [ $EXIT_CODE -eq 0 ]; then
    echo "ERROR DETECTED! Check logs for details."
else
    echo "Monitoring completed without errors."
fi
```