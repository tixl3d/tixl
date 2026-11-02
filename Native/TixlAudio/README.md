# tixlaudio

The native audio library: mixing, decoding, DSP nodes, taps and device callbacks. It wraps
[miniaudio](https://miniaud.io) (public domain / MIT-0) and stb_vorbis. Nothing managed runs on the audio
thread; C# calls the flat `ta_*` functions through `Core/Audio/Native/NativeAudio.cs`.

The built libraries are checked in next to the other natives, so building TiXL needs no C compiler. After
changing anything here, rebuild them:

| Platform | Command | Output |
|---|---|---|
| Linux | `./build.sh` | `Dependencies/linux-x64/libtixlaudio.so` |
| Windows | `./build-windows.sh <zig>` (cross-compiles from Linux) | `Dependencies/tixlaudio.dll` |

## Changes to the vendored miniaudio.h

Version 0.11.25. Search for `TiXL patch`:

- **Seeking** (`ma_engine_node_process_pcm_frames__sound`): a seek drops the input read ahead and the
  resampler history, which still belonged to the old position.
- **MP3 seek tables** (dr_mp3): seeks with a table landed up to several frames off linear decoding. Discarded
  frames are now counted by byte position, because a frame short of bit reservoir decodes to nothing and is
  skipped silently. Seeks also honour the gapless delay, start after ID3 and Xing data, synthesize the
  leading frames so the first audible frame has its overlap state, and fall back to linear decoding if the
  position still comes out wrong.

## Testing MP3 seeks

`tests/mp3_seek_test.c` compares 400 seeks per file with a linear decode, sample by sample. Run it on files
with and without a Xing/LAME tag, at low bitrates, and on VBR with silence, where frames are smallest:

```
cc -O2 -std=gnu99 -w -DMA_HEADER='"../miniaudio.h"' -o /tmp/mp3_seek_test tests/mp3_seek_test.c -lm -lpthread -ldl
/tmp/mp3_seek_test /full/path/to/file.mp3
```

The exit code is 0 when every seek matched exactly.
