# Directory Organization Strategy

## Purpose
This document defines a permanent, generic strategy for organizing files in software repositories. It helps maintain clean, navigable project structures and reduces cognitive overhead when working with codebases.

## Standard Directory Structure

### Core Directories (Keep in Root)
```
/
├── src/                    # Source code
├── test/                   # Test projects
├── docs/                   # Documentation
├── scripts/                # Build, deployment, and automation scripts
├── planning/               # Project planning documents
├── research/               # Experimental code, prototypes, and findings
├── logs/                   # Runtime logs (gitignored)
├── Artifacts/              # Build outputs (gitignored)
├── .gitignore             # Git exclusions
├── README.md              # Project overview
├── CLAUDE.md              # AI assistant instructions
└── [build files]          # Solution files, build configs, etc.
```

### Documentation Organization (`docs/`)
```
docs/
├── INDEX.md               # Documentation index
├── [active-docs].md       # Current, relevant documentation
├── historical/            # Archived documentation
│   └── [topic]/          # Organized by topic/feature
├── reference/            # Reference materials
└── roadmap/              # Planning and roadmap docs
```

### Scripts Organization (`scripts/`)
```
scripts/
├── README.md             # Scripts documentation
├── [active-scripts]      # Current, maintained scripts
└── archive/             # Old/deprecated scripts
```

### Research Organization (`research/`)
```
research/
├── [test-project]/       # Small experimental projects
├── [topic]/              # Research organized by topic
└── [standalone-tests]    # One-off test scripts
```

### Logs Organization (`logs/`)
```
logs/
├── ai-dev-loop/          # Automated development loop logs
├── test-runs/            # Test execution logs
└── [component]/          # Component-specific logs
```

## File Placement Guidelines

### By File Type

#### Markdown Documentation
- **README.md, CLAUDE.md, KNOWN-ISSUES.md** → Root (essential docs)
- **Feature/architectural docs** → `docs/`
- **Research findings** → `docs/historical/[topic]/` or `research/`
- **Planning documents** → `planning/`
- **TODO lists** → `planning/` or `docs/`

#### Scripts
- **Active automation scripts** → `scripts/`
- **One-off test scripts** → `research/`
- **Deprecated scripts** → `scripts/archive/`
- **Build scripts still in use** → `scripts/` or root if critical

#### Code Files
- **Production code** → `src/`
- **Tests** → `test/`
- **Experimental/debug code** → `research/`
- **One-off test files (*.cs, *.csx)** → `research/`

#### Logs
- **All log files** → `logs/` (must be gitignored)
- **Never commit logs** to version control

#### Build Artifacts
- **All build outputs** → `Artifacts/`, `bin/`, `obj/`, `dist/`
- **Must be gitignored**

### Decision Tree: Where Does This File Go?

```
Is it a log file or build artifact?
├─ YES → logs/ or Artifacts/ (and gitignore it)
└─ NO ↓

Is it production source code?
├─ YES → src/
└─ NO ↓

Is it a test?
├─ YES → test/ (if formal) or research/ (if experimental)
└─ NO ↓

Is it documentation?
├─ YES ↓
│   ├─ Essential/current? → docs/ (or root if critical like README)
│   └─ Historical/archived? → docs/historical/
└─ NO ↓

Is it a script?
├─ YES ↓
│   ├─ Active/maintained? → scripts/
│   ├─ One-off/experimental? → research/
│   └─ Deprecated? → scripts/archive/
└─ NO ↓

Is it experimental/research?
├─ YES → research/
└─ NO → planning/ or root (with good justification)
```

## .gitignore Recommendations

Every repository should have a `.gitignore` that excludes:

```gitignore
# Build outputs
bin/
obj/
Artifacts/
dist/
*.dll
*.exe
*.pdb

# IDE and editor files
.vs/
.vscode/
.idea/
*.suo
*.user
*.userprefs
.DS_Store

# Runtime files
logs/
*.log

# Package management
node_modules/
packages/
.nuget/

# OS files
Thumbs.db
Desktop.ini
```

## Cleanup Checklist

When cleaning up a repository, follow this checklist:

### Phase 1: Inventory
- [ ] List all files in root directory
- [ ] Identify file types and purposes
- [ ] Check file dates to identify stale content
- [ ] Review existing directory structure

### Phase 2: Categorize
- [ ] Mark production files (keep in place)
- [ ] Mark documentation (active vs historical)
- [ ] Mark scripts (active vs archived)
- [ ] Mark research/experimental files
- [ ] Mark logs and artifacts (delete or gitignore)

### Phase 3: Execute
- [ ] Create target directories if needed
- [ ] Move historical docs → `docs/historical/[topic]/`
- [ ] Move experimental scripts → `research/`
- [ ] Move deprecated scripts → `scripts/archive/`
- [ ] Move logs → `logs/[category]/` or delete
- [ ] Move research/test code → `research/`
- [ ] Delete empty directories
- [ ] Update or create `.gitignore`

### Phase 4: Validate
- [ ] Verify all important files are accessible
- [ ] Check that builds still work
- [ ] Update any documentation referencing moved files
- [ ] Commit changes with clear message

## Maintenance Procedures

### Weekly
- Move any new log files to `logs/` or delete them
- Check root directory for new clutter

### Monthly
- Review `research/` for completed experiments to archive or delete
- Move resolved documentation to `docs/historical/`
- Clean up old logs in `logs/`

### Per Feature/Fix
- When completing a feature:
  - Move planning docs to `docs/historical/`
  - Archive experimental code from `research/`
  - Clean up any temporary scripts

### Before Release
- Deep clean using the Cleanup Checklist
- Verify all temporary/debug code is removed or archived
- Ensure `.gitignore` is comprehensive

## Anti-Patterns to Avoid

### Don't Do This:
❌ Log files in root or scattered throughout project
❌ Test scripts mixed with production code
❌ Multiple documentation files for same topic in different locations
❌ Build artifacts committed to version control
❌ Deeply nested directory structures (> 4 levels without good reason)
❌ Inconsistent naming (mix of kebab-case, snake_case, PascalCase)
❌ Orphaned empty directories
❌ Temporary/experimental directories without clear ownership

### Do This Instead:
✅ All logs in `logs/` (gitignored)
✅ Test scripts in `research/` or `test/`
✅ Consolidate related docs, keep only one active version
✅ Gitignore all generated/build files
✅ Flat structure where possible, organize by purpose not by time
✅ Pick one naming convention per context (kebab-case for files, PascalCase for projects)
✅ Delete empty directories
✅ Move experiments to `research/[descriptive-name]/`

## Repository-Specific Adaptations

While this strategy is generic, adapt it to your repository's needs:

### For Libraries/Frameworks
- Add `samples/` for example code
- Add `docs/api/` for API documentation
- Consider `benchmarks/` for performance tests

### For Applications
- Consider `config/` for configuration files
- Consider `migrations/` for database migrations
- Consider `deployment/` for deployment configs

### For Monorepos
- Apply this structure at the root AND within each major project
- Use consistent organization across all sub-projects
- Consider `tools/` for shared tooling

## Summary

A clean repository structure:
- Reduces cognitive load
- Makes onboarding easier
- Improves maintainability
- Enables better collaboration
- Shows professionalism

Follow this guide to keep repositories organized, navigable, and production-ready.

---

*Last updated: 2025-10-07*
*Applies to: All software repositories*
