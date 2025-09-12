---
description: Stop AppHost and kill all Shooter components
---

# Stop All Components

Stop the AppHost and kill all Shooter processes.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

echo "Stopping AppHost and all components..."
./scripts/kill-shooter-processes.sh
echo "All Shooter processes stopped."
```