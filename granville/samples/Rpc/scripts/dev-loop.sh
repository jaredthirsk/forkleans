#!/bin/bash
# dev-loop.sh - Automated development loop for Shooter game

# Colors for output
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
RUN_DURATION=${1:-300}  # Default 5 minutes
MONITOR_PID=""
APP_PID=""

# Cleanup function
cleanup() {
    echo -e "\n${YELLOW}Shutting down...${NC}"
    
    # Kill monitoring if running
    if [ -n "$MONITOR_PID" ] && kill -0 $MONITOR_PID 2>/dev/null; then
        kill $MONITOR_PID 2>/dev/null
    fi
    
    # Kill app if running
    if [ -n "$APP_PID" ] && kill -0 $APP_PID 2>/dev/null; then
        kill -SIGINT $APP_PID 2>/dev/null
        sleep 2
    fi
    
    # Clean up any remaining processes
    ./scripts/kill-shooter-processes.sh 2>/dev/null
    
    echo -e "${GREEN}Cleanup complete${NC}"
}

# Set up trap for cleanup on exit
trap cleanup EXIT

echo -e "${BLUE}=== Shooter Development Loop ===${NC}"
echo "Run duration: ${RUN_DURATION} seconds"
echo ""

# Step 1: Clean previous run
echo -e "${YELLOW}Step 1: Cleaning previous run...${NC}"
./scripts/kill-shooter-processes.sh 2>/dev/null

# Step 2: Start the application
echo -e "${YELLOW}Step 2: Starting application...${NC}"
cd Shooter.AppHost && ./rl.sh &
APP_PID=$!
cd ..

# Wait for startup
sleep 10

# Step 3: Start monitoring in background
echo -e "${YELLOW}Step 3: Starting monitoring...${NC}"
./scripts/monitor-shooter.sh > ../logs/monitor-$(date +%Y%m%d_%H%M%S).log 2>&1 &
MONITOR_PID=$!

echo -e "${GREEN}Monitoring PID: $MONITOR_PID${NC}"
echo -e "${GREEN}Application PID: $APP_PID${NC}"

# Step 4: Run for specified duration
echo -e "${YELLOW}Step 4: Running for ${RUN_DURATION} seconds...${NC}"
echo "Press Ctrl+C to stop early"

START_TIME=$(date +%s)
while true; do
    CURRENT_TIME=$(date +%s)
    ELAPSED=$((CURRENT_TIME - START_TIME))
    
    if [ $ELAPSED -ge $RUN_DURATION ]; then
        echo -e "\n${GREEN}Run duration reached${NC}"
        break
    fi
    
    # Check if app is still running
    if ! kill -0 $APP_PID 2>/dev/null; then
        echo -e "\n${RED}Application crashed!${NC}"
        break
    fi
    
    # Show status every 30 seconds
    if [ $((ELAPSED % 30)) -eq 0 ] && [ $ELAPSED -gt 0 ]; then
        echo -e "${BLUE}[$(date '+%H:%M:%S')] Still running... ($ELAPSED/$RUN_DURATION seconds)${NC}"
        
        # Check latest monitoring output
        if [ -n "$MONITOR_PID" ] && kill -0 $MONITOR_PID 2>/dev/null; then
            LATEST=$(tail -5 ../logs/monitor-*.log 2>/dev/null | grep -E "⚠|✓" | tail -2)
            if [ -n "$LATEST" ]; then
                echo "$LATEST"
            fi
        fi
    fi
    
    sleep 1
done

# Step 5: Analyze results
echo -e "\n${YELLOW}Step 5: Analyzing results...${NC}"

# Check for bot hangs
BOT_HANGS=$(grep -c "Bot hang detected" ../logs/monitor-*.log 2>/dev/null)
if [ "$BOT_HANGS" -gt 0 ]; then
    echo -e "${RED}⚠ Bot hangs detected: $BOT_HANGS occurrences${NC}"
fi

# Check for zone transition issues
ZONE_ISSUES=$(grep -c "High zone transition rate" ../logs/monitor-*.log 2>/dev/null)
if [ "$ZONE_ISSUES" -gt 0 ]; then
    echo -e "${YELLOW}⚠ Zone transition issues: $ZONE_ISSUES occurrences${NC}"
fi

# Check for errors
ERROR_COUNT=$(grep -c "errors in recent logs" ../logs/monitor-*.log 2>/dev/null)
if [ "$ERROR_COUNT" -gt 0 ]; then
    echo -e "${RED}⚠ Errors detected in $ERROR_COUNT checks${NC}"
fi

echo -e "\n${GREEN}Development loop complete${NC}"
echo "Check logs in: ../logs/"