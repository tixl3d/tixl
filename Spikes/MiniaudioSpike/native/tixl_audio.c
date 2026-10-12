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

#define TA_MAX_SOUNDS 16
#define TA_MAX_BUSES 16
#define TA_MAX_ECHOES 8
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
} ta_tap_ring;

typedef struct
{
    ma_node_base base;
    ta_tap_ring ring;
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

typedef struct
{
    ma_bool32 used;
    ta_source_kind kind;
    union
    {
        ma_decoder decoder;
        ma_waveform waveform;
        ta_push_source push;
    } source;
    ma_sound sound;
    ma_uint32 sourceSampleRate;
} ta_sound_slot;

typedef struct
{
    ma_bool32 used;
    ma_sound_group group;
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
    ma_atomic_int32 applyRoutesImmediately;

    ma_device captureDevice;
    ma_bool32 captureRunning;
    ma_atomic_float capturePeak;
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
        ma_node_attach_output_bus(command->source, 0, command->target, 0);
        applied++;
    }
    ma_atomic_uint32_set(&engine->routeQueueApplied, applied);
}

static void ta_render_block(ta_engine* engine, float* output, ma_uint32 frameCount, ma_uint64 startNs)
{
    ta_apply_queued_routes(engine);
    ma_engine_read_pcm_frames(&engine->engine, output, frameCount, NULL);
    ta_tap_ring_write(&engine->outputTap, output, frameCount, 2);

    float gain = ma_atomic_float_get(&engine->outputGain);
    if (gain != 1.0f)
    {
        for (ma_uint32 index = 0; index < frameCount * 2; index++)
            output[index] *= gain;
    }

    ma_int32 tracked = ma_atomic_int32_get(&engine->trackedSound);
    if (tracked >= 0 && tracked < TA_MAX_SOUNDS && engine->sounds[tracked].used)
    {
        ma_uint64 cursor = 0;
        ma_sound_get_cursor_in_pcm_frames(&engine->sounds[tracked].sound, &cursor);
        ma_atomic_uint32_fetch_add(&engine->positionSequence, 1);
        engine->trackedCursorFrames = cursor;
        engine->trackedCursorTimeNs = startNs;
        engine->trackedIsPlaying = ma_sound_is_playing(&engine->sounds[tracked].sound);
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
        ma_sound_uninit(&slot->sound);
        if (slot->kind == TA_SOURCE_FILE_DECODER)
            ma_decoder_uninit(&slot->source.decoder);
        else if (slot->kind == TA_SOURCE_PUSH)
            ma_pcm_rb_uninit(&slot->source.push.ring);
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
            ma_sound_group_uninit(&engine->buses[index].group);
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
    if (handle < 0 || handle >= TA_MAX_SOUNDS || !engine->sounds[handle].used)
        return NULL;
    return &engine->sounds[handle];
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
    slot->used = MA_TRUE;
    return handle;
}

/** Returns a sound handle attached to the output, or a negated ma_result on failure. */
TA_API ma_int32 ta_sound_load(ta_engine* engine, const char* path, ma_uint32 flags)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return -MA_TOO_BIG;

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
            return -result;
        result = ma_sound_init_from_data_source(&engine->engine, &slot->source.decoder, soundFlags, NULL, &slot->sound);
        if (result != MA_SUCCESS)
            ma_decoder_uninit(&slot->source.decoder);
        slot->kind = TA_SOURCE_FILE_DECODER;
    }
    if (result != MA_SUCCESS)
        return -result;
    return ta_finish_sound(engine, handle);
}

/** A sine generator, the native counterpart of a tone operator. */
TA_API ma_int32 ta_sound_create_sine(ta_engine* engine, double frequency, double amplitude)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return -MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ma_waveform_config config = ma_waveform_config_init(ma_format_f32, 2, engine->sampleRate, ma_waveform_type_sine, amplitude, frequency);
    ma_result result = ma_waveform_init(&config, &slot->source.waveform);
    if (result != MA_SUCCESS)
        return -result;
    result = ma_sound_init_from_data_source(&engine->engine, &slot->source.waveform, ta_sound_flags(0), NULL, &slot->sound);
    if (result != MA_SUCCESS)
        return -result;
    slot->kind = TA_SOURCE_WAVEFORM;
    return ta_finish_sound(engine, handle);
}

