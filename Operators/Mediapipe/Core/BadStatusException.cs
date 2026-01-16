using Mediapipe.Framework.Port;

namespace Mediapipe.Core;

public class BadStatusException : Exception
{
    public BadStatusException(string message) : base(message)
    {
    }

    public BadStatusException(StatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public StatusCode StatusCode { get; private set; }
}