---
name: File format versioning
description: Rules for incrementing SymbolFormatVersion when .t3 or .t3ui format changes
type: process
---

## File Format Version (`Core\Model\SymbolFormatVersion.cs`)

When completing a feature that changes the .t3 or .t3ui file format:

1. **Increment `SymbolFormatVersion.Current`**
2. **Add a `FormatChange` entry** to the `Changes` array describing what changed
3. **Synchronize with TiXL version increment** — not every intermediate commit needs a format version bump, only when a feature is complete and a version is tagged

### When to increment
- New fields added to .t3 or .t3ui JSON
- Structural changes to existing JSON (renamed keys, nested objects)
- New serialization sections (e.g. adding EditorState to .t3ui Settings)

### When NOT to increment
- Intermediate commits during a feature branch
- Code-only changes that don't affect file format
- Adding optional fields that older versions silently ignore (use judgment — if data would be lost on round-trip through an older editor, increment)

### Plans that will need a format version increment
- Migrating CoreSettings fields into per-symbol ProjectSettings
- Adding EditorState persistence (layout, timeline, camera) to .t3ui
- Adding output settings per-project to .t3ui
- Any future keyframe format changes
