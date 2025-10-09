# Zone Transition Debugging Documentation

This directory contains comprehensive documentation about debugging zone transition issues in the Granville RPC Shooter sample application.

## Overview

Zone transitions are a critical feature in the distributed shooter game where players move between different zones managed by separate ActionServer instances. When working correctly, transitions should be seamless and complete within ~0.5 seconds.

## Documentation Structure

### Core Documentation
- **`fixes-applied.md`** - Detailed list of all successful fixes with code changes
- **`remaining-challenges.md`** - Current issues that still need resolution
- **`lessons-learned.md`** - Key insights and patterns discovered during debugging
- **`critical-components.md`** - Components that must be preserved and understanding of their purpose
- **`session-summary.md`** - Complete summary of the debugging session

### Operational Guides  
- **`debugging-techniques.md`** - Effective methods for diagnosing zone transition issues
- **`anomaly-reference.md`** - Complete guide to all warning messages and their meanings
- **`troubleshooting-playbook.md`** - Step-by-step procedures for common issues
- **`configuration-reference.md`** - Complete reference of all configuration parameters
- **`performance-analysis.md`** - Performance monitoring and optimization techniques
- **`quick-reference-scripts.md`** - Copy-paste diagnostic tools and monitoring commands

### Development Resources
- **`code-patterns.md`** - Important code patterns and anti-patterns identified  
- **`testing-strategies.md`** - Comprehensive testing approaches and scenarios
- **`architecture-deep-dive.md`** - In-depth system architecture and component interactions

### Additional Resources
- **`INDEX.md`** - Navigation guide organized by topic, task, symptom, and role
- **`historical/`** - Previous zone transition documentation (superseded but preserved for reference)

## Quick Reference

### Most Common Issues Encountered
1. **Chronic Zone Mismatch Spam** - Health monitor detecting mismatches on every world state update
2. **RPC Timeout Crashes** - 30-second timeouts causing client crashes  
3. **False Zone Change Detection** - Incorrect triggering of zone transitions
4. **Failed Zone Transitions** - Transitions initiated but not completing properly

### Key Files Modified
- `Shooter.Client.Common/GranvilleRpcGameClientService.cs` - Main zone transition logic
- `Shooter.Client.Common/ZoneTransitionHealthMonitor.cs` - Health monitoring and mismatch detection
- `Shooter.Client.Common/ZoneTransitionDebouncer.cs` - Zone boundary hysteresis and debouncing
- `Shooter.Bot/Services/BotService.cs` - Bot input handling

### Primary Tools for Debugging
- Log analysis with grep patterns for `[HEALTH_MONITOR]`, `[ZONE_TRANSITION]`, `[ZONE_DEBOUNCE]`
- Health reports logged every 30 seconds showing success rates
- Zone transition timing logs showing duration of transitions

## 🚀 Quick Start Guide

### If You're New to Zone Transition Debugging
1. **Start Here**: Read `session-summary.md` for context
2. **Understand the System**: Review `architecture-deep-dive.md`
3. **Learn the Tools**: Study `debugging-techniques.md`
4. **When Issues Arise**: Use `troubleshooting-playbook.md`

### If You're Investigating a Specific Issue
1. **Immediate Problems**: Run scripts from `quick-reference-scripts.md`
2. **Performance Issues**: Use methods from `performance-analysis.md`
3. **Configuration Problems**: Consult `configuration-reference.md`
4. **Development Work**: Follow patterns in `code-patterns.md`

### If You're Making Changes
1. **Before Changing**: Review `critical-components.md` (DO NOT MODIFY list)
2. **Testing Approach**: Use strategies from `testing-strategies.md`
3. **After Changes**: Verify using scripts in `quick-reference-scripts.md`

## Status as of Latest Session

✅ **Resolved**: RPC timeout crashes, grain observer exception spam, explosive chronic mismatch growth
🔧 **Partial**: Zone transition logic (working but with some failures)  
❌ **Remaining**: Root cause of failed zone transitions causing slow mismatch accumulation

## 🆘 Emergency Procedures

### System Completely Broken
```bash
# Emergency health check
cd granville/samples/Rpc  
./quick-health-check.sh  # (from quick-reference-scripts.md)
```

### Performance Degradation
```bash
# Performance dashboard
./performance-dashboard.sh  # (from quick-reference-scripts.md)
```

### Need Immediate Fix Verification
```bash
# Verify critical fixes are in place
./apply-emergency-fixes.sh  # (from quick-reference-scripts.md)
```