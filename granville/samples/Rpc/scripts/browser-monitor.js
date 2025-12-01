#!/usr/bin/env node
/**
 * Browser monitoring script for Shooter game
 * Uses Playwright to launch/control Chrome and monitor the game client
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

// Detect Windows host IP when running in WSL
function getGameUrl() {
    if (process.env.GAME_URL) {
        return process.env.GAME_URL;
    }

    // Check if we're running in WSL
    const isWSL = fs.existsSync('/proc/version') &&
                  fs.readFileSync('/proc/version', 'utf8').toLowerCase().includes('microsoft');

    if (isWSL) {
        // Use the Windows machine's LAN IP which is accessible from WSL
        // This is the IP address of the Windows host on the local network
        const windowsLanIP = '192.168.1.136';
        console.log(`[Browser] Detected WSL environment, using Windows LAN IP: ${windowsLanIP}`);
        return `http://${windowsLanIP}:5200/game`;
    }

    // Fallback to localhost for non-WSL environments
    return 'http://localhost:5200/game';
}

const config = {
    gameUrl: getGameUrl(),
    screenshotInterval: parseInt(process.env.SCREENSHOT_INTERVAL) || 15000, // 15 seconds
    outputDir: process.env.OUTPUT_DIR || './ai-dev-loop/browser-screenshots',
    maxScreenshots: parseInt(process.env.MAX_SCREENSHOTS) || 20,
    headless: process.env.HEADLESS === 'true',
    windowX: parseInt(process.env.WINDOW_X) || 0,
    windowY: parseInt(process.env.WINDOW_Y) || 300,  // Default 300px from top
    browserCount: parseInt(process.env.BROWSER_COUNT) || 2,  // Number of browser instances
    windowXOffset: parseInt(process.env.WINDOW_X_OFFSET) || 960,  // Horizontal offset between browsers
};

// Array to hold multiple browser instances
let browserInstances = [];

async function setupBrowser(instanceIndex) {
    const browserLabel = `Browser-${instanceIndex}`;
    console.log(`[${browserLabel}] Launching Chrome...`);

    // Calculate window position for this instance
    const windowX = config.windowX + (instanceIndex - 1) * config.windowXOffset;
    const windowY = config.windowY;

    const launchArgs = [
        '--no-sandbox',
        '--disable-setuid-sandbox',
        '--disable-dev-shm-usage',
    ];

    // Add window position if not headless
    if (!config.headless) {
        launchArgs.push(`--window-position=${windowX},${windowY}`);
        console.log(`[${browserLabel}] Window position: ${windowX},${windowY}`);
    }

    const browser = await chromium.launch({
        headless: config.headless,
        args: launchArgs,
    });

    // Create page with large viewport for better screenshots
    const page = await browser.newPage({
        viewport: {
            width: 1920,
            height: 1200
        }
    });

    // Create output directory for this instance
    const instanceOutputDir = path.join(config.outputDir, `browser-${instanceIndex}`);
    if (!fs.existsSync(instanceOutputDir)) {
        fs.mkdirSync(instanceOutputDir, { recursive: true });
    }

    // Browser instance state
    const instance = {
        index: instanceIndex,
        label: browserLabel,
        browser,
        page,
        screenshotCount: 0,
        consoleErrors: [],
        outputDir: instanceOutputDir
    };

    // Monitor console messages
    page.on('console', msg => {
        const type = msg.type();
        const text = msg.text();

        if (type === 'error') {
            const error = {
                time: new Date().toISOString(),
                type: 'console.error',
                message: text
            };
            instance.consoleErrors.push(error);
            console.log(`[${browserLabel}] ERROR: ${text}`);
        } else if (type === 'warning') {
            console.log(`[${browserLabel}] WARNING: ${text}`);
        }
    });

    // Monitor page errors
    page.on('pageerror', error => {
        const errorInfo = {
            time: new Date().toISOString(),
            type: 'page-error',
            message: error.message,
            stack: error.stack
        };
        instance.consoleErrors.push(errorInfo);
        console.log(`[${browserLabel}] PAGE ERROR: ${error.message}`);
    });

    // Monitor network failures
    page.on('requestfailed', request => {
        console.log(`[${browserLabel}] REQUEST FAILED: ${request.url()} - ${request.failure().errorText}`);
    });

    console.log(`[${browserLabel}] Chrome launched successfully`);
    return instance;
}

async function navigateToGame(instance) {
    console.log(`[${instance.label}] Navigating to ${config.gameUrl}...`);

    try {
        await instance.page.goto(config.gameUrl, {
            waitUntil: 'networkidle',
            timeout: 30000
        });
        console.log(`[${instance.label}] Page loaded successfully`);

        // Wait a bit for canvas to initialize
        await instance.page.waitForTimeout(2000);

        // Check if canvas exists
        const hasCanvas = await instance.page.evaluate(() => {
            const canvas = document.querySelector('canvas');
            return canvas !== null;
        });

        if (hasCanvas) {
            console.log(`[${instance.label}] ✓ Canvas element found`);
        } else {
            console.log(`[${instance.label}] ⚠ Canvas element not found - game may not have loaded properly`);
        }

        return true;
    } catch (error) {
        console.error(`[${instance.label}] Failed to navigate: ${error.message}`);
        return false;
    }
}

async function takeScreenshot(instance) {
    if (instance.screenshotCount >= config.maxScreenshots) {
        console.log(`[${instance.label}] Screenshot limit reached (${config.maxScreenshots})`);
        return;
    }

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    const filename = `screenshot-${timestamp}.png`;
    const filepath = path.join(instance.outputDir, filename);

    try {
        await instance.page.screenshot({ path: filepath, fullPage: false });
        instance.screenshotCount++;
        console.log(`[${instance.label}] Screenshot saved: ${filename}`);

        // Get page title and URL
        const title = await instance.page.title();
        const url = instance.page.url();
        console.log(`[${instance.label}] Page: "${title}" at ${url}`);

    } catch (error) {
        console.error(`[${instance.label}] Screenshot failed: ${error.message}`);
    }
}

async function checkPageHealth(instance) {
    try {
        // Check if page is still responsive
        const isResponsive = await instance.page.evaluate(() => {
            return typeof document !== 'undefined';
        });

        if (!isResponsive) {
            console.log(`[${instance.label}] ⚠ Page appears unresponsive`);
            return false;
        }

        // Check for game-specific indicators
        const gameStatus = await instance.page.evaluate(() => {
            const canvas = document.querySelector('canvas');
            const errors = document.querySelectorAll('.error, .alert-danger');
            return {
                hasCanvas: canvas !== null,
                canvasWidth: canvas?.width || 0,
                canvasHeight: canvas?.height || 0,
                errorCount: errors.length
            };
        });

        if (gameStatus.errorCount > 0) {
            console.log(`[${instance.label}] ⚠ Found ${gameStatus.errorCount} error elements on page`);
        }

        return true;
    } catch (error) {
        console.error(`[${instance.label}] Health check failed: ${error.message}`);
        return false;
    }
}

async function saveErrorReport(instance) {
    if (instance.consoleErrors.length === 0) {
        return;
    }

    const reportPath = path.join(instance.outputDir, 'browser-errors.json');

    try {
        fs.writeFileSync(reportPath, JSON.stringify({
            timestamp: new Date().toISOString(),
            browserInstance: instance.index,
            totalErrors: instance.consoleErrors.length,
            errors: instance.consoleErrors
        }, null, 2));

        console.log(`[${instance.label}] Error report saved: ${reportPath}`);
    } catch (error) {
        console.error(`[${instance.label}] Failed to save error report: ${error.message}`);
    }
}

async function monitorInstance(instance) {
    console.log(`[${instance.label}] Starting monitoring...`);

    // Initial screenshot
    await takeScreenshot(instance);

    // Periodic screenshots and health checks
    const interval = setInterval(async () => {
        await checkPageHealth(instance);
        await takeScreenshot(instance);
    }, config.screenshotInterval);

    // Store interval for cleanup
    instance.interval = interval;
}

async function shutdownAll() {
    console.log('\n[Browser] Shutting down all browser instances...');

    for (const instance of browserInstances) {
        if (instance.interval) {
            clearInterval(instance.interval);
        }
        await saveErrorReport(instance);
        await instance.browser.close();
        console.log(`[${instance.label}] Closed`);
    }

    process.exit(0);
}

async function main() {
    console.log('=== Browser Monitor for Shooter Game ===');
    console.log(`Number of browser instances: ${config.browserCount}`);
    console.log(`Screenshot interval: ${config.screenshotInterval}ms`);
    console.log(`Output directory: ${config.outputDir}`);

    try {
        // Setup signal handlers for graceful shutdown
        process.on('SIGINT', shutdownAll);
        process.on('SIGTERM', shutdownAll);

        // Launch all browser instances
        for (let i = 1; i <= config.browserCount; i++) {
            console.log(`\n--- Setting up Browser ${i} ---`);
            const instance = await setupBrowser(i);
            browserInstances.push(instance);
        }

        console.log(`\n✓ All ${config.browserCount} browser instance(s) launched`);

        // Retry navigation with increasing delays to allow services to start
        const maxRetries = 10;
        const retryDelays = [5000, 10000, 15000, 20000, 30000, 30000, 30000, 30000, 30000, 30000]; // Total ~3.5 minutes

        // Navigate all browsers (with retries for the first one only)
        for (let attempt = 1; attempt <= maxRetries; attempt++) {
            console.log(`\n--- Navigation attempt ${attempt}/${maxRetries} ---`);
            let allSuccess = true;

            for (const instance of browserInstances) {
                const success = await navigateToGame(instance);
                if (!success) {
                    allSuccess = false;
                }
            }

            if (allSuccess) {
                console.log(`\n✓ All browsers successfully connected on attempt ${attempt}`);
                break;
            }

            if (attempt < maxRetries) {
                const delay = retryDelays[attempt - 1];
                console.log(`\nWaiting ${delay}ms before retry ${attempt + 1}...`);
                await new Promise(resolve => setTimeout(resolve, delay));
            } else {
                console.error('\n✗ Failed to load game in all browsers after all retries, exiting...');
                process.exit(1);
            }
        }

        // Start monitoring all instances
        console.log('\n--- Starting monitoring for all browsers ---');
        for (const instance of browserInstances) {
            await monitorInstance(instance);
        }

        console.log('\n✓ All browser instances are now being monitored');
        console.log('Press Ctrl+C to stop monitoring and close browsers');

    } catch (error) {
        console.error(`\n✗ Fatal error: ${error.message}`);
        console.error(error.stack);
        process.exit(1);
    }
}

// Check if Playwright is installed
try {
    require.resolve('playwright');
    main();
} catch (e) {
    console.error('[Browser] Playwright not installed!');
    console.error('[Browser] Install with: npm install -D playwright');
    console.error('[Browser] Or: npx playwright install chromium');
    process.exit(1);
}
