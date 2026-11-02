# Native libraries for macOS

Dylibs here are copied into the Editor and Player output on macOS (see the `osx` item groups in
`Editor/Editor.csproj` and `Player/Player.csproj`).

`libtixlaudio.dylib` is built from `Native/TixlAudio/` and needs Apple's SDK, so it has to be built on a Mac:

```
cc -std=c99 -O2 -fPIC -dynamiclib -fvisibility=hidden -o libtixlaudio.dylib ../../Native/TixlAudio/tixl_audio.c \
    -framework CoreFoundation -framework CoreAudio -framework AudioToolbox
```

Until it is here, audio stays silent on macOS.
