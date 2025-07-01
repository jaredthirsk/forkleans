#!/bin/bash

# Manual integration test for Shooter RPC sample
# This script starts all services and monitors their logs for errors

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}Shooter Manual Integration Test${NC}"
echo "================================="

# Parse command line arguments
INCLUDE_BOT=false
CHECK_ERRORS=true

while [[ $# -gt 0 ]]; do
    case $1 in
        --with-bot)
            INCLUDE_BOT=true
            shift
            ;;
        --no-error-check)
            CHECK_ERRORS=false
            shift
            ;;
        --help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --with-bot        Also start a bot client"
            echo "  --no-error-check  Don't check for specific errors"
            echo "  --help            Show this help message"
            echo ""
            echo "This script starts the Shooter services and monitors their logs."
            echo "Use this for manual integration testing when the xUnit tests timeout."
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Clean up any existing processes
echo -e "${YELLOW}Cleaning up any existing processes...${NC}"
../scripts/kill-shooter-processes.sh 2>/dev/null || pkill -f "Shooter\." || true
sleep 2

# Clean up old log files
echo -e "${YELLOW}Cleaning up old logs...${NC}"
rm -f ../logs/*.log
mkdir -p ../logs

# Start the Silo
echo -e "\n${GREEN}Starting Silo...${NC}"
cd ../Shooter.Silo
dotnet run -c Release > ../logs/silo-manual.log 2>&1 &
SILO_PID=$!
cd ..
echo "Silo started with PID $SILO_PID"

# Wait for Silo to be ready
echo "Waiting for Silo to be ready..."
sleep 10

# Start the ActionServer
echo -e "\n${GREEN}Starting ActionServer...${NC}"
cd ../Shooter.ActionServer
dotnet run -c Release > ../logs/actionserver-manual.log 2>&1 &
ACTIONSERVER_PID=$!
cd ..
echo "ActionServer started with PID $ACTIONSERVER_PID"

# Wait for ActionServer to connect
echo "Waiting for ActionServer to connect to Silo..."
sleep 10

# Optionally start a Bot
if [ "$INCLUDE_BOT" = true ]; then
    echo -e "\n${GREEN}Starting Bot...${NC}"
    cd ../Shooter.Bot
    TEST_MODE=true dotnet run -c Release > ../logs/bot-manual.log 2>&1 &
    BOT_PID=$!
    cd ..
    echo "Bot started with PID $BOT_PID"
    sleep 10
fi

# Check for errors if requested
if [ "$CHECK_ERRORS" = true ]; then
    echo -e "\n${YELLOW}=== Checking for common errors ===${NC}"
    
    # Check ActionServer registration
    if grep -q "No active nodes are compatible with grain proxy_iworldmanager" ../logs/actionserver-manual.log; then
        echo -e "${RED}❌ ERROR: 'No active nodes are compatible' error found!${NC}"
        grep -A5 -B5 "No active nodes are compatible" ../logs/actionserver-manual.log
    elif grep -q "Registering ActionServer" ../logs/actionserver-manual.log; then
        echo -e "${GREEN}✅ ActionServer started and attempting registration${NC}"
        if grep -q "Successfully registered" ../logs/actionserver-manual.log; then
            echo -e "${GREEN}✅ ActionServer successfully registered!${NC}"
        elif grep -q "Failed to register ActionServer" ../logs/actionserver-manual.log; then
            echo -e "${RED}❌ Registration failed${NC}"
            grep -A10 "Failed to register" ../logs/actionserver-manual.log
        fi
    fi
    
    # Check for RPC handshake
    if grep -q "RPC handshake completed" ../logs/actionserver-manual.log; then
        echo -e "${GREEN}✅ RPC handshake completed${NC}"
    fi
    
    # Check Bot connection if started
    if [ "$INCLUDE_BOT" = true ]; then
        if grep -q "connected as player" ../logs/bot-manual.log; then
            echo -e "${GREEN}✅ Bot connected successfully${NC}"
        else
            echo -e "${RED}❌ Bot failed to connect${NC}"
        fi
    fi
fi

# Show service status
echo -e "\n${YELLOW}=== Service Status ===${NC}"
echo "Silo PID: $SILO_PID"
echo "ActionServer PID: $ACTIONSERVER_PID"
[ "$INCLUDE_BOT" = true ] && echo "Bot PID: $BOT_PID"

echo -e "\n${YELLOW}=== Log Files ===${NC}"
echo "Silo log: ../logs/silo-manual.log"
echo "ActionServer log: ../logs/actionserver-manual.log"
[ "$INCLUDE_BOT" = true ] && echo "Bot log: ../logs/bot-manual.log"

echo -e "\n${GREEN}Services are running. Monitor logs with:${NC}"
echo "  tail -f ../logs/*.log"
echo ""
echo -e "${YELLOW}To stop all services:${NC}"
echo "  ../scripts/kill-shooter-processes.sh"
echo ""
echo -e "${YELLOW}To run proper tests:${NC}"
echo "  ./run-tests.sh --simple"