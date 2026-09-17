# Pitfalls

Things that look alarming when they happen, what is actually going on, and what to do next.

## Missing operators

After startup TiXL shows **Warning: Missing Symbols**, and some graphs contain magenta outlined boxes labelled *missing*.

### What happened

A graph uses an operator whose definition TiXL could not find while loading. The usual reasons:

- **A package is not installed or did not load.** The project uses operators from another project, an extension, or a newer TiXL version than the one you are running.
- **Another project failed to load.** If that project shows up under *Broken* in the project list, fix or restore it first — see [Backups](Backups.md).
- **The operator was deleted or renamed** outside of TiXL, or lived in a project you no longer have.

### Nothing is lost

Missing operators are kept in your project files exactly as they were — including their parameters, connections, animation curves and position in the graph. You can keep working and saving. As soon as the operator is available again, it comes back as if nothing happened.

This also means a warning is not a reason to stop saving or to restore a backup.

### Finding them

The warning lists every affected operator together with what is missing in it.

- **Show in Graph** opens that operator and frames the gaps.
- The folder icon reveals the operator's `.t3` file in the file browser.

In the graph, a missing operator is drawn as a magenta box at its old position, with magenta wires to everything it was connected to. Hover it to see its full name — the part before the last dot is the namespace, which usually tells you which package it came from, e.g. `Lib.io.midi.MidiInput`. If a missing operator is outside the visible area, a magenta marker on the border of the graph window points towards it.

Inputs that were fed by a missing operator stay visible, so your layout does not shift.

### What to do next

- **You want the operator back:** install or load the package it came from, or fix the project that failed to load, then restart TiXL.
- **The operator is obsolete:** right-click the magenta box and choose **Delete Missing Operator**. This removes it and its connections from the file the next time you save. It can be undone with `Ctrl+Z` like any other edit.
- **You are not sure:** leave it. It costs nothing and keeps the option to restore it later.

### Names in older projects

Projects saved before TiXL 4.4 did not store the full name of each operator they use. For those, the warning may show only a short name, the name you gave the operator, or — rarely — just an id. Saving the project once with a current version, while everything is available, stores the full names for next time.

## See also

- [Backups](Backups.md) — restoring an earlier version of a project.
- [FAQ](FAQ.md)
