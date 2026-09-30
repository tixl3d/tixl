# BASS natives for Linux (x86-64)

Drop the un4seen shared objects here and they are copied into the Editor and Player output on Linux only
(see the `linux-x64` item groups in `Editor/Editor.csproj` and `Player/Player.csproj`). Windows keeps using
the `.dll` files one folder up.

| File             | Package (un4seen.com/files/)     | Take from the archive |
|------------------|----------------------------------|-----------------------|
| `libbass.so`     | `bass24-linux.zip`               | `x64/libbass.so`      |
| `libbassmix.so`  | `bassmix24-linux.zip`            | `x64/libbassmix.so`   |
| `libbassflac.so` | `bassflac24-linux.zip`           | `x64/libbassflac.so`  |

`ManagedBass` P/Invokes the plain names `bass`, `bassmix` and `bassflac`; .NET maps those to `lib<name>.so`
on Linux, so no resolver or rename is needed.

There is no `libbasswasapi.so` — BASSWASAPI is Windows-only. Live audio input goes through it, so on Linux
`BassLibrary.IsWasapiAvailable` stays false and the external-device audio source is unavailable until that
path is rewritten against BASS's own record API (`Bass.RecordInit` / `Bass.RecordStart`). Playback, decoding,
the mixer and the timeline's waveform images need only BASS and BASSmix, and work as soon as the files above
are in place.

BASS is not redistributable under the repo's own licence: it is free for non-commercial use, paid otherwise,
and shipping it means accepting un4seen's terms. That is why the binaries are not committed here. Keep the
matching licence text with them, next to the other vendored licences in `Dependencies/licenses`.
