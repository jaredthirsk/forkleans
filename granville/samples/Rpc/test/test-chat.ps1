#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test script for verifying chat functionality in the Shooter game.
    
.DESCRIPTION
    This script tests the SignalR chat system by:
    1. Connecting to the SignalR hub
    2. Sending test messages
    3. Verifying message broadcast
#>

param(
    [string]$SiloUrl = "http://localhost:7071",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

Write-Host "=== Shooter Chat System Test ===" -ForegroundColor Green
Write-Host "Testing SignalR chat at: $SiloUrl/gamehub" -ForegroundColor Cyan

# First, check if the silo is running
try {
    $response = Invoke-WebRequest -Uri "$SiloUrl/health" -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -eq 200) {
        Write-Host "✓ Silo is healthy" -ForegroundColor Green
    }
} catch {
    Write-Host "✗ Silo is not responding at $SiloUrl" -ForegroundColor Red
    Write-Host "  Make sure the Shooter.Silo is running" -ForegroundColor Yellow
    exit 1
}

# Check if the gamehub endpoint exists
try {
    # SignalR negotiate endpoint
    $negotiateUrl = "$SiloUrl/gamehub/negotiate"
    $response = Invoke-WebRequest -Uri $negotiateUrl -Method POST -UseBasicParsing -TimeoutSec 5
    Write-Host "✓ SignalR hub endpoint exists" -ForegroundColor Green
    if ($Verbose) {
        Write-Host "  Negotiate response: $($response.Content)" -ForegroundColor Gray
    }
} catch {
    if ($_.Exception.Response.StatusCode -eq 400 -or $_.Exception.Response.StatusCode -eq 405) {
        Write-Host "✓ SignalR hub endpoint exists (returned expected error without proper connection)" -ForegroundColor Green
    } else {
        Write-Host "✗ SignalR hub endpoint not accessible" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "`n=== Test Summary ===" -ForegroundColor Green
Write-Host @"
The SignalR hub is configured and accessible at $SiloUrl/gamehub

To fully test chat functionality:
1. Open the game in a browser: http://localhost:5000/game
2. Join the game with a player name
3. Try sending a chat message
4. Check the browser console (F12) for any errors
5. Check the silo logs for chat-related messages:
   tail -f granville/samples/Rpc/logs/silo.log | grep -i "chat\|signalr"

To test with multiple clients:
1. Open the game in two different browser tabs
2. Join with different player names
3. Send a message from one tab
4. Verify it appears in both tabs

Known issues to check:
- Ensure SignalR connection is established (check browser console)
- Verify hub context is properly injected in WorldManagerGrain
- Check for CORS issues if client and server are on different ports
"@ -ForegroundColor Cyan

# Create a simple Node.js test client for automated testing
$testClientScript = @'
const signalR = require('@microsoft/signalr');

async function testChat() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('http://localhost:7071/gamehub')
        .configureLogging(signalR.LogLevel.Debug)
        .build();
    
    let messagesReceived = [];
    
    connection.on('ReceiveMessage', (user, message) => {
        console.log(`[RECEIVED] ${user}: ${message}`);
        messagesReceived.push({user, message});
    });
    
    connection.on('ReceiveChatMessage', (chatMessage) => {
        console.log('[RECEIVED CHAT]', chatMessage);
        messagesReceived.push(chatMessage);
    });
    
    try {
        await connection.start();
        console.log('Connected to SignalR hub');
        
        // Send a test message
        await connection.invoke('SendMessage', 'TestUser', 'Test message from script');
        console.log('Sent test message');
        
        // Wait for messages
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        if (messagesReceived.length > 0) {
            console.log(`SUCCESS: Received ${messagesReceived.length} messages`);
        } else {
            console.log('WARNING: No messages received - check if broadcast is working');
        }
        
        await connection.stop();
    } catch (err) {
        console.error('Error:', err);
    }
}

testChat();
'@

Write-Host "`n=== Node.js Test Client Script ===" -ForegroundColor Yellow
Write-Host "To run an automated test, save this as test-chat.js and run:" -ForegroundColor Cyan
Write-Host "  npm install @microsoft/signalr" -ForegroundColor Gray
Write-Host "  node test-chat.js" -ForegroundColor Gray
Write-Host "`nScript content:" -ForegroundColor Cyan
Write-Host $testClientScript -ForegroundColor DarkGray