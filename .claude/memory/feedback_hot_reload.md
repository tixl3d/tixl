---
name: Hot code reload workflow
description: User can hot-reload code changes while Editor is running - no need to close and rebuild
type: feedback
---

Don't wait for the Editor to be closed to test changes. Just save the files and the user can try hot code reloading while the app is running.

**Why:** Build errors from locked DLLs are avoidable — the user has hot reload set up.
**How to apply:** After editing C# files, just confirm the change is saved. Don't attempt `dotnet build` while the Editor is running. The user will hot-reload.
