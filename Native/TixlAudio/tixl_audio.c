/*
 * Thin native audio layer over miniaudio. Everything that runs on the audio thread lives here, so the
 * realtime path never enters managed code and a .NET garbage collection cannot stall it. C# only sets
 * parameters, rewires nodes and reads results once per frame.
 *
 * The engine never owns a device: in realtime mode our own device callback pulls it, which lets the device
 * be swapped without touching the graph; in offline mode the caller pulls it, which is how export renders.
 */

#define STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#undef STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"

#include <string.h>
#include <stdlib.h>

#if defined(_WIN32)
    #define TA_API __declspec(dllexport)
#else
    #include <time.h>
    #define TA_API __attribute__((visibility("default")))
#endif

#define TA_MAX_SOUNDS 256
#define TA_SOUND_SLOT_BITS 8 /* a sound handle is (generation << bits) | slot */
#define TA_MAX_BUSES 64
#define TA_BUS_HANDLE_BIT 0x40000000u
#define TA_MAX_GENERATION (0x3FFFFFFFu >> TA_SOUND_SLOT_BITS)
#define TA_HANDLE_OUTPUT 0x7FFFFFFF /* routes to the device output; meters what reaches it */
#define TA_TAP_STALE_FRAMES 4096 /* a tap nothing wrote to for this long meters as silence (85 ms at 48 kHz) */
#define TA_MAX_ECHOES 8
#define TA_CAPTURE_RING_FRAMES (1 << 19) /* power of two; 10.9 s at 48 kHz, so a stalled frame loop doesn't lose a recording */
#define TA_MAX_TAPS 8 /* index 0 is the device output */
#define TA_TAP_CAPACITY 16384 /* mono frames, power of two */
#define TA_ECHO_MAX_SECONDS 2
#define TA_MP3_SEEK_POINTS 4096

enum
{
    TA_MODE_DEVICE = 0,
    TA_MODE_OFFLINE = 1,
};

enum
{
    TA_BACKEND_DEFAULT = 0,
    TA_BACKEND_NULL = 1,
    TA_BACKEND_PULSEAUDIO = 2,
    TA_BACKEND_ALSA = 3,
    TA_BACKEND_JACK = 4,
};

enum
{
    TA_LOAD_STREAM_RESOURCE_MANAGER = 1, /* miniaudio's own streaming, which never builds an MP3 seek table */
    TA_LOAD_DECODE_TO_MEMORY = 2,
    TA_LOAD_SPATIAL = 4,
};

enum
{
    TA_NODE_ENDPOINT = 0,
    TA_NODE_SOUND = 1,
    TA_NODE_BUS = 2,
    TA_NODE_ECHO = 3,
    TA_NODE_TAP = 4,
};

typedef enum
{
    TA_SOURCE_FILE_DECODER,
    TA_SOURCE_RESOURCE_MANAGER,
    TA_SOURCE_WAVEFORM,
    TA_SOURCE_PUSH,
    TA_SOURCE_GENERATOR,
} ta_source_kind;

typedef struct
{
    ma_uint64 callbackCount;
    ma_uint64 lateCallbackCount;
    ma_uint64 maxCallbackGapNs;
    ma_uint64 maxCallbackDurationNs;
    ma_uint64 totalCallbackDurationNs;
    ma_uint64 totalFramesRendered;
} ta_stats;

typedef struct
{
    ma_uint32 sampleRate;
    ma_uint32 channels;
    ma_uint32 periodFrames;
    ma_uint32 periods;
    ma_uint32 backend;
} ta_info;

/* Mono history of a signal; written by the audio thread, read by the frame loop. */
typedef struct
{
    float samples[TA_TAP_CAPACITY];
    ma_atomic_uint64 framesWritten;
    ma_atomic_float peak;
    ma_atomic_uint64 lastWriteClock;
} ta_tap_ring;

typedef struct
{
    ma_node_base base;
    ta_tap_ring ring;
    ma_atomic_uint64* clock; /* frames the engine has rendered, to tell a stopped source from a quiet one */
} ta_tap_node;

typedef struct
{
    ma_node_base base;
    float* delayLine; /* interleaved stereo */
    ma_uint32 capacityFrames;
    ma_uint32 writeFrame;
    ma_uint32 sampleRate;
    ma_atomic_float delaySeconds;
    ma_atomic_float feedback;
    ma_atomic_float wetMix;
} ta_echo_node;

/* Pushed PCM (video audio, generators fed from the frame loop). Underruns play silence instead of ending the sound. */
typedef struct
{
    ma_data_source_base base;
    ma_pcm_rb ring;
    ma_uint32 channels;
    ma_uint32 sampleRate;
    ma_uint64 cursor;
    ma_atomic_uint64 underrunFrames;
} ta_push_source;

/* Waveform × ADSR, computed on the audio thread. The frame loop only writes parameters and raises signals. */
typedef struct
{
    ma_data_source_base base;
    ma_uint32 sampleRate;
    ma_uint64 cursor;

    ma_atomic_float frequency;
    ma_atomic_int32 waveform;
    ma_atomic_float attackSeconds;
    ma_atomic_float decaySeconds;
    ma_atomic_float sustainLevel;
    ma_atomic_float releaseSeconds;
    ma_atomic_float durationSeconds; /* < 0: until released */
    ma_atomic_int32 attackSignal;
    ma_atomic_int32 releaseSignal;
    ma_atomic_int32 resetSignal;
    ma_atomic_int32 isActive;

    ma_int32 stage;
    float envelope;
    double releaseStartValue;
    ma_int64 stageFrames;
    ma_int64 playedFrames;
    ma_int64 durationFrames;
    double phase;
    ma_uint32 noiseState;
    double pinkB0, pinkB1, pinkB2;
} ta_generator_source;

typedef struct
{
    ma_bool32 used;
    ta_source_kind kind;
    union
    {
        ma_decoder decoder;
        ma_waveform waveform;
        ta_push_source push;
        ta_generator_source generator;
    } source;
    ma_sound sound;
    ta_tap_node tap; /* every source routes through its own tap, so any source can be metered and analysed */
    ma_uint32 sourceSampleRate;
    ma_uint32 generation;
} ta_sound_slot;

typedef struct
{
    ma_bool32 used;
    ma_uint32 generation;
    ma_sound_group group;
    ta_tap_node tap;
} ta_bus_slot;

typedef struct
{
    ma_bool32 used;
    ta_echo_node node;
} ta_echo_slot;

typedef struct
{
    ma_bool32 used;
    ta_tap_node node;
} ta_tap_slot;

/* Rewiring requested by the frame loop, applied by the audio thread between blocks so no block sees half a change. */
#define TA_ROUTE_QUEUE_CAPACITY 256 /* power of two */

typedef struct
{
    ma_node* source;
    ma_node* target;
} ta_route_command;

typedef struct
{
    ma_int32 mode;
    ma_bool32 hasContext;
    ma_bool32 hasDevice;
    ma_context context;
    ma_device device;
    ma_uint32 requestedPeriodFrames;
    ma_engine engine;
    ma_uint32 sampleRate;

    ta_sound_slot sounds[TA_MAX_SOUNDS];
    ta_bus_slot buses[TA_MAX_BUSES];
    ta_echo_slot echoes[TA_MAX_ECHOES];
    ta_tap_slot taps[TA_MAX_TAPS];
    ta_tap_ring outputTap;

    ta_stats stats;
    ma_uint64 lastCallbackStartNs;
    ma_uint32 lastCallbackFrames;

    /* Seqlock: the audio thread publishes the tracked sound's cursor and when it was taken. */
    ma_atomic_uint32 positionSequence;
    ma_atomic_int32 trackedSound;
    ma_uint64 trackedCursorFrames;
    ma_uint64 trackedCursorTimeNs;
    ma_uint32 trackedIsPlaying;

    /* Applied after the output tap, so analysis still sees the full signal when the output is muted. */
    ma_atomic_float outputGain;

    ta_route_command routeQueue[TA_ROUTE_QUEUE_CAPACITY];
    ma_atomic_uint32 routeQueueWritten;
    ma_atomic_uint32 routeQueueApplied;

    ma_device captureDevice;
    ma_bool32 captureRunning;
    float* captureRing; /* interleaved stereo */
    ma_atomic_uint64 captureFramesWritten;

    /* Interleaved stereo copy of the output for waveform displays; outputTap only keeps the mono mix. */
    float outputStereo[TA_TAP_CAPACITY * 2];
    ma_atomic_uint64 outputStereoWritten;
    ma_atomic_uint64 renderClock;
} ta_engine;

static ma_uint64 ta_now_ns_internal(void)
{
#if defined(_WIN32)
    LARGE_INTEGER counter, frequency;
    QueryPerformanceCounter(&counter);
    QueryPerformanceFrequency(&frequency);
    return (ma_uint64)((double)counter.QuadPart * 1e9 / (double)frequency.QuadPart);
#else
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (ma_uint64)now.tv_sec * 1000000000ull + (ma_uint64)now.tv_nsec;
#endif
}

/* ---- Tap ring ---- */

static void ta_tap_ring_write(ta_tap_ring* ring, const float* frames, ma_uint32 frameCount, ma_uint32 channels)
{
    ma_uint64 written = ma_atomic_uint64_get(&ring->framesWritten);
    float peak = ma_atomic_float_get(&ring->peak);
    for (ma_uint32 frameIndex = 0; frameIndex < frameCount; frameIndex++)
    {
        float mono = channels >= 2
                         ? 0.5f * (frames[frameIndex * channels] + frames[frameIndex * channels + 1])
                         : frames[frameIndex * channels];
        ring->samples[(written + frameIndex) & (TA_TAP_CAPACITY - 1)] = mono;
        float magnitude = mono < 0 ? -mono : mono;
        if (magnitude > peak)
            peak = magnitude;
    }
    ma_atomic_float_set(&ring->peak, peak);
    ma_atomic_uint64_set(&ring->framesWritten, written + frameCount);
}

