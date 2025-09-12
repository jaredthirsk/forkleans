---
description: Restart the AppHost and all Shooter components
---

# Restart AppHost

Kill all Shooter processes and restart the AppHost with fresh logs.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

# Kill all Shooter processes
./scripts/kill-shooter-processes.sh
sleep 2

# Clear logs
rm -f logs/*.log 2>/dev/null

# Start AppHost
cd Shooter.AppHost && nohup dotnet run > ../logs/apphost.log 2>&1 &
echo "AppHost restarted. Dashboard will be available shortly."
sleep 5

# Get dashboard URL
grep "Login to the dashboard" ../logs/apphost.log | tail -1
```