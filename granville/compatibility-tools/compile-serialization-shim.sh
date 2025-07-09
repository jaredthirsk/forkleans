#!/bin/bash

echo "Compiling Orleans.Serialization shim with Mono..."

# Check if mcs is available
if ! command -v mcs &> /dev/null; then
    echo "Mono C# compiler (mcs) not found. Installing mono-devel..."
    sudo apt-get update
    sudo apt-get install -y mono-devel
fi

# Compile the shim
mcs -target:library \
    -out:shims-proper/Orleans.Serialization.dll \
    -r:../../src/Orleans.Serialization/bin/Release/net8.0/Granville.Orleans.Serialization.dll \
    -r:../../src/Orleans.Serialization.Abstractions/bin/Release/net8.0/Granville.Orleans.Serialization.Abstractions.dll \
    -r:../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll \
    -nostdlib -r:/usr/lib/mono/4.5/mscorlib.dll \
    -r:/usr/lib/mono/4.5/System.dll \
    -r:/usr/lib/mono/4.5/System.Core.dll \
    -r:/usr/lib/mono/4.5/System.Runtime.dll \
    shims-proper/Orleans.Serialization.cs

if [ $? -eq 0 ]; then
    echo "✓ Successfully compiled Orleans.Serialization.dll"
else
    echo "✗ Compilation failed"
fi