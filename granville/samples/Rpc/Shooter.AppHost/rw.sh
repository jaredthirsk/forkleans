#!/bin/bash
./k.sh
rm ../logs/*.log -f

dotnet-win clean
dotnet-win run -- "$@"
#dotnet-win run -c Release -- "$@"


