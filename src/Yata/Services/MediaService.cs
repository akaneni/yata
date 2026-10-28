using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Yata.Helpers;

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

/// <summary>One media session as seen by <see cref="MediaService.Pick"/>.</summary>
internal readonly record struct SessionCandidate(bool IsPlaying, bool IsShown, bool IsSystemCurrent);

/// <summary>
/// Follows a Windows media session (SMTC), which covers Spotify, browsers and most players:
/// the one actually playing, rather than whatever Windows last called "current".
/// All events and state changes are delivered on the UI dispatcher.
/// </summary>
public sealed class MediaService
{
    private readonly Dispatcher _dispatcher;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onMediaProperties;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlaybackInfo;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;
    // Watches every session's play/pause, so a player that starts in the background can take over.
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onAnyPlayback;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private IReadOnlyList<GlobalSystemMediaTransportControlsSession> _watched = Array.Empty<GlobalSystemMediaTransportControlsSession>();
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
        _onMediaProperties = (_, _) => _dispatcher.Post("media", () => _ = ReadMediaPropertiesAsync());
        _onPlaybackInfo = (_, _) => _dispatcher.Post("media", () => { ReadPlaybackInfo(); Publish(); });
        _onTimeline = (_, _) => _dispatcher.Post("media", () => { ReadTimeline(); Publish(); });
        _onAnyPlayback = (_, _) => _dispatcher.Post("media", AttachBestSession);
    }

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.Post("media", AttachBestSession);
        _manager.SessionsChanged += (_, _) => _dispatcher.Post("media", () => { WatchSessions(); AttachBestSession(); });
        WatchSessions();
        AttachBestSession();
    }

    /// <summary>
    /// Which session to show: keep the shown one while it plays; otherwise prefer Windows'
    /// current session if it plays, then any session that plays; with nothing playing, stay
    /// on the paused one already shown, then fall back to Windows' current one. -1 for none.
    /// </summary>
    internal static int Pick(IReadOnlyList<SessionCandidate> sessions)
    {
        int Find(Func<SessionCandidate, bool> match)
        {
            for (int i = 0; i < sessions.Count; i++)
                if (match(sessions[i]))
                    return i;
            return -1;
        }

        foreach (var rule in new Func<SessionCandidate, bool>[]
                 {
                     s => s.IsShown && s.IsPlaying,
                     s => s.IsSystemCurrent && s.IsPlaying,
                     s => s.IsPlaying,
                     s => s.IsShown,
                     s => s.IsSystemCurrent,
                 })
        {
            int index = Find(rule);
            if (index >= 0)
                return index;
        }
        return sessions.Count > 0 ? 0 : -1;
    }

    private void WatchSessions()
    {
        foreach (var session in _watched)
            session.PlaybackInfoChanged -= _onAnyPlayback;
        try
        {
            _watched = _manager?.GetSessions().ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();
        }
        catch (Exception ex)
        {
            Log.Warn("media", "Falha ao listar as sessões de mídia", ex);
            _watched = Array.Empty<GlobalSystemMediaTransportControlsSession>();
        }
        foreach (var session in _watched)
            session.PlaybackInfoChanged += _onAnyPlayback;
    }

    private void AttachBestSession()
    {
        var current = _manager?.GetCurrentSession();
        var candidates = _watched.Select(s => new SessionCandidate(
            IsPlaying(s),
            _session != null && SameSession(s, _session),
            current != null && SameSession(s, current))).ToList();
        int index = Pick(candidates);
        Attach(index >= 0 ? _watched[index] : current);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false; // Closed between the listing and the read.
        }
    }

    // WinRT may hand out a new wrapper for the same session; the app id is what identifies it.
    private static bool SameSession(GlobalSystemMediaTransportControlsSession a, GlobalSystemMediaTransportControlsSession b)
    {
        try
        {
            return ReferenceEquals(a, b) || a.SourceAppUserModelId == b.SourceAppUserModelId;
        }
        catch (Exception)
        {
            return false;
        }
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

    private void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (_session != null && next != null && SameSession(_session, next))
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

        GlobalSystemMediaTransportControlsSessionPlaybackInfo info;
        try
        {
            info = _session.GetPlaybackInfo();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return; // The player closed this session meanwhile; SessionsChanged brings the next one.
        }
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

        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline;
        try
        {
            timeline = _session.GetTimelineProperties();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return; // Same: a session that just closed.
        }
        _position = timeline.Position - timeline.StartTime;
        _duration = timeline.EndTime - timeline.StartTime;
        _positionUpdatedAt = timeline.LastUpdatedTime;
    }

    /// <summary>
    /// Shows a made-up track, playing from the start, instead of following Windows' sessions.
    /// Only for measurements (--bench): the controls do nothing without a session.
    /// </summary>
    internal void Simulate(string title, string artist, TimeSpan duration)
    {
        _title = title;
        _artist = artist;
        _isPlaying = true;
        _playbackRate = 1;
        _position = TimeSpan.Zero;
        _duration = duration;
        _positionUpdatedAt = DateTimeOffset.Now;
        Current = Snapshot();
        Changed?.Invoke();
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