/**
 * Copies samples written after sinceFrame (at most maxCount, the newest ones if more arrived) and returns how
 * many were copied; *totalFrames receives the write position to pass as sinceFrame next time.
 */
static ma_int32 ta_tap_ring_read_since(ta_tap_ring* ring, ma_uint64 sinceFrame, float* destination, ma_int32 maxCount, ma_uint64* totalFrames)
{
    if (maxCount > TA_TAP_CAPACITY / 2)
        maxCount = TA_TAP_CAPACITY / 2;

    ma_uint64 written = ma_atomic_uint64_get(&ring->framesWritten);
    *totalFrames = written;
    if (written <= sinceFrame)
        return 0;

    ma_uint64 available = written - sinceFrame;
    ma_uint64 count = available < (ma_uint64)maxCount ? available : (ma_uint64)maxCount;
    ma_uint64 start = written - count;
    for (ma_uint64 index = 0; index < count; index++)
        destination[index] = ring->samples[(start + index) & (TA_TAP_CAPACITY - 1)];
    return (ma_int32)count;
}

/** Copies the newest count samples, zero-padded at the start if fewer exist; returns the write position. */
static ma_uint64 ta_tap_ring_read_latest(ta_tap_ring* ring, float* destination, ma_int32 count)
{
    if (count > TA_TAP_CAPACITY / 2)
        count = TA_TAP_CAPACITY / 2;

    ma_uint64 written = ma_atomic_uint64_get(&ring->framesWritten);
    ma_uint64 start = written >= (ma_uint64)count ? written - (ma_uint64)count : 0;
    for (ma_int32 index = 0; index < count; index++)
    {
        ma_uint64 frame = start + (ma_uint64)index;
        destination[index] = frame < written ? ring->samples[frame & (TA_TAP_CAPACITY - 1)] : 0.0f;
    }
    return written;
}

/* ---- Tap node: a pass-through that records what flows through it, e.g. a bus's submix. ---- */

static void ta_tap_process(ma_node* node, const float** framesIn, ma_uint32* frameCountIn, float** framesOut, ma_uint32* frameCountOut)
{
    ta_tap_node* tap = (ta_tap_node*)node;
    ma_uint32 frameCount = *frameCountOut;
    (void)frameCountIn;

    if (framesOut[0] != framesIn[0])
        memcpy(framesOut[0], framesIn[0], (size_t)frameCount * 2 * sizeof(float));
    ta_tap_ring_write(&tap->ring, framesIn[0], frameCount, 2);
    if (tap->clock != NULL)
        ma_atomic_uint64_set(&tap->ring.lastWriteClock, ma_atomic_uint64_get(tap->clock));
}

static ma_node_vtable g_tapVtable = { ta_tap_process, NULL, 1, 1, MA_NODE_FLAG_PASSTHROUGH };

/* ---- Echo: custom DSP as a native node; parameters are atomics written from C#. ---- */

static void ta_echo_process(ma_node* node, const float** framesIn, ma_uint32* frameCountIn, float** framesOut, ma_uint32* frameCountOut)
{
    ta_echo_node* echo = (ta_echo_node*)node;
    const float* input = framesIn[0];
    float* output = framesOut[0];
    ma_uint32 frameCount = *frameCountOut;
    (void)frameCountIn;

    float feedback = ma_atomic_float_get(&echo->feedback);
    float wetMix = ma_atomic_float_get(&echo->wetMix);
    ma_uint32 delayFrames = (ma_uint32)(ma_atomic_float_get(&echo->delaySeconds) * (float)echo->sampleRate);
    if (delayFrames < 1)
        delayFrames = 1;
    if (delayFrames >= echo->capacityFrames)
        delayFrames = echo->capacityFrames - 1;

    for (ma_uint32 frameIndex = 0; frameIndex < frameCount; frameIndex++)
    {
        ma_uint32 readFrame = (echo->writeFrame + echo->capacityFrames - delayFrames) % echo->capacityFrames;
        for (ma_uint32 channel = 0; channel < 2; channel++)
        {
            float dry = input[frameIndex * 2 + channel];
            float delayed = echo->delayLine[readFrame * 2 + channel];
            echo->delayLine[echo->writeFrame * 2 + channel] = dry + delayed * feedback;
            output[frameIndex * 2 + channel] = dry * (1.0f - wetMix) + delayed * wetMix;
        }
        echo->writeFrame = (echo->writeFrame + 1) % echo->capacityFrames;
    }
}

static ma_node_vtable g_echoVtable = { ta_echo_process, NULL, 1, 1, 0 };

static ma_result ta_stereo_node_init(ta_engine* engine, const ma_node_vtable* vtable, ma_node* node)
{
    ma_uint32 channels[1] = { 2 };
    ma_node_config config = ma_node_config_init();
    config.vtable = vtable;
    config.pInputChannels = channels;
    config.pOutputChannels = channels;
    return ma_node_init(ma_engine_get_node_graph(&engine->engine), &config, NULL, node);
}

/* ---- Push source ---- */

static ma_result ta_push_read(ma_data_source* dataSource, void* framesOut, ma_uint64 frameCount, ma_uint64* framesRead)
{
    ta_push_source* push = (ta_push_source*)dataSource;
    float* output = (float*)framesOut;
    ma_uint64 remaining = frameCount;

    while (remaining > 0)
    {
        ma_uint32 chunk = remaining > 0xFFFFFFFF ? 0xFFFFFFFF : (ma_uint32)remaining;
        void* buffer;
        if (ma_pcm_rb_acquire_read(&push->ring, &chunk, &buffer) != MA_SUCCESS || chunk == 0)
            break;
        memcpy(output, buffer, (size_t)chunk * push->channels * sizeof(float));
        ma_pcm_rb_commit_read(&push->ring, chunk);
        output += (size_t)chunk * push->channels;
        remaining -= chunk;
    }

    if (remaining > 0)
    {
        memset(output, 0, (size_t)remaining * push->channels * sizeof(float));
        ma_atomic_uint64_set(&push->underrunFrames, ma_atomic_uint64_get(&push->underrunFrames) + remaining);
    }

    push->cursor += frameCount;
    *framesRead = frameCount;
    return MA_SUCCESS;
}

static ma_result ta_push_seek(ma_data_source* dataSource, ma_uint64 frameIndex)
{
    (void)dataSource;
    (void)frameIndex;
    return MA_NOT_IMPLEMENTED;
}

static ma_result ta_push_get_format(ma_data_source* dataSource, ma_format* format, ma_uint32* channels, ma_uint32* sampleRate, ma_channel* channelMap, size_t channelMapCap)
{
    ta_push_source* push = (ta_push_source*)dataSource;
    *format = ma_format_f32;
    *channels = push->channels;
    *sampleRate = push->sampleRate;
    ma_channel_map_init_standard(ma_standard_channel_map_default, channelMap, channelMapCap, push->channels);
    return MA_SUCCESS;
}

static ma_result ta_push_get_cursor(ma_data_source* dataSource, ma_uint64* cursor)
{
    *cursor = ((ta_push_source*)dataSource)->cursor;
    return MA_SUCCESS;
}

static ma_result ta_push_get_length(ma_data_source* dataSource, ma_uint64* length)
{
    (void)dataSource;
    *length = 0;
    return MA_NOT_IMPLEMENTED;
}

static ma_data_source_vtable g_pushVtable = { ta_push_read, ta_push_seek, ta_push_get_format, ta_push_get_cursor, ta_push_get_length, NULL, 0 };

/* ---- Rendering: shared by the device callback and offline export ---- */

static void ta_apply_queued_routes(ta_engine* engine)
{
    ma_uint32 written = ma_atomic_uint32_get(&engine->routeQueueWritten);
    ma_uint32 applied = ma_atomic_uint32_get(&engine->routeQueueApplied);
    while (applied != written)
    {
        ta_route_command* command = &engine->routeQueue[applied & (TA_ROUTE_QUEUE_CAPACITY - 1)];
        if (command->target != NULL)
            ma_node_attach_output_bus(command->source, 0, command->target, 0);
        else
            ma_node_detach_output_bus(command->source, 0);
        applied++;
    }
    ma_atomic_uint32_set(&engine->routeQueueApplied, applied);
}

static ta_sound_slot* ta_sound(ta_engine* engine, ma_int32 handle);
static void ta_release_sound(ta_sound_slot* slot);

static void ta_render_block(ta_engine* engine, float* output, ma_uint32 frameCount, ma_uint64 startNs)
{
    ta_apply_queued_routes(engine);
    ma_engine_read_pcm_frames(&engine->engine, output, frameCount, NULL);
    ta_tap_ring_write(&engine->outputTap, output, frameCount, 2);
    ma_atomic_uint64_set(&engine->renderClock, ma_atomic_uint64_get(&engine->renderClock) + frameCount);

    ma_uint64 stereoWritten = ma_atomic_uint64_get(&engine->outputStereoWritten);
    for (ma_uint32 frame = 0; frame < frameCount; frame++)
    {
        ma_uint64 slot = (stereoWritten + frame) & (TA_TAP_CAPACITY - 1);
        engine->outputStereo[slot * 2] = output[frame * 2];
        engine->outputStereo[slot * 2 + 1] = output[frame * 2 + 1];
    }
    ma_atomic_uint64_set(&engine->outputStereoWritten, stereoWritten + frameCount);

    float gain = ma_atomic_float_get(&engine->outputGain);
    if (gain != 1.0f)
    {
        for (ma_uint32 index = 0; index < frameCount * 2; index++)
            output[index] *= gain;
    }

    ma_int32 tracked = ma_atomic_int32_get(&engine->trackedSound);
    ta_sound_slot* trackedSlot = ta_sound(engine, tracked);
    if (trackedSlot != NULL)
    {
        ma_uint64 cursor = 0;
        ma_sound_get_cursor_in_pcm_frames(&trackedSlot->sound, &cursor);
        ma_atomic_uint32_fetch_add(&engine->positionSequence, 1);
        engine->trackedCursorFrames = cursor;
        engine->trackedCursorTimeNs = startNs;
        engine->trackedIsPlaying = ma_sound_is_playing(&trackedSlot->sound);
        ma_atomic_uint32_fetch_add(&engine->positionSequence, 1);
    }
}

