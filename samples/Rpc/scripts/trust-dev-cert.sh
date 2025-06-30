#!/bin/bash

# Export the development certificate
dotnet dev-certs https -ep /tmp/localhost.crt --format PEM

# For Ubuntu/Debian systems
if [ -d "/usr/local/share/ca-certificates" ]; then
    sudo cp /tmp/localhost.crt /usr/local/share/ca-certificates/aspnetcore-localhost.crt
    sudo update-ca-certificates
fi

# For RHEL/CentOS/Fedora systems
if [ -d "/etc/pki/ca-trust/source/anchors" ]; then
    sudo cp /tmp/localhost.crt /etc/pki/ca-trust/source/anchors/aspnetcore-localhost.crt
    sudo update-ca-trust
fi

# Clean up
rm /tmp/localhost.crt

echo "Development certificate has been trusted system-wide."
echo "You may need to restart your applications for the changes to take effect."