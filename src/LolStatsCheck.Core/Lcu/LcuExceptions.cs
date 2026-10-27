namespace LolStatsCheck.Core.Lcu;

public class LcuException : Exception
{
    public LcuException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class LcuHttpException : LcuException
{
    public LcuHttpException(int statusCode, string path)
        : base($"LCU 请求失败：HTTP {statusCode} ({path})")
    {
        StatusCode = statusCode;
        Path = path;
    }

    public int StatusCode { get; }
    public string Path { get; }
}

public sealed class LcuConnectionException : LcuException
{
    public LcuConnectionException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class LcuTimeoutException : LcuException
{
    public LcuTimeoutException(string path, Exception? inner = null)
        : base($"LCU 请求超时 ({path})", inner)
    {
        Path = path;
    }

    public string Path { get; }
}
