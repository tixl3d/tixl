# BASS natives for macOS

Copied into the Editor and Player output on macOS only (see the `osx` item groups in `Editor/Editor.csproj`
and `Player/Player.csproj`). They are universal binaries; the arm64 slice is the one TiXL uses.

| File                | Package (un4seen.com/files/) | Take from the archive |
|---------------------|------------------------------|-----------------------|
| `libbass.dylib`     | `bass24-osx.zip`             | `libbass.dylib`       |
| `libbassmix.dylib`  | `bassmix24-osx.zip`          | `libbassmix.dylib`    |
| `libbassflac.dylib` | `bassflac24-osx.zip`         | `libbassflac.dylib`   |

`ManagedBass` P/Invokes the plain names `bass`, `bassmix` and `bassflac`; .NET maps those to `lib<name>.dylib`
on macOS. As on Linux, there is no BASSWASAPI, so live audio input is unavailable for now.
