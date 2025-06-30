# Shooter RPC Scripts

This directory contains utility scripts for managing the Shooter RPC sample.

## Process Management Scripts

### kill-shooter-processes.sh
Kills all running Shooter processes (Silo, ActionServer, Bot, Client).

```bash
./kill-shooter-processes.sh
```

Use this to clean up stale processes before starting fresh.

### show-shooter-processes.sh
Shows all running Shooter processes with details including:
- Process ID (PID)
- Component name
- Working directory
- Memory usage

```bash
./show-shooter-processes.sh
```

## Development Scripts

### trust-dev-cert.sh
Trusts the ASP.NET Core development certificate for HTTPS connections.

```bash
./trust-dev-cert.sh
```

Run this if you encounter SSL/TLS certificate errors when accessing HTTPS endpoints.

## Usage from Parent Directory

When running from the samples/Rpc directory:

```bash
scripts/kill-shooter-processes.sh
scripts/show-shooter-processes.sh
scripts/trust-dev-cert.sh
```

## Common Workflows

1. **Before starting services:**
   ```bash
   scripts/kill-shooter-processes.sh
   ```

2. **Check what's running:**
   ```bash
   scripts/show-shooter-processes.sh
   ```

3. **Fix HTTPS certificate issues:**
   ```bash
   scripts/trust-dev-cert.sh
   ```