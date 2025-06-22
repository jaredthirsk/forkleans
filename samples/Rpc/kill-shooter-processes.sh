#!/bin/bash

# Script to kill all Shooter-related processes
# This includes Silo, ActionServer, Client, Bot, and AppHost processes

echo "Looking for Shooter processes..."

# Function to kill processes matching a pattern
kill_processes() {
    local pattern=$1
    local name=$2
    
    # Find PIDs using ps with full command line
    pids=$(ps aux | grep -E "$pattern" | grep -v grep | awk '{print $2}')
    
    if [ -n "$pids" ]; then
        echo "Found $name processes: $pids"
        for pid in $pids; do
            # Show the full command for confirmation
            echo "  Killing PID $pid: $(ps -p $pid -o args= 2>/dev/null || echo "Process already terminated")"
            kill -TERM $pid 2>/dev/null || true
        done
    else
        echo "No $name processes found"
    fi
}

# Kill each type of Shooter process
# Using patterns that match the DLL names when run with dotnet
kill_processes "Shooter\.Silo\.dll|dotnet.*Shooter\.Silo" "Shooter.Silo"
kill_processes "Shooter\.ActionServer\.dll|dotnet.*Shooter\.ActionServer" "Shooter.ActionServer"
kill_processes "Shooter\.Client\.dll|dotnet.*Shooter\.Client" "Shooter.Client"
kill_processes "Shooter\.Bot\.dll|dotnet.*Shooter\.Bot" "Shooter.Bot"
kill_processes "Shooter\.AppHost\.dll|dotnet.*Shooter\.AppHost" "Shooter.AppHost"

# Also check for processes by checking /proc/*/cmdline for more accuracy
echo ""
echo "Double-checking using /proc filesystem..."

for pid in $(ls /proc | grep -E '^[0-9]+$'); do
    if [ -r "/proc/$pid/cmdline" ]; then
        cmdline=$(tr '\0' ' ' < /proc/$pid/cmdline 2>/dev/null || true)
        if echo "$cmdline" | grep -qE "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)"; then
            if kill -0 $pid 2>/dev/null; then
                echo "Found additional Shooter process PID $pid: $cmdline"
                kill -TERM $pid 2>/dev/null || true
            fi
        fi
    fi
done

echo ""
echo "Cleanup complete. Waiting 2 seconds for processes to terminate..."
sleep 2

# Final check
remaining=$(ps aux | grep -E "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)" | grep -v grep | wc -l)
if [ "$remaining" -gt 0 ]; then
    echo "Warning: $remaining Shooter processes still running. Forcing kill..."
    ps aux | grep -E "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)" | grep -v grep | awk '{print $2}' | xargs -r kill -9 2>/dev/null || true
else
    echo "All Shooter processes terminated successfully"
fi