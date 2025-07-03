#!/bin/bash
# Script to build Granville Orleans assemblies in correct order

CONFIGURATION=${1:-Release}

echo "Building Granville Orleans assemblies..."

# Clean previous builds
echo "Cleaning previous builds..."
find src -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
find src -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

# Build projects in dependency order
projects=(
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj"
    "src/Orleans.Serialization/Orleans.Serialization.csproj"
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj"
    "src/Orleans.Core/Orleans.Core.csproj"
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj"
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj"
    "src/Orleans.Runtime/Orleans.Runtime.csproj"
)

# Function to create compatibility copies
create_compatibility_copies() {
    local project_dir=$1
    local config=$2
    
    find "$project_dir/bin/$config" -name "Granville.Orleans.*.dll" 2>/dev/null | while read granville_dll; do
        orleans_name=$(basename "$granville_dll" | sed 's/^Granville\.//')
        orleans_path=$(dirname "$granville_dll")/"$orleans_name"
        
        # Always create/update the compatibility copy
        cp -f "$granville_dll" "$orleans_path"
        echo "  Created compatibility copy: $orleans_name"
    done
}

# Build each project
for project in "${projects[@]}"; do
    echo -e "\nBuilding $project..."
    
    if dotnet build "$project" -c "$CONFIGURATION" --no-dependencies 2>&1; then
        project_dir=$(dirname "$project")
        create_compatibility_copies "$project_dir" "$CONFIGURATION"
        echo "✓ Successfully built $project"
    else
        echo "  Build failed, retrying with dependencies..."
        if dotnet build "$project" -c "$CONFIGURATION" 2>&1; then
            project_dir=$(dirname "$project")
            create_compatibility_copies "$project_dir" "$CONFIGURATION"
            echo "✓ Successfully built $project (with dependencies)"
        else
            echo "✗ Failed to build $project"
            exit 1
        fi
    fi
done

echo -e "\n=== Build Summary ==="
echo "All Granville Orleans assemblies have been built successfully!"
echo "Assemblies are named Granville.Orleans.* with compatibility copies as Orleans.*"

# List all built assemblies
echo -e "\nBuilt assemblies:"
find src -path "*/bin/*" -name "Granville.Orleans.*.dll" 2>/dev/null | while read dll; do
    echo "  $dll"
done