#!/bin/bash

# Script to show all Shooter-related processes with their working directories
# This includes Silo, ActionServer, Client, Bot, and AppHost processes

echo "Shooter processes currently running:"
echo "===================================="
echo ""

found_any=false

# Check each numeric directory in /proc
for pid in $(ls /proc | grep -E '^[0-9]+$'); do
    if [ -r "/proc/$pid/cmdline" ] && [ -r "/proc/$pid/cwd" ]; then
        cmdline=$(tr '\0' ' ' < /proc/$pid/cmdline 2>/dev/null || true)
        
        # Check if this is a Shooter process (including those launched by AppHost)
        if echo "$cmdline" | grep -qE "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)" || echo "$cmdline" | grep -qE "dotnet run.*--project.*Shooter\.(Silo|ActionServer|Client|Bot)"; then
            # Get the working directory
            cwd=$(readlink /proc/$pid/cwd 2>/dev/null || echo "Unknown")
            
            # Get process start time and memory usage
            if [ -r "/proc/$pid/stat" ]; then
                # Extract RSS (resident set size) in pages
                rss=$(awk '{print $24}' /proc/$pid/stat 2>/dev/null || echo "0")
                # Convert pages to MB (assuming 4KB pages)
                mem_mb=$((rss * 4 / 1024))
            else
                mem_mb="Unknown"
            fi
            
            # Get the specific Shooter component
            component=$(echo "$cmdline" | grep -oE "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)" | head -1)
            # If not found, check for dotnet run pattern
            if [ -z "$component" ]; then
                component=$(echo "$cmdline" | grep -oE "Shooter\.(Silo|ActionServer|Client|Bot)" | head -1)
                if [ -n "$component" ]; then
                    component="$component (dotnet run)"
                fi
            fi
            
            echo "PID: $pid"
            echo "Component: $component"
            echo "Working Dir: $cwd"
            echo "Memory: ${mem_mb} MB"
            echo "Command: $cmdline" | cut -c1-100
            echo "---"
            found_any=true
        fi
    fi
done

if [ "$found_any" = false ]; then
    echo "No Shooter processes found running"
fi

echo ""
echo "To kill all Shooter processes, run: ./kill-shooter-processes.sh"