#!/bin/bash
# Script that monitors logs and blocks until an error is found
# Returns 0 when an error is detected, 1 on timeout

LOG_DIR="/mnt/c/forks/orleans/granville/samples/Rpc/logs"
IGNORE_FILE="/mnt/c/forks/orleans/granville/samples/Rpc/scripts/error-ignore-list.txt"
ERROR_FILE="/tmp/shooter-error-detected.txt"
TIMEOUT=${1:-0}  # 0 means no timeout

# Create default ignore list if not exists
if [ ! -f "$IGNORE_FILE" ]; then
    cat > "$IGNORE_FILE" << 'EOF'
# Ignore list for error monitoring
# Add patterns (regex) to ignore, one per line
Development environment
development certificate
Graceful shutdown
Cleaning up
Using development
Development mode
ASPNETCORE_ENVIRONMENT
DOTNET_ENVIRONMENT
EOF
fi

# Error patterns to detect
ERROR_PATTERNS=(
    "ERROR\]"
    "FATAL"
    "Exception:"
    "Unhandled exception"
    "System\.\w+Exception"
    "Connection refused"
    "Socket error"
    "Timeout waiting"
    "Deadlock detected"
    "Failed to"
    "Could not"
    "Unable to"
    "error:"
    "Error:"
)

# Warning patterns that should be treated as errors
CRITICAL_WARNINGS=(
    "Connection lost"
    "High latency"
    "Memory pressure"
    "Thread pool starvation"
)

echo "Monitoring logs for errors in: $LOG_DIR"
echo "Ignore patterns loaded from: $IGNORE_FILE"
echo "This script will block until an error is detected..."

# Function to check if line should be ignored
should_ignore() {
    local line="$1"
    while IFS= read -r pattern; do
        # Skip comments and empty lines
        [[ "$pattern" =~ ^#.*$ ]] && continue
        [[ -z "$pattern" ]] && continue
        
        if echo "$line" | grep -qi "$pattern"; then
            return 0  # Should ignore
        fi
    done < "$IGNORE_FILE"
    return 1  # Should not ignore
}

# Function to check if line contains error
contains_error() {
    local line="$1"
    
    # Check error patterns
    for pattern in "${ERROR_PATTERNS[@]}"; do
        if echo "$line" | grep -E "$pattern" > /dev/null 2>&1; then
            return 0
        fi
    done
    
    # Check critical warnings
    for pattern in "${CRITICAL_WARNINGS[@]}"; do
        if echo "$line" | grep -E "$pattern" > /dev/null 2>&1; then
            return 0
        fi
    done
    
    return 1
}

# Start monitoring
START_TIME=$(date +%s)

# Use tail to monitor all log files
tail -f "$LOG_DIR"/*.log 2>/dev/null | while read -r line; do
    # Check timeout if set
    if [ "$TIMEOUT" -gt 0 ]; then
        CURRENT_TIME=$(date +%s)
        ELAPSED=$((CURRENT_TIME - START_TIME))
        if [ "$ELAPSED" -gt "$TIMEOUT" ]; then
            echo "Timeout reached after $TIMEOUT seconds without errors"
            exit 1
        fi
    fi
    
    # Check if line contains error
    if contains_error "$line"; then
        # Check if should ignore
        if ! should_ignore "$line"; then
            echo ""
            echo "ERROR DETECTED!"
            echo "==============="
            echo "$line"
            echo ""
            echo "$line" > "$ERROR_FILE"
            
            # Get some context around the error
            echo "Getting context from logs..."
            for logfile in "$LOG_DIR"/*.log; do
                if grep -q "$line" "$logfile" 2>/dev/null; then
                    echo "Error found in: $logfile"
                    echo ""
                    echo "Context (last 20 lines):"
                    tail -20 "$logfile"
                    break
                fi
            done
            
            exit 0  # Error found
        fi
    fi
done