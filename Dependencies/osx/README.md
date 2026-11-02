# Native libraries for macOS

Dylibs here are copied into the Editor and Player output on macOS (see the `osx` item groups in
`Editor/Editor.csproj` and `Player/Player.csproj`).

`libtixlaudio.dylib` is built from `Native/TixlAudio/` by `build-macos.sh`. It needs Apple's SDK, so it has to run on a
Mac; the macOS CI job does this for every DMG. Without it, audio stays silent on macOS.
