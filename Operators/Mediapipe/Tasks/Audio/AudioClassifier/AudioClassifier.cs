using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Tasks.Audio.Core;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using AudioClassifierResult = Mediapipe.Tasks.Components.Containers.ClassificationResult;

namespace Mediapipe.Tasks.Audio.AudioClassifier;

public sealed class AudioClassifier : BaseAudioTaskApi
{
    private const string _AUDIO_IN_STREAM_NAME = "audio_in";
    private const string _AUDIO_TAG = "AUDIO";
    private const string _CLASSIFICATIONS_STREAM_NAME = "classifications_out";
    private const string _CLASSIFICATIONS_TAG = "CLASSIFICATIONS";
    private const string _SAMPLE_RATE_IN_STREAM_NAME = "sample_rate_in";
    private const string _SAMPLE_RATE_TAG = "SAMPLE_RATE";
    private const string _TASK_GRAPH_NAME = "mediapipe.tasks.audio.audio_classifier.AudioClassifierGraph";
    private const string _TIMESTAMPED_CLASSIFICATIONS_STREAM_NAME = "timestamped_classifications_out";
    private const string _TIMESTAMPED_CLASSIFICATIONS_TAG = "TIMESTAMPED_CLASSIFICATIONS";

    private const int _MICRO_SECONDS_PER_MILLISECOND = 1000;

    private double? _defaultSampleRate;

    private AudioClassifier(
        CalculatorGraphConfig graphConfig,
        RunningMode runningMode,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, packetCallback)
    {
    }