static void ta_data_callback(ma_device* device, void* output, const void* input, ma_uint32 frameCount)
{
    ta_engine* engine = (ta_engine*)device->pUserData;
    ma_uint64 startNs = ta_now_ns_internal();
    (void)input;

    if (engine->lastCallbackStartNs != 0)
    {
        ma_uint64 gapNs = startNs - engine->lastCallbackStartNs;
        ma_uint64 expectedNs = (ma_uint64)engine->lastCallbackFrames * 1000000000ull / engine->sampleRate;
        if (gapNs > engine->stats.maxCallbackGapNs)
            engine->stats.maxCallbackGapNs = gapNs;
        if (gapNs > expectedNs * 2)
            engine->stats.lateCallbackCount++;
    }
    engine->lastCallbackStartNs = startNs;
    engine->lastCallbackFrames = frameCount;

    ta_render_block(engine, (float*)output, frameCount, startNs);

    ma_uint64 durationNs = ta_now_ns_internal() - startNs;
    if (durationNs > engine->stats.maxCallbackDurationNs)
        engine->stats.maxCallbackDurationNs = durationNs;
    engine->stats.totalCallbackDurationNs += durationNs;
    engine->stats.totalFramesRendered += frameCount;
    engine->stats.callbackCount++;
}

/* ---- Devices ---- */

static ma_result ta_get_devices(ta_engine* engine, ma_int32 capture, ma_device_info** infos, ma_uint32* count)
{
    if (!engine->hasContext)
        return MA_INVALID_OPERATION;

    ma_device_info* playbackInfos;
    ma_device_info* captureInfos;
    ma_uint32 playbackCount, captureCount;
    ma_result result = ma_context_get_devices(&engine->context, &playbackInfos, &playbackCount, &captureInfos, &captureCount);
    if (result != MA_SUCCESS)
        return result;
    *infos = capture ? captureInfos : playbackInfos;
    *count = capture ? captureCount : playbackCount;
    return MA_SUCCESS;
}

static ma_result ta_open_device(ta_engine* engine, ma_int32 deviceIndex)
{
    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.format = ma_format_f32;
    config.playback.channels = 2;
    config.sampleRate = engine->sampleRate; /* miniaudio converts if the hardware runs at another rate */
    config.periodSizeInFrames = engine->requestedPeriodFrames;
    config.dataCallback = ta_data_callback;
    config.pUserData = engine;
    if (deviceIndex >= 0)
    {
        ma_device_info* infos;
        ma_uint32 count;
        ma_result listResult = ta_get_devices(engine, 0, &infos, &count);
        if (listResult != MA_SUCCESS)
            return listResult;
        if ((ma_uint32)deviceIndex >= count)
            return MA_INVALID_ARGS;
        config.playback.pDeviceID = &infos[deviceIndex].id;
    }

    ma_result result = ma_device_init(&engine->context, &config, &engine->device);
    if (result != MA_SUCCESS)
        return result;

    engine->lastCallbackStartNs = 0;
    result = ma_device_start(&engine->device);
    if (result != MA_SUCCESS)
    {
        ma_device_uninit(&engine->device);
        return result;
    }
    engine->hasDevice = MA_TRUE;
    return MA_SUCCESS;
}

static void ta_close_device(ta_engine* engine)
{
    if (!engine->hasDevice)
        return;
    ma_device_uninit(&engine->device);
    engine->hasDevice = MA_FALSE;
}

/* ---- Engine lifetime ---- */

TA_API ma_uint64 ta_now_ns(void)
{
    return ta_now_ns_internal();
}

/**
 * Creates an engine. Device mode opens the default output on the given backend; offline mode has no device
 * and is rendered with ta_render.
 */
TA_API ta_engine* ta_create(ma_int32 mode, ma_int32 backend, ma_uint32 sampleRate, ma_uint32 periodFrames, ma_int32* outResult)
{
    ma_result result;
    ta_engine* engine = (ta_engine*)calloc(1, sizeof(ta_engine));
    if (engine == NULL)
    {
        *outResult = MA_OUT_OF_MEMORY;
        return NULL;
    }
    engine->mode = mode;
    engine->sampleRate = sampleRate;
    engine->requestedPeriodFrames = periodFrames;
    ma_atomic_int32_set(&engine->trackedSound, -1);
    ma_atomic_float_set(&engine->outputGain, 1.0f);

    ma_engine_config engineConfig = ma_engine_config_init();
    engineConfig.noDevice = MA_TRUE;
    engineConfig.channels = 2;
    engineConfig.sampleRate = sampleRate;
    result = ma_engine_init(&engineConfig, &engine->engine);
    if (result != MA_SUCCESS)
        goto failed;

    if (mode == TA_MODE_DEVICE)
    {
        ma_backend backends[1];
        ma_uint32 backendCount = 1;
        switch (backend)
        {
            case TA_BACKEND_NULL: backends[0] = ma_backend_null; break;
            case TA_BACKEND_PULSEAUDIO: backends[0] = ma_backend_pulseaudio; break;
            case TA_BACKEND_ALSA: backends[0] = ma_backend_alsa; break;
            case TA_BACKEND_JACK: backends[0] = ma_backend_jack; break;
            default: backendCount = 0; break;
        }

        ma_context_config contextConfig = ma_context_config_init();
        result = ma_context_init(backendCount ? backends : NULL, backendCount, &contextConfig, &engine->context);
        if (result != MA_SUCCESS)
            goto failedEngine;
        engine->hasContext = MA_TRUE;

        result = ta_open_device(engine, -1);
        if (result != MA_SUCCESS)
            goto failedContext;
    }

    *outResult = MA_SUCCESS;
    return engine;

failedContext:
    ma_context_uninit(&engine->context);
failedEngine:
    ma_engine_uninit(&engine->engine);
failed:
    free(engine);
    *outResult = result;
    return NULL;
}

TA_API void ta_capture_stop(ta_engine* engine);

TA_API void ta_destroy(ta_engine* engine)
{
    if (engine == NULL)
        return;

    ta_capture_stop(engine);
    ta_close_device(engine);

    for (ma_int32 index = 0; index < TA_MAX_SOUNDS; index++)
    {
        ta_sound_slot* slot = &engine->sounds[index];
        if (!slot->used)
            continue;
        ta_release_sound(slot);
    }
    for (ma_int32 index = 0; index < TA_MAX_ECHOES; index++)
    {
        if (!engine->echoes[index].used)
            continue;
        ma_node_uninit(&engine->echoes[index].node.base, NULL);
        free(engine->echoes[index].node.delayLine);
    }
    for (ma_int32 index = 0; index < TA_MAX_TAPS; index++)
    {
        if (engine->taps[index].used)
            ma_node_uninit(&engine->taps[index].node.base, NULL);
    }
    for (ma_int32 index = 0; index < TA_MAX_BUSES; index++)
    {
        if (engine->buses[index].used)
        {
            ma_sound_group_uninit(&engine->buses[index].group);
            ma_node_uninit(&engine->buses[index].tap.base, NULL);
        }
    }

    ma_engine_uninit(&engine->engine);
    if (engine->hasContext)
        ma_context_uninit(&engine->context);
    free(engine);
}

/** Reopens playback on another device (-1 for the default) while the graph keeps its state. */
TA_API ma_int32 ta_switch_device(ta_engine* engine, ma_int32 deviceIndex)
{
    if (engine->mode != TA_MODE_DEVICE)
        return MA_INVALID_OPERATION;
    ta_close_device(engine);
    return ta_open_device(engine, deviceIndex);
}

/**
 * Closes the output device but keeps the graph, so the same engine can be pulled with ta_render — export
 * renders the live graph this way. ta_attach_device resumes realtime playback.
 */
TA_API void ta_detach_device(ta_engine* engine)
{
    ta_close_device(engine);
    ta_apply_queued_routes(engine);
}

TA_API ma_int32 ta_attach_device(ta_engine* engine, ma_int32 deviceIndex)
{
    if (engine->mode != TA_MODE_DEVICE || engine->hasDevice)
        return MA_INVALID_OPERATION;
    return ta_open_device(engine, deviceIndex);
}

/** Pulls frameCount stereo frames while no device does; export calls this once per rendered video frame. */
TA_API ma_int32 ta_render(ta_engine* engine, float* output, ma_uint32 frameCount)
{
    if (engine->hasDevice)
        return MA_INVALID_OPERATION;
    ta_render_block(engine, output, frameCount, 0);
    return MA_SUCCESS;
}

TA_API void ta_get_info(ta_engine* engine, ta_info* info)
{
    info->sampleRate = engine->sampleRate;
    info->channels = 2;
    info->periodFrames = engine->hasDevice ? engine->device.playback.internalPeriodSizeInFrames : 0;
    info->periods = engine->hasDevice ? engine->device.playback.internalPeriods : 0;
    info->backend = engine->hasContext ? (ma_uint32)engine->context.backend : 0xFFFFFFFF;
}

TA_API const char* ta_backend_name(ma_uint32 backend)
{
    return backend == 0xFFFFFFFF ? "Offline" : ma_get_backend_name((ma_backend)backend);
}

/** Stats are written by the audio thread without locking; a torn read only skews one sample of a diagnostic. */
TA_API void ta_get_stats(ta_engine* engine, ta_stats* stats)
{
    *stats = engine->stats;
}

