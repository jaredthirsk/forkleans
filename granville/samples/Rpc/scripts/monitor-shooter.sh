#!/bin/bash
# monitor-shooter.sh - Real-time monitoring for Shooter game issues

# Colors for output
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
MONITOR_INTERVAL=5
BOT_HANG_THRESHOLD=10
TRANSITION_RATE_THRESHOLD=5
LOG_DIR="../logs"

echo -e "${BLUE}=== Shooter Game Monitor ===${NC}"
echo "Monitoring interval: ${MONITOR_INTERVAL}s"
echo "Bot hang threshold: ${BOT_HANG_THRESHOLD}s"
echo "Transition rate threshold: ${TRANSITION_RATE_THRESHOLD} per 100 lines"
echo ""

# Function to check bot health
check_bot_health() {
    local bot_logs="${LOG_DIR}/bot*.log"
    
    # Check if bot logs exist
    if ! ls $bot_logs 1> /dev/null 2>&1; then
        echo -e "${YELLOW}No bot logs found${NC}"
        return
    fi
    
    # Get last position update from any bot
    local last_update=$(tail -n 500 $bot_logs 2>/dev/null | grep -E "Position:|Moving to|Target position" | tail -1)
    
    if [ -n "$last_update" ]; then
        # Extract timestamp (assuming format like "2024-01-15 10:30:45")
        local timestamp=$(echo "$last_update" | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}')
        
        if [ -n "$timestamp" ]; then
            # Convert to seconds since epoch
            local last_ts=$(date -d "$timestamp" +%s 2>/dev/null)
            local current_ts=$(date +%s)
            
            if [ -n "$last_ts" ]; then
                local diff=$((current_ts - last_ts))
                
                if [ $diff -gt $BOT_HANG_THRESHOLD ]; then
                    echo -e "${RED}⚠ Bot hang detected: No updates for ${diff}s${NC}"
                    echo "  Last update: $timestamp"
                    
                    # Check for recent errors
                    local recent_errors=$(tail -n 100 $bot_logs 2>/dev/null | grep -iE "error|exception|failed|timeout" | tail -3)
                    if [ -n "$recent_errors" ]; then
                        echo -e "${RED}  Recent errors:${NC}"
                        echo "$recent_errors" | sed 's/^/    /'
                    fi
                else
                    echo -e "${GREEN}✓ Bots active (last update: ${diff}s ago)${NC}"
                fi
            fi
        fi
    else
        echo -e "${YELLOW}⚠ No recent bot position updates found${NC}"
    fi
}

# Function to check zone transitions
check_zone_transitions() {
    local silo_log="${LOG_DIR}/shooter-silo.log"
    
    if [ ! -f "$silo_log" ]; then
        echo -e "${YELLOW}Silo log not found${NC}"
        return
    fi
    
    # Count recent zone transitions
    local transition_count=$(tail -n 100 "$silo_log" 2>/dev/null | grep -c "Zone transition\|Transitioning to zone\|CheckForServerTransition")
    
    if [ $transition_count -gt $TRANSITION_RATE_THRESHOLD ]; then
        echo -e "${YELLOW}⚠ High zone transition rate: ${transition_count} in last 100 lines${NC}"
        
        # Show recent transition pattern
        local recent_transitions=$(tail -n 100 "$silo_log" 2>/dev/null | grep -E "Zone transition|Transitioning to zone" | tail -3)
        if [ -n "$recent_transitions" ]; then
            echo "  Recent transitions:"
            echo "$recent_transitions" | sed 's/^/    /'
        fi
    else
        echo -e "${GREEN}✓ Zone transitions normal (${transition_count} recent)${NC}"
    fi
    
    # Check for transition-related errors
    local transition_errors=$(tail -n 200 "$silo_log" 2>/dev/null | grep -iE "transition.*error|timer.*disposed|transition.*failed")
    if [ -n "$transition_errors" ]; then
        echo -e "${RED}⚠ Zone transition errors detected:${NC}"
        echo "$transition_errors" | tail -3 | sed 's/^/    /'
    fi
}

# Function to check for general errors
check_errors() {
    local error_count=$(tail -n 200 ${LOG_DIR}/*.log 2>/dev/null | grep -ciE "error|exception|failed|timeout")
    
    if [ $error_count -gt 0 ]; then
        echo -e "${RED}⚠ ${error_count} errors in recent logs${NC}"
        
        # Show most recent errors
        local recent_errors=$(tail -n 200 ${LOG_DIR}/*.log 2>/dev/null | grep -iE "error|exception|failed|timeout" | tail -3)
        if [ -n "$recent_errors" ]; then
            echo "  Recent errors:"
            echo "$recent_errors" | sed 's/^/    /'
        fi
    else
        echo -e "${GREEN}✓ No recent errors${NC}"
    fi
}

# Function to check SSL/certificate issues
check_ssl_issues() {
    local ssl_errors=$(tail -n 100 ${LOG_DIR}/bot*.log 2>/dev/null | grep -ciE "certificate|ssl|tls|untrusted")
    
    if [ $ssl_errors -gt 0 ]; then
        echo -e "${RED}⚠ SSL/Certificate issues detected (${ssl_errors} occurrences)${NC}"
        local recent_ssl=$(tail -n 100 ${LOG_DIR}/bot*.log 2>/dev/null | grep -iE "certificate|ssl|tls|untrusted" | tail -2)
        if [ -n "$recent_ssl" ]; then
            echo "  Recent SSL issues:"
            echo "$recent_ssl" | sed 's/^/    /'
        fi
    else
        echo -e "${GREEN}✓ No SSL issues${NC}"
    fi
}

# Function to check process health
check_processes() {
    local shooter_processes=$(pgrep -f "Shooter" | wc -l)
    
    if [ $shooter_processes -eq 0 ]; then
        echo -e "${RED}⚠ No Shooter processes running${NC}"
    else
        echo -e "${GREEN}✓ ${shooter_processes} Shooter processes running${NC}"
        
        # Check for specific components
        if pgrep -f "Shooter.Silo" > /dev/null; then
            echo -e "  ${GREEN}✓ Silo running${NC}"
        else
            echo -e "  ${YELLOW}⚠ Silo not running${NC}"
        fi
        
        if pgrep -f "Shooter.ActionServer" > /dev/null; then
            local action_count=$(pgrep -f "Shooter.ActionServer" | wc -l)
            echo -e "  ${GREEN}✓ ${action_count} ActionServer(s) running${NC}"
        else
            echo -e "  ${YELLOW}⚠ No ActionServers running${NC}"
        fi
        
        if pgrep -f "Shooter.Bot" > /dev/null; then
            local bot_count=$(pgrep -f "Shooter.Bot" | wc -l)
            echo -e "  ${GREEN}✓ ${bot_count} Bot(s) running${NC}"
        else
            echo -e "  ${YELLOW}⚠ No Bots running${NC}"
        fi
    fi
}

# Main monitoring loop
echo -e "${BLUE}Starting continuous monitoring... (Ctrl+C to stop)${NC}"
echo ""

while true; do
    echo -e "${BLUE}[$(date '+%H:%M:%S')] Checking system health...${NC}"
    
    check_processes
    echo ""
    
    check_bot_health
    echo ""
    
    check_zone_transitions
    echo ""
    
    check_ssl_issues
    echo ""
    
    check_errors
    echo ""
    
    echo -e "${BLUE}--------------------------------${NC}"
    sleep $MONITOR_INTERVAL
done