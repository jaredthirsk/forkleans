# Issue 003: SSL Certificate Trust (RESOLVED)

## Summary
Development SSL certificate was not trusted by the system, causing connection failures for bots and clients.

## Status
**RESOLVED** - Fixed on 2025-09-25

## Symptoms (Historical)
- Bots failed to connect to Silo
- SSL connection errors in logs
- SignalR connections failed
- Error: `UntrustedRoot` certificate chain

## Evidence

### From AI Dev Loop (2025-09-25 17:37)
```
System.Security.Authentication.AuthenticationException:
The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot

System.Net.Http.HttpRequestException:
The SSL connection could not be established, see inner exception.
```

Multiple components affected:
- Bot connections to Silo
- Client SignalR connections
- All HTTPS communications

## Root Cause
The ASP.NET Core development certificate was not trusted at the system level in the WSL2/Ubuntu environment.

## Resolution

### Steps Taken (2025-09-25)
1. Exported the ASP.NET Core development certificate:
   ```bash
   dotnet dev-certs https --export-path /tmp/aspnetcore-cert.crt --format Pem
   ```

2. Added certificate to system trust store (with sudo):
   ```bash
   sudo cp /tmp/aspnetcore-cert.crt /usr/local/share/ca-certificates/aspnetcore-dev.crt
   sudo update-ca-certificates
   ```

3. Verified certificate installation:
   ```bash
   ls -la /etc/ssl/certs/ | grep aspnet
   # Output shows:
   # aspnetcore-dev.pem -> /usr/local/share/ca-certificates/aspnetcore-dev.crt
   ```

### Result
- ✅ SSL certificate now trusted system-wide
- ✅ Bots can connect successfully
- ✅ SignalR connections work properly
- ✅ No more `UntrustedRoot` errors

## Prevention
For future development environments:
1. Always trust development certificates after setup
2. Document certificate trust process in setup guide
3. Include certificate trust check in startup diagnostics

## Monitoring
The AI dev loop now includes patterns to detect SSL certificate issues:
- `SSL connection could not be established`
- `UntrustedRoot`
- Certificate chain errors

## Files Modified
- System: `/usr/local/share/ca-certificates/aspnetcore-dev.crt` (added)
- System: `/etc/ssl/certs/` (updated with symlinks)

## Related Documentation
- [Microsoft Docs: Trust HTTPS certificate](https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl#trust-the-aspnet-core-https-development-certificate)
- [WSL2 Certificate Trust Guide](https://github.com/microsoft/WSL/issues/5125)