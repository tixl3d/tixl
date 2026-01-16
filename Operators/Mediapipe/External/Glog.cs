using Mediapipe.PInvoke;

namespace Mediapipe.External;

public static class Glog
{
    public enum Severity
    {
        INFO = 0,
        WARNING = 1,
        ERROR = 2,
        FATAL = 3
    }

    private static bool _Logtostderr;

    private static int _Stderrthreshold = 2;

    private static int _Minloglevel;

    private static string? _LogDir;

    private static int _V;

    public static bool Logtostderr
    {
        get => _Logtostderr;
        set
        {
            UnsafeNativeMethods.glog_FLAGS_logtostderr(value);
            _Logtostderr = value;
        }
    }

    public static int Stderrthreshold
    {
        get => _Stderrthreshold;
        set
        {
            UnsafeNativeMethods.glog_FLAGS_stderrthreshold(value);
            _Stderrthreshold = value;
        }
    }

    public static int Minloglevel
    {
        get => _Minloglevel;
        set
        {
            UnsafeNativeMethods.glog_FLAGS_minloglevel(value);
            _Minloglevel = value;
        }
    }

    public static string? LogDir
    {
        get => _LogDir;
        set
        {
            UnsafeNativeMethods.glog_FLAGS_log_dir(value ?? "");
            _LogDir = value;
        }
    }

    public static int V
    {
        get => _V;
        set
        {
            UnsafeNativeMethods.glog_FLAGS_v(value);
            _V = value;
        }
    }

    public static void Initialize(string name)
    {
        UnsafeNativeMethods.google_InitGoogleLogging__PKc(name).Assert();
    }

    public static void Shutdown()
    {
        UnsafeNativeMethods.google_ShutdownGoogleLogging().Assert();
    }

    public static void Log(Severity severity, string str)
    {
        switch (severity)
        {
            case Severity.INFO:
            {
                UnsafeNativeMethods.glog_LOG_INFO__PKc(str);
                break;
            }
            case Severity.WARNING:
            {
                UnsafeNativeMethods.glog_LOG_WARNING__PKc(str);
                break;
            }
            case Severity.ERROR:
            {
                UnsafeNativeMethods.glog_LOG_ERROR__PKc(str);
                break;
            }
            case Severity.FATAL:
            {
                UnsafeNativeMethods.glog_LOG_FATAL__PKc(str);
                break;
            }
            default:
            {
                throw new ArgumentException($"Unknown Severity: {severity}");
            }
        }
    }

    public static void FlushLogFiles(Severity severity = Severity.INFO)
    {
        UnsafeNativeMethods.google_FlushLogFiles(severity);
    }
}