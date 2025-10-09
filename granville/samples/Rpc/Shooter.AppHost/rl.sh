#!/bin/bash
# rl.sh - Run Shooter.AppHost with optional browser monitoring
#
# Usage:
#   ./rl.sh                    # Run with browser monitoring (default)
#   ./rl.sh --no-browser       # Run without browser monitoring
#   ./rl.sh --browser-interval 30  # Custom screenshot interval (seconds)
#   ./rl.sh --skip-clean       # Skip dotnet clean (faster startup)

# Parse arguments
ENABLE_BROWSER=true
SCREENSHOT_INTERVAL=15
GAME_URL="http://localhost:5200/game"
SKIP_CLEAN=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --no-browser)
            ENABLE_BROWSER=false
            shift
            ;;
        --browser-interval)
            SCREENSHOT_INTERVAL="$2"
            shift 2
            ;;
        --game-url)
            GAME_URL="$2"
            shift 2
            ;;
        --skip-clean)
            SKIP_CLEAN=true
            shift
            ;;
        *)
            # Pass unknown args to dotnet run
            break
            ;;
    esac
done

#./k.sh
/mnt/c/forks/orleans/granville/samples/Rpc/scripts/kill-shooter-processes.sh
rm /mnt/c/forks/orleans/granville/samples/Rpc/logs/*.log -f

# Start browser monitor if enabled
BROWSER_PID=""
if [ "$ENABLE_BROWSER" = true ]; then
    echo "[rl.sh] Checking for browser monitoring dependencies..."

    if command -v node &> /dev/null; then
        # Check if Playwright is available
        PLAYWRIGHT_CHECK=$(node -e "try { require.resolve('playwright'); console.log('ok'); } catch(e) { console.log('missing'); }" 2>/dev/null)

        if [ "$PLAYWRIGHT_CHECK" = "ok" ]; then
            echo "[rl.sh] Starting browser monitor..."

            # Set environment variables for browser monitor
            export GAME_URL="$GAME_URL"
            export SCREENSHOT_INTERVAL=$((SCREENSHOT_INTERVAL * 1000))
            export OUTPUT_DIR="./browser-screenshots-$(date +%Y%m%d-%H%M%S)"
            export HEADLESS="false"

            # Start browser monitor in background
            MONITOR_SCRIPT="/mnt/c/forks/orleans/granville/samples/Rpc/scripts/browser-monitor.js"
            node "$MONITOR_SCRIPT" &
            BROWSER_PID=$!

            echo "[rl.sh] Browser monitor started (PID: $BROWSER_PID)"
            echo "[rl.sh] Screenshots will be saved to: $OUTPUT_DIR"
        else
            echo "[rl.sh] Playwright not installed, skipping browser monitoring"
            echo "[rl.sh] Install with: npm install -D playwright && npx playwright install chromium"
        fi
    else
        echo "[rl.sh] Node.js not found, skipping browser monitoring"
    fi
else
    echo "[rl.sh] Browser monitoring disabled"
fi

# Cleanup function for browser monitor
cleanup_browser() {
    if [ -n "$BROWSER_PID" ] && kill -0 "$BROWSER_PID" 2>/dev/null; then
        echo "[rl.sh] Stopping browser monitor (PID: $BROWSER_PID)..."
        kill -TERM "$BROWSER_PID" 2>/dev/null
        wait "$BROWSER_PID" 2>/dev/null
    fi
}

# Trap exit signals to cleanup browser monitor
trap cleanup_browser EXIT INT TERM

# Work around dotnet CLI issue with Windows filesystem mounts
# by temporarily changing to a Linux filesystem directory
ORIGINAL_DIR=$(pwd)
TEMP_DIR="/tmp/dotnet-run-$$"
mkdir -p "$TEMP_DIR"
cd "$TEMP_DIR"

# Run dotnet commands from Linux filesystem
cd "$ORIGINAL_DIR"
if [ "$SKIP_CLEAN" = false ]; then
    echo "[rl.sh] Running dotnet clean..."
    dotnet clean 2>/dev/null || true
else
    echo "[rl.sh] Skipping dotnet clean (--skip-clean specified)"
fi
cd "$TEMP_DIR"

echo "[rl.sh] Starting AppHost..."
dotnet run --project "$ORIGINAL_DIR/Shooter.AppHost.csproj" -- "$@"
#dotnet run --project "$ORIGINAL_DIR/Shooter.AppHost.csproj" -c Release -- "$@"

# Clean up and return
cd "$ORIGINAL_DIR"
rm -rf "$TEMP_DIR"

# Cleanup browser monitor on exit
cleanup_browser