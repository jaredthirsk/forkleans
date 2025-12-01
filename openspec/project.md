# Project Context

## Purpose

This project is a fork of Microsoft Orleans that adds Granville RPC functionality for high-performance UDP-based communication while maintaining full compatibility with the Orleans ecosystem.

**Key Goals:**
- Add Granville RPC capabilities to Orleans for high-performance, UDP-based grain communication
- Rename assemblies from `Microsoft.Orleans.*` to `Granville.Orleans.*` to avoid NuGet namespace conflicts
- Maintain compatibility with third-party Orleans packages (e.g., UFX.Orleans.SignalRBackplane)
- Keep the fork production-ready and synchronized with Orleans upstream releases
- Provide multiple compatibility approaches for consumers (side-by-side or assembly redirection)

## Tech Stack

**Primary Technologies:**
- .NET 8.0+ / C# 12
- Microsoft Orleans (distributed systems framework)
- Granville RPC (custom UDP-based RPC protocol)
- ASP.NET Core & Blazor Server (for sample applications)
- SignalR (for real-time UI updates in Blazor)
- Phaser.js (for game client in Shooter sample)
- PowerShell Core (build scripts and automation)

**Build & Development Tools:**
- MSBuild / dotnet CLI
- NuGet (package management)
- Git (version control)
- Inno Setup Compiler (Windows installers)
- WSL2 (Linux development on Windows)

**Testing:**
- xUnit (Orleans test suite)
- Integration tests for RPC functionality

## Project Conventions

### Code Style

**HJSON File Convention:**
- Always omit root braces in HJSON files for cleaner, more readable configuration
- See `/st/dev/.agent-os/standards/code-style/hjson.md` for complete style guide

**Naming Conventions:**
1. **Official Orleans** (Microsoft NuGet packages):
   - Package prefix: `Microsoft.Orleans`
   - DLL prefix: `Orleans`
   - C# Namespace: `Orleans`

2. **Granville Shim** (compatibility layer):
   - Package prefix: `Microsoft.Orleans`
   - Package version suffix: `-granville-shim`
   - DLL prefix: `Orleans`
   - C# Namespace: `Orleans`

3. **Granville** (our packages):
   - Package prefix: `Granville`
   - DLL prefix: `Granville`

4. **Granville RPC** (original features):
   - Package prefix: `Granville.Rpc`
   - DLL prefix: `Granville.Rpc`
   - C# Namespace: `Granville.Rpc`

5. **Granville Orleans** (forked assemblies):
   - Package prefix: `Granville.Orleans`
   - DLL prefix: `Granville.Orleans`
   - C# Namespace: `Orleans` (maintain Orleans namespace)

**File Organization:**
- Prefer PowerShell scripts (.ps1) over bash (.sh) unless only practical in bash
- PowerShell scripts must start with `#!/usr/bin/env pwsh`
- Use Linux newlines or dos2unix for cross-platform compatibility

### Architecture Patterns

