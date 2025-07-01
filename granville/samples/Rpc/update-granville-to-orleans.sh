#!/bin/bash
# Script to update Granville namespace to Orleans in all .cs files

# Change to the samples/Rpc directory
cd /mnt/g/forks/orleans/samples/Rpc

# Find all .cs files and replace Granville with Orleans
find . -name "*.cs" -type f -not -path "./obj/*" -not -path "./bin/*" | while read -r file; do
    echo "Processing: $file"
    
    # Create a backup (optional - comment out if not needed)
    # cp "$file" "$file.bak"
    
    # Replace all Granville occurrences with Orleans
    sed -i \
        -e 's/using Granville;/using Orleans;/g' \
        -e 's/using Granville\.Concurrency;/using Orleans.Concurrency;/g' \
        -e 's/using Granville\.Runtime;/using Orleans.Runtime;/g' \
        -e 's/using Granville\.Timers;/using Orleans.Timers;/g' \
        -e 's/using Granville\.Utilities;/using Orleans.Utilities;/g' \
        -e 's/using Granville\.Configuration;/using Orleans.Configuration;/g' \
        -e 's/using Granville\.Hosting;/using Orleans.Hosting;/g' \
        -e 's/using Granville\.Metadata;/using Orleans.Metadata;/g' \
        -e 's/using Granville\.Serialization;/using Orleans.Serialization;/g' \
        -e 's/using Granville\.Rpc/using Orleans.Rpc/g' \
        -e 's/\[Granville\.GenerateSerializer\]/[Orleans.GenerateSerializer]/g' \
        -e 's/\[Granville\.Alias(/[Orleans.Alias(/g' \
        -e 's/\[Granville\.Id(/[Orleans.Id(/g' \
        -e 's/\[Granville\.Runtime\.PersistentState(/[Orleans.Runtime.PersistentState(/g' \
        -e 's/Granville\.Rpc\./Orleans.Rpc./g' \
        -e 's/Granville\.Runtime\./Orleans.Runtime./g' \
        -e 's/Granville\.IGrainFactory/Orleans.IGrainFactory/g' \
        -e 's/Granville\.IClusterClient/Orleans.IClusterClient/g' \
        -e 's/Granville\.GrainReferences\./Orleans.GrainReferences./g' \
        -e 's/Granville\.Grain/Orleans.Grain/g' \
        -e 's/Granville\.DeactivationReason/Orleans.DeactivationReason/g' \
        -e 's/GranvilleRpcGameClientService/OrleansRpcGameClientService/g' \
        -e 's/GranvilleCodeGen/OrleansCodeGen/g' \
        -e 's/"Granville\.Rpc"/"Orleans.Rpc"/g' \
        -e 's/"Microsoft\.Granville\./"Microsoft.Orleans./g' \
        -e 's/Granville RPC/Orleans RPC/g' \
        "$file"
done

echo "Replacement complete!"