TA_API void ta_reset_stats(ta_engine* engine)
{
    memset(&engine->stats, 0, sizeof(engine->stats));
}

TA_API void ta_set_output_gain(ta_engine* engine, float gain)
{
    ma_atomic_float_set(&engine->outputGain, gain);
}

/* ---- Sounds ---- */

static ta_sound_slot* ta_sound(ta_engine* engine, ma_int32 handle)
{
    if (handle < 0 || handle == TA_HANDLE_OUTPUT || ((ma_uint32)handle & TA_BUS_HANDLE_BIT) != 0)
        return NULL;
    /* A handle kept past its unload must fail rather than reach whichever sound reused the slot. */
    ta_sound_slot* slot = &engine->sounds[handle & (TA_MAX_SOUNDS - 1)];
    return slot->used && slot->generation == ((ma_uint32)handle >> TA_SOUND_SLOT_BITS) ? slot : NULL;
}

/** Skips 0 so that a zeroed handle is never valid. */
static ma_uint32 ta_next_generation(ma_uint32 generation)
{
    generation = (generation + 1) & TA_MAX_GENERATION;
    return generation != 0 ? generation : 1;
}

static void ta_tap_node_prepare(ta_engine* engine, ta_tap_node* tap)
{
    memset(&tap->ring, 0, sizeof(tap->ring));
    tap->clock = &engine->renderClock;
}

/** Releases what a sound owns, on every path that gives one up. */
static void ta_release_sound(ta_sound_slot* slot)
{
    ma_sound_uninit(&slot->sound);
    ma_node_uninit(&slot->tap.base, NULL);
    if (slot->kind == TA_SOURCE_FILE_DECODER)
        ma_decoder_uninit(&slot->source.decoder);
    else if (slot->kind == TA_SOURCE_PUSH)
        ma_pcm_rb_uninit(&slot->source.push.ring);
}

/** Queued routes hold raw node pointers, so they must be applied before any node is freed. */
static void ta_drain_routes(ta_engine* engine)
{
    if (engine->hasDevice)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (ma_atomic_uint32_get(&engine->routeQueueApplied) == ma_atomic_uint32_get(&engine->routeQueueWritten))
                return;
            ma_sleep(1);
        }
    }
    ta_apply_queued_routes(engine);
}

static ma_int32 ta_free_sound_slot(ta_engine* engine)
{
    for (ma_int32 index = 0; index < TA_MAX_SOUNDS; index++)
    {
        if (!engine->sounds[index].used)
            return index;
    }
    return -1;
}

static ma_uint32 ta_sound_flags(ma_uint32 flags)
{
    ma_uint32 soundFlags = 0;
    if (!(flags & TA_LOAD_SPATIAL))
        soundFlags |= MA_SOUND_FLAG_NO_SPATIALIZATION;
    return soundFlags;
}

static ma_int32 ta_finish_sound(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = &engine->sounds[handle];
    ma_uint32 sourceSampleRate = 0;
    ma_sound_get_data_format(&slot->sound, NULL, NULL, &sourceSampleRate, NULL, 0);
    slot->sourceSampleRate = sourceSampleRate;

    /* Starts unrouted, so nothing can be heard until the caller has placed the source. */
    ta_tap_node_prepare(engine, &slot->tap);
    ma_result tapResult = ta_stereo_node_init(engine, &g_tapVtable, &slot->tap.base);
    if (tapResult != MA_SUCCESS)
    {
        ma_sound_uninit(&slot->sound);
        if (slot->kind == TA_SOURCE_FILE_DECODER)
            ma_decoder_uninit(&slot->source.decoder);
        else if (slot->kind == TA_SOURCE_PUSH)
            ma_pcm_rb_uninit(&slot->source.push.ring);
        return tapResult;
    }
    ma_node_attach_output_bus(&slot->sound, 0, &slot->tap.base, 0);

    slot->used = MA_TRUE;
    /* Wraps within the positive handle range; 0 is skipped so a zeroed handle is never valid. */
    slot->generation = ta_next_generation(slot->generation);
    return (ma_int32)((slot->generation << TA_SOUND_SLOT_BITS) | (ma_uint32)handle);
}

/** Returns a sound handle attached to the output, or a negative ma_result on failure. */
TA_API ma_int32 ta_sound_load(ta_engine* engine, const char* path, ma_uint32 flags)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ma_uint32 soundFlags = ta_sound_flags(flags);
    ma_result result;
    if (flags & (TA_LOAD_STREAM_RESOURCE_MANAGER | TA_LOAD_DECODE_TO_MEMORY))
    {
        soundFlags |= (flags & TA_LOAD_DECODE_TO_MEMORY) ? MA_SOUND_FLAG_DECODE : MA_SOUND_FLAG_STREAM;
        result = ma_sound_init_from_file(&engine->engine, path, soundFlags, NULL, NULL, &slot->sound);
        slot->kind = TA_SOURCE_RESOURCE_MANAGER;
    }
    else
    {
        ma_decoder_config decoderConfig = ma_decoder_config_init(ma_format_f32, 0, 0);
        decoderConfig.seekPointCount = TA_MP3_SEEK_POINTS;
        result = ma_decoder_init_file(path, &decoderConfig, &slot->source.decoder);
        if (result != MA_SUCCESS)
            return result;
        result = ma_sound_init_from_data_source(&engine->engine, &slot->source.decoder, soundFlags, NULL, &slot->sound);
        if (result != MA_SUCCESS)
            ma_decoder_uninit(&slot->source.decoder);
        slot->kind = TA_SOURCE_FILE_DECODER;
    }
    if (result != MA_SUCCESS)
        return result;
    return ta_finish_sound(engine, handle);
}

/** A sine generator, the native counterpart of a tone operator. */
TA_API ma_int32 ta_sound_create_sine(ta_engine* engine, double frequency, double amplitude)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ma_waveform_config config = ma_waveform_config_init(ma_format_f32, 2, engine->sampleRate, ma_waveform_type_sine, amplitude, frequency);
    ma_result result = ma_waveform_init(&config, &slot->source.waveform);
    if (result != MA_SUCCESS)
        return result;
    result = ma_sound_init_from_data_source(&engine->engine, &slot->source.waveform, ta_sound_flags(0), NULL, &slot->sound);
    if (result != MA_SUCCESS)
        return result;
    slot->kind = TA_SOURCE_WAVEFORM;
    return ta_finish_sound(engine, handle);
}

/** A source fed with ta_push_write; capacityFrames is how much may be queued ahead. */
TA_API ma_int32 ta_sound_create_push(ta_engine* engine, ma_uint32 channels, ma_uint32 sampleRate, ma_uint32 capacityFrames)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ta_push_source* push = &slot->source.push;
    memset(push, 0, sizeof(*push));
    push->channels = channels;
    push->sampleRate = sampleRate;

    ma_data_source_config sourceConfig = ma_data_source_config_init();
    sourceConfig.vtable = &g_pushVtable;
    ma_result result = ma_data_source_init(&sourceConfig, &push->base);
    if (result != MA_SUCCESS)
        return result;
    result = ma_pcm_rb_init(ma_format_f32, channels, capacityFrames, NULL, NULL, &push->ring);
    if (result != MA_SUCCESS)
        return result;
    /* Pushed audio arrives at the engine rate; without pitch the sound reads exactly what each block needs, nothing ahead. */
    result = ma_sound_init_from_data_source(&engine->engine, push, ta_sound_flags(0) | MA_SOUND_FLAG_NO_PITCH, NULL, &slot->sound);
    if (result != MA_SUCCESS)
    {
        ma_pcm_rb_uninit(&push->ring);
        return result;
    }
    slot->kind = TA_SOURCE_PUSH;
    return ta_finish_sound(engine, handle);
}

/** Queues interleaved frames; returns how many fit. Single producer: call from one thread only. */
TA_API ma_int32 ta_push_write(ta_engine* engine, ma_int32 handle, const float* frames, ma_uint32 frameCount)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL || slot->kind != TA_SOURCE_PUSH)
        return MA_INVALID_ARGS;

    ta_push_source* push = &slot->source.push;
    ma_uint32 written = 0;
    while (written < frameCount)
    {
        ma_uint32 chunk = frameCount - written;
        void* buffer;
        if (ma_pcm_rb_acquire_write(&push->ring, &chunk, &buffer) != MA_SUCCESS || chunk == 0)
            break;
        memcpy(buffer, frames + (size_t)written * push->channels, (size_t)chunk * push->channels * sizeof(float));
        ma_pcm_rb_commit_write(&push->ring, chunk);
        written += chunk;
    }
    return (ma_int32)written;
}

/** Drops everything queued, e.g. when export starts and live audio is still waiting in the queue. */
TA_API void ta_push_clear(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot != NULL && slot->kind == TA_SOURCE_PUSH)
        ma_pcm_rb_reset(&slot->source.push.ring);
}

TA_API ma_int32 ta_push_queued_frames(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot != NULL && slot->kind == TA_SOURCE_PUSH ? (ma_int32)ma_pcm_rb_available_read(&slot->source.push.ring) : -1;
}

TA_API ma_uint64 ta_push_underrun_frames(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot != NULL && slot->kind == TA_SOURCE_PUSH ? ma_atomic_uint64_get(&slot->source.push.underrunFrames) : 0;
}

TA_API void ta_sound_unload(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL)
        return;

    if (ma_atomic_int32_get(&engine->trackedSound) == handle)
        ma_atomic_int32_set(&engine->trackedSound, -1);

    ta_drain_routes(engine);
    ta_release_sound(slot);
    ma_uint32 generation = slot->generation;
    memset(slot, 0, sizeof(*slot));
    slot->generation = generation;
}

