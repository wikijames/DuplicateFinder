# Codex instructions — DuplicateFinder

Read `README.md` for public goals and `local-only/PROJECT_CONTEXT.md` if it exists. The latter is intentionally untracked and may contain private notes: **never add, commit, push, paste into issues, or include it in generated logs or PRs**. Ask before transmitting its contents to external services or tools.

## Operating rules
- Inspect the workspace and `git status` before edits. Never assume discussed files exist.
- Target C# / .NET 10; keep `DuplicateFinder.Core` UI-agnostic and portable.
- Prefer streaming file operations, bounded memory and I/O concurrency; optimize based on measurements.
- Implement one small, testable change at a time. Run `dotnet build` and `dotnet test`; report results.
- No deletion of real files, no automatic cleanups, no `git push`, no remote changes without explicit approval.
- Use synthetic test fixtures. Avoid scanning personal directories or collecting identifying paths in logs.
- Treat third-party apps and tools as untrusted; don't run them on the host for analysis.
- Respect third-party EULAs and applicable law. No activation bypass, keygens, trial circumvention or copied proprietary code/assets. Keep observations/specs separate from implementation.
- If a requirement is ambiguous, propose a minimal plan and ask before broad changes.
