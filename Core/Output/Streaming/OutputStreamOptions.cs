#nullable enable
using System;

namespace T3.Core.Output.Streaming;

/// <summary>Which of <see cref="OutputStreamSettings"/> a stream kind actually honours, so a host can offer
/// exactly those and no dead controls.</summary>
[Flags]
public enum OutputStreamOptions
{
    None = 0,
    FrameRate = 1,
    Alpha = 2,
}
