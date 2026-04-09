using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore.Pipes;
using T3.Core.Utils;

namespace T3.Editor.Gui.Windows.RenderExport.FFMpeg
{
    internal class FFMpegAudioPipeSource : IPipeSource
    {
        private readonly BlockingCollection<byte[]> _buffer;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly Action _onPipeBroken;

        public FFMpegAudioPipeSource(BlockingCollection<byte[]> buffer, int sampleRate, int channels, Action onPipeBroken = null)
        {
            _buffer = buffer;
            _sampleRate = sampleRate;
            _channels = channels;
            _onPipeBroken = onPipeBroken;
        }

        public string GetStreamArguments()
        {
            // f32le = PCM 32-bit floating-point little-endian
            return $"-f f32le -ar {_sampleRate} -ac {_channels}";
        }

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var data in _buffer.GetConsumingEnumerable(cancellationToken))
                {
                    await outputStream.WriteAsync(data, 0, data.Length, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (ObjectDisposedException)
            {
                 // Buffer disposed
            }
            catch (IOException e)
            {
                Log.Debug($"FFMpeg Audio Pipe Source broken: {e.Message}");
                _onPipeBroken?.Invoke();
            }
            catch (Exception e)
            {
                Log.Debug($"FFMpeg Audio Pipe Source error: {e.Message}");
                _onPipeBroken?.Invoke();
            }
        }
    }
}
