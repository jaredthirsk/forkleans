# Browser Monitoring for Shooter Sample

This document describes the automated browser monitoring feature integrated into the Shooter sample's development tools.

## Overview

The browser monitoring system uses Playwright to automatically:
- Launch Chrome and navigate to the game client
- Take periodic screenshots of the game in action
- Monitor browser console for JavaScript errors
- Detect page crashes or hangs
- Report findings to help with debugging

This is particularly useful during automated testing (ai-dev-loop) or manual development (rl.sh).

## Prerequisites

### Install Node.js
Node.js is required to run the browser monitoring script.

```bash
# Check if Node.js is installed
node --version

# If not installed, install via your package manager
# Ubuntu/Debian:
sudo apt-get install nodejs npm

# Or use nvm (recommended):
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash
nvm install --lts
```

### Install Playwright

```bash
# From the Shooter sample directory
cd /mnt/c/forks/orleans/granville/samples/Rpc

# Install Playwright as a dev dependency
npm install -D playwright

# Download Chromium browser binary
npx playwright install chromium
```

## Usage

### With rl.sh (Manual Development)

```bash
cd Shooter.AppHost

# Run with browser monitoring (default)
./rl.sh

# Run without browser monitoring
./rl.sh --no-browser

# Run with custom screenshot interval (30 seconds)
./rl.sh --browser-interval 30

# Run with custom game URL
./rl.sh --game-url http://localhost:5200/game
```

Screenshots are saved to: `./browser-screenshots-TIMESTAMP/`

### With ai-dev-loop.ps1 (Automated Testing)

```powershell
cd granville/samples/Rpc/scripts

# Run with browser monitoring (default)
./ai-dev-loop.ps1

# Run without browser monitoring
./ai-dev-loop.ps1 -NoBrowser

# Run with custom screenshot interval (30 seconds)
./ai-dev-loop.ps1 -BrowserScreenshotInterval 30
```

Screenshots are saved to: `ai-dev-loop/SESSION-ID/browser-screenshots/`

## What Gets Monitored

### Screenshots
- Taken at regular intervals (default: 15 seconds)
- Saved as PNG files with timestamps
- Limited to 20 screenshots per session (configurable)

### Console Logs
- JavaScript errors logged to console
- Warnings captured and reported
- Page errors (uncaught exceptions) detected

### Network Issues
- Failed HTTP requests logged
- SSL/certificate errors detected
- Connection failures reported

### Page Health
- Canvas element presence checked
- Page responsiveness monitored
- Error elements on page detected

## Output Files

### Screenshots
- **Location**: `browser-screenshots/` or `ai-dev-loop/SESSION-ID/browser-screenshots/`
- **Format**: `screenshot-TIMESTAMP.png`
- **Content**: Full browser viewport of the game

### Error Reports
- **File**: `browser-screenshots/browser-errors.json`
- **Contains**:
  - Timestamp of each error
  - Error type (console.error, page-error, etc.)
  - Error message and stack trace
  - Total error count

## Configuration

### Environment Variables

The browser monitor script supports these environment variables:

- `GAME_URL` - URL to navigate to (default: `http://localhost:5200/game`)
- `SCREENSHOT_INTERVAL` - Interval in milliseconds (default: 15000)
- `OUTPUT_DIR` - Where to save screenshots (default: `./ai-dev-loop/browser-screenshots`)
- `HEADLESS` - Run browser in headless mode (default: false)
- `MAX_SCREENSHOTS` - Maximum screenshots to take (default: 20)

### Script Parameters

**rl.sh**:
- `--no-browser` - Disable browser monitoring
- `--browser-interval N` - Screenshot interval in seconds
- `--game-url URL` - Custom game URL

**ai-dev-loop.ps1**:
- `-NoBrowser` - Disable browser monitoring
- `-BrowserScreenshotInterval N` - Screenshot interval in seconds

## Troubleshooting

### "Node.js not found"
Install Node.js using your package manager or nvm.

### "Playwright not installed"
Run: `npm install -D playwright && npx playwright install chromium`

### "Browser fails to launch"
Ensure you have necessary system dependencies:
```bash
# Ubuntu/Debian
sudo apt-get install libgbm1 libasound2

# Or install all Playwright dependencies
npx playwright install-deps
```

### Browser opens but page doesn't load
- Check if the AppHost is running
- Verify the game URL (default: http://localhost:5200/game)
- Check for port conflicts

### Screenshots are blank or show errors
- Wait for the AppHost to fully start (20+ seconds)
- Check browser console in the automated window for JavaScript errors
- Verify the game client is actually running

## Advanced Usage

### Running browser monitor standalone

You can run the browser monitor script directly:

```bash
cd /mnt/c/forks/orleans/granville/samples/Rpc/scripts

# Set configuration via environment
export GAME_URL="http://localhost:5200/game"
export SCREENSHOT_INTERVAL=10000
export OUTPUT_DIR="./my-screenshots"
export HEADLESS=false

# Run the script
node browser-monitor.js
```

Press Ctrl+C to stop and generate the error report.

### Headless mode

For CI/CD environments, run in headless mode:

```bash
HEADLESS=true node browser-monitor.js
```

## Integration with AI Development Loop

The browser monitoring integrates seamlessly with the AI dev loop:

1. AppHost starts and launches all services
2. Browser monitor launches after 20 second warmup
3. Screenshots taken at regular intervals
4. Any browser errors added to error detection
5. On iteration end, browser monitor stops gracefully
6. Screenshots and error reports available for AI analysis

The AI can then:
- View screenshots to verify game is rendering
- Analyze console errors to identify client-side issues
- Correlate browser issues with server-side logs
- Detect UI freezes or rendering problems

## Future Enhancements

Potential improvements:
- [ ] Automated gameplay interaction (mouse/keyboard)
- [ ] Performance metrics (FPS, memory usage)
- [ ] Visual regression testing (compare screenshots)
- [ ] Network traffic inspection
- [ ] WebSocket message monitoring
- [ ] Automatic error pattern detection
- [ ] Screenshot diffs to detect changes
- [ ] Video recording of game sessions

## See Also

- [AI Development Loop Documentation](../scripts/ai-dev-loop.ps1) - Automated testing workflow
- [Playwright Documentation](https://playwright.dev/) - Official Playwright docs
- [Shooter Sample Overview](../CLAUDE.md) - Main documentation
