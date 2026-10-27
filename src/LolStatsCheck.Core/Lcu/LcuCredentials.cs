using System.Text;

namespace LolStatsCheck.Core.Lcu;

public sealed class LcuCredentials
{
    public LcuCredentials(int port, string password, string protocol = "https")
    {
        Port = port;
        Password = password;
        Protocol = string.IsNullOrWhiteSpace(protocol) ? "https" : protocol;
    }

    public int Port { get; }
    public string Password { get; }
    public string Protocol { get; }

    public string BaseUrl => $"{Protocol}://127.0.0.1:{Port}";

    public string BasicAuth =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + Password));

    public override string ToString() => $"{Protocol}://127.0.0.1:{Port}";
}

public sealed record LcuLocation(
    string ClientExePath,
    LcuCredentials Credentials,
    string Source);
