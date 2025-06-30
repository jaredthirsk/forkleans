# Health Check Implementation for Shooter Services

This document describes the health check system implemented for the Shooter sample services to provide explicit readiness signals.

## Overview

The health check system addresses the "Services Never Signal Ready" problem by implementing ASP.NET Core health checks that provide explicit readiness endpoints for each service.

## Implementation Details

### 1. Silo Health Check

**File**: `Shooter.Silo/HealthChecks/OrleansHealthCheck.cs`

The Silo health check verifies:
- Application has started
- Orleans cluster is accessible
- WorldManagerGrain is responsive
- Can retrieve list of ActionServers

**Endpoints**:
- `/health` - General health status
- `/health/ready` - Readiness check (tagged with "ready")

### 2. ActionServer Health Check

**File**: `Shooter.ActionServer/HealthChecks/ActionServerHealthCheck.cs`

The ActionServer health check verifies:
- Application has started
- RPC server is running (checks endpoint and server ID)
- Orleans client is connected
- Zone assignment is complete
- Can communicate with WorldManagerGrain

**Endpoints**:
- `/health` - General health status
- `/health/ready` - Readiness check (tagged with "ready")

### 3. Bot Service

The Bot service is a console application and doesn't expose HTTP endpoints. Readiness is determined by:
- Checking if the bot log file exists
- Parsing log file for connection confirmation messages

## ShooterTestFixture Updates

The `ShooterTestFixture` has been updated to use the health endpoints instead of just checking for log files:

1. **Silo Readiness**: Polls `https://localhost:7071/health/ready`
2. **ActionServer Readiness**: 
   - Extracts HTTP port from log files
   - Polls `http://localhost:{port}/health/ready`
3. **Bot Readiness**: 
   - Checks log files for connection confirmation

### Features:
- Progress reporting during startup
- Detailed diagnostics on timeout
- SSL certificate validation bypass for test environment
- Configurable timeout (default: 120 seconds)

## Benefits

1. **Explicit Readiness Signals**: Services now explicitly report when they're ready to handle requests
2. **Better Diagnostics**: Health endpoints provide detailed information about service state
3. **Faster Test Startup**: Tests can start as soon as services are ready instead of using fixed delays
4. **More Reliable**: Reduces flaky tests caused by timing issues

## Usage

The health checks are automatically registered when the services start. No additional configuration is required.

To manually check service health:
```bash
# Check Silo health
curl https://localhost:7071/health/ready

# Check ActionServer health (replace port with actual port)
curl http://localhost:5000/health/ready
```

The response will be:
- HTTP 200 with status "Healthy" when ready
- HTTP 503 with status "Degraded" or "Unhealthy" when not ready