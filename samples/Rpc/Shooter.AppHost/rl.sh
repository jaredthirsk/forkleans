#!/bin/bash
./k.sh
rm ../logs/*.log -f

dotnet clean
dotnet run -c Release -- "$@"