**Repository Organization:**
- Orleans upstream files kept as untouched as possible
- All Granville-specific additions under `/granville/`
- Exception: `/src/Rpc/` could be considered for Orleans upstream adoption
- Modifications to upstream documented in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`

**Assembly Compatibility:**
Two approaches supported for third-party package compatibility:
1. **Type Forwarding** (Option 1): Shim assemblies that forward types to Granville assemblies
2. **Assembly Redirects** (Option 2): Runtime redirection of assembly loads

See `/granville/compatibility-tools/README.md` and `/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`

**Dependency Injection:**
- Granville RPC uses keyed service registration to coexist with Orleans in the same DI container
- Both Orleans `IGrainFactory` and RPC `IGrainFactory` can exist together (RPC uses "rpc" key)
- Follow Orleans patterns for circular dependencies: constructor injection vs. `ConsumeServices()` method

**Build System:**
- MSBuild customizations in `/Directory.Build.targets` for assembly renaming
- `/Directory.Build.props` contains `<GranvilleRevision>` for versioning
- Orleans code generation disabled (`Orleans_DesignTimeBuild=true`)
- Granville code generation enabled (`Granville_DesignTimeBuild=false`)

### Testing Strategy

**Goals:**
- Orleans solution (`Orleans.sln`) must still build properly
- Orleans tests in `/test/` directory must continue working
- Demonstrates minimal changes to Orleans core
- Granville RPC functionality tested through sample applications and integration tests

**Sample Applications:**
- Shooter sample serves as reference implementation for Granville RPC consumers
- Uses package references (not project references) to mirror real-world usage

### Git Workflow

**Branching:**
- Main branch: `main`
- Fork maintenance focused on keeping up with Orleans upstream releases

**Commit Conventions:**
- Use conventional commit format (feat, fix, chore, docs, etc.)
- Include emoji and Claude Code attribution in automated commits
- Document all upstream modifications in fork maintenance files

**Version Management:**
- Follow Orleans versioning (e.g., 9.1.2) with added 4th revision number (e.g., 9.1.2.50)
- Bump revision using `/granville/scripts/bump-granville-version.ps1` before building
- Aggressive revision bumping acceptable to avoid version conflicts

## Domain Context

**Orleans Fundamentals:**
- Virtual Actor model (Grains)
- Distributed systems framework for .NET
- Location transparency and automatic activation
- Built-in persistence, streaming, and clustering

**Granville RPC Additions:**
- High-performance UDP-based communication protocol
- Designed for low-latency, high-throughput scenarios
- Complements Orleans' standard TCP communication
- Example use case: Real-time multiplayer games (Shooter sample)

**Key Concepts:**
- **Grains**: Virtual actors that represent game entities, services, or state
- **Silos**: Server nodes that host grains
- **Clusters**: Collections of silos working together
- **Zone Transitions**: Moving player grains between different game zones (RPC-specific)

**Blazor Server & SignalR:**
- Blazor Server natively uses SignalR for real-time communication
- Can use MediatR to enable Blazor components to notify each other
- Suitable for real-time UI updates without additional WebSocket infrastructure

## Important Constraints

**Production-Ready Requirement:**
- All code, scripts, and configurations must be production-quality
- Temporary workarounds must be cleaned up quickly
- Repository should always be in a polished, maintainable state

**Upstream Compatibility:**
- Must be able to adopt changes from Orleans upstream
- Fork maintenance scripts should restore production-ready state after merges
- Breaking changes from Orleans require careful handling

**Third-Party Package Support:**
- Consumers must be able to use third-party Orleans extensions
- Support both side-by-side usage and assembly redirection approaches
- Avoid impersonating official Microsoft packages

**Platform Constraints:**
- WSL2 environment with Windows filesystem mounts (`/mnt/c`, `/mnt/d`, etc.)
- Use Windows executables for git authentication, nuget, dotnet on Windows filesystems
- Wrapper commands: `pwsh_win`, `git_win`, `nuget_win`, `dotnet_win`

**Process Management:**
- Never kill all dotnet processes - be specific to avoid disrupting other services
- Multiple dotnet processes run concurrently on development machine

## External Dependencies

**Key External Services:**
- NuGet.org (Orleans packages and dependencies)
- Microsoft Orleans upstream repository
- Local NuGet feed at `/Artifacts/Release/`

**Build Dependencies:**
- Windows: Git for Windows, PowerShell, .NET SDK, Inno Setup Compiler
- Linux: dotnet CLI, PowerShell Core
- Cross-platform: nuget.exe, CS-Script (via Chocolatey on Windows)

**Third-Party Orleans Packages (Examples):**
- UFX.Orleans.SignalRBackplane
- Various Orleans providers and extensions from NuGet ecosystem

**Development Tools:**
- ildasm (from .NET Core 3+) for assembly inspection
- dos2unix for line ending conversion

## Directory Structure Reference

See `/granville/REPO-ORGANIZATION.md` for comprehensive repository structure documentation.

**Key Directories:**
- `/Artifacts/Release/` - Local NuGet feed and package output
- `/granville/` - All Granville-specific content
  - `/granville/scripts/` - Build and maintenance scripts
  - `/granville/docs/` - Granville-specific documentation
  - `/granville/samples/` - Sample applications (Shooter game)
  - `/granville/compatibility-tools/` - Type-forwarding shims
  - `/granville/fork-maintenance/` - Upstream modification documentation
- `/src/Rpc/` - Granville RPC implementation (candidate for upstream)

**Build Entry Points:**
- `/granville/samples/Rpc/Shooter.AppHost/rl.sh` - Run Shooter sample
- `/granville/scripts/build-all-granville.ps1` - Complete build pipeline

## Source Repository Organization Conventions

Following personal conventions for planning and development:
- `plan/` - Project planning documents
  - `plan/PRD.md` - Product Requirements Document
  - `plan/tasks.md` - Master tasks list
  - `plan/done.md` - Completed tasks (if tasks.md grows large)
  - `plan/clarifying-questions.md` - Questions for specification clarity
- `research/` - Temporary test projects for troubleshooting (clean up when obsolete)
- `src/` - Main source code
- `artifacts/` or `dist/` - Build output artifacts
- `test/` - Test projects
