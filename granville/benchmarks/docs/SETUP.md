# Benchmark Setup Guide

## Prerequisites

1. **.NET 8 SDK** or later
2. **PowerShell Core** (pwsh) for running scripts
3. **Local NuGet feed** configured for Granville packages
4. **Network emulation tools** (optional but recommended):
   - Linux: `tc` (Traffic Control) - usually pre-installed
   - Windows: clumsy from https://jagt.github.io/clumsy/
   - Administrator/sudo privileges for system-level emulation

## Initial Setup

1. **Build Granville Orleans**:
   ```bash
   pwsh ./granville/scripts/build-granville-orleans.ps1
   ```

2. **Build RPC Components**:
   ```bash
   cd src/Rpc
   dotnet build -c Release
   ```

3. **Build Benchmark Projects**:
   ```bash
   cd granville/benchmarks
   dotnet build -c Release
   ```

## Running Benchmarks

### Micro-benchmarks (BenchmarkDotNet)

Quick run for development:
```bash
pwsh ./scripts/run-microbenchmarks.ps1 -Quick
```

Full run:
```bash
pwsh ./scripts/run-microbenchmarks.ps1
```

Filter specific benchmarks:
```bash
pwsh ./scripts/run-microbenchmarks.ps1 -Filter "*Latency*"
```

### End-to-End Benchmarks

Basic E2E test:
```bash
pwsh ./scripts/run-e2e-benchmarks.ps1
```

### Network Condition Testing

Test all network profiles (Perfect → Satellite):
```bash
pwsh ./scripts/test-network-conditions.ps1
```

### MMO Scaling Tests

Test zone-based player distribution:
```bash
pwsh ./scripts/test-mmo-scaling.ps1
```

### Stress Testing

Connection storms, burst traffic, error injection:
```bash
pwsh ./scripts/test-stress-conditions.ps1
```

## Configuration

Benchmark configurations are stored in `/config/`:
- `default.json` - Default settings for basic testing
- `stress.json` - High-load stress testing scenarios
- `network-condition-test.json` - Network emulation testing
- `mmo-scaling-test.json` - MMO workload scaling tests
- `raw-transport-test.json` - Pure transport overhead measurement
- `simulation-transport-test.json` - Application-level network simulation

See [NETWORK-EMULATION.md](NETWORK-EMULATION.md) for detailed network condition configuration.

## Monitoring

During benchmark execution, you can monitor:
- CPU and memory usage via Task Manager/htop
- Network traffic via Wireshark or tcpdump
- Application logs in `/logs/`

## Troubleshooting

### Common Issues

1. **Port conflicts**: Ensure ports 7070-7080 are available for test servers
2. **Build failures**: 
   - Check NuGet feed configuration for Granville packages
   - Verify local Orleans build completed: `pwsh ./granville/scripts/build-granville-orleans.ps1`
   - Path issues: Use `./src/` not `../src/` in benchmark project references

### Network Emulation Issues

3. **Permission denied (Linux)**:
   ```bash
   sudo usermod -a -G sudo $USER  # Add user to sudo group
   # Re-login or run: newgrp sudo
   ```

4. **Network emulation not working (Windows)**:
   - Install clumsy from https://jagt.github.io/clumsy/
   - Run PowerShell as Administrator
   - Check Windows Defender/antivirus isn't blocking clumsy

5. **tc command not found (Linux)**:
   ```bash
   sudo apt install iproute2  # Ubuntu/Debian
   sudo yum install iproute   # RHEL/CentOS
   ```

### Performance Issues

6. **Inconsistent results**: 
   - Disable antivirus scanning for benchmark directories
   - Close other network-intensive applications
   - Use dedicated test network interface if available
   - Run multiple iterations for statistical significance

7. **High variance in measurements**:
   - Check for background processes consuming CPU
   - Ensure stable power settings (disable CPU scaling)
   - Consider using system-level network emulation instead of application-level