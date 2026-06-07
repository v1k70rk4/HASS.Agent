using System.Text.Json;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Media;
using HASS.Agent.Companion.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HASS.Agent.Companion.Http;

internal sealed class LocalApiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CompanionSettings _settings;
    private readonly INotificationSink _notificationSink;
    private readonly MediaSessionService _mediaService;
    private readonly FileLog _log;
    private WebApplication? _app;

    public LocalApiServer(CompanionSettings settings, INotificationSink notificationSink, MediaSessionService mediaService, FileLog log)
    {
        _settings = settings;
        _notificationSink = notificationSink;
        _mediaService = mediaService;
        _log = log;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = AppIdentity.ExecutableName
        });

        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        builder.WebHost.UseUrls(_settings.ListenUrl);

        var app = builder.Build();

        app.MapGet("/", () => Results.Redirect("/info"));

        app.MapGet("/info", () => Results.Json(new InfoResponse(
            _settings.SerialNumber,
            new DeviceInfoResponse(
                _settings.DeviceName,
                _settings.Manufacturer,
                _settings.Model,
                _settings.SoftwareVersion),
            new ApiCapabilitiesResponse(
                Notifications: true,
                MediaPlayer: true,
                Buttons: false,
                SystemSensors: false,
                Commands: []))));

        app.MapGet("/media", async () =>
        {
            var state = await _mediaService.GetCurrentStateAsync();
            return Results.Json(state);
        });

        app.MapPost("/media", async (HttpContext context) =>
        {
            MediaCommand? command;
            try
            {
                command = await JsonSerializer.DeserializeAsync<MediaCommand>(
                    context.Request.Body,
                    JsonOptions,
                    context.RequestAborted);
            }
            catch (JsonException ex)
            {
                _log.Warning($"Rejected malformed media command: {ex.Message}");
                return Results.BadRequest(new { error = "invalid_json" });
            }

            if (command is null || string.IsNullOrWhiteSpace(command.Command))
            {
                return Results.BadRequest(new { error = "command_required" });
            }

            await _mediaService.HandleCommandAsync(command);
            return Results.Ok(new { status = "command_processed" });
        });

        app.MapPost("/notify", async (HttpContext context) =>
        {
            NotificationPayload? payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<NotificationPayload>(
                    context.Request.Body,
                    JsonOptions,
                    context.RequestAborted);
            }
            catch (JsonException ex)
            {
                _log.Warning($"Rejected malformed notification payload: {ex.Message}");
                return Results.BadRequest(new { error = "invalid_json" });
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Message))
            {
                return Results.BadRequest(new { error = "message_required" });
            }

            _notificationSink.ShowNotification(payload);
            return Results.Ok(new { status = "notification_processed" });
        });

        _app = app;
        await app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        await _app.StopAsync(TimeSpan.FromSeconds(5));
        await _app.DisposeAsync();
        _app = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
