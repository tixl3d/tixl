namespace T3.Core.Audio
{
    /// <summary>
    /// Interface for audio sources that can provide audio data for export/rendering.
    /// </summary>
    internal interface IAudioExportSource
    {
        /// <summary>
        /// Fill the provided buffer with audio data for the given time range (in seconds).
        /// </summary>
        /// <param name="startTime">Start time in seconds (localFxTime).</param>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="buffer">Buffer to fill (interleaved float samples, stereo or mono as appropriate).</param>
        /// <returns>Number of samples written (per channel).</returns>
        int RenderAudio(double startTime, double duration, float[] buffer);
    }
}
