# Example scene generator

Writes the example graphs of `Examples.Lib.physics` and `Examples.Lib.volume` (`.cs`, `.t3`, `.t3ui`).

```sh
python3 Scripts/example-scenes/physics.py .
python3 Scripts/example-scenes/volumes.py .
```

- `opmeta.py` reads every operator in `Operators/Lib/Symbols`: its guid, symbol name and the name, guid and type of each
  slot. Scenes refer to operators and slots by name; a misspelt slot or a connection between different types stops the
  script before anything is written.
- `gen.py` holds the scene builder and the pieces every example shares: the floor, drawing bodies and geometry, and the
  lit render set-up (shadow, key and fill light, haze, camera, SSAO).
- `physics.py` and `volumes.py` describe one function per example, with its graph sections and guided tour.

Close the editor, or at least the examples, before regenerating: it rewrites `.t3`/`.t3ui` files on save. Ids are derived
from the example and node names, so regenerating keeps every id stable and only changed values show up in a diff.
The scenes are checked headlessly in `Tests/Lib.Tests/Physics/PhysicsExampleScenesTests.cs` and
`Tests/Lib.Tests/Volume/VolumeExampleScenesTests.cs`.
