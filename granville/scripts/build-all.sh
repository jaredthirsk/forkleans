#!/bin/bash
# build-all.sh - Meta script to build all Granville Orleans components

CONFIGURATION=${1:-Release}
SKIP_PACKAGING=false
SKIP_SAMPLES=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-packaging)
            SKIP_PACKAGING=true
            shift
            ;;
        --skip-samples)
            SKIP_SAMPLES=true
            shift
            ;;
        --configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done

echo -e "\e[32m=== Granville Orleans Complete Build ===\e[0m"
echo -e "\e[36mConfiguration: $CONFIGURATION\e[0m"

# Get the script directory and root
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$SCRIPT_DIR/../.."

cd "$ROOT_DIR" || exit 1

# Function to handle errors
handle_error() {
    echo -e "\n\e[31m=== Build Failed ===\e[0m"
    echo -e "\e[31m$1\e[0m"
    exit 1
}

# Step 1: Clean previous builds
echo -e "\n\e[33m=== Step 1: Cleaning previous builds ===\e[0m"
"$SCRIPT_DIR/clean-build-artifacts.ps1" || handle_error "Clean failed"

# Step 2: Build Granville Orleans core assemblies
echo -e "\n\e[33m=== Step 2: Building Granville Orleans assemblies ===\e[0m"
"$SCRIPT_DIR/build-granville.sh" "$CONFIGURATION" || handle_error "Granville Orleans build failed"

# Step 3: Build Granville RPC
echo -e "\n\e[33m=== Step 3: Building Granville RPC ===\e[0m"
if command -v pwsh &> /dev/null; then
    pwsh "$SCRIPT_DIR/build-granville-rpc.ps1" || handle_error "Granville RPC build failed"
else
    echo "PowerShell Core not found, trying Windows PowerShell..."
    powershell.exe -File "$SCRIPT_DIR/build-granville-rpc.ps1" || handle_error "Granville RPC build failed"
fi

# Step 4: Create NuGet packages (unless skipped)
if [ "$SKIP_PACKAGING" = false ]; then
    echo -e "\n\e[33m=== Step 4: Creating NuGet packages ===\e[0m"
    
    echo -e "\e[36mBuilding Orleans packages...\e[0m"
    if command -v pwsh &> /dev/null; then
        pwsh "$SCRIPT_DIR/build-orleans-packages.ps1" || handle_error "Orleans packaging failed"
    else
        powershell.exe -File "$SCRIPT_DIR/build-orleans-packages.ps1" || handle_error "Orleans packaging failed"
    fi
    
    echo -e "\e[36mBuilding RPC packages...\e[0m"
    if command -v pwsh &> /dev/null; then
        pwsh "$SCRIPT_DIR/build-granville-rpc-packages.ps1" || handle_error "RPC packaging failed"
    else
        powershell.exe -File "$SCRIPT_DIR/build-granville-rpc-packages.ps1" || handle_error "RPC packaging failed"
    fi
else
    echo -e "\n\e[33m=== Step 4: Skipping packaging (as requested) ===\e[0m"
fi

# Step 5: Build samples (unless skipped)
if [ "$SKIP_SAMPLES" = false ]; then
    echo -e "\n\e[33m=== Step 5: Building Shooter sample ===\e[0m"
    
    cd "$ROOT_DIR/granville/samples/Rpc" || handle_error "Cannot find samples directory"
    
    echo -e "\e[36mBuilding GranvilleSamples.sln...\e[0m"
    dotnet build GranvilleSamples.sln -c "$CONFIGURATION" || handle_error "Samples build failed"
    
    cd "$ROOT_DIR" || exit 1
else
    echo -e "\n\e[33m=== Step 5: Skipping samples (as requested) ===\e[0m"
fi

# Step 6: Summary
echo -e "\n\e[32m=== Build Complete! ===\e[0m"
echo -e "\e[36mBuilt components:\e[0m"
echo -e "  \e[90m✓ Granville Orleans core assemblies\e[0m"
echo -e "  \e[90m✓ Granville RPC libraries\e[0m"

if [ "$SKIP_PACKAGING" = false ]; then
    echo -e "  \e[90m✓ NuGet packages (in Artifacts/Release/)\e[0m"
fi

if [ "$SKIP_SAMPLES" = false ]; then
    echo -e "  \e[90m✓ Shooter sample application\e[0m"
fi

echo -e "\n\e[33mNext steps:\e[0m"
echo -e "  \e[90m- To run the sample: cd granville/samples/Rpc/Shooter.AppHost && dotnet run\e[0m"
echo -e "  \e[90m- To use packages: Setup local feed with ./granville/scripts/setup-local-feed.ps1\e[0m"

exit 0