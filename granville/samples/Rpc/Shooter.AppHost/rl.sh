#!/bin/bash
# rl.sh - Run Shooter.AppHost with optional browser monitoring
#
# Usage:
#   ./rl.sh                    # Run with browser monitoring (default: 2 browsers)
#   ./rl.sh --no-browser       # Run without browser monitoring
#   ./rl.sh --headless         # Run browser in headless mode (no visible window)
#   ./rl.sh --browser-interval 30  # Custom screenshot interval (seconds)
#   ./rl.sh --browser-count 2  # Number of browser instances (default: 2)
#   ./rl.sh --window-x 0 --window-y 300  # Position browser windows (default: 0,300)
#   ./rl.sh --skip-clean       # Skip dotnet clean (faster startup)

# Parse arguments
ENABLE_BROWSER=true
HEADLESS=false
SCREENSHOT_INTERVAL=15
GAME_URL="http://localhost:5200/game"
BROWSER_COUNT=2
WINDOW_X=0
WINDOW_Y=300
SKIP_CLEAN=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --no-browser)
            ENABLE_BROWSER=false
            shift
            ;;
        --headless)
            HEADLESS=true
            shift
            ;;
        --browser-interval)
            SCREENSHOT_INTERVAL="$2"
            shift 2
            ;;
        --browser-count)
            BROWSER_COUNT="$2"
            shift 2
            ;;
        --game-url)
            GAME_URL="$2"
            shift 2
            ;;
        --window-x)
            WINDOW_X="$2"
            shift 2
            ;;
        --window-y)
            WINDOW_Y="$2"
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
            echo "[rl.sh] Starting browser monitor ($BROWSER_COUNT instance(s))..."

            # Set environment variables for browser monitor
            # Note: Do NOT set GAME_URL here - let browser-monitor.js detect WSL and use the correct IP
            export SCREENSHOT_INTERVAL=$((SCREENSHOT_INTERVAL * 1000))
            export OUTPUT_DIR="./browser-screenshots-$(date +%Y%m%d-%H%M%S)"
            export HEADLESS="$HEADLESS"
            export BROWSER_COUNT="$BROWSER_COUNT"
            export WINDOW_X="$WINDOW_X"
            export WINDOW_Y="$WINDOW_Y"

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
    /home/jared/bin/dotnet_win clean 2>/dev/null || true
else
    echo "[rl.sh] Skipping dotnet clean (--skip-clean specified)"
fi

# Always build to ensure changes are compiled (use --no-restore to speed up)
# Use dotnet-win for Windows filesystem to avoid timestamp sync issues with incremental builds
echo "[rl.sh] Building projects..."
/home/jared/bin/dotnet_win build --no-restore 2>/dev/null || /home/jared/bin/dotnet_win build

cd "$TEMP_DIR"

# Convert WSL path to Windows path for dotnet-win
WINDOWS_PROJECT_PATH=$(wslpath -w "$ORIGINAL_DIR/Shooter.AppHost.csproj")

echo "[rl.sh] Starting AppHost..."
/home/jared/bin/dotnet_win run --no-build --project "$WINDOWS_PROJECT_PATH" -- "$@"
#dotnet-win run --project "$WINDOWS_PROJECT_PATH" -c Release -- "$@"

# Clean up and return
cd "$ORIGINAL_DIR"
rm -rf "$TEMP_DIR"

# Cleanup browser monitor on exit
cleanup_browser