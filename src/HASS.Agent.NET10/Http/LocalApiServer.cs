using System.Text.Json;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Logging;
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
    private readonly FileLog _log;
    private WebApplication? _app;

    public LocalApiServer(CompanionSettings settings, INotificationSink notificationSink, FileLog log)
    {
        _settings = settings;
        _notificationSink = notificationSink;
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
                MediaPlayer: false,
                Buttons: false,
                SystemSensors: false,
                Update: true,
                Commands: []))));

        app.MapPost("/notify", async (HttpContext context) =>
        {
            if (!IsAuthorized(context))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

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

    private bool IsAuthorized(HttpContext context)
    {
        var apiKey = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return true;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(authorization[7..].Trim(), apiKey, StringComparison.Ordinal);
        }

        return false;
    }
}
