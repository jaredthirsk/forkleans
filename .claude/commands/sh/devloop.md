---
description: Run AI-driven development loop for Shooter debugging
---

# AI Development Loop

Start the AI-driven development loop that automatically monitors, detects errors, and waits for Claude to analyze and fix issues.

**Parameters:**
- Default: Skips clean step for faster iterations (20 min runtime), 2 visible browsers
- Use `--clean` argument to force clean build (slower but thorough)
- Use `--headless` argument to run browsers in headless mode (no visible windows)
- Use `--browser-count N` argument to set number of browser instances (default: 2)

**IMPORTANT FOR CLAUDE**: After starting this command in the background, you MUST proactively monitor the background bash process every 60-120 seconds by checking `BashOutput`. When the dev loop detects errors (you'll see "Still waiting for fix..." in the output), immediately:
1. Read `/mnt/c/forks/orleans/granville/samples/Rpc/ai-dev-loop/[latest-session]/last-error.txt`
2. Analyze the errors and apply fixes
3. Write "FIXED" to the `fix-complete.txt` file

Do NOT wait for the user to ask - monitor proactively!

**Browser Monitoring Status (2025-10-09)**:
The browser monitor now correctly detects WSL and attempts to connect to the Windows host IP (172.28.240.1:5200) instead of localhost. The Shooter client binds to 0.0.0.0:5200 which should allow WSL access. However, **Windows Firewall blocks WSL->Windows connections on port 5200** by default.

To enable browser monitoring from WSL, the user needs to add a Windows Firewall rule:
```powershell
# Run in Windows PowerShell as Administrator:
New-NetFirewallRule -DisplayName "Allow WSL Shooter Client" -Direction Inbound -LocalPort 5200 -Protocol TCP -Action Allow
```

Alternatively, access the game from a Windows browser at `http://localhost:5200/game` instead.

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

# Parse arguments
SKIP_CLEAN_ARG="-SkipClean"
HEADLESS_ARG=""
BROWSER_COUNT_ARG=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --clean)
            SKIP_CLEAN_ARG=""  # Don't pass -SkipClean (force clean)
            shift
            ;;
        --headless)
            HEADLESS_ARG="-Headless"
            shift
            ;;
        --browser-count)
            BROWSER_COUNT_ARG="-BrowserCount $2"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done

# Start the AI development loop in background
# Default: -SkipClean for faster iterations (20 min runtime allows for build)
pwsh ./scripts/ai-dev-loop.ps1 -RunDuration 1200 -MaxIterations 3 $SKIP_CLEAN_ARG $HEADLESS_ARG $BROWSER_COUNT_ARG &
```