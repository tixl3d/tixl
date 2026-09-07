# Lib.flow

## Sub-namespaces

- [Lib.flow.context](context/README.md)
- [Lib.flow.skillQuest](skillquest/README.md)

## Operators

- [**BlendScenes**](BlendScenes.md) — Uses a float index to alpha blend between draw scenes.
- [**ExecRepeatedly**](ExecRepeatedly.md) — Controls how frequently the sub graph is being executed.
- [**Execute**](Execute.md) — Performs a sequence of drawing operations and then restores the graphics context. You can use it for grouping or activating and deactivating parts of your graphs.
- [**ExecuteOnce**](ExecuteOnce.md) — Executes the subgraph only once. This can be useful for initializing buffers or resetting states.
- [**LoadSoundtrack**](LoadSoundtrack.md) — This operator has no function other than to point out that, unlike meshes, images and other assets, audio files are integrated via the "Playback Settings" menu.
- [**LogMessage**](LogMessage.md) — Prints a message to the console log.
- [**Loop**](Loop.md) — Executes the sub graph multiple times. This can be very useful for iterating a drawing function.
- [**Once**](Once.md) — Briefly indicates whether the incoming Boolean value has changed.
- [**ResetSubtreeTrigger**](ResetSubtreeTrigger.md)
- [**Switch**](Switch.md) — Switches between connected graphs. Can be used to "cut" between scenes.
- [**TimeClip**](TimeClip.md) — Creates a time clip bar within the DopeView of the Timeline, similar to how video editing apps show clips.

---

*Auto-generated from the operator library.*
