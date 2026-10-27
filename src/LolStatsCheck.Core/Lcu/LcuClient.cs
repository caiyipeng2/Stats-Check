using System.Net.Http.Headers;
using System.Text.Json;

namespace LolStatsCheck.Core.Lcu;

public sealed class LcuClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private bool _disposed;

    public LcuClient(LcuCredentials credentials)
    {
        Credentials = credentials;

        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is { Host: "127.0.0.1" }
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(credentials.BaseUrl),
            Timeout = TimeSpan.FromSeconds(8)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials.BasicAuth);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public LcuCredentials Credentials { get; }

    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> TryGetStringAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (LcuHttpException ex) when (ex.StatusCode is 403 or 404)
        {
            return null;
        }
    }

    public async Task<byte[]?> GetBytesAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        var json = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public async Task<T?> TryGetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetJsonAsync<T>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (LcuHttpException ex) when (ex.StatusCode is 403 or 404)
        {
            return default;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new LcuHttpException(status, path);
            }

            return response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LcuTimeoutException(path, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LcuConnectionException($"无法连接本机客户端 ({path})", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
