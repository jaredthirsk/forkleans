#!/bin/bash

# Robust gameplay testing script for Shooter sample
# Tests for common issues like timeouts, zone transitions, and connection problems

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
LOG_DIR="$ROOT_DIR/logs"
TEST_LOG="$LOG_DIR/robustness-test.log"
ITERATION=0
MAX_ITERATIONS=5
SUCCESS_COUNT=0
ISSUES_FOUND=()

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Ensure log directory exists
mkdir -p "$LOG_DIR"

echo -e "${CYAN}=== Shooter Robustness Test ===${NC}" | tee "$TEST_LOG"
echo "Starting at $(date)" | tee -a "$TEST_LOG"

# Function to kill all Shooter processes
cleanup_processes() {
    echo -e "${YELLOW}Cleaning up processes...${NC}"
    "$SCRIPT_DIR/kill-shooter-processes.sh" >/dev/null 2>&1 || true
    sleep 2
}

# Function to start all components
start_components() {
    echo -e "${YELLOW}Starting components...${NC}"
    
    # Start Silo
    echo "  Starting Silo..."
    cd "$ROOT_DIR"
    nohup dotnet run --project Shooter.Silo --no-build > "$LOG_DIR/silo-test.log" 2>&1 &
    SILO_PID=$!
    sleep 5
    
    # Start ActionServers
    for port in 7072 7073 7074; do
        echo "  Starting ActionServer on port $port..."
        nohup dotnet run --project Shooter.ActionServer --no-build -- --urls "https://localhost:$port" > "$LOG_DIR/actionserver-$port-test.log" 2>&1 &
        sleep 2
    done
    
    # Start Client
    echo "  Starting Client..."
    nohup dotnet run --project Shooter.Client --no-build > "$LOG_DIR/client-test.log" 2>&1 &
    CLIENT_PID=$!
    sleep 3
    
    # Start Bot
    echo "  Starting Bot..."
    nohup dotnet run --project Shooter.Bot --no-build > "$LOG_DIR/bot-test.log" 2>&1 &
    BOT_PID=$!
    
    echo -e "${GREEN}All components started${NC}"
}

# Function to check for issues in logs
check_for_issues() {
    local duration=$1
    local issues=()
    
    echo "Monitoring for $duration seconds..."
    
    for ((i=0; i<duration; i+=5)); do
        # Check if processes are still running
        if ! ps -p $SILO_PID > /dev/null 2>&1; then
            issues+=("SILO_CRASHED")
            echo -e "${RED}Silo process crashed!${NC}"
        fi
        
        # Check for timeout errors
        if grep -q "timed out after" "$LOG_DIR"/*-test.log 2>/dev/null; then
            local timeout_count=$(grep -c "timed out after" "$LOG_DIR"/*-test.log 2>/dev/null | awk -F: '{sum+=$2} END {print sum}')
            if [ "$timeout_count" -gt 0 ]; then
                issues+=("TIMEOUT_ERRORS:$timeout_count")
                echo -e "${YELLOW}Found $timeout_count timeout errors${NC}"
            fi
        fi
        
        # Check for zone mismatch
        if grep -q "ZONE_MISMATCH" "$LOG_DIR"/*-test.log 2>/dev/null; then
            local zone_count=$(grep -c "ZONE_MISMATCH" "$LOG_DIR"/*-test.log 2>/dev/null | awk -F: '{sum+=$2} END {print sum}')
            if [ "$zone_count" -gt 0 ]; then
                issues+=("ZONE_MISMATCH:$zone_count")
                echo -e "${YELLOW}Found $zone_count zone mismatch warnings${NC}"
            fi
        fi
        
        # Check for exceptions
        if grep -q "Exception:" "$LOG_DIR"/*-test.log 2>/dev/null; then
            local exception_count=$(grep -c "Exception:" "$LOG_DIR"/*-test.log 2>/dev/null | awk -F: '{sum+=$2} END {print sum}')
            if [ "$exception_count" -gt 5 ]; then  # Allow some exceptions
                issues+=("EXCESSIVE_EXCEPTIONS:$exception_count")
                echo -e "${RED}Found $exception_count exceptions (threshold: 5)${NC}"
            fi
        fi
        
        # Check for stuck transitions
        if grep -q "Stuck in transitioning state" "$LOG_DIR"/*-test.log 2>/dev/null; then
            issues+=("STUCK_TRANSITION")
            echo -e "${RED}Found stuck transition!${NC}"
        fi
        
        sleep 5
    done
    
    # Return issues
    if [ ${#issues[@]} -eq 0 ]; then
        return 0
    else
        ISSUES_FOUND+=("Iteration $ITERATION: ${issues[*]}")
        return 1
    fi
}

# Function to collect diagnostic info
collect_diagnostics() {
    echo -e "${YELLOW}Collecting diagnostics...${NC}"
    
    # Get process information
    echo "=== Active Processes ===" >> "$TEST_LOG"
    ps aux | grep -E "Shooter\." | grep -v grep >> "$TEST_LOG" 2>/dev/null || true
    
    # Get recent errors from logs
    echo "=== Recent Errors ===" >> "$TEST_LOG"
    grep -E "ERROR|Exception|timed out" "$LOG_DIR"/*-test.log 2>/dev/null | tail -20 >> "$TEST_LOG" || true
    
    # Get memory usage
    echo "=== Memory Usage ===" >> "$TEST_LOG"
    free -h >> "$TEST_LOG"
}

# Main test loop
for ((ITERATION=1; ITERATION<=MAX_ITERATIONS; ITERATION++)); do
    echo -e "\n${CYAN}=== Test Iteration $ITERATION/$MAX_ITERATIONS ===${NC}" | tee -a "$TEST_LOG"
    echo "Starting iteration at $(date)" | tee -a "$TEST_LOG"
    
    # Clean up from previous iteration
    cleanup_processes
    rm -f "$LOG_DIR"/*-test.log
    
    # Start components
    start_components
    
    # Let everything stabilize
    echo "Waiting for stabilization..."
    sleep 10
    
    # Monitor for issues
    if check_for_issues 60; then
        SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
        echo -e "${GREEN}Iteration $ITERATION completed successfully${NC}" | tee -a "$TEST_LOG"
    else
        echo -e "${RED}Iteration $ITERATION failed with issues${NC}" | tee -a "$TEST_LOG"
        collect_diagnostics
    fi
    
    # Clean up
    cleanup_processes
done

# Final summary
echo -e "\n${CYAN}=== Test Summary ===${NC}" | tee -a "$TEST_LOG"
echo "Success rate: $SUCCESS_COUNT/$MAX_ITERATIONS ($((SUCCESS_COUNT * 100 / MAX_ITERATIONS))%)" | tee -a "$TEST_LOG"

if [ ${#ISSUES_FOUND[@]} -gt 0 ]; then
    echo -e "${YELLOW}Issues found:${NC}" | tee -a "$TEST_LOG"
    for issue in "${ISSUES_FOUND[@]}"; do
        echo "  - $issue" | tee -a "$TEST_LOG"
    done
fi

echo "Test completed at $(date)" | tee -a "$TEST_LOG"

# Exit with appropriate code
if [ $SUCCESS_COUNT -eq $MAX_ITERATIONS ]; then
    echo -e "${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}Some tests failed. Check $TEST_LOG for details.${NC}"
    exit 1
fi