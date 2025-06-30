#!/bin/bash

# Run Shooter integration tests
# This script runs the tests and can optionally keep logs for debugging

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}Running Shooter Integration Tests${NC}"
echo "=================================="

# Parse arguments
KEEP_LOGS=false
FILTER=""
VERBOSITY="normal"

while [[ $# -gt 0 ]]; do
    case $1 in
        --keep-logs)
            KEEP_LOGS=true
            shift
            ;;
        --filter)
            FILTER="--filter $2"
            shift 2
            ;;
        --verbose)
            VERBOSITY="detailed"
            shift
            ;;
        --simple)
            FILTER="--filter FullyQualifiedName~SimpleLogTest"
            shift
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            echo "Usage: $0 [--keep-logs] [--filter <test-name>] [--verbose] [--simple]"
            echo ""
            echo "Options:"
            echo "  --keep-logs    Keep test logs after running tests"
            echo "  --filter       Run only tests matching the specified filter"
            echo "  --verbose      Show detailed test output"
            echo "  --simple       Run only simple tests that don't require services"
            exit 1
            ;;
    esac
done

# Create logs directory if it doesn't exist
mkdir -p ../logs/test

# Clean up old test logs unless --keep-logs is specified
if [ "$KEEP_LOGS" = false ]; then
    echo -e "${YELLOW}Cleaning up old test logs...${NC}"
    rm -rf ../logs/test/*
fi

# Run the tests
echo -e "${GREEN}Starting tests...${NC}"
dotnet test Shooter.Tests/Shooter.Tests.csproj -c Debug -v $VERBOSITY $FILTER

TEST_RESULT=$?

# Show summary
if [ $TEST_RESULT -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed!${NC}"
else
    echo -e "${RED}✗ Some tests failed.${NC}"
    echo -e "${YELLOW}Check the test logs in logs/test/ for details${NC}"
    
    # If we weren't already running simple tests, suggest it
    if [[ ! "$FILTER" =~ "SimpleLogTest" ]]; then
        echo ""
        echo -e "${YELLOW}Note: Integration tests may timeout when starting services.${NC}"
        echo "To run simple tests that don't require services:"
        echo "  ./run-tests.sh --simple"
        echo ""
        echo "For full integration testing, see Shooter.Tests/TESTING_GUIDE.md"
    fi
fi

# Show log file locations if tests were run
if [ -d "../logs/test" ] && [ "$(ls -A ../logs/test)" ]; then
    echo -e "\n${YELLOW}Test log directories:${NC}"
    ls -la ../logs/test/
fi

exit $TEST_RESULT