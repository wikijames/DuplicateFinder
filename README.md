# DuplicateFinder

An independently developed, cross-platform duplicate-file finder. Windows desktop is the first target; Android is a future goal.

## Status (15 November 2026)

**Phase 3 review workflow landed; Phase 4 feasibility work starting.** The solution now includes safe file scanning, exact duplicate detection, an Avalonia desktop review flow, and explicit confirmation before cleanup. The next milestone is DF-401: evaluate image-similarity methods and cross-platform decoding constraints. Exact byte matching remains separate from any future visual-similarity results.

## Goals

1. Recursively discover files with low memory overhead, explicit handling of inaccessible folders, reparse points and cancellation.
2. Filter by size, extension, path and file attributes.
3. Find exact duplicates through size grouping, optional sampled hashing, streaming full-file hashing and collision-safe verification where warranted.
4. Later: visually similar images and videos (a separate feature from exact byte duplicates).
5. Show clear scan progress, grouped results, previews and review-first actions.
6. Support safe move-to-recycle-bin or quarantine/backup before any permanent deletion; never delete automatically.
7. Benchmark on SSD/HDD and large directories without assuming unlimited memory.
8. Reuse a platform-neutral C# core for a future Android UI, adapting to Android's scoped-storage permissions and APIs.

## Technology and architecture

- C#, .NET 10, Avalonia UI for Windows; Android support is a *planned feasibility target*, not yet proven.
- Keep `DuplicateFinder.Core` independent of UI and Windows-specific APIs.
- Use xUnit and small, deterministic test fixtures; avoid destructive tests on user folders.
- Prefer bounded concurrency and streaming I/O. Measure before optimizing.
- Suggested future layout: `src/DuplicateFinder.Desktop`, `tests/`, `docs/`, `.github/workflows/`.

## Development process

- Small changes, build and test after each step, review diffs before committing.
- `main` stays stable; use `feature/...` branches and PRs for meaningful features.
- No automatic pushes, repository publishing or destructive actions without approval.
- Maintain an issue/roadmap for each milestone.
- Never commit credentials, personal files, diagnostic dumps, licensed third-party binaries or private research artifacts.

## Independent implementation

This project implements general-purpose file management functionality using original code, public documentation and independently authored behavioral specifications. It does not include third-party proprietary code, icons, assets, extracted resources or licensing mechanisms. Any study of other software must respect its EULA and applicable law, be performed in an isolated environment, and remain separate from implementation. Preserve accurate attribution and provenance in appropriate private records; do not disguise copied material.

## Getting started

Desktop scanning supports cancellation. Cleanup defaults to guarded same-drive quarantine with explicit batch restore; permanent deletion is a separate choice. Every operation requires confirmation and content validation. See [guarded quarantine and restore](docs/guarded-quarantine.md) and [cleanup safety](docs/diagnostics-and-cleanup-safety.md) for limits, including the original Windows profile required for recovery, unsupported recycle-bin execution, and groups above 128 removal targets.

```powershell
dotnet build
dotnet test
```

The project is early-stage; these commands validate the foundation, not a complete product.

## Phase-based backlog and issue plan

See the [source-based progress review](docs/roadmap-progress.md) for confirmed work, outstanding gaps, and the next small steps.

This README acts as the working backlog until the corresponding GitHub Issues are created for each milestone. Use these ticket identifiers in commit messages, branch names, and pull request descriptions when the work is ready.

### Phase 0 — Foundation and repository discipline
- DF-001 — Repository setup and documentation: finalise README, agent rules, project context, and branch workflow.
- DF-002 — Privacy and repository hygiene: validate .gitignore, local-only exclusions, and first-commit review.
- DF-003 — GitHub workflow setup: configure default branch, remote, pull request flow, and branch protection strategy.
- DF-004 — Initial CI foundation: add lightweight GitHub Actions to build and test on pull requests to main.

### Phase 1 — File discovery and filtering
- DF-101 — Implement recursive file enumeration with bounded concurrency and cancellation support.
- DF-102 — Add path, extension, size, and attribute filtering for candidate scan sets.
- DF-103 — Handle inaccessible directories, reparse points, symlinks, and permission edge cases safely.
- DF-104 — Measure and tune scanning performance on SSD/HDD and low-memory laptops.

### Phase 2 — Exact duplicate detection
- DF-201 — Add size-grouping pipeline and duplicate candidate generation.
- DF-202 — Implement streaming hash verification for exact-file comparison.
- DF-203 — Add collision-safe validation and deterministic test fixtures.
- DF-204 — Benchmark scanning and hashing throughput against realistic synthetic data.

### Phase 3 — Desktop experience
- DF-301 — Design the desktop architecture around a UI-agnostic core library.
- DF-302 — Implement the Avalonia desktop shell and scanning workflow.
- DF-303 — Add result grouping, preview panels, and review-first actions.
- DF-304 — Add safe deletion and quarantine flows with clear user confirmation.

### Phase 4 — Image and media similarity research
- DF-401 — Evaluate duplicate image similarity heuristics and constraints for Windows builds. See [the feasibility note](docs/image-media-similarity-feasibility.md).
- DF-402 — Implement optional visual similarity analysis without affecting exact-match performance.
- DF-403 — Define the feature boundary between exact duplicates and similarity-based matching.

### Phase 5 — Resilience, performance, and release readiness
- DF-501 — Add robust logging, diagnostics, and cancellation/error reporting.
- DF-502 — Profile memory use and tune concurrency for lower-end hardware.
- DF-503 — Prepare release documentation, security review, and quality gates.

### Phase 6 — Android feasibility
- DF-601 — Review Android storage access model and feasibility for a future app.
- DF-602 — Validate UI/core boundaries for a later Android port.
- DF-603 — Define the minimum viable Android prototype and success criteria.

This backlog is intended to keep delivery incremental, reviewable, and realistic for the current hardware constraints.

## Naming standards

Use human-readable, professional naming everywhere in the project.

- Branch names should use meaningful names such as `feature/file-discovery`, `fix/scan-cancellation`, or `docs/roadmap` rather than numeric IDs or generated labels.
- File names should describe purpose, for example `FileScanner.cs`, `DuplicateScanResult.cs`, or `ScanOptions.cs`, not placeholders like `Class1.cs` or `UnitTest1.cs`.
- Type, method, property, and variable names should read like domain language, not internal counters or shorthand.
- Prefer clear names such as `ScanRoot`, `EnumerateFiles`, and `DuplicateGroup` over vague names like `Thing`, `Data`, or `Helper`.
- Keep names consistent across the solution and avoid mixing technical numbers, internal issue IDs, or accidental code-generation names into the public codebase.

This standard applies to branch names, file names, code symbols, tests, and PR descriptions.
