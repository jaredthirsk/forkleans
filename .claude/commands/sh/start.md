---
description: Start the AppHost if not already running
---

# Start AppHost

Start the AppHost if it's not already running. If it's already running, just show the dashboard URL.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

# Check if already running
if pgrep -f "Shooter.AppHost" > /dev/null; then
    echo "AppHost is already running."
    # Get dashboard URL from logs
    if [ -f logs/apphost.log ]; then
        grep "Login to the dashboard" logs/apphost.log | tail -1
    fi
else
    echo "Starting AppHost..."
    cd Shooter.AppHost && nohup dotnet run > ../logs/apphost.log 2>&1 &
    echo "AppHost started."
    sleep 5
    # Get dashboard URL
    grep "Login to the dashboard" ../logs/apphost.log | tail -1
fi
```