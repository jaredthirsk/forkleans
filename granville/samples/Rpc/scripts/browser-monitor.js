#!/usr/bin/env node
/**
 * Browser monitoring script for Shooter game
 * Uses Playwright to launch/control Chrome and monitor the game client
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const config = {
    gameUrl: process.env.GAME_URL || 'http://localhost:5200/game',
    screenshotInterval: parseInt(process.env.SCREENSHOT_INTERVAL) || 15000, // 15 seconds
    outputDir: process.env.OUTPUT_DIR || './ai-dev-loop/browser-screenshots',
    maxScreenshots: parseInt(process.env.MAX_SCREENSHOTS) || 20,
    headless: process.env.HEADLESS === 'true',
};

let browser = null;
let page = null;
let screenshotCount = 0;
let consoleErrors = [];

async function setupBrowser() {
    console.log('[Browser] Launching Chrome...');

    browser = await chromium.launch({
        headless: config.headless,
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-dev-shm-usage',
        ],
    });

    page = await browser.newPage();

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
            consoleErrors.push(error);
            console.log(`[Browser] ERROR: ${text}`);
        } else if (type === 'warning') {
            console.log(`[Browser] WARNING: ${text}`);
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
        consoleErrors.push(errorInfo);
        console.log(`[Browser] PAGE ERROR: ${error.message}`);
    });

    // Monitor network failures
    page.on('requestfailed', request => {
        console.log(`[Browser] REQUEST FAILED: ${request.url()} - ${request.failure().errorText}`);
    });

    console.log('[Browser] Chrome launched successfully');
}

async function navigateToGame() {
    console.log(`[Browser] Navigating to ${config.gameUrl}...`);

    try {
        await page.goto(config.gameUrl, {
            waitUntil: 'networkidle',
            timeout: 30000
        });
        console.log('[Browser] Page loaded successfully');

        // Wait a bit for canvas to initialize
        await page.waitForTimeout(2000);

        // Check if canvas exists
        const hasCanvas = await page.evaluate(() => {
            const canvas = document.querySelector('canvas');
            return canvas !== null;
        });

        if (hasCanvas) {
            console.log('[Browser] ✓ Canvas element found');
        } else {
            console.log('[Browser] ⚠ Canvas element not found - game may not have loaded properly');
        }

        return true;
    } catch (error) {
        console.error(`[Browser] Failed to navigate: ${error.message}`);
        return false;
    }
}

async function takeScreenshot() {
    if (screenshotCount >= config.maxScreenshots) {
        console.log(`[Browser] Screenshot limit reached (${config.maxScreenshots})`);
        return;
    }

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    const filename = `screenshot-${timestamp}.png`;
    const filepath = path.join(config.outputDir, filename);

    try {
        // Ensure output directory exists
        if (!fs.existsSync(config.outputDir)) {
            fs.mkdirSync(config.outputDir, { recursive: true });
        }

        await page.screenshot({ path: filepath, fullPage: false });
        screenshotCount++;
        console.log(`[Browser] Screenshot saved: ${filename}`);

        // Get page title and URL
        const title = await page.title();
        const url = page.url();
        console.log(`[Browser] Page: "${title}" at ${url}`);

    } catch (error) {
        console.error(`[Browser] Screenshot failed: ${error.message}`);
    }
}

async function checkPageHealth() {
    try {
        // Check if page is still responsive
        const isResponsive = await page.evaluate(() => {
            return typeof document !== 'undefined';
        });

        if (!isResponsive) {
            console.log('[Browser] ⚠ Page appears unresponsive');
            return false;
        }

        // Check for game-specific indicators
        const gameStatus = await page.evaluate(() => {
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
            console.log(`[Browser] ⚠ Found ${gameStatus.errorCount} error elements on page`);
        }

        return true;
    } catch (error) {
        console.error(`[Browser] Health check failed: ${error.message}`);
        return false;
    }
}

async function saveErrorReport() {
    if (consoleErrors.length === 0) {
        return;
    }

    const reportPath = path.join(config.outputDir, 'browser-errors.json');

    try {
        fs.writeFileSync(reportPath, JSON.stringify({
            timestamp: new Date().toISOString(),
            totalErrors: consoleErrors.length,
            errors: consoleErrors
        }, null, 2));

        console.log(`[Browser] Error report saved: ${reportPath}`);
    } catch (error) {
        console.error(`[Browser] Failed to save error report: ${error.message}`);
    }
}

async function monitor() {
    console.log('[Browser] Starting monitoring...');
    console.log(`[Browser] Screenshot interval: ${config.screenshotInterval}ms`);
    console.log(`[Browser] Output directory: ${config.outputDir}`);

    // Initial screenshot
    await takeScreenshot();

    // Periodic screenshots and health checks
    const interval = setInterval(async () => {
        await checkPageHealth();
        await takeScreenshot();
    }, config.screenshotInterval);

    // Handle graceful shutdown
    process.on('SIGINT', async () => {
        console.log('\n[Browser] Shutting down...');
        clearInterval(interval);
        await saveErrorReport();
        await browser.close();
        process.exit(0);
    });

    process.on('SIGTERM', async () => {
        console.log('\n[Browser] Shutting down...');
        clearInterval(interval);
        await saveErrorReport();
        await browser.close();
        process.exit(0);
    });
}

async function main() {
    console.log('=== Browser Monitor for Shooter Game ===');

    try {
        await setupBrowser();

        // Retry navigation with increasing delays to allow services to start
        const maxRetries = 10;
        const retryDelays = [5000, 10000, 15000, 20000, 30000, 30000, 30000, 30000, 30000, 30000]; // Total ~3.5 minutes
        let success = false;

        for (let attempt = 1; attempt <= maxRetries; attempt++) {
            console.log(`[Browser] Navigation attempt ${attempt}/${maxRetries}...`);
            success = await navigateToGame();

            if (success) {
                console.log(`[Browser] Successfully connected on attempt ${attempt}`);
                break;
            }

            if (attempt < maxRetries) {
                const delay = retryDelays[attempt - 1];
                console.log(`[Browser] Waiting ${delay}ms before retry ${attempt + 1}...`);
                await new Promise(resolve => setTimeout(resolve, delay));
            }
        }

        if (!success) {
            console.error('[Browser] Failed to load game after all retries, exiting...');
            process.exit(1);
        }

        await monitor();

    } catch (error) {
        console.error(`[Browser] Fatal error: ${error.message}`);
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
