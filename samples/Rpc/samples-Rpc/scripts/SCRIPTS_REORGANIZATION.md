# Scripts Reorganization Summary

## What Was Done

Moved all shell scripts from the root `samples/Rpc/` directory to a dedicated `scripts/` subdirectory.

### Scripts Moved

1. **kill-shooter-processes.sh** - Process cleanup utility
2. **show-shooter-processes.sh** - Process monitoring utility  
3. **trust-dev-cert.sh** - Development certificate trust utility

### Updated References

Updated all documentation and scripts that referenced these utilities:

1. **CLAUDE.md** - Updated all script paths to `scripts/`
2. **test/manual-integration-test.sh** - Updated references to kill-shooter-processes.sh

### New Structure

```
samples/Rpc/
├── scripts/                      # All utility scripts
│   ├── README.md                # Scripts documentation
│   ├── SCRIPTS_REORGANIZATION.md # This file
│   ├── kill-shooter-processes.sh
│   ├── show-shooter-processes.sh
│   └── trust-dev-cert.sh
├── test/                        # Test scripts and projects
├── docs/                        # Documentation
└── ...                          # Project directories
```

### Benefits

1. **Cleaner Root Directory** - Scripts organized in dedicated directory
2. **Better Organization** - Clear separation of utilities from project code
3. **Easier Discovery** - All scripts in one location with documentation
4. **Consistent Structure** - Follows pattern of test/ and docs/ subdirectories

### Usage

From the `samples/Rpc` directory:

```bash
# Process management
scripts/kill-shooter-processes.sh
scripts/show-shooter-processes.sh

# Development utilities
scripts/trust-dev-cert.sh
```