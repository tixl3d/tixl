// Compile-time stand-in for the editor's logger: the included network-layer sources call
// T3.Core.Logging.Log, which lives in the (windows-targeted) Core assembly. Headless runs
// just mirror it to stdout.
namespace T3.Core.Logging
{
    public static class Log
    {
        public static void Debug(string message) => Console.WriteLine($"[D] {message}");
        public static void Info(string message) => Console.WriteLine($"[I] {message}");
        public static void Warning(string message) => Console.WriteLine($"[W] {message}");
        public static void Error(string message) => Console.WriteLine($"[E] {message}");
    }
}

// CollabTransport.cs imports this editor namespace; an empty anchor keeps it resolvable here.
namespace T3.Editor.Gui.Interaction
{
    internal static class HeadlessNamespaceAnchor
    {
    }
}
