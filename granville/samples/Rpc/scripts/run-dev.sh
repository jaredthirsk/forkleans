#!/bin/bash

# Comprehensive development runner for Shooter sample
# Provides an interactive menu for common development tasks

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
LOG_DIR="$ROOT_DIR/logs"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m'

# Ensure log directory exists
mkdir -p "$LOG_DIR"

# Function to display the menu
show_menu() {
    clear
    echo -e "${CYAN}╔══════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║     Shooter Development Environment          ║${NC}"
    echo -e "${CYAN}╚══════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "${GREEN}Quick Actions:${NC}"
    echo "  1) Start All Components (with build)"
    echo "  2) Start All Components (no build)"
    echo "  3) Stop All Components (graceful)"
    echo "  4) Force Kill All Processes"
    echo "  5) Restart All Components"
    echo ""
    echo -e "${YELLOW}Testing:${NC}"
    echo "  6) Run Robustness Test (automated)"
    echo "  7) Run Dev Manager Test Loop"
    echo "  8) Monitor Logs (real-time)"
    echo ""
    echo -e "${MAGENTA}Diagnostics:${NC}"
    echo "  9) Show Running Processes"
    echo " 10) Check Log Files for Errors"
    echo " 11) Clear All Logs"
    echo " 12) Trigger Silo GC"
    echo ""
    echo -e "${RED}Advanced:${NC}"
    echo " 13) Start with Bot"
    echo " 14) Start Minimal (Silo + 1 ActionServer)"
    echo " 15) Tail All Logs"
    echo ""
    echo "  q) Quit"
    echo ""
}

# Function to start all components
start_all() {
    local skip_build=$1
    echo -e "${YELLOW}Starting all components...${NC}"
    
    if [ "$skip_build" != "true" ]; then
        echo "Building projects..."
        cd "$ROOT_DIR"
        dotnet build --configuration Debug
    fi
    
    # Kill any existing processes
    "$SCRIPT_DIR/kill-shooter-processes.sh" >/dev/null 2>&1 || true
    sleep 2
    
    # Start components
    cd "$ROOT_DIR"
    echo "Starting Silo..."
    nohup dotnet run --project Shooter.Silo --no-build > "$LOG_DIR/silo.log" 2>&1 &
    sleep 5
    
    for port in 7072 7073 7074 7075; do
        echo "Starting ActionServer on port $port..."
        nohup dotnet run --project Shooter.ActionServer --no-build -- --urls "https://localhost:$port" > "$LOG_DIR/actionserver-$port.log" 2>&1 &
        sleep 2
    done
    
    echo "Starting Client..."
    nohup dotnet run --project Shooter.Client --no-build > "$LOG_DIR/client.log" 2>&1 &
    
    echo -e "${GREEN}All components started!${NC}"
    echo "Client available at: http://localhost:5000"
    echo ""
    read -p "Press Enter to continue..."
}

# Function to stop gracefully
stop_graceful() {
    echo -e "${YELLOW}Sending graceful shutdown signal...${NC}"
    
    # Try to send shutdown signal to Silo
    if curl -X POST "http://localhost:7071/api/admin/shutdown?delaySeconds=2" >/dev/null 2>&1; then
        echo "Graceful shutdown initiated"
        sleep 5
    else
        echo "Could not send graceful shutdown, using force kill"
        "$SCRIPT_DIR/kill-shooter-processes.sh"
    fi
    
    echo -e "${GREEN}All components stopped${NC}"
    read -p "Press Enter to continue..."
}

# Function to monitor logs
monitor_logs() {
    echo -e "${CYAN}Monitoring logs (Ctrl+C to stop)...${NC}"
    echo ""
    
    # Use multitail if available, otherwise fall back to tail
    if command -v multitail &> /dev/null; then
        multitail "$LOG_DIR"/*.log
    else
        # Simple tail follow of all logs
        tail -f "$LOG_DIR"/*.log | while read line; do
            # Color code based on content
            if echo "$line" | grep -q "ERROR\|Exception"; then
                echo -e "${RED}$line${NC}"
            elif echo "$line" | grep -q "WARNING\|ZONE_MISMATCH"; then
                echo -e "${YELLOW}$line${NC}"
            elif echo "$line" | grep -q "INFO"; then
                echo -e "${GREEN}$line${NC}"
            else
                echo "$line"
            fi
        done
    fi
}

# Function to check for errors
check_errors() {
    echo -e "${CYAN}Checking for errors in logs...${NC}"
    echo ""
    
    for logfile in "$LOG_DIR"/*.log; do
        if [ -f "$logfile" ]; then
            filename=$(basename "$logfile")
            error_count=$(grep -c "ERROR\|Exception" "$logfile" 2>/dev/null || echo "0")
            warning_count=$(grep -c "WARNING" "$logfile" 2>/dev/null || echo "0")
            timeout_count=$(grep -c "timed out" "$logfile" 2>/dev/null || echo "0")
            
            if [ "$error_count" -gt 0 ] || [ "$timeout_count" -gt 0 ]; then
                echo -e "${RED}$filename: $error_count errors, $warning_count warnings, $timeout_count timeouts${NC}"
                
                # Show last few errors
                echo "  Recent errors:"
                grep -E "ERROR|Exception|timed out" "$logfile" | tail -3 | while read line; do
                    echo "    $line" | cut -c1-100
                done
            elif [ "$warning_count" -gt 0 ]; then
                echo -e "${YELLOW}$filename: $warning_count warnings${NC}"
            else
                echo -e "${GREEN}$filename: Clean${NC}"
            fi
        fi
    done
    
    echo ""
    read -p "Press Enter to continue..."
}

# Function to show processes
show_processes() {
    "$SCRIPT_DIR/show-shooter-processes.sh"
    echo ""
    read -p "Press Enter to continue..."
}

# Function to trigger GC
trigger_gc() {
    echo -e "${YELLOW}Triggering garbage collection on Silo...${NC}"
    
    if curl -X POST "http://localhost:7071/api/admin/gc" 2>/dev/null; then
        echo -e "${GREEN}GC triggered successfully${NC}"
    else
        echo -e "${RED}Failed to trigger GC (is Silo running?)${NC}"
    fi
    
    echo ""
    read -p "Press Enter to continue..."
}

# Main loop
while true; do
    show_menu
    read -p "Enter choice: " choice
    
    case $choice in
        1)
            start_all false
            ;;
        2)
            start_all true
            ;;
        3)
            stop_graceful
            ;;
        4)
            "$SCRIPT_DIR/kill-shooter-processes.sh"
            read -p "Press Enter to continue..."
            ;;
        5)
            stop_graceful
            sleep 2
            start_all true
            ;;
        6)
            "$SCRIPT_DIR/test-robustness.sh"
            read -p "Press Enter to continue..."
            ;;
        7)
            pwsh "$SCRIPT_DIR/dev-manager.ps1" -Action test-loop
            read -p "Press Enter to continue..."
            ;;
        8)
            monitor_logs
            ;;
        9)
            show_processes
            ;;
        10)
            check_errors
            ;;
        11)
            echo "Clearing all logs..."
            rm -f "$LOG_DIR"/*.log
            echo -e "${GREEN}Logs cleared${NC}"
            read -p "Press Enter to continue..."
            ;;
        12)
            trigger_gc
            ;;
        13)
            start_all true
            echo "Starting Bot..."
            cd "$ROOT_DIR"
            nohup dotnet run --project Shooter.Bot --no-build > "$LOG_DIR/bot.log" 2>&1 &
            echo -e "${GREEN}Bot started${NC}"
            read -p "Press Enter to continue..."
            ;;
        14)
            echo "Starting minimal setup..."
            "$SCRIPT_DIR/kill-shooter-processes.sh" >/dev/null 2>&1 || true
            cd "$ROOT_DIR"
            nohup dotnet run --project Shooter.Silo --no-build > "$LOG_DIR/silo.log" 2>&1 &
            sleep 5
            nohup dotnet run --project Shooter.ActionServer --no-build > "$LOG_DIR/actionserver.log" 2>&1 &
            sleep 2
            nohup dotnet run --project Shooter.Client --no-build > "$LOG_DIR/client.log" 2>&1 &
            echo -e "${GREEN}Minimal setup started${NC}"
            read -p "Press Enter to continue..."
            ;;
        15)
            echo -e "${CYAN}Tailing all logs (Ctrl+C to stop)...${NC}"
            tail -f "$LOG_DIR"/*.log
            ;;
        q|Q)
            echo -e "${CYAN}Goodbye!${NC}"
            exit 0
            ;;
        *)
            echo -e "${RED}Invalid choice${NC}"
            read -p "Press Enter to continue..."
            ;;
    esac
done