using System.Collections.Generic;
using System.Threading;

namespace T3.Core.Audio
{
    /// <summary>
    /// Registry for audio export sources (e.g., operator instances that provide audio for rendering).
    /// Thread-safe to allow registration/unregistration during enumeration.
    /// </summary>
    internal static class AudioExportSourceRegistry
    {
        private static readonly List<IAudioExportSource> _sources = new();
        private static readonly Lock _lock = new();

        /// <summary>
        /// Returns a snapshot of the current sources to allow safe enumeration
        /// while other threads may modify the collection.
        /// </summary>
        internal static IReadOnlyList<IAudioExportSource> Sources
        {
            get
            {
                lock (_lock)
                {
                    return _sources.ToArray();
                }
            }
        }

        public static void Register(IAudioExportSource source)
        {
            lock (_lock)
            {
                if (!_sources.Contains(source))
                    _sources.Add(source);
            }
        }

        public static void Unregister(IAudioExportSource source)
        {
            lock (_lock)
            {
                _sources.Remove(source);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _sources.Clear();
            }
        }
    }
}
