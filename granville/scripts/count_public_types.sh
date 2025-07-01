#\!/bin/bash

count_types_in_project() {
    local project=$1
    echo "=== $project ==="
    
    # Find all public type declarations with better regex
    echo "Counting public types..."
    
    # Count classes (excluding nested)
    classes=$(find "$project" -name "*.cs" -type f -exec grep -P "^\s*(public\s+)?(abstract\s+ < /dev/null | sealed\s+|static\s+|partial\s+)*(public\s+)?class\s+\w+(?!.*\{.*\})" {} \; | grep -c "public")
    
    # Count interfaces
    interfaces=$(find "$project" -name "*.cs" -type f -exec grep -P "^\s*public\s+(partial\s+)?interface\s+\w+" {} \; | wc -l)
    
    # Count structs
    structs=$(find "$project" -name "*.cs" -type f -exec grep -P "^\s*public\s+(readonly\s+)?(partial\s+)?struct\s+\w+" {} \; | wc -l)
    
    # Count enums
    enums=$(find "$project" -name "*.cs" -type f -exec grep -P "^\s*public\s+enum\s+\w+" {} \; | wc -l)
    
    # Count delegates
    delegates=$(find "$project" -name "*.cs" -type f -exec grep -P "^\s*public\s+delegate\s+" {} \; | wc -l)
    
    total=$((classes + interfaces + structs + enums + delegates))
    
    echo "  Classes: $classes"
    echo "  Interfaces: $interfaces"
    echo "  Structs: $structs"
    echo "  Enums: $enums"
    echo "  Delegates: $delegates"
    echo "  Total: $total"
    echo ""
}

# Count for each project
count_types_in_project "./src/Orleans.Core"
count_types_in_project "./src/Orleans.Core.Abstractions"
count_types_in_project "./src/Orleans.Runtime"
count_types_in_project "./src/Orleans.Serialization"
count_types_in_project "./src/Orleans.Serialization.Abstractions"

# Total across all projects
echo "=== SUMMARY ==="
echo "Combined totals for type forwarding scope"
