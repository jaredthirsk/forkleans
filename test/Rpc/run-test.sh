#!/bin/bash

echo "Orleans RPC Integration Test"
echo "============================"
echo ""
echo "Usage:"
echo "  Run server:   ./run-test.sh server [port]"
echo "  Run client:   ./run-test.sh client [host] [port]"
echo "  Run combined: ./run-test.sh combined"
echo ""

case "$1" in
  server)
    PORT=${2:-11111}
    echo "Starting RPC server on port $PORT..."
    dotnet run --project Orleans.Rpc.IntegrationTest.Server -- $PORT
    ;;
  client)
    HOST=${2:-127.0.0.1}
    PORT=${3:-11111}
    echo "Starting RPC client connecting to $HOST:$PORT..."
    dotnet run --project Orleans.Rpc.IntegrationTest.Client -- $HOST $PORT
    ;;
  combined)
    echo "Starting combined test..."
    dotnet run --project Orleans.Rpc.IntegrationTest.Combined
    ;;
  *)
    echo "Please specify 'server', 'client', or 'combined'"
    exit 1
    ;;
esac