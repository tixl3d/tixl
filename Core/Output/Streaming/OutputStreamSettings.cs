#nullable enable
namespace T3.Core.Output.Streaming;

/// <summary>Settings a stream can be given, where its transport has a notion of them (see
/// <see cref="IOutputStreamProvider.Supported"/>). Frame size is never among them: it follows the texture.</summary>
public readonly record struct OutputStreamSettings(int FrameRate, bool EnableAlpha);
