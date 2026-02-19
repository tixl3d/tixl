namespace Lib.io.audio
{
    /// <summary>
    /// Shared utility methods for audio player operators.
    /// </summary>
    internal static class AudioPlayerUtils
    {
        /// <summary>
        /// Computes a stable GUID from an instance path for operator identification.
        /// </summary>
        public static Guid ComputeInstanceGuid(IEnumerable<Guid> instancePath)
        {
            unchecked
            {
                ulong hash = 0xCBF29CE484222325;
                const ulong prime = 0x100000001B3;

                foreach (var id in instancePath)
                {
                    var bytes = id.ToByteArray();
                    foreach (var b in bytes)
                    {
                        hash ^= b;
                        hash *= prime;
                    }
                }

                var guidBytes = new byte[16];
                var hashBytes = BitConverter.GetBytes(hash);
                Array.Copy(hashBytes, 0, guidBytes, 0, 8);
                Array.Copy(hashBytes, 0, guidBytes, 8, 8);
                return new Guid(guidBytes);
            }
        }
    }
}
