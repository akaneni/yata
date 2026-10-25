using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Yata.Services;

public sealed record MediaState(
    string Title,
    string Artist,
    string SourceApp,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    double PlaybackRate,
    BitmapImage? Thumbnail)
{
    /// <summary>Position extrapolated from the last timeline update while playing.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var position = Position;
            if (IsPlaying)
                position += TimeSpan.FromTicks((long)((DateTimeOffset.Now - PositionUpdatedAt).Ticks * PlaybackRate));

            if (position < TimeSpan.Zero)
                return TimeSpan.Zero;
            return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
        }
    }
}

/// <summary>
/// Follows the current Windows media session (SMTC), which covers Spotify, browsers and
/// most players. All events and state changes are delivered on the UI dispatcher.
/// </summary>
public sealed class MediaService
{
    private readonly Dispatcher _dispatcher;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onMediaProperties;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlaybackInfo;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _propertiesVersion;

    private string _title = "";
    private string _artist = "";
    private BitmapImage? _thumbnail;
    private bool _isPlaying;
    private double _playbackRate = 1;
    private TimeSpan _position;
    private TimeSpan _duration;
    private DateTimeOffset _positionUpdatedAt = DateTimeOffset.Now;

    public MediaState? Current { get; private set; }

    public event Action? Changed;

    public MediaService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _onMediaProperties = (_, _) => _dispatcher.InvokeAsync(() => _ = ReadMediaPropertiesAsync());
        _onPlaybackInfo = (_, _) => _dispatcher.InvokeAsync(() => { ReadPlaybackInfo(); Publish(); });
        _onTimeline = (_, _) => _dispatcher.InvokeAsync(() => { ReadTimeline(); Publish(); });
    }

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(AttachCurrentSession);
        _manager.SessionsChanged += (_, _) => _dispatcher.InvokeAsync(AttachCurrentSession);
        AttachCurrentSession();
    }

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => Run(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync());

    public Task SeekAsync(TimeSpan position)
    {
        // Optimistic update so the progress bar doesn't jump back while the player catches up.
        _position = position;
        _positionUpdatedAt = DateTimeOffset.Now;
        Publish();
        return Run(s => s.TryChangePlaybackPositionAsync(position.Ticks));
    }

    private async Task Run(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
    {
        var session = _session;
        if (session == null)
            return;
        try
        {
            await action(session);
        }
        catch (Exception)
        {
            // The player may have closed in the meantime; nothing useful to report.
        }
    }

    private void AttachCurrentSession()
    {
        var next = _manager?.GetCurrentSession();
        if (_session != null && next != null && _session.SourceAppUserModelId == next.SourceAppUserModelId)
            return;

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= _onMediaProperties;
            _session.PlaybackInfoChanged -= _onPlaybackInfo;
            _session.TimelinePropertiesChanged -= _onTimeline;
        }

        _session = next;
        _propertiesVersion++;
        _title = _artist = "";
        _thumbnail = null;

        if (_session == null)
        {
            Publish();
            return;
        }

        _session.MediaPropertiesChanged += _onMediaProperties;
        _session.PlaybackInfoChanged += _onPlaybackInfo;
        _session.TimelinePropertiesChanged += _onTimeline;

        ReadPlaybackInfo();
        ReadTimeline();
        _ = ReadMediaPropertiesAsync();
    }

    private async Task ReadMediaPropertiesAsync()
    {
        var session = _session;
        if (session == null)
            return;

        int version = ++_propertiesVersion;
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (version != _propertiesVersion || properties == null)
                return;

            var thumbnail = properties.Thumbnail != null ? await LoadThumbnailAsync(properties.Thumbnail) : null;
            if (version != _propertiesVersion)
                return;

            _title = properties.Title ?? "";
            _artist = properties.Artist ?? "";
            _thumbnail = thumbnail;
            Publish();
        }
        catch (Exception)
        {
            // Sessions can disappear between the event and the read.
        }
    }

    private void ReadPlaybackInfo()
    {
        if (_session == null)
            return;

        var info = _session.GetPlaybackInfo();
        bool isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        // Freeze the extrapolated position on pause; many players don't send a timeline update.
        if (_isPlaying && !isPlaying)
        {
            _position = Snapshot().CurrentPosition;
            _positionUpdatedAt = DateTimeOffset.Now;
        }

        _isPlaying = isPlaying;
        _playbackRate = info.PlaybackRate ?? 1.0;
    }

    private void ReadTimeline()
    {
        if (_session == null)
            return;

        var timeline = _session.GetTimelineProperties();
        _position = timeline.Position - timeline.StartTime;
        _duration = timeline.EndTime - timeline.StartTime;
        _positionUpdatedAt = timeline.LastUpdatedTime;
    }

    private MediaState Snapshot() => new(
        _title, _artist, _session?.SourceAppUserModelId ?? "", _isPlaying,
        _position, _duration, _positionUpdatedAt, _playbackRate, _thumbnail);

    private void Publish()
    {
        Current = _session == null || string.IsNullOrWhiteSpace(_title) ? null : Snapshot();
        Changed?.Invoke();
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 240;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