/** A source fed with ta_push_write; capacityFrames is how much may be queued ahead. */
TA_API ma_int32 ta_sound_create_push(ta_engine* engine, ma_uint32 channels, ma_uint32 sampleRate, ma_uint32 capacityFrames)
{
    ma_int32 handle = ta_free_sound_slot(engine);
    if (handle < 0)
        return -MA_TOO_BIG;

    ta_sound_slot* slot = &engine->sounds[handle];
    ta_push_source* push = &slot->source.push;
    memset(push, 0, sizeof(*push));
    push->channels = channels;
    push->sampleRate = sampleRate;

    ma_data_source_config sourceConfig = ma_data_source_config_init();
    sourceConfig.vtable = &g_pushVtable;
    ma_result result = ma_data_source_init(&sourceConfig, &push->base);
    if (result != MA_SUCCESS)
        return -result;
    result = ma_pcm_rb_init(ma_format_f32, channels, capacityFrames, NULL, NULL, &push->ring);
    if (result != MA_SUCCESS)
        return -result;
    /* Pushed audio arrives at the engine rate; without pitch the sound reads exactly what each block needs, nothing ahead. */
    result = ma_sound_init_from_data_source(&engine->engine, push, ta_sound_flags(0) | MA_SOUND_FLAG_NO_PITCH, NULL, &slot->sound);
    if (result != MA_SUCCESS)
    {
        ma_pcm_rb_uninit(&push->ring);
        return -result;
    }
    slot->kind = TA_SOURCE_PUSH;
    return ta_finish_sound(engine, handle);
}

/** Queues interleaved frames; returns how many fit. Single producer: call from one thread only. */
TA_API ma_int32 ta_push_write(ta_engine* engine, ma_int32 handle, const float* frames, ma_uint32 frameCount)
{
    ta_sound_slot* slot = ta_sound(engine, handle);
    if (slot == NULL || slot->kind != TA_SOURCE_PUSH)
        return -MA_INVALID_ARGS;

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

    ma_sound_uninit(&slot->sound);
    if (slot->kind == TA_SOURCE_FILE_DECODER)
        ma_decoder_uninit(&slot->source.decoder);
    else if (slot->kind == TA_SOURCE_PUSH)
        ma_pcm_rb_uninit(&slot->source.push.ring);
    memset(slot, 0, sizeof(*slot));
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

/* ---- Buses, effects, taps and routing ---- */

TA_API ma_int32 ta_bus_create(ta_engine* engine)
{
    for (ma_int32 index = 0; index < TA_MAX_BUSES; index++)
    {
        ta_bus_slot* slot = &engine->buses[index];
        if (slot->used)
            continue;
        ma_result result = ma_sound_group_init(&engine->engine, MA_SOUND_FLAG_NO_SPATIALIZATION | MA_SOUND_FLAG_NO_PITCH, NULL, &slot->group);
        if (result != MA_SUCCESS)
            return -result;
        slot->used = MA_TRUE;
        return index;
    }
    return -MA_TOO_BIG;
}

TA_API void ta_bus_set_volume(ta_engine* engine, ma_int32 bus, float volume)
{
    if (bus >= 0 && bus < TA_MAX_BUSES && engine->buses[bus].used)
        ma_sound_group_set_volume(&engine->buses[bus].group, volume);
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
            return -MA_OUT_OF_MEMORY;
        ma_atomic_float_set(&echo->delaySeconds, 0.3f);
        ma_atomic_float_set(&echo->feedback, 0.4f);
        ma_atomic_float_set(&echo->wetMix, 0.0f);

        ma_result result = ta_stereo_node_init(engine, &g_echoVtable, &echo->base);
        if (result != MA_SUCCESS)
        {
            free(echo->delayLine);
            return -result;
        }
        ma_node_attach_output_bus(&echo->base, 0, ma_engine_get_endpoint(&engine->engine), 0);
        slot->used = MA_TRUE;
        return index;
    }
    return -MA_TOO_BIG;
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
            return -result;
        ma_node_attach_output_bus(&slot->node.base, 0, ma_engine_get_endpoint(&engine->engine), 0);
        slot->used = MA_TRUE;
        return index;
    }
    return -MA_TOO_BIG;
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