TA_API ma_int32 ta_sound_play(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot ? ma_sound_start(&slot->sound) : MA_INVALID_ARGS;
}

/** Stops without rewinding, like pausing a BASS channel. */
TA_API ma_int32 ta_sound_pause(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot ? ma_sound_stop(&slot->sound) : MA_INVALID_ARGS;
}

/**
 * Clears per-source processing history (input read ahead, resampler state) of sources that aren't seeked. Export calls it on every
 * source after detaching the device so a render doesn't depend on what was played before it.
 */
TA_API void ta_sound_reset_state(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL || engine->hasDevice)
        return;
    slot->sound.processingCacheFramesRemaining = 0;
    ma_resampler_reset(&slot->sound.engineNode.resampler);
}

TA_API ma_int32 ta_sound_seek_seconds(ta_engine* engine, ma_int32 handle, double seconds)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL || seconds < 0)
        return MA_INVALID_ARGS;
    return ma_sound_seek_to_pcm_frame(&slot->sound, (ma_uint64)(seconds * slot->sourceSampleRate));
}

TA_API void ta_sound_set_pan(ta_engine* engine, ma_int32 handle, float pan)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot != NULL)
        ma_sound_set_pan(&slot->sound, pan);
}

TA_API ma_int32 ta_sound_is_playing(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot != NULL && ma_sound_is_playing(&slot->sound);
}

/** The next source position to be mixed, or a seek target that hasn't been applied yet. -1 for a stale handle. */
TA_API double ta_sound_get_cursor_seconds(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    ma_uint64 cursor = 0;
    if (slot == NULL || slot->sourceSampleRate == 0 || ma_sound_get_cursor_in_pcm_frames(&slot->sound, &cursor) != MA_SUCCESS)
        return -1;
    return (double)cursor / slot->sourceSampleRate;
}

TA_API ma_int32 ta_sound_get_format(ta_engine* engine, ma_int32 handle, ma_uint32* channels, ma_uint32* sampleRate)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL)
        return MA_INVALID_ARGS;
    return ma_sound_get_data_format(&slot->sound, NULL, channels, sampleRate, NULL, 0);
}

TA_API double ta_sound_get_length_seconds(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    ma_uint64 length = 0;
    if (slot == NULL || slot->sourceSampleRate == 0 || ma_sound_get_length_in_pcm_frames(&slot->sound, &length) != MA_SUCCESS)
        return -1;
    return (double)length / slot->sourceSampleRate;
}

TA_API void ta_sound_set_volume(ta_engine* engine, ma_int32 handle, float volume)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot)
        ma_sound_set_volume(&slot->sound, volume);
}

/** Playback speed by resampling, so pitch follows speed like BASS's Frequency attribute. */
TA_API void ta_sound_set_pitch(ta_engine* engine, ma_int32 handle, float pitch)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot)
        ma_sound_set_pitch(&slot->sound, pitch);
}

TA_API void ta_sound_set_position(ta_engine* engine, ma_int32 handle, float x, float y, float z)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot)
        ma_sound_set_position(&slot->sound, x, y, z);
}

TA_API void ta_set_listener_position(ta_engine* engine, float x, float y, float z)
{
    ma_engine_listener_set_position(&engine->engine, 0, x, y, z);
}

TA_API void ta_set_tracked_sound(ta_engine* engine, ma_int32 handle)
{
    ma_atomic_int32_set(&engine->trackedSound, handle);
}

/**
 * Playback position of the tracked sound, extrapolated from the last audio callback with the shared
 * monotonic clock so it advances smoothly between callbacks. Returns -1 before the first callback.
 */
TA_API double ta_get_playback_seconds(ta_engine* engine, ma_uint64 nowNs)
{
    ta_sound_slot* slot = ta_sound(engine, ma_atomic_int32_get(&engine->trackedSound));
    if (slot == NULL || slot->sourceSampleRate == 0)
        return -1;

    ma_uint64 cursorFrames, cursorTimeNs;
    ma_uint32 isPlaying, sequence;
    do
    {
        sequence = ma_atomic_uint32_get(&engine->positionSequence);
        cursorFrames = engine->trackedCursorFrames;
        cursorTimeNs = engine->trackedCursorTimeNs;
        isPlaying = engine->trackedIsPlaying;
    }
    while ((sequence & 1) != 0 || sequence != ma_atomic_uint32_get(&engine->positionSequence));

    if (cursorTimeNs == 0)
        return -1;

    double seconds = (double)cursorFrames / slot->sourceSampleRate;
    if (isPlaying && nowNs > cursorTimeNs)
        seconds += (double)(nowNs - cursorTimeNs) * 1e-9;
    return seconds;
}

/** Copies the newest frameCount interleaved stereo output frames, zero-padded at the start if fewer exist. */
TA_API void ta_output_read_stereo(ta_engine* engine, float* destination, ma_int32 frameCount)
{
    if (frameCount > TA_TAP_CAPACITY / 2)
        frameCount = TA_TAP_CAPACITY / 2;

    ma_uint64 written = ma_atomic_uint64_get(&engine->outputStereoWritten);
    ma_uint64 start = written >= (ma_uint64)frameCount ? written - (ma_uint64)frameCount : 0;
    for (ma_int32 index = 0; index < frameCount; index++)
    {
        ma_uint64 frame = start + (ma_uint64)index;
        ma_uint64 slot = frame & (TA_TAP_CAPACITY - 1);
        destination[index * 2] = frame < written ? engine->outputStereo[slot * 2] : 0.0f;
        destination[index * 2 + 1] = frame < written ? engine->outputStereo[slot * 2 + 1] : 0.0f;
    }
}

/* ---- Buses, effects, taps and routing ---- */

static ta_bus_slot* ta_bus(ta_engine* engine, ma_int32 handle)
{
    if (handle <= 0 || handle == TA_HANDLE_OUTPUT || ((ma_uint32)handle & TA_BUS_HANDLE_BIT) == 0)
        return NULL;
    ta_bus_slot* slot = &engine->buses[((ma_uint32)handle & (TA_MAX_SOUNDS - 1)) % TA_MAX_BUSES];
    ma_uint32 generation = ((ma_uint32)handle & ~TA_BUS_HANDLE_BIT) >> TA_SOUND_SLOT_BITS;
    return slot->used && slot->generation == generation ? slot : NULL;
}

/** Returns a bus handle routed to the output, or a negative ma_result on failure. */
TA_API ma_int32 ta_bus_create(ta_engine* engine)
{
    for (ma_int32 index = 0; index < TA_MAX_BUSES; index++)
    {
        ta_bus_slot* slot = &engine->buses[index];
        if (slot->used)
            continue;
        ma_result result = ma_sound_group_init(&engine->engine, MA_SOUND_FLAG_NO_SPATIALIZATION | MA_SOUND_FLAG_NO_PITCH, NULL, &slot->group);
        if (result != MA_SUCCESS)
            return result;
        ta_tap_node_prepare(engine, &slot->tap);
        result = ta_stereo_node_init(engine, &g_tapVtable, &slot->tap.base);
        if (result != MA_SUCCESS)
        {
            ma_sound_group_uninit(&slot->group);
            return result;
        }
        ma_node_attach_output_bus(&slot->group, 0, &slot->tap.base, 0);
        ma_node_attach_output_bus(&slot->tap.base, 0, ma_engine_get_endpoint(&engine->engine), 0);
        slot->generation = ta_next_generation(slot->generation);
        slot->used = MA_TRUE;
        return (ma_int32)(TA_BUS_HANDLE_BIT | (slot->generation << TA_SOUND_SLOT_BITS) | (ma_uint32)index);
    }
    return MA_TOO_BIG;
}

/** Whatever is still routed into the bus goes silent until it is routed elsewhere. */
TA_API void ta_bus_destroy(ta_engine* engine, ma_int32 handle)
{
    ta_bus_slot* slot = ta_bus(engine, handle);
    if (slot == NULL)
        return;
    ta_drain_routes(engine);
    ma_sound_group_uninit(&slot->group);
    ma_node_uninit(&slot->tap.base, NULL);
    slot->used = MA_FALSE;
}

TA_API void ta_bus_set_volume(ta_engine* engine, ma_int32 handle, float volume)
{
    ta_bus_slot* slot = ta_bus(engine, handle);
    if (slot != NULL)
        ma_sound_group_set_volume(&slot->group, volume);
}

/** A stopped bus no longer pulls its sources, so they keep their position and generators don't advance. */
TA_API void ta_bus_set_playing(ta_engine* engine, ma_int32 handle, ma_int32 playing)
{
    ta_bus_slot* slot = ta_bus(engine, handle);
    if (slot == NULL)
        return;
    if (playing)
        ma_sound_group_start(&slot->group);
    else
        ma_sound_group_stop(&slot->group);
}

static ta_tap_ring* ta_handle_ring(ta_engine* engine, ma_int32 handle)
{
    if (handle == TA_HANDLE_OUTPUT)
        return &engine->outputTap;
    ta_sound_slot* sound = ta_sound(engine, handle);
    if (sound != NULL)
        return &sound->tap.ring;
    ta_bus_slot* bus = ta_bus(engine, handle);
    return bus != NULL ? &bus->tap.ring : NULL;
}

static ma_bool32 ta_ring_is_stale(ta_engine* engine, ma_int32 handle, ta_tap_ring* ring)
{
    return handle != TA_HANDLE_OUTPUT
           && ma_atomic_uint64_get(&engine->renderClock) - ma_atomic_uint64_get(&ring->lastWriteClock) > TA_TAP_STALE_FRAMES;
}

