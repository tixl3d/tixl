using Mediapipe.PInvoke;

namespace Mediapipe.External;

public static class Protobuf
{
    public delegate void LogHandler(int level, string filename, int line, string message);

    public static readonly LogHandler DefaultLogHandler = LogProtobufMessage;

    public static void SetLogHandler(LogHandler logHandler)
    {
        UnsafeNativeMethods.google_protobuf__SetLogHandler__PF(logHandler).Assert();
    }

    /// <summary>
    ///     Reset the <see cref="LogHandler" />.
    ///     If <see cref="SetLogHandler" /> is called, this method should be called before the program exits.
    /// </summary>
    public static void ResetLogHandler()
    {
        UnsafeNativeMethods.google_protobuf__ResetLogHandler().Assert();
    }

    private static void LogProtobufMessage(int level, string filename, int line, string message)
    {
        switch (level)
        {
            case 1:
            {
                Console.WriteLine($"[libprotobuf WARNING {filename}:{line}] {message}");
                return;
            }
            case 2:
            {
                Console.WriteLine($"[libprotobuf ERROR {filename}:{line}] {message}");
                return;
            }
            case 3:
            {
                Console.WriteLine($"[libprotobuf FATAL {filename}:{line}] {message}");
                return;
            }
            default:
            {
                Console.WriteLine($"[libprotobuf INFO {filename}:{line}] {message}");
                return;
            }
        }
    }
}