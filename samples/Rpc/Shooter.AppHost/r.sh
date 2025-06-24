#!/bin/bash
./k.sh
rm ../Shooter.Silo/logs/*.log
rm ../Shooter.Client/logs/client.log
rm ../Shooter.ActionServer/logs/actionserver*.log
rm ../Shooter.Bot/logs/*.log
rm logs/apphost.log

dotnet clean
dotnet run -c Release -- "$@"