/** The highest sample of a sound, bus or the output in its newest frames. Doesn't consume anything, so any number of meters can read it. */
TA_API float ta_get_level(ta_engine* engine, ma_int32 handle, ma_int32 frameCount)
{
    ta_tap_ring* ring = ta_handle_ring(engine, handle);
    if (ring == NULL || ta_ring_is_stale(engine, handle, ring))
        return 0;

    if (frameCount > TA_TAP_CAPACITY / 2)
        frameCount = TA_TAP_CAPACITY / 2;
    ma_uint64 written = ma_atomic_uint64_get(&ring->framesWritten);
    ma_uint64 count = written < (ma_uint64)frameCount ? written : (ma_uint64)frameCount;
    float peak = 0;
    for (ma_uint64 index = 0; index < count; index++)
    {
        float sample = ring->samples[(written - 1 - index) & (TA_TAP_CAPACITY - 1)];
        float magnitude = sample < 0 ? -sample : sample;
        if (magnitude > peak)
            peak = magnitude;
    }
    return peak;
}

/** Copies the newest mono samples of a sound, bus or the output; returns 0 and silence for a stale handle or a stopped source. */
TA_API ma_int32 ta_read_latest(ta_engine* engine, ma_int32 handle, float* destination, ma_int32 count)
{
    ta_tap_ring* ring = ta_handle_ring(engine, handle);
    if (ring == NULL || ta_ring_is_stale(engine, handle, ring))
    {
        memset(destination, 0, (size_t)count * sizeof(float));
        return 0;
    }
    ta_tap_ring_read_latest(ring, destination, count);
    return count;
}

/**
 * Routes a sound or bus into a bus, to TA_HANDLE_OUTPUT, or to nothing (target 0). With a running device the
 * change is queued and applied by the audio thread before its next block, so no block sees half a change.
 */
TA_API ma_int32 ta_route(ta_engine* engine, ma_int32 source, ma_int32 target)
{
    ma_node* sourceNode = NULL;
    ta_sound_slot* sound = ta_sound(engine, source);
    if (sound != NULL)
    {
        sourceNode = &sound->tap.base;
    }
    else
    {
        ta_bus_slot* bus = ta_bus(engine, source);
        if (bus != NULL)
            sourceNode = &bus->tap.base;
    }
    if (sourceNode == NULL)
        return MA_INVALID_ARGS;

    ma_node* targetNode = NULL;
    if (target == TA_HANDLE_OUTPUT)
    {
        targetNode = ma_engine_get_endpoint(&engine->engine);
    }
    else if (target != 0)
    {
        ta_bus_slot* bus = ta_bus(engine, target);
        if (bus == NULL)
            return MA_INVALID_ARGS;
        targetNode = &bus->group;
    }

    if (!engine->hasDevice)
    {
        ta_apply_queued_routes(engine);
        return targetNode != NULL ? ma_node_attach_output_bus(sourceNode, 0, targetNode, 0) : ma_node_detach_output_bus(sourceNode, 0);
    }

    ma_uint32 written = ma_atomic_uint32_get(&engine->routeQueueWritten);
    if (written - ma_atomic_uint32_get(&engine->routeQueueApplied) >= TA_ROUTE_QUEUE_CAPACITY)
        return MA_BUSY;
    engine->routeQueue[written & (TA_ROUTE_QUEUE_CAPACITY - 1)] = (ta_route_command){ sourceNode, targetNode };
    ma_atomic_uint32_set(&engine->routeQueueWritten, written + 1);
    return MA_SUCCESS;
}

TA_API ma_int32 ta_echo_create(ta_engine* engine)
{
    for (ma_int32 index = 0; index < TA_MAX_ECHOES; index++)
    {
        ta_echo_slot* slot = &engine->echoes[index];
        if (slot->used)
            continue;

        ta_echo_node* echo = &slot->node;
        echo->sampleRate = engine->sampleRate;
        echo->capacityFrames = engine->sampleRate * TA_ECHO_MAX_SECONDS;
        echo->writeFrame = 0;
        echo->delayLine = (float*)calloc((size_t)echo->capacityFrames * 2, sizeof(float));
        if (echo->delayLine == NULL)
            return MA_OUT_OF_MEMORY;
        ma_atomic_float_set(&echo->delaySeconds, 0.3f);
        ma_atomic_float_set(&echo->feedback, 0.4f);
        ma_atomic_float_set(&echo->wetMix, 0.0f);

        ma_result result = ta_stereo_node_init(engine, &g_echoVtable, &echo->base);
        if (result != MA_SUCCESS)
        {
            free(echo->delayLine);
            return result;
        }
        ma_node_attach_output_bus(&echo->base, 0, ma_engine_get_endpoint(&engine->engine), 0);
        slot->used = MA_TRUE;
        return index;
    }
    return MA_TOO_BIG;
}

TA_API void ta_echo_set(ta_engine* engine, ma_int32 echo, float delaySeconds, float feedback, float wetMix)
{
    if (echo < 0 || echo >= TA_MAX_ECHOES || !engine->echoes[echo].used)
        return;
    ta_echo_node* node = &engine->echoes[echo].node;
    ma_atomic_float_set(&node->delaySeconds, delaySeconds);
    ma_atomic_float_set(&node->feedback, feedback);
    ma_atomic_float_set(&node->wetMix, wetMix);
}

/** Creates a pass-through tap node; tap 0 is reserved for the device output. */
TA_API ma_int32 ta_tap_create(ta_engine* engine)
{
    for (ma_int32 index = 1; index < TA_MAX_TAPS; index++)
    {
        ta_tap_slot* slot = &engine->taps[index];
        if (slot->used)
            continue;
        memset(&slot->node.ring, 0, sizeof(slot->node.ring));
        ma_result result = ta_stereo_node_init(engine, &g_tapVtable, &slot->node.base);
        if (result != MA_SUCCESS)
            return result;
        ma_node_attach_output_bus(&slot->node.base, 0, ma_engine_get_endpoint(&engine->engine), 0);
        slot->used = MA_TRUE;
        return index;
    }
    return MA_TOO_BIG;
}

static ta_tap_ring* ta_tap(ta_engine* engine, ma_int32 tap)
{
    if (tap == 0)
        return &engine->outputTap;
    if (tap < 0 || tap >= TA_MAX_TAPS || !engine->taps[tap].used)
        return NULL;
    return &engine->taps[tap].node.ring;
}

TA_API ma_uint64 ta_tap_read_latest(ta_engine* engine, ma_int32 tap, float* destination, ma_int32 count)
{
    ta_tap_ring* ring = ta_tap(engine, tap);
    return ring ? ta_tap_ring_read_latest(ring, destination, count) : 0;
}

TA_API ma_int32 ta_tap_read_since(ta_engine* engine, ma_int32 tap, ma_uint64 sinceFrame, float* destination, ma_int32 maxCount, ma_uint64* totalFrames)
{
    ta_tap_ring* ring = ta_tap(engine, tap);
    if (ring == NULL)
    {
        *totalFrames = 0;
        return 0;
    }
    return ta_tap_ring_read_since(ring, sinceFrame, destination, maxCount, totalFrames);
}

/** Returns the highest absolute sample since the last call and resets it. */
TA_API float ta_tap_take_peak(ta_engine* engine, ma_int32 tap)
{
    ta_tap_ring* ring = ta_tap(engine, tap);
    return ring ? ma_atomic_exchange_f32(&ring->peak.value, 0.0f) : 0.0f;
}


/* ---- Waveform and analysis: decode without playback ---- */

/**
 * Decodes a whole file to mono and writes min/max pairs for bucketCount equal slices. Runs on the caller's
 * thread; it's meant for a background job, not the frame loop.
 */
TA_API ma_int32 ta_decode_peaks(const char* path, ma_int32 bucketCount, float* minMaxPairs, double* lengthSeconds)
{
    ma_decoder decoder;
    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, 1, 0);
    ma_result result = ma_decoder_init_file(path, &config, &decoder);
    if (result != MA_SUCCESS)
        return result;

    ma_uint64 totalFrames = 0;
    result = ma_decoder_get_length_in_pcm_frames(&decoder, &totalFrames);
    if (result != MA_SUCCESS || totalFrames == 0 || bucketCount <= 0)
    {
        ma_decoder_uninit(&decoder);
        return result != MA_SUCCESS ? result : MA_INVALID_FILE;
    }
    *lengthSeconds = (double)totalFrames / decoder.outputSampleRate;

    for (ma_int32 bucket = 0; bucket < bucketCount; bucket++)
    {
        minMaxPairs[bucket * 2] = 0;
        minMaxPairs[bucket * 2 + 1] = 0;
    }

    float chunk[4096];
    ma_uint64 frameIndex = 0;
    for (;;)
    {
        ma_uint64 framesRead = 0;
        result = ma_decoder_read_pcm_frames(&decoder, chunk, 4096, &framesRead);
        for (ma_uint64 index = 0; index < framesRead; index++, frameIndex++)
        {
            ma_uint64 bucket = frameIndex * (ma_uint64)bucketCount / totalFrames;
            if (bucket >= (ma_uint64)bucketCount)
                bucket = (ma_uint64)bucketCount - 1;
            float sample = chunk[index];
            if (sample < minMaxPairs[bucket * 2])
                minMaxPairs[bucket * 2] = sample;
            if (sample > minMaxPairs[bucket * 2 + 1])
                minMaxPairs[bucket * 2 + 1] = sample;
        }
        if (result != MA_SUCCESS || framesRead == 0)
            break;
    }

    ma_decoder_uninit(&decoder);
    return MA_SUCCESS;
}

/**
 * Decodes frameCount interleaved frames starting at startFrame (in the file's own rate) without resampling,
 * so the result can be compared sample by sample with another decoder.
 */
