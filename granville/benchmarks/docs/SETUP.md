# Benchmark Setup Guide

## Prerequisites

1. **.NET 8 SDK** or later
2. **PowerShell Core** (pwsh) for running scripts
3. **Local NuGet feed** configured for Granville packages
4. **Network tools** (optional):
   - Linux: `tc` for network emulation
   - Windows: Network emulation tools

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

### Micro-benchmarks

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

```bash
pwsh ./scripts/run-e2e-benchmarks.ps1
```

## Configuration

Benchmark configurations are stored in `/config/`:
- `default.json` - Default settings
- `stress.json` - High-load stress testing
- `network-impaired.json` - Simulated poor network conditions

## Monitoring

During benchmark execution, you can monitor:
- CPU and memory usage via Task Manager/htop
- Network traffic via Wireshark or tcpdump
- Application logs in `/logs/`

## Troubleshooting

1. **Port conflicts**: Ensure ports 7070-7080 are available
2. **Permission issues**: Run as administrator for network emulation
3. **Build failures**: Check NuGet feed configuration
4. **Performance issues**: Disable antivirus scanning for benchmark directories