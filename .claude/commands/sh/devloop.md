---
description: Run AI-driven development loop for Shooter debugging
---

# AI Development Loop

Start the AI-driven development loop that automatically monitors, detects errors, and waits for Claude to analyze and fix issues.

**Parameters:**
- Default: Skips clean step for faster iterations (20 min runtime)
- Use `--clean` argument to force clean build (slower but thorough)

**IMPORTANT FOR CLAUDE**: After starting this command in the background, you MUST proactively monitor the background bash process every 60-120 seconds by checking `BashOutput`. When the dev loop detects errors (you'll see "Still waiting for fix..." in the output), immediately:
1. Read `/mnt/c/forks/orleans/granville/samples/Rpc/ai-dev-loop/[latest-session]/last-error.txt`
2. Analyze the errors and apply fixes
3. Write "FIXED" to the `fix-complete.txt` file

Do NOT wait for the user to ask - monitor proactively!

```bash
#!/bin/bash
cd /mnt/c/forks/orleans/granville/samples/Rpc

# Parse arguments
CLEAN_ARG=""
for arg in "$@"; do
    if [[ "$arg" == "--clean" ]]; then
        CLEAN_ARG=""  # Don't pass -SkipClean (defaults to cleaning)
    fi
done

# Start the AI development loop in background
# Default: -SkipClean for faster iterations (20 min runtime allows for build)
if [[ -z "$CLEAN_ARG" && "$*" != *"--clean"* ]]; then
    pwsh ./scripts/ai-dev-loop.ps1 -RunDuration 1200 -MaxIterations 3 -SkipClean &
else
    pwsh ./scripts/ai-dev-loop.ps1 -RunDuration 1200 -MaxIterations 3 &
fi
```