TA_API ma_int32 ta_decode_window(const char* path, ma_uint64 startFrame, ma_uint32 frameCount, ma_uint32 channels, ma_int32 useSeekTable, float* output, ma_uint32* sampleRate)
{
    ma_decoder decoder;
    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, channels, 0);
    config.seekPointCount = useSeekTable ? TA_MP3_SEEK_POINTS : 0;
    ma_result result = ma_decoder_init_file(path, &config, &decoder);
    if (result != MA_SUCCESS)
        return result;

    *sampleRate = decoder.outputSampleRate;
    result = ma_decoder_seek_to_pcm_frame(&decoder, startFrame);
    ma_uint64 framesRead = 0;
    if (result == MA_SUCCESS)
        result = ma_decoder_read_pcm_frames(&decoder, output, frameCount, &framesRead);
    for (ma_uint64 index = framesRead * channels; index < (ma_uint64)frameCount * channels; index++)
        output[index] = 0;

    ma_decoder_uninit(&decoder);
    return result == MA_AT_END ? MA_SUCCESS : result;
}

/* ---- Generator source ---- */

enum { TA_STAGE_IDLE, TA_STAGE_ATTACK, TA_STAGE_DECAY, TA_STAGE_SUSTAIN, TA_STAGE_RELEASE };
enum { TA_WAVE_SINE, TA_WAVE_SQUARE, TA_WAVE_SAWTOOTH, TA_WAVE_TRIANGLE, TA_WAVE_WHITE_NOISE, TA_WAVE_PINK_NOISE };
#define TA_PI 3.14159265358979323846

static float ta_generator_envelope(ta_generator_source* generator)
{
    if (ma_atomic_int32_exchange(&generator->resetSignal, 0))
    {
        generator->stage = TA_STAGE_IDLE;
        generator->envelope = 0;
        generator->stageFrames = 0;
        generator->playedFrames = 0;
    }

    float rate = (float)generator->sampleRate;
    if (ma_atomic_int32_exchange(&generator->attackSignal, 0))
    {
        generator->stage = TA_STAGE_ATTACK;
        generator->stageFrames = 0;
        generator->playedFrames = 0;
        float duration = ma_atomic_float_get(&generator->durationSeconds);
        generator->durationFrames = duration < 0 ? -1 : (ma_int64)(duration * rate);
    }

    ma_bool32 startRelease = ma_atomic_int32_exchange(&generator->releaseSignal, 0)
                             || (generator->durationFrames >= 0 && generator->playedFrames >= generator->durationFrames);
    if (startRelease && generator->stage != TA_STAGE_IDLE && generator->stage != TA_STAGE_RELEASE)
    {
        generator->releaseStartValue = generator->envelope;
        generator->stageFrames = 0;
        generator->stage = TA_STAGE_RELEASE;
    }

    float sustain = ma_atomic_float_get(&generator->sustainLevel);
    switch (generator->stage)
    {
        case TA_STAGE_ATTACK:
        {
            ma_int64 frames = (ma_int64)(ma_atomic_float_get(&generator->attackSeconds) * rate);
            if (generator->stageFrames < frames)
            {
                generator->envelope = (float)generator->stageFrames / (float)frames;
            }
            else
            {
                generator->envelope = 1;
                generator->stageFrames = 0;
                generator->stage = TA_STAGE_DECAY;
            }
            break;
        }
        case TA_STAGE_DECAY:
        {
            ma_int64 frames = (ma_int64)(ma_atomic_float_get(&generator->decaySeconds) * rate);
            if (generator->stageFrames < frames)
            {
                generator->envelope = 1.0f - (float)generator->stageFrames / (float)frames * (1.0f - sustain);
            }
            else
            {
                generator->envelope = sustain;
                generator->stageFrames = 0;
                generator->stage = TA_STAGE_SUSTAIN;
            }
            break;
        }
        case TA_STAGE_SUSTAIN:
            generator->envelope = sustain;
            break;
        case TA_STAGE_RELEASE:
        {
            ma_int64 frames = (ma_int64)(ma_atomic_float_get(&generator->releaseSeconds) * rate);
            if (generator->stageFrames < frames)
            {
                generator->envelope = (float)(generator->releaseStartValue * (1.0 - (double)generator->stageFrames / (double)frames));
            }
            else
            {
                generator->envelope = 0;
                generator->stageFrames = 0;
                generator->stage = TA_STAGE_IDLE;
            }
            break;
        }
        default:
            generator->envelope = 0;
            break;
    }

    generator->stageFrames++;
    if (generator->stage != TA_STAGE_IDLE && generator->stage != TA_STAGE_RELEASE)
        generator->playedFrames++;
    return generator->envelope;
}

static float ta_generator_noise(ta_generator_source* generator)
{
    /* xorshift32: no locks and no libc state, so it is safe on the audio thread. */
    ma_uint32 x = generator->noiseState;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    generator->noiseState = x;
    return (float)((double)x / 2147483648.0 - 1.0);
}

static float ta_generator_sample(ta_generator_source* generator, double phase, ma_int32 waveform)
{
    double t = phase / (2.0 * TA_PI);
    switch (waveform)
    {
        case TA_WAVE_SQUARE:
            return t < 0.5 ? 0.8f : -0.8f;
        case TA_WAVE_SAWTOOTH:
            return (float)(2.0 * t - 1.0) * 0.8f;
        case TA_WAVE_TRIANGLE:
            return (float)(4.0 * fabs(t - 0.5) - 1.0) * 0.8f;
        case TA_WAVE_WHITE_NOISE:
            return ta_generator_noise(generator) * 0.5f;
        case TA_WAVE_PINK_NOISE:
        {
            double white = ta_generator_noise(generator);
            generator->pinkB0 = 0.99765 * generator->pinkB0 + white * 0.0990460;
            generator->pinkB1 = 0.96300 * generator->pinkB1 + white * 0.2965164;
            generator->pinkB2 = 0.57000 * generator->pinkB2 + white * 1.0526913;
            return (float)((generator->pinkB0 + generator->pinkB1 + generator->pinkB2 + white * 0.1848) * 0.15);
        }
        default:
            return (float)sin(phase);
    }
}

static ma_result ta_generator_read(ma_data_source* dataSource, void* framesOut, ma_uint64 frameCount, ma_uint64* framesRead)
{
    ta_generator_source* generator = (ta_generator_source*)dataSource;
    float* output = (float*)framesOut;
    double phaseIncrement = 2.0 * TA_PI * ma_atomic_float_get(&generator->frequency) / generator->sampleRate;
    ma_int32 waveform = ma_atomic_int32_get(&generator->waveform);

    for (ma_uint64 frame = 0; frame < frameCount; frame++)
    {
        float envelope = ta_generator_envelope(generator);
        output[frame] = ta_generator_sample(generator, generator->phase, waveform) * envelope;
        generator->phase += phaseIncrement;
        if (generator->phase >= 2.0 * TA_PI)
            generator->phase -= 2.0 * TA_PI;
    }

    /* A raised signal counts as active too, so a trigger reads as active before the audio thread has seen it. */
    ma_int32 pending = ma_atomic_int32_get(&generator->attackSignal);
    ma_atomic_int32_set(&generator->isActive, generator->stage != TA_STAGE_IDLE || pending);
    generator->cursor += frameCount;
    *framesRead = frameCount;
    return MA_SUCCESS;
}

static ma_result ta_generator_seek(ma_data_source* dataSource, ma_uint64 frameIndex)
{
    (void)dataSource;
    (void)frameIndex;
    return MA_NOT_IMPLEMENTED;
}

static ma_result ta_generator_get_format(ma_data_source* dataSource, ma_format* format, ma_uint32* channels, ma_uint32* sampleRate, ma_channel* channelMap, size_t channelMapCap)
{
    ta_generator_source* generator = (ta_generator_source*)dataSource;
    *format = ma_format_f32;
    *channels = 1;
    *sampleRate = generator->sampleRate;
    ma_channel_map_init_standard(ma_standard_channel_map_default, channelMap, channelMapCap, 1);
    return MA_SUCCESS;
}

static ma_data_source_vtable g_generatorVtable = { ta_generator_read, ta_generator_seek, ta_generator_get_format, NULL, NULL, NULL, 0 };

/** Returns a playing sound that stays silent until ta_generator_signal triggers its envelope. */
TA_API ma_int32 ta_sound_create_generator(ta_engine* engine)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ta_generator_source* generator = &slot->source.generator;
    memset(generator, 0, sizeof(*generator));
    generator->sampleRate = engine->sampleRate;
    generator->noiseState = 0x9E3779B9u + (ma_uint32)handle;
    generator->durationFrames = -1;
    ma_atomic_float_set(&generator->frequency, 440.0f);
    ma_atomic_float_set(&generator->attackSeconds, 0.01f);
    ma_atomic_float_set(&generator->decaySeconds, 0.1f);
    ma_atomic_float_set(&generator->sustainLevel, 0.7f);
    ma_atomic_float_set(&generator->releaseSeconds, 0.3f);
    ma_atomic_float_set(&generator->durationSeconds, -1.0f);

    ma_data_source_config sourceConfig = ma_data_source_config_init();
    sourceConfig.vtable = &g_generatorVtable;
    ma_result result = ma_data_source_init(&sourceConfig, &generator->base);
    if (result != MA_SUCCESS)
        return result;
    result = ma_sound_init_from_data_source(&engine->engine, generator, ta_sound_flags(0) | MA_SOUND_FLAG_NO_PITCH, NULL, &slot->sound);
    if (result != MA_SUCCESS)
        return result;
    slot->kind = TA_SOURCE_GENERATOR;
    ma_int32 finished = ta_finish_sound(engine, handle);
    if (finished > 0)
        ma_sound_start(&slot->sound);
    return finished;
}

static ta_generator_source* ta_generator(ta_engine* engine, ma_int32 handle)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    return slot != NULL && slot->kind == TA_SOURCE_GENERATOR ? &slot->source.generator : NULL;
}

