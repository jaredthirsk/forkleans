#\!/bin/bash

analyze_project() {
    local project_path=$1
    local project_name=$(basename "$project_path")
    
    echo "=== $project_name ==="
    echo ""
    
    # Count public types
    echo "Public types count:"
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+(class < /dev/null | interface|struct|enum|delegate)\s+\w+" {} \; | wc -l
    
    echo ""
    echo "Breakdown by type:"
    echo -n "  Classes: "
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+class\s+\w+" {} \; | wc -l
    echo -n "  Interfaces: "
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+interface\s+\w+" {} \; | wc -l
    echo -n "  Structs: "
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+struct\s+\w+" {} \; | wc -l
    echo -n "  Enums: "
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+enum\s+\w+" {} \; | wc -l
    echo -n "  Delegates: "
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*public\s+delegate\s+\w+" {} \; | wc -l
    
    echo ""
    echo "Namespaces used:"
    find "$project_path" -name "*.cs" -type f -exec grep -hE "^\s*namespace\s+" {} \; | sed 's/^\s*namespace\s*//' | sed 's/[{;].*$//' | sort | uniq | head -10
    
    echo ""
    echo "----------------------------------------"
    echo ""
}

# Analyze each project
analyze_project "./src/Orleans.Core"
analyze_project "./src/Orleans.Runtime"
analyze_project "./src/Orleans.Serialization"
analyze_project "./src/Orleans.Core.Abstractions"
analyze_project "./src/Orleans.Serialization.Abstractions"

