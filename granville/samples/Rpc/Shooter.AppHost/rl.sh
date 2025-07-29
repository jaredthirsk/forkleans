#!/bin/bash
./k.sh
rm /mnt/c/forks/orleans/granville/samples/Rpc/logs/*.log -f

dotnet clean
#dotnet run -- "$@"
dotnet run -c Release -- "$@"
