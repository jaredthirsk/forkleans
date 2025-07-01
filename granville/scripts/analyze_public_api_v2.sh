#\!/bin/bash

analyze_project() {
    local project_path=$1
    local project_name=$(basename "$project_path")
    
    echo "=== $project_name ==="
    echo ""
    
    # Count public types more accurately
    echo "Public types (excluding nested and generic):"
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*(public\s+)?(abstract\s+ < /dev/null | sealed\s+|static\s+|partial\s+)*(public\s+)?(class|interface|struct|enum|delegate)\s+[A-Za-z_][A-Za-z0-9_]*(\s|$|<)" {} \; | grep -E "public" | wc -l
    
    echo ""
    echo "Sample public types:"
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*(public\s+)?(abstract\s+|sealed\s+|static\s+|partial\s+)*(public\s+)?(class|interface|struct|enum|delegate)\s+[A-Za-z_][A-Za-z0-9_]*" {} \; | grep -E "public" | head -10
    
    echo ""
    echo "All unique namespaces:"
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*namespace\s+" {} \; | sed 's/^\s*namespace\s*//' | sed 's/[{;].*$//' | sort | uniq | wc -l
    echo ""
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*namespace\s+" {} \; | sed 's/^\s*namespace\s*//' | sed 's/[{;].*$//' | sort | uniq | head -15
    
    echo ""
    echo "----------------------------------------"
    echo ""
}

# Analyze each project
analyze_project "./src/Orleans.Core"
analyze_project "./src/Orleans.Runtime"
analyze_project "./src/Orleans.Serialization"

