---
description: Analyze logs for errors and system status
---

# Analyze Logs

Analyze all Shooter logs for errors, exceptions, and system health status.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

echo "Analyzing logs for errors..."
echo ""

# Check for recent errors
echo "=== Recent Errors (last 24 hours) ==="
find logs -name "*.log" -type f -mtime -1 -exec grep -l "ERROR\|Exception\|Failed" {} \; 2>/dev/null | while read -r logfile; do
    echo "$(basename "$logfile"):"
    grep -E "ERROR|Exception|Failed" "$logfile" | tail -5
    echo ""
done

# Count errors by type
echo "=== Error Summary ==="
find logs -name "*.log" -type f -exec grep -h "Exception" {} \; 2>/dev/null | sed 's/.*\(System\.[A-Za-z]*Exception\).*/\1/' | sort | uniq -c | sort -rn

# Check for unobserved task exceptions
echo ""
echo "=== Unobserved Task Exceptions ==="
grep -r "Unobserved task exception" logs --include="*.log" 2>/dev/null | wc -l | xargs echo "Total count:"

# Check system health
echo ""
echo "=== Current System Status ==="
./scripts/show-shooter-processes.sh
```