/** durationSeconds < 0 sustains until a release signal. */
TA_API void ta_generator_set(ta_engine* engine, ma_int32 handle, float frequency, ma_int32 waveform, float attackSeconds, float decaySeconds, float sustainLevel, float releaseSeconds, float durationSeconds)
{
    ta_generator_source* generator = ta_generator(engine, handle);
    if (generator == NULL)
        return;
    ma_atomic_float_set(&generator->frequency, frequency < 20.0f ? 20.0f : (frequency > 20000.0f ? 20000.0f : frequency));
    ma_atomic_int32_set(&generator->waveform, waveform);
    ma_atomic_float_set(&generator->attackSeconds, attackSeconds < 0.001f ? 0.001f : attackSeconds);
    ma_atomic_float_set(&generator->decaySeconds, decaySeconds < 0.001f ? 0.001f : decaySeconds);
    ma_atomic_float_set(&generator->sustainLevel, sustainLevel < 0 ? 0 : (sustainLevel > 1 ? 1 : sustainLevel));
    ma_atomic_float_set(&generator->releaseSeconds, releaseSeconds < 0.001f ? 0.001f : releaseSeconds);
    ma_atomic_float_set(&generator->durationSeconds, durationSeconds);
}

TA_API void ta_generator_attack(ta_engine* engine, ma_int32 handle)
{
    ta_generator_source* generator = ta_generator(engine, handle);
    if (generator == NULL)
        return;
    ma_atomic_int32_set(&generator->attackSignal, 1);
    ma_atomic_int32_set(&generator->isActive, 1);
}

TA_API void ta_generator_release(ta_engine* engine, ma_int32 handle)
{
    ta_generator_source* generator = ta_generator(engine, handle);
    if (generator != NULL)
        ma_atomic_int32_set(&generator->releaseSignal, 1);
}

TA_API void ta_generator_reset(ta_engine* engine, ma_int32 handle)
{
    ta_generator_source* generator = ta_generator(engine, handle);
    if (generator == NULL)
        return;
    ma_atomic_int32_set(&generator->attackSignal, 0);
    ma_atomic_int32_set(&generator->resetSignal, 1);
    ma_atomic_int32_set(&generator->isActive, 0);
}

TA_API ma_int32 ta_generator_is_active(ta_engine* engine, ma_int32 handle)
{
    ta_generator_source* generator = ta_generator(engine, handle);
    return generator != NULL && ma_atomic_int32_get(&generator->isActive);
}

/* ---- Offline decoders: independent of any engine, so analysis can run on worker threads. ---- */

TA_API ma_decoder* ta_offline_open(const char* path, ma_uint32* channels, ma_uint32* sampleRate, ma_uint64* frameCount, ma_int32* outResult)
{
    ma_decoder* decoder = (ma_decoder*)malloc(sizeof(ma_decoder));
    if (decoder == NULL)
    {
        *outResult = MA_OUT_OF_MEMORY;
        return NULL;
    }

    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, 0, 0);
    config.seekPointCount = TA_MP3_SEEK_POINTS;
    ma_result result = ma_decoder_init_file(path, &config, decoder);
    if (result == MA_SUCCESS)
    {
        result = ma_decoder_get_length_in_pcm_frames(decoder, frameCount);
        if (result != MA_SUCCESS)
            ma_decoder_uninit(decoder);
    }
    if (result != MA_SUCCESS)
    {
        free(decoder);
        *outResult = result;
        return NULL;
    }

    *channels = decoder->outputChannels;
    *sampleRate = decoder->outputSampleRate;
    *outResult = MA_SUCCESS;
    return decoder;
}

TA_API ma_int32 ta_offline_seek(ma_decoder* decoder, ma_uint64 frame)
{
    return ma_decoder_seek_to_pcm_frame(decoder, frame);
}

/** Returns the number of interleaved frames read; fewer than asked for at the end of the file. */
TA_API ma_int64 ta_offline_read(ma_decoder* decoder, float* output, ma_uint64 frameCount)
{
    ma_uint64 framesRead = 0;
    ma_result result = ma_decoder_read_pcm_frames(decoder, output, frameCount, &framesRead);
    return result == MA_SUCCESS || result == MA_AT_END ? (ma_int64)framesRead : result;
}

TA_API void ta_offline_close(ma_decoder* decoder)
{
    ma_decoder_uninit(decoder);
    free(decoder);
}

/* ---- Capture ---- */

TA_API ma_int32 ta_get_device_count(ta_engine* engine, ma_int32 capture)
{
    ma_device_info* infos;
    ma_uint32 count;
    return ta_get_devices(engine, capture, &infos, &count) == MA_SUCCESS ? (ma_int32)count : 0;
}

/** Writes the device's UTF-8 name into the caller's buffer and returns its length in bytes. */
TA_API ma_int32 ta_get_device_name(ta_engine* engine, ma_int32 capture, ma_int32 index, char* buffer, ma_int32 bufferLength)
{
    ma_device_info* infos;
    ma_uint32 count;
    if (ta_get_devices(engine, capture, &infos, &count) != MA_SUCCESS || index < 0 || (ma_uint32)index >= count || bufferLength <= 0)
        return 0;

    size_t length = strlen(infos[index].name);
    if (length >= (size_t)bufferLength)
        length = (size_t)bufferLength - 1;
    memcpy(buffer, infos[index].name, length);
    buffer[length] = 0;
    return (ma_int32)length;
}

static void ta_capture_callback(ma_device* device, void* output, const void* input, ma_uint32 frameCount)
{
    ta_engine* engine = (ta_engine*)device->pUserData;
    const float* samples = (const float*)input;
    ma_uint64 written = ma_atomic_uint64_get(&engine->captureFramesWritten);
    (void)output;

    for (ma_uint32 frame = 0; frame < frameCount; frame++)
    {
        ma_uint64 slot = (written + frame) & (TA_CAPTURE_RING_FRAMES - 1);
        engine->captureRing[slot * 2] = samples[frame * 2];
        engine->captureRing[slot * 2 + 1] = samples[frame * 2 + 1];
    }
    ma_atomic_uint64_set(&engine->captureFramesWritten, written + frameCount);
}

/**
 * Opens a capture device (-1 for the default). Loopback records an output device and only exists on
 * WASAPI; on PulseAudio/PipeWire the output monitors are listed as ordinary capture devices instead.
 */
TA_API ma_int32 ta_capture_start(ta_engine* engine, ma_int32 deviceIndex, ma_int32 loopback)
{
    if (engine->captureRunning || !engine->hasContext)
        return MA_INVALID_OPERATION;

    ma_device_config config = ma_device_config_init(loopback ? ma_device_type_loopback : ma_device_type_capture);
    config.capture.format = ma_format_f32;
    config.capture.channels = 2;
    config.sampleRate = engine->sampleRate; /* miniaudio converts, so analysis and recordings see one format */
    config.periodSizeInMilliseconds = 10;
    config.dataCallback = ta_capture_callback;
    config.pUserData = engine;
    if (deviceIndex >= 0)
    {
        ma_device_info* infos;
        ma_uint32 count;
        ma_result listResult = ta_get_devices(engine, !loopback, &infos, &count);
        if (listResult != MA_SUCCESS)
            return listResult;
        if ((ma_uint32)deviceIndex >= count)
            return MA_INVALID_ARGS;
        config.capture.pDeviceID = &infos[deviceIndex].id;
    }

    engine->captureRing = (float*)calloc(TA_CAPTURE_RING_FRAMES * 2, sizeof(float));
    if (engine->captureRing == NULL)
        return MA_OUT_OF_MEMORY;
    ma_atomic_uint64_set(&engine->captureFramesWritten, 0);

    ma_result result = ma_device_init(&engine->context, &config, &engine->captureDevice);
    if (result != MA_SUCCESS)
    {
        free(engine->captureRing);
        engine->captureRing = NULL;
        return result;
    }

    result = ma_device_start(&engine->captureDevice);
    if (result != MA_SUCCESS)
    {
        ma_device_uninit(&engine->captureDevice);
        free(engine->captureRing);
        engine->captureRing = NULL;
        return result;
    }
    engine->captureRunning = MA_TRUE;
    return MA_SUCCESS;
}

TA_API void ta_capture_stop(ta_engine* engine)
{
    if (!engine->captureRunning)
        return;
    ma_device_uninit(&engine->captureDevice);
    free(engine->captureRing);
    engine->captureRing = NULL;
    engine->captureRunning = MA_FALSE;
}

/** False once the device has stopped by itself, e.g. after being unplugged. */
TA_API ma_int32 ta_capture_is_running(ta_engine* engine)
{
    return engine->captureRunning && ma_device_is_started(&engine->captureDevice);
}

TA_API ma_uint64 ta_capture_frames_written(ta_engine* engine)
{
    return ma_atomic_uint64_get(&engine->captureFramesWritten);
}

TA_API ma_int32 ta_capture_ring_capacity(void)
{
    return TA_CAPTURE_RING_FRAMES;
}

/** Copies interleaved stereo frames starting at an absolute frame position; returns how many were copied. */
TA_API ma_int32 ta_capture_read(ta_engine* engine, ma_uint64 startFrame, float* destination, ma_int32 frameCount)
{
    if (!engine->captureRunning)
        return 0;

    ma_uint64 written = ma_atomic_uint64_get(&engine->captureFramesWritten);
    if (startFrame >= written || written - startFrame > TA_CAPTURE_RING_FRAMES)
        return 0;

    ma_uint64 available = written - startFrame;
    ma_int32 count = available < (ma_uint64)frameCount ? (ma_int32)available : frameCount;
    for (ma_int32 frame = 0; frame < count; frame++)
    {
        ma_uint64 slot = (startFrame + (ma_uint64)frame) & (TA_CAPTURE_RING_FRAMES - 1);
        destination[frame * 2] = engine->captureRing[slot * 2];
        destination[frame * 2 + 1] = engine->captureRing[slot * 2 + 1];
    }
    return count;
}
