#!/bin/bash
./k.sh
rm /mnt/c/forks/orleans/granville/samples/Rpc/logs/*.log -f

# Work around dotnet CLI issue with Windows filesystem mounts
# by temporarily changing to a Linux filesystem directory
ORIGINAL_DIR=$(pwd)
TEMP_DIR="/tmp/dotnet-run-$$"
mkdir -p "$TEMP_DIR"
cd "$TEMP_DIR"

# Run dotnet commands from Linux filesystem
# Skip clean for now due to syntax issues, or use the full path
cd "$ORIGINAL_DIR"
dotnet clean 2>/dev/null || true
cd "$TEMP_DIR"
dotnet run --project "$ORIGINAL_DIR/Shooter.AppHost.csproj" -- "$@"
#dotnet run --project "$ORIGINAL_DIR/Shooter.AppHost.csproj" -c Release -- "$@"

# Clean up and return
cd "$ORIGINAL_DIR"
rm -rf "$TEMP_DIR"