static ma_node* ta_node(ta_engine* engine, ma_int32 kind, ma_int32 index)
{
    switch (kind)
    {
        case TA_NODE_ENDPOINT:
            return ma_engine_get_endpoint(&engine->engine);
        case TA_NODE_SOUND:
            return index >= 0 && index < TA_MAX_SOUNDS && engine->sounds[index].used ? (ma_node*)&engine->sounds[index].sound : NULL;
        case TA_NODE_BUS:
            return index >= 0 && index < TA_MAX_BUSES && engine->buses[index].used ? (ma_node*)&engine->buses[index].group : NULL;
        case TA_NODE_ECHO:
            return index >= 0 && index < TA_MAX_ECHOES && engine->echoes[index].used ? (ma_node*)&engine->echoes[index].node.base : NULL;
        case TA_NODE_TAP:
            return index > 0 && index < TA_MAX_TAPS && engine->taps[index].used ? (ma_node*)&engine->taps[index].node.base : NULL;
        default:
            return NULL;
    }
}

/**
 * Routes a node's output into another node, replacing its previous target. With a running device the change
 * is queued and applied by the audio thread before its next block.
 */
TA_API ma_int32 ta_connect(ta_engine* engine, ma_int32 sourceKind, ma_int32 sourceIndex, ma_int32 targetKind, ma_int32 targetIndex)
{
    ma_node* source = ta_node(engine, sourceKind, sourceIndex);
    ma_node* target = ta_node(engine, targetKind, targetIndex);
    if (source == NULL || target == NULL || sourceKind == TA_NODE_ENDPOINT)
        return MA_INVALID_ARGS;

    if (!engine->hasDevice || ma_atomic_int32_get(&engine->applyRoutesImmediately))
        return ma_node_attach_output_bus(source, 0, target, 0);

    ma_uint32 written = ma_atomic_uint32_get(&engine->routeQueueWritten);
    if (written - ma_atomic_uint32_get(&engine->routeQueueApplied) >= TA_ROUTE_QUEUE_CAPACITY)
        return MA_BUSY;
    engine->routeQueue[written & (TA_ROUTE_QUEUE_CAPACITY - 1)] = (ta_route_command){ source, target };
    ma_atomic_uint32_set(&engine->routeQueueWritten, written + 1);
    return MA_SUCCESS;
}

/** For comparison only: rewire from the calling thread while the audio thread may be mid-block. */
TA_API void ta_set_apply_routes_immediately(ta_engine* engine, ma_int32 immediately)
{
    ma_atomic_int32_set(&engine->applyRoutesImmediately, immediately);
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
    ma_uint32 sampleCount = frameCount * device->capture.channels;
    float peak = ma_atomic_float_get(&engine->capturePeak);
    (void)output;

    for (ma_uint32 index = 0; index < sampleCount; index++)
    {
        float magnitude = samples[index] < 0 ? -samples[index] : samples[index];
        if (magnitude > peak)
            peak = magnitude;
    }
    ma_atomic_float_set(&engine->capturePeak, peak);
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

    ma_result result = ma_device_init(&engine->context, &config, &engine->captureDevice);
    if (result != MA_SUCCESS)
        return result;

    result = ma_device_start(&engine->captureDevice);
    if (result != MA_SUCCESS)
    {
        ma_device_uninit(&engine->captureDevice);
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
    engine->captureRunning = MA_FALSE;
}

/** Returns the highest absolute sample since the last call and resets it. */
TA_API float ta_capture_take_peak(ta_engine* engine)
{
    return ma_atomic_exchange_f32(&engine->capturePeak.value, 0.0f);
}
