#!/bin/bash

echo "Trusting ASP.NET Core development certificate..."

# Check if dotnet is available
if ! command -v dotnet &> /dev/null; then
    echo "Error: dotnet CLI is not installed or not in PATH"
    exit 1
fi

# Export the development certificate
echo "Exporting development certificate..."
if ! dotnet dev-certs https -ep /tmp/localhost.crt --format PEM; then
    echo "Error: Failed to export development certificate"
    echo "Try running: dotnet dev-certs https --trust"
    exit 1
fi

# Check if certificate file was created
if [ ! -f "/tmp/localhost.crt" ]; then
    echo "Error: Certificate file was not created"
    exit 1
fi

CERT_INSTALLED=false

# For Ubuntu/Debian systems
if [ -d "/usr/local/share/ca-certificates" ]; then
    echo "Installing certificate for Ubuntu/Debian..."
    if sudo cp /tmp/localhost.crt /usr/local/share/ca-certificates/aspnetcore-localhost.crt; then
        sudo update-ca-certificates
        CERT_INSTALLED=true
        echo "✓ Certificate installed for Ubuntu/Debian"
    else
        echo "✗ Failed to install certificate for Ubuntu/Debian"
    fi
fi

# For RHEL/CentOS/Fedora systems
if [ -d "/etc/pki/ca-trust/source/anchors" ]; then
    echo "Installing certificate for RHEL/CentOS/Fedora..."
    if sudo cp /tmp/localhost.crt /etc/pki/ca-trust/source/anchors/aspnetcore-localhost.crt; then
        sudo update-ca-trust
        CERT_INSTALLED=true
        echo "✓ Certificate installed for RHEL/CentOS/Fedora"
    else
        echo "✗ Failed to install certificate for RHEL/CentOS/Fedora"
    fi
fi

# Clean up
rm /tmp/localhost.crt

if [ "$CERT_INSTALLED" = true ]; then
    echo ""
    echo "✓ Development certificate has been trusted system-wide."
    echo "You may need to restart your applications for the changes to take effect."
    echo ""
    echo "To verify the certificate is trusted, you can:"
    echo "1. Open https://localhost:7071 in your browser"
    echo "2. Check that there are no certificate warnings"
else
    echo ""
    echo "✗ Could not install certificate - unsupported system"
    echo "You may need to manually trust the certificate:"
    echo "1. Run: dotnet dev-certs https --trust"
    echo "2. Or manually add the certificate to your system's trust store"
fi