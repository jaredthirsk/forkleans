# Shooter RPC Sample - Known Issues

This directory documents known issues with the Shooter RPC sample application, including active problems and resolved issues for historical reference.

## Issue Categories

### Active Issues
- **[001-client-hang](./001-client-hang/README.md)** - Client experiences 42-45 second hangs with no error messages
- **[002-signalr-disconnection](./002-signalr-disconnection/README.md)** - SignalR connections unexpectedly closing

### Resolved Issues
- **[003-ssl-certificate-resolved](./003-ssl-certificate-resolved/README.md)** - SSL certificate trust issue (RESOLVED 2025-09-25)

## Quick Status Summary

| Issue ID | Title | Status | Severity | Last Updated |
|----------|-------|--------|----------|--------------|
| 001 | Client Hang (42-45s) | **Active** | Critical | 2025-09-25 |
| 002 | SignalR Disconnection | **Active** | High | 2025-09-25 |
| 003 | SSL Certificate Trust | **Resolved** | Critical | 2025-09-25 |

## Monitoring & Detection

### AI Development Loop
The AI dev loop (`/scripts/ai-dev-loop.ps1`) continuously monitors for these issues with:
- Real-time log analysis
- Pattern matching for known errors
- Hang detection via log inactivity monitoring
- Automatic error report generation

### Enhanced Monitoring Features
- **ClientHeartbeatService** - Detects client unresponsiveness
- **Error Pattern Detection** - SSL, RPC, zone transition issues
- **Log Inactivity Detection** - Identifies hangs when logs stop

## Common Patterns

### Related Issues
- Issues 001 and 002 appear to be related - both occur around the same time
- The 42-second hang affects all components simultaneously
- SignalR disconnection may be a symptom of the broader hang issue

### Environment
- WSL2/Ubuntu environment
- .NET 9.0
- Aspire orchestration
- Orleans/Granville RPC

## Contributing
When adding new issues:
1. Create a new directory with format: `NNN-issue-name/`
2. Include a `README.md` with the standard template
3. Document evidence, symptoms, and potential causes
4. Update this index file

## Log Locations
- AI dev loop sessions: `/mnt/c/forks/orleans/ai-dev-loop/`
- Component logs: `../logs/`
- Aspire dashboard: http://localhost:15033

## Contact
For questions about these issues, refer to the main [Shooter RPC documentation](../CLAUDE.md) or the [Granville fork documentation](../../../../CLAUDE.md).