    /// <summary>
    ///     Creates an <see cref="AudioClassifier" /> object from a TensorFlow Lite model and the default
    ///     <see cref="AudioClassifierOptions" />.
    ///     Note that the created <see cref="AudioClassifier" /> instance is in audio clips mode, for classifying on
    ///     independent audio clips.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <returns>
    ///     <see cref="AudioClassifier" /> object that's created from the model and the default
    ///     <see cref="AudioClassifierOptions" />.
    /// </returns>
    public static AudioClassifier CreateFromModelPath(string modelPath)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        AudioClassifierOptions options = new(baseOptions);
        return CreateFromOptions(options);
    }

    /// <summary>
    ///     Creates the <see cref="AudioClassifier" /> object from <paramref name="options" />.
    /// </summary>
    /// <param name="options">Options for the audio classifier task.</param>
    /// <returns>
    ///     <see cref="AudioClassifier" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static AudioClassifier CreateFromOptions(AudioClassifierOptions options)
    {
        TaskInfo<AudioClassifierOptions> taskInfo = new(
            _TASK_GRAPH_NAME,
            [
                string.Join(":", _AUDIO_TAG, _AUDIO_IN_STREAM_NAME),
                string.Join(":", _SAMPLE_RATE_TAG, _SAMPLE_RATE_IN_STREAM_NAME)
            ],
            [
                string.Join(":", _CLASSIFICATIONS_TAG, _CLASSIFICATIONS_STREAM_NAME),
                string.Join(":", _TIMESTAMPED_CLASSIFICATIONS_TAG, _TIMESTAMPED_CLASSIFICATIONS_STREAM_NAME)
            ],
            options);

        return new AudioClassifier(
            taskInfo.GenerateGraphConfig(options.RunningMode == RunningMode.AUDIO_STREAM),
            options.RunningMode,
            BuildPacketsCallback(options));
    }

    /// <summary>
    ///     Performs audio classification on the provided audio clip.
    ///     Only use this method when the AudioClassifier is created with the audio clips running mode.
    ///     The input audio clip may be longer than what the model is able to process in a single inference.
    ///     When this occurs, the input audio clip is split into multiple chunks starting at different timestamps.
    ///     For this reason, this function returns a list of <see cref="AudioClassifierResult" /> objects, each associated
    ///     with a timestamp corresponding to the start (in milliseconds) of the chunk data that was classified.
    /// </summary>
    /// <returns>
    ///     A list of <see cref="AudioClassifierResult" /> that contains a list of classification result objects,
    ///     each associated with a timestamp corresponding to the start (in milliseconds) of the chunk data that was
    ///     classified.
    /// </returns>
    public List<AudioClassifierResult> Classify(Matrix audioClip, double audioSampleRate)
    {
        using PacketMap outputPackets = ClassifyInternal(audioClip, audioSampleRate);

        List<AudioClassifierResult> result = new();
        _ = TryBuildAudioClassifierResultList(outputPackets, result);
        return result;
    }

    /// <summary>
    ///     Performs audio classification on the provided audio clip.
    ///     Only use this method when the AudioClassifier is created with the audio clips running mode.
    ///     The input audio clip may be longer than what the model is able to process in a single inference.
    ///     When this occurs, the input audio clip is split into multiple chunks starting at different timestamps.
    ///     For this reason, this function returns a list of <see cref="AudioClassifierResult" /> objects, each associated
    ///     with a timestamp corresponding to the start (in milliseconds) of the chunk data that was classified.
    /// </summary>
    /// <param name="result">
    ///     <see cref="List{AudioClassifierResult}" /> to which the result will be written.
    ///     It contains a list of classification result objects,
    ///     each associated with a timestamp corresponding to the start (in milliseconds) of the chunk data that was
    ///     classified.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the <see cref="result" /> exists, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryClassify(Matrix audioClip, double audioSampleRate, List<AudioClassifierResult> result)
    {
        using PacketMap outputPackets = ClassifyInternal(audioClip, audioSampleRate);
        return TryBuildAudioClassifierResultList(outputPackets, result);
    }

    private PacketMap ClassifyInternal(Matrix audioClip, double audioSampleRate)
    {
        if (audioClip.IsRowMajor) throw new ArgumentException("Input audio clip must be a column-major matrix.");
        PacketMap packetMap = new();
        packetMap.Emplace(_AUDIO_IN_STREAM_NAME, PacketHelper.CreateColMajorMatrix(audioClip));
        packetMap.Emplace(_SAMPLE_RATE_IN_STREAM_NAME, PacketHelper.CreateDouble(audioSampleRate));

        return ProcessAudioClip(packetMap);
    }

    /// <summary>
    ///     Sends audio data (a block in a continuous audio stream) to perform audio classification.
    ///     Only use this method when the <see cref="AudioClassifier" /> is created with the audio stream running mode.
    ///     The input timestamps should be monotonically increasing for adjacent calls of this method.
    ///     This method will return immediately after the input audio data is accepted. The results will be available via the
    ///     <see cref="AudioClassifierOptions.ResultCallbackFunc" /> provided in the <see cref="AudioClassifierOptions" />.
    ///     The <see cref="ClassifyAsync" /> method is designed to process auido stream data such as microphone input.
    ///     The input audio data may be longer than what the model is able to process in a single inference.
    ///     When this occurs, the input audio block is split into multiple chunks.
    ///     For this reason, the callback may be called multiple times (once per chunk) for each call to this function.
    /// </summary>
    public void ClassifyAsync(Matrix audioClip, double audioSampleRate, long timestampMillisec)
    {
        if (audioClip.IsRowMajor) throw new ArgumentException("Input audio clip must be a column-major matrix.");
        long timestampMicrosec = timestampMillisec * _MICRO_SECONDS_PER_MILLISECOND;

        if (_defaultSampleRate is null)
        {
            SetSampleRate(_SAMPLE_RATE_IN_STREAM_NAME, audioSampleRate);
            _defaultSampleRate = audioSampleRate;
        }
        else if (audioSampleRate != _defaultSampleRate)
        {
            throw new ArgumentException(
                $"The audio sample rate provided({audioSampleRate}) is inconsistent with the previous received({_defaultSampleRate}).");
        }

        PacketMap packetMap = new();
        packetMap.Emplace(_AUDIO_IN_STREAM_NAME, PacketHelper.CreateColMajorMatrixAt(audioClip, timestampMicrosec));

        SendAudioStreamData(packetMap);
    }

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(AudioClassifierOptions options)
    {
        AudioClassifierOptions.ResultCallbackFunc? resultCallback = options.ResultCallback;
        if (resultCallback == null) return null;
        ClassificationResult result = AudioClassifierResult.Alloc(options.MaxResults ?? 0);

        return outputPackets =>
        {
            using Packet<AudioClassifierResult>? outPacket =
                outputPackets.At<AudioClassifierResult>(_CLASSIFICATIONS_STREAM_NAME);
            if (outPacket == null || outPacket.IsEmpty()) return;

            outPacket.Get(ref result);
            long timestamp = outPacket.TimestampMicroseconds() / _MICRO_SECONDS_PER_MILLISECOND;

            resultCallback(result, timestamp);
        };
    }

    private static bool TryBuildAudioClassifierResultList(PacketMap outputPackets, List<AudioClassifierResult> result)
    {
        using Packet<List<AudioClassifierResult>> outPacket =
            outputPackets.At<List<AudioClassifierResult>>(_TIMESTAMPED_CLASSIFICATIONS_STREAM_NAME);
        if (outPacket.IsEmpty()) return false;
        outPacket.Get(result);
        return true;
    }
}