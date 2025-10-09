#!/bin/bash
#
# AI Development Loop for Shooter - Bash Version
# Uses rl.sh directly to avoid PowerShell I/O issues
#

MAX_ITERATIONS=10
RUN_DURATION=60

# Session setup
SESSION_ID=$(date +%Y%m%d-%H%M%S)
WORK_DIR="ai-dev-loop/$SESSION_ID"
mkdir -p "$WORK_DIR"

# Statistics
ITERATION=0
ERRORS_FOUND=0
FIXES_APPLIED=0
SUCCESSFUL_RUNS=0
START_TIME=$(date)

echo "=== AI Development Loop ==="
echo "Session: $SESSION_ID"
echo "Working directory: $WORK_DIR"
echo "Max iterations: $MAX_ITERATIONS"
echo "Run duration: $RUN_DURATION seconds"
echo ""

function write_state() {
    local state="$1"
    local message="$2"

    echo "[$state] $message"

    # Write JSON state
    cat > "$WORK_DIR/current-state.json" <<EOF
{
  "Iteration": $ITERATION,
  "ErrorsFound": $ERRORS_FOUND,
  "SuccessfulRuns": $SUCCESSFUL_RUNS,
  "State": "$state",
  "FixesApplied": $FIXES_APPLIED,
  "Message": "$message",
  "LastUpdate": "$(date -Iseconds)",
  "StartTime": "$START_TIME"
}
EOF
}

function start_shooter() {
    write_state "Starting" "Launching Shooter via rl.sh..."

    # Kill any existing processes
    ./granville/samples/Rpc/scripts/kill-shooter-processes.sh || true
    sleep 2

    # Start using rl.sh in the AppHost directory
    cd granville/samples/Rpc/Shooter.AppHost
    ./rl.sh > "../../../$WORK_DIR/apphost-$ITERATION.log" 2> "../../../$WORK_DIR/apphost-$ITERATION-err.log" &
    APPHOST_PID=$!
    cd - > /dev/null

    # Wait for services to start
    write_state "Running" "Aspire AppHost started via rl.sh. Monitoring for issues..."
    sleep 15

    echo $APPHOST_PID
}

function monitor_for_errors() {
    local apphost_pid="$1"
    local duration="$2"

    echo "Monitoring for $duration seconds..."

    local start_time=$(date +%s)
    local end_time=$((start_time + duration))

    while [ $(date +%s) -lt $end_time ]; do
        # Check if AppHost process crashed
        if ! kill -0 $apphost_pid 2>/dev/null; then
            wait $apphost_pid
            local exit_code=$?
            if [ $exit_code -ne 0 ]; then
                echo "AppHost crashed with exit code $exit_code" > "$WORK_DIR/last-error.txt"
                return 1
            fi
        fi

        # Check for errors in logs
        if [ -f "$WORK_DIR/apphost-$ITERATION-err.log" ]; then
            if grep -q -E "(Unhandled exception|FATAL|Exception:|ERROR|Connection refused|Timeout waiting|Deadlock detected)" "$WORK_DIR/apphost-$ITERATION-err.log"; then
                local error=$(grep -E "(Unhandled exception|FATAL|Exception:|ERROR|Connection refused)" "$WORK_DIR/apphost-$ITERATION-err.log" | head -1)
                echo "AppHost: $error" > "$WORK_DIR/last-error.txt"
                tail -200 "$WORK_DIR/apphost-$ITERATION-err.log" > "$WORK_DIR/error-context.log"
                return 1
            fi
        fi

        echo -n "."
        sleep 2
    done

    echo ""
    return 0
}

function wait_for_fix() {
    write_state "Investigating" "Error captured. Ready for AI analysis."

    # Create instructions for AI
    cat > "$WORK_DIR/ai-instructions.txt" <<EOF
=== AI DEBUGGING INSTRUCTIONS ===

An error has been detected in iteration $ITERATION:
$(cat "$WORK_DIR/last-error.txt")

Files available for analysis:
- Error details: $WORK_DIR/last-error.txt
- Full context (last 200 lines): $WORK_DIR/error-context.log
- All logs: $WORK_DIR/*.log

To fix this issue:
1. Read the error file: Read $WORK_DIR/last-error.txt
2. Read the context: Read $WORK_DIR/error-context.log
3. Analyze the specific component logs if needed
4. Identify the root cause
5. Apply fixes to the source code
6. Signal completion by writing "FIXED" to: $WORK_DIR/fix-complete.txt

The script will automatically restart and test your fix.

Current statistics:
- Iterations: $ITERATION
- Errors found: $ERRORS_FOUND
- Fixes applied: $FIXES_APPLIED
- Successful runs: $SUCCESSFUL_RUNS
EOF

    cat "$WORK_DIR/ai-instructions.txt"

    echo ""
    echo "Waiting for AI to analyze and fix the issue..."
    echo "AI should read: $WORK_DIR/last-error.txt"
    echo "When fixed, AI should write 'FIXED' to: $WORK_DIR/fix-complete.txt"

    # Wait for AI to signal fix is complete
    local timeout=300  # 5 minutes
    local waited=0

    while [ $waited -lt $timeout ]; do
        if [ -f "$WORK_DIR/fix-complete.txt" ]; then
            FIXES_APPLIED=$((FIXES_APPLIED + 1))
            write_state "Fixing" "Fix applied! Restarting to test..."
            rm "$WORK_DIR/fix-complete.txt"
            return 0
        fi

        sleep 5
        waited=$((waited + 5))

        if [ $((waited % 30)) -eq 0 ]; then
            echo "Still waiting for fix... ($waited seconds)"
        fi
    done

    write_state "Investigating" "Timeout waiting for fix. Manual intervention needed."
    return 1
}

function stop_all_processes() {
    ./granville/samples/Rpc/scripts/kill-shooter-processes.sh || true
    sleep 2
}

# Main loop
trap stop_all_processes EXIT

for ((i=1; i<=MAX_ITERATIONS; i++)); do
    ITERATION=$i
    echo ""
    echo "=== Iteration $i ==="

    # Start services
    apphost_pid=$(start_shooter)

    # Monitor for errors
    if monitor_for_errors $apphost_pid $RUN_DURATION; then
        SUCCESSFUL_RUNS=$((SUCCESSFUL_RUNS + 1))
        write_state "Success" "Run completed successfully!"

        # Extended test for stability
        if [ $i -lt $MAX_ITERATIONS ]; then
            echo "Running extended test (2x duration) to confirm stability..."
            if monitor_for_errors $apphost_pid $((RUN_DURATION * 2)); then
                write_state "Success" "System is stable! No errors in extended test."
                break
            fi
        fi
    else
        ERRORS_FOUND=$((ERRORS_FOUND + 1))

        # Error found - wait for fix
        if ! wait_for_fix; then
            echo "Fix not applied. Stopping loop."
            break
        fi
    fi

    # Stop all processes
    stop_all_processes
done

# Final report
runtime=$(( $(date +%s) - $(date -d "$START_TIME" +%s) ))
echo ""
echo "=== Final Report ==="
echo "Total runtime: $(date -d@$runtime -u +%H:%M:%S)"
echo "Iterations: $ITERATION"
echo "Errors found: $ERRORS_FOUND"
echo "Fixes applied: $FIXES_APPLIED"
echo "Successful runs: $SUCCESSFUL_RUNS"
echo "Session data: $WORK_DIR"

write_state "Completed" "Dev loop completed"
echo ""
echo "Dev loop session complete. Results in: $WORK_DIR"