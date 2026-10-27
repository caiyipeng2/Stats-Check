using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.App.Services;

public enum IconKind
{
    Champion,
    Profile
}

public sealed class IconLoadedEventArgs : EventArgs
{
    public IconLoadedEventArgs(IconKind kind, int id, ImageSource image)
    {
        Kind = kind;
        Id = id;
        Image = image;
    }

    public IconKind Kind { get; }
    public int Id { get; }
    public ImageSource Image { get; }
}

public sealed class ImageCache : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
    private readonly ChampionCatalog _catalog;
    private readonly bool _mock;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(4, 4);
    private readonly ConcurrentDictionary<int, ImageSource> _champions = new();
    private readonly ConcurrentDictionary<int, ImageSource> _profiles = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _championRetry = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _profileRetry = new();
    private readonly HashSet<int> _championLoading = new();
    private readonly HashSet<int> _profileLoading = new();
    private readonly object _loadingLock = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public ImageCache(ChampionCatalog catalog, bool mock)
    {
        _catalog = catalog;
        _mock = mock;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public event EventHandler<IconLoadedEventArgs>? IconLoaded;

    public ImageSource? GetChampion(int championId)
    {
        if (_disposed || _mock || championId <= 0)
        {
            return null;
        }

        if (_champions.TryGetValue(championId, out var image))
        {
            return image;
        }

        if (_championRetry.TryGetValue(championId, out var retryAt) && retryAt > DateTimeOffset.Now)
        {
            return null;
        }

        StartChampion(championId);
        return null;
    }

    public ImageSource? GetProfile(int iconId)
    {
        if (_disposed || _mock || iconId <= 0)
        {
            return null;
        }

        if (_profiles.TryGetValue(iconId, out var image))
        {
            return image;
        }

        if (_profileRetry.TryGetValue(iconId, out var retryAt) && retryAt > DateTimeOffset.Now)
        {
            return null;
        }

        StartProfile(iconId);
        return null;
    }

    public void PreloadChampion(int championId) => _ = GetChampion(championId);

    public void PreloadProfile(int iconId) => _ = GetProfile(iconId);

    private void StartChampion(int championId)
    {
        lock (_loadingLock)
        {
            if (!_championLoading.Add(championId))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            var acquired = false;
            try
            {
                await _gate.WaitAsync(_cts.Token).ConfigureAwait(false);
                acquired = true;
                var bytes = await _catalog.GetChampionIconAsync(championId, _cts.Token).ConfigureAwait(false);
                var image = Decode(bytes);
                if (image is null)
                {
                    _championRetry[championId] = DateTimeOffset.Now.Add(RetryDelay);
                    return;
                }

                _champions[championId] = image;
                _championRetry.TryRemove(championId, out _);
                RaiseLoaded(IconKind.Champion, championId, image);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                _championRetry[championId] = DateTimeOffset.Now.Add(RetryDelay);
            }
            finally
            {
                if (acquired)
                {
                    _gate.Release();
                }

                lock (_loadingLock)
                {
                    _championLoading.Remove(championId);
                }
            }
        });
    }

    private void StartProfile(int iconId)
    {
        lock (_loadingLock)
        {
            if (!_profileLoading.Add(iconId))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            var acquired = false;
            try
            {
                await _gate.WaitAsync(_cts.Token).ConfigureAwait(false);
                acquired = true;
                var bytes = await _catalog.GetProfileIconAsync(iconId, _cts.Token).ConfigureAwait(false);
                var image = Decode(bytes);
                if (image is null)
                {
                    _profileRetry[iconId] = DateTimeOffset.Now.Add(RetryDelay);
                    return;
                }

                _profiles[iconId] = image;
                _profileRetry.TryRemove(iconId, out _);
                RaiseLoaded(IconKind.Profile, iconId, image);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                _profileRetry[iconId] = DateTimeOffset.Now.Add(RetryDelay);
            }
            finally
            {
                if (acquired)
                {
                    _gate.Release();
                }

                lock (_loadingLock)
                {
                    _profileLoading.Remove(iconId);
                }
            }
        });
    }

    private void RaiseLoaded(IconKind kind, int id, ImageSource image)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                IconLoaded?.Invoke(this, new IconLoadedEventArgs(kind, id, image));
            }
        });
    }

    private static ImageSource? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        IconLoaded = null;
    }
}
