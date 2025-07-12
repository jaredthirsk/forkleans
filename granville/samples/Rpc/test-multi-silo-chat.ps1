#!/usr/bin/env pwsh
# Test script to verify chat works across multiple silos

Write-Host "=== Multi-Silo Chat Test ===" -ForegroundColor Cyan
Write-Host "This script will start 2 silos and test chat functionality across them"
Write-Host ""

# Configuration
$silo1Port = 11111
$silo1GatewayPort = 30000
$silo2Port = 11112
$silo2GatewayPort = 30001

# Function to start a silo
function Start-Silo {
    param(
        [int]$SiloPort,
        [int]$GatewayPort,
        [string]$Name
    )
    
    Write-Host "Starting $Name on ports $SiloPort/$GatewayPort..." -ForegroundColor Yellow
    
    $env:ORLEANS_SILO_PORT = $SiloPort
    $env:ORLEANS_GATEWAY_PORT = $GatewayPort
    
    # Start the silo in a new terminal
    if ($IsWindows) {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$PWD/Shooter.Silo'; dotnet run --urls 'https://localhost:$($GatewayPort + 31311)' -- --silo-port $SiloPort --gateway-port $GatewayPort"
    } else {
        # On Linux/Mac
        gnome-terminal -- bash -c "cd $PWD/Shooter.Silo && dotnet run --urls 'https://localhost:$(($GatewayPort + 31311))' -- --silo-port $SiloPort --gateway-port $GatewayPort; exec bash" 2>/dev/null || \
        xterm -e "cd $PWD/Shooter.Silo && dotnet run --urls 'https://localhost:$(($GatewayPort + 31311))' -- --silo-port $SiloPort --gateway-port $GatewayPort; read -p 'Press enter to close...'" &
    }
}

# Instructions
Write-Host @"
Multi-Silo Chat Test Instructions:
1. This script will start 2 Orleans silos
2. Open 2 browser windows and connect to:
   - Silo 1: https://localhost:61311
   - Silo 2: https://localhost:61312
3. Join the game in both browser windows
4. Send a chat message from one window
5. Verify the message appears in BOTH windows

Note: The UFX SignalR backplane should distribute messages across both silos.

Press Enter to start the silos...
"@ -ForegroundColor Green

Read-Host

# Start both silos
Start-Silo -SiloPort $silo1Port -GatewayPort $silo1GatewayPort -Name "Silo 1"
Start-Sleep -Seconds 5
Start-Silo -SiloPort $silo2Port -GatewayPort $silo2GatewayPort -Name "Silo 2"

Write-Host ""
Write-Host "Silos are starting up..." -ForegroundColor Yellow
Write-Host "Wait for both silos to fully start, then test chat functionality" -ForegroundColor Yellow
Write-Host ""
Write-Host "To stop the test, close the silo terminal windows" -ForegroundColor Cyan