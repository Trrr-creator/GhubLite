namespace GhubLite.Core.Hidpp;

public sealed class HidppException : Exception
{
    public byte? ErrorCode { get; }

    public HidppException(string message, byte? errorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}

public sealed class HidppTimeoutException : Exception
{
    public HidppTimeoutException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
