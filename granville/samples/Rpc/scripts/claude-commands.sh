#!/bin/bash
# Shooter Project Management Commands
# Usage: ./cc <command>  (cc = "claude commands")

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
APPHOST_DIR="$PROJECT_DIR/Shooter.AppHost"
LOGS_DIR="$PROJECT_DIR/logs"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

case "$1" in
    restart)
        echo -e "${YELLOW}Restarting AppHost...${NC}"
        # Kill all Shooter processes
        "$SCRIPT_DIR/kill-shooter-processes.sh"
        sleep 2
        # Clear logs
        rm -f "$LOGS_DIR"/*.log 2>/dev/null
        # Start AppHost
        cd "$APPHOST_DIR" && nohup dotnet run > "$LOGS_DIR/apphost.log" 2>&1 &
        echo -e "${GREEN}AppHost restarted. Dashboard will be available shortly.${NC}"
        sleep 5
        # Get dashboard URL
        grep "Login to the dashboard" "$LOGS_DIR/apphost.log" | tail -1
        ;;
        
    devloop)
        echo -e "${YELLOW}Starting dev loop...${NC}"
        # First restart
        "$0" restart
        sleep 5
        # Start monitoring in background
        echo -e "${YELLOW}Starting error monitoring...${NC}"
        "$SCRIPT_DIR/monitor-for-errors.sh" &
        MONITOR_PID=$!
        echo -e "${GREEN}Dev loop started. Monitoring PID: $MONITOR_PID${NC}"
        echo "Monitor will block until an error is detected."
        # Wait for monitor to complete
        wait $MONITOR_PID
        EXIT_CODE=$?
        if [ $EXIT_CODE -eq 0 ]; then
            echo -e "${RED}Error detected! Check logs for details.${NC}"
        else
            echo -e "${GREEN}Monitoring completed without errors.${NC}"
        fi
        ;;
        
    stop)
        echo -e "${YELLOW}Stopping AppHost and all components...${NC}"
        "$SCRIPT_DIR/kill-shooter-processes.sh"
        echo -e "${GREEN}All Shooter processes stopped.${NC}"
        ;;
        
    start)
        # Check if already running
        if pgrep -f "Shooter.AppHost" > /dev/null; then
            echo -e "${YELLOW}AppHost is already running.${NC}"
            # Get dashboard URL from logs
            if [ -f "$LOGS_DIR/apphost.log" ]; then
                grep "Login to the dashboard" "$LOGS_DIR/apphost.log" | tail -1
            fi
        else
            echo -e "${YELLOW}Starting AppHost...${NC}"
            cd "$APPHOST_DIR" && nohup dotnet run > "$LOGS_DIR/apphost.log" 2>&1 &
            echo -e "${GREEN}AppHost started.${NC}"
            sleep 5
            # Get dashboard URL
            grep "Login to the dashboard" "$LOGS_DIR/apphost.log" | tail -1
        fi
        ;;
        
    analyze)
        echo -e "${YELLOW}Analyzing logs for errors...${NC}"
        echo ""
        
        # Check for recent errors
        echo -e "${YELLOW}=== Recent Errors (last 24 hours) ===${NC}"
        find "$LOGS_DIR" -name "*.log" -type f -mtime -1 -exec grep -l "ERROR\|Exception\|Failed" {} \; | while read -r logfile; do
            echo -e "${GREEN}$(basename "$logfile"):${NC}"
            grep -E "ERROR|Exception|Failed" "$logfile" | tail -5
            echo ""
        done
        
        # Count errors by type
        echo -e "${YELLOW}=== Error Summary ===${NC}"
        find "$LOGS_DIR" -name "*.log" -type f -exec grep -h "Exception" {} \; | sed 's/.*\(System\.[A-Za-z]*Exception\).*/\1/' | sort | uniq -c | sort -rn
        
        # Check for unobserved task exceptions
        echo ""
        echo -e "${YELLOW}=== Unobserved Task Exceptions ===${NC}"
        grep -r "Unobserved task exception" "$LOGS_DIR" --include="*.log" | wc -l | xargs echo "Total count:"
        
        # Check system health
        echo ""
        echo -e "${YELLOW}=== Current System Status ===${NC}"
        "$SCRIPT_DIR/show-shooter-processes.sh"
        ;;
        
    *)
        echo "Shooter Project Management Commands"
        echo ""
        echo "Usage: $0 {restart|devloop|stop|start|analyze}"
        echo ""
        echo "Commands:"
        echo "  restart  - Kill all processes and restart AppHost"
        echo "  devloop  - Restart AppHost and start monitoring for errors"
        echo "  stop     - Stop AppHost and kill all components"
        echo "  start    - Start AppHost (does nothing if already running)"
        echo "  analyze  - Analyze logs for errors and system status"
        ;;
esac