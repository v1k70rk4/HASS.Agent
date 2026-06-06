using System.Text.Json;
using HASS.Agent.Companion.Logging;
using Windows.Media.Control;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace HASS.Agent.Companion.Media;

internal sealed class MediaSessionService : IDisposable
{
    private readonly FileLog _log;
    private readonly AudioEndpointService _audioEndpoint;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private MediaPlayer? _localPlayer;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private Func<MediaStateMessage, Task>? _publishState;

    public MediaSessionService(FileLog log)
    {
        _log = log;
        _audioEndpoint = new AudioEndpointService(log);
    }

    public async Task StartAsync(Func<MediaStateMessage, Task> publishState, CancellationToken cancellationToken)
    {
        if (_worker is not null)
        {
            return;
        }

        _publishState = publishState;
        _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _localPlayer = new MediaPlayer
        {
            AutoPlay = false,
            IsLoopingEnabled = false
        };

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => MonitorAsync(_cts.Token), CancellationToken.None);
        _log.Info("Media session monitor started.");
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _localPlayer?.Dispose();
        _localPlayer = null;
        _sessionManager = null;
        _cts.Dispose();
        _cts = null;
        _worker = null;
        _log.Info("Media session monitor stopped.");
    }

    public async Task HandleCommandAsync(MediaCommand command)
    {
        var commandName = command.Command?.Trim().ToLowerInvariant();
        var session = GetCurrentSession();

        switch (commandName)
        {
            case "volumeup":
                MediaKeySender.VolumeUp();
                break;
            case "volumedown":
                MediaKeySender.VolumeDown();
                break;
            case "setvolume":
                if (TryGetInt(command.Data, out var volume))
                {
                    _audioEndpoint.SetVolume(volume);
                }
                break;
            case "mute":
                if (TryGetBool(command.Data, out var muted))
                {
                    _audioEndpoint.SetMuted(muted);
                }
                else
                {
                    MediaKeySender.MuteToggle();
                }
                break;
            case "play":
                if (session is not null)
                {
                    await session.TryPlayAsync();
                }
                else
                {
                    MediaKeySender.PlayPause();
                }
                break;
            case "pause":
                if (session is not null)
                {
                    await session.TryPauseAsync();
                }
                else
                {
                    MediaKeySender.PlayPause();
                }
                break;
            case "stop":
                if (session is not null)
                {
                    await session.TryStopAsync();
                }
                else
                {
                    MediaKeySender.Stop();
                }
                break;
            case "next":
                if (session is not null)
                {
                    await session.TrySkipNextAsync();
                }
                else
                {
                    MediaKeySender.Next();
                }
                break;
            case "previous":
                if (session is not null)
                {
                    await session.TrySkipPreviousAsync();
                }
                else
                {
                    MediaKeySender.Previous();
                }
                break;
            case "seek":
                if (session is not null && TryGetDouble(command.Data, out var seconds))
                {
                    await session.TryChangePlaybackPositionAsync((long)TimeSpan.FromSeconds(seconds).TotalMilliseconds * 10_000);
                }
                break;
            case "playmedia":
                if (command.Data is not null)
                {
                    PlayMedia(command.Data.ToString());
                }
                break;
            default:
                _log.Warning($"Unsupported media command received: {command.Command ?? "[null]"}");
                break;
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await BuildStateMessageAsync();
                if (_publishState is not null)
                {
                    await _publishState(message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning($"Unable to publish media state: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<MediaStateMessage> BuildStateMessageAsync()
    {
        var session = GetCurrentSession();
        if (session is null)
        {
            return new MediaStateMessage
            {
                State = "off",
                Volume = _audioEndpoint.GetVolume(),
                Muted = _audioEndpoint.GetMuted()
            };
        }

        var mediaProperties = await session.TryGetMediaPropertiesAsync();
        var playbackInfo = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        return new MediaStateMessage
        {
            State = ConvertState(playbackInfo?.PlaybackStatus),
            Title = mediaProperties?.Title,
            Artist = mediaProperties?.Artist,
            AlbumArtist = mediaProperties?.AlbumArtist,
            AlbumTitle = mediaProperties?.AlbumTitle,
            Duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds),
            CurrentPosition = Math.Max(0, timeline.Position.TotalSeconds),
            Volume = _audioEndpoint.GetVolume(),
            Muted = _audioEndpoint.GetMuted()
        };
    }

    private GlobalSystemMediaTransportControlsSession? GetCurrentSession()
    {
        var sessions = _sessionManager?.GetSessions();
        if (sessions is null || sessions.Count == 0)
        {
            return null;
        }

        return sessions.FirstOrDefault(session =>
            session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) ?? sessions[0];
    }

    private void PlayMedia(string? mediaUri)
    {
        if (string.IsNullOrWhiteSpace(mediaUri))
        {
            return;
        }

        if (!Uri.TryCreate(mediaUri, UriKind.Absolute, out var uri))
        {
            _log.Warning($"Rejected invalid playmedia URI: {mediaUri}");
            return;
        }

        _localPlayer ??= new MediaPlayer();
        _localPlayer.Source = MediaSource.CreateFromUri(uri);
        _localPlayer.Play();
    }

    private static string ConvertState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "off",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => "off",
            _ => "idle"
        };
    }

    private static bool TryGetInt(object? value, out int result)
    {
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Number)
        {
            return json.TryGetInt32(out result);
        }

        return int.TryParse(value?.ToString(), out result);
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Number)
        {
            return json.TryGetDouble(out result);
        }

        return double.TryParse(value?.ToString(), out result);
    }

    private static bool TryGetBool(object? value, out bool result)
    {
        if (value is JsonElement json)
        {
            if (json.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result = json.GetBoolean();
                return true;
            }
        }

        return bool.TryParse(value?.ToString(), out result);
    }
}
