using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Http;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Media;
using HASS.Agent.Companion.SystemCommands;
using HASS.Agent.Companion.SystemStatus;
using HASS.Agent.Companion.Runtime;
using MQTTnet;
using MQTTnet.Protocol;

namespace HASS.Agent.Companion.Mqtt;

internal sealed class MqttCompanionService : IDisposable
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CompanionSettings _settings;
    private readonly INotificationSink _notificationSink;
    private readonly MediaSessionService? _mediaSessionService;
    private readonly SystemMetricsService? _systemMetricsService;
    private readonly SystemCommandService _systemCommandService;
    private readonly CompanionRuntimeRole _role;
    private readonly FileLog _log;
    private readonly MqttClientFactory _factory = new();
    private IMqttClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private readonly SemaphoreSlim _restartLock = new(1, 1);

    // Connection state tracking — when only capabilities/sensors change,
    // we refresh discovery without restarting the whole MQTT connection.
    private string? _connectedHost;
    private int _connectedPort;
    private string? _connectedUser;
    private string? _connectedPassword;
    private bool _connectedTls;
    private string? _connectedDeviceName;
    private bool _subscribedNotifications;
    private bool _subscribedMediaPlayer;
    private bool _subscribedButtons;

    public MqttCompanionService(
        CompanionSettings settings,
        INotificationSink notificationSink,
        MediaSessionService? mediaSessionService,
        SystemMetricsService? systemMetricsService,
        SystemCommandService systemCommandService,
        CompanionRuntimeRole role,
        FileLog log)
    {
        _settings = settings;
        _notificationSink = notificationSink;
        _mediaSessionService = mediaSessionService;
        _systemMetricsService = systemMetricsService;
        _systemCommandService = systemCommandService;
        _role = role;
        _log = log;
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        if (!_settings.MqttEnabled)
        {
            _log.Info("MQTT disabled.");
            return;
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task RestartAsync()
    {
        await _restartLock.WaitAsync();
        try
        {
            if (!_settings.MqttEnabled)
            {
                if (_client is { IsConnected: true })
                {
                    _log.Info("MQTT disabled, publishing offline discovery.");
                    await PublishDiscoveryAsync(offline: true);
                }

                await StopAsync(publishOffline: false);
                return;
            }

            // When only capabilities/sensors changed (not connection settings),
            // just refresh discovery on the existing connection — no need to
            // restart MQTT, which would briefly take the media player offline.
            if (_client is { IsConnected: true } && !NeedsReconnect())
            {
                _log.Info("Refreshing MQTT discovery (connection unchanged).");
                await PublishDiscoveryAsync();
                return;
            }

            _log.Info("Restarting MQTT runtime.");
            await StopAsync(publishOffline: false);
            Start();
        }
        finally
        {
            _restartLock.Release();
        }
    }

    private bool NeedsReconnect()
    {
        return _connectedHost != _settings.MqttHost
            || _connectedPort != _settings.MqttPort
            || _connectedUser != _settings.MqttUsername
            || _connectedPassword != _settings.GetMqttPassword()
            || _connectedTls != _settings.MqttUseTls
            || _connectedDeviceName != _settings.DeviceName
            || _subscribedNotifications != _settings.MqttNotificationsEnabled
            || _subscribedMediaPlayer != _settings.MqttMediaPlayerEnabled
            || _subscribedButtons != (_settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0);
    }

    public async Task PublishNotificationActionAsync(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        var trimmedAction = action.Trim();
        _log.Info($"Publishing notification action: {trimmedAction}");

        await PublishJsonAsync(
            $"hass.agent/notifications/{_settings.DeviceName}/actions",
            new NotificationActionMessage(_settings.DeviceName, trimmedAction, DateTimeOffset.UtcNow, null),
            retain: false);
    }

    public async Task StopAsync(bool publishOffline = true)
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();

        if (publishOffline)
        {
            await PublishDiscoveryAsync(offline: true);
        }
        if (_mediaSessionService is not null)
        {
            await _mediaSessionService.StopAsync();
        }

        if (_client is { IsConnected: true })
        {
            await _client.DisconnectAsync();
        }

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

        _cts.Dispose();
        _cts = null;
        _worker = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CancellationTokenSource? connectionCts = null;
            Task? systemSensorsTask = null;
            Task? updateTask = null;

            try
            {
                _client?.Dispose();
                _client = _factory.CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += HandleMessageAsync;

                var options = BuildOptions();
                _log.Info($"Connecting MQTT to {_settings.MqttHost}:{_settings.MqttPort}.");
                await _client.ConnectAsync(options, cancellationToken);

                _log.Info("MQTT connected.");
                await SubscribeAsync(cancellationToken);
                await PublishDiscoveryAsync();
                if (_role == CompanionRuntimeRole.App)
                {
                    await PublishUpdateStateAsync();
                }

                // Store connection state so RestartAsync can decide whether a
                // full reconnect is needed or just a discovery refresh.
                _connectedHost = _settings.MqttHost;
                _connectedPort = _settings.MqttPort;
                _connectedUser = _settings.MqttUsername;
                _connectedPassword = _settings.GetMqttPassword();
                _connectedTls = _settings.MqttUseTls;
                _connectedDeviceName = _settings.DeviceName;
                _subscribedNotifications = _settings.MqttNotificationsEnabled;
                _subscribedMediaPlayer = _settings.MqttMediaPlayerEnabled;
                _subscribedButtons = _settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0;

                connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                if (_role == CompanionRuntimeRole.App && _settings.MqttMediaPlayerEnabled && _mediaSessionService is not null)
                {
                    await _mediaSessionService.StartAsync(
                        PublishMediaStateAsync,
                        thumbnail => PublishRawAsync(
                            $"hass.agent/media_player/{_settings.DeviceName}/thumbnail",
                            thumbnail,
                            retain: false),
                        connectionCts.Token);
                }

                if (ShouldPublishSystemSensors() && _systemMetricsService is not null)
                {
                    systemSensorsTask = Task.Run(
                        () => PublishSystemSensorsLoopAsync(connectionCts.Token),
                        connectionCts.Token);
                }

                if (_role == CompanionRuntimeRole.App)
                {
                    updateTask = Task.Run(
                        () => PublishUpdateLoopAsync(connectionCts.Token),
                        connectionCts.Token);
                }

                while (_client.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning($"MQTT connection loop failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            finally
            {
                if (connectionCts is not null)
                {
                    await connectionCts.CancelAsync();
                }

                if (systemSensorsTask is not null)
                {
                    try
                    {
                        await systemSensorsTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when the MQTT connection is stopped.
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"System sensor publisher stopped unexpectedly: {ex.Message}");
                    }
                }

                if (updateTask is not null)
                {
                    try
                    {
                        await updateTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when the MQTT connection is stopped.
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"Update state publisher stopped unexpectedly: {ex.Message}");
                    }
                }

                connectionCts?.Dispose();
                if (_mediaSessionService is not null)
                {
                    await _mediaSessionService.StopAsync();
                }
            }
        }
    }

    private MqttClientOptions BuildOptions()
    {
        var builder = _factory.CreateClientOptionsBuilder()
            .WithClientId(_settings.GetMqttClientId(_role.Token()))
            .WithTcpServer(_settings.MqttHost, _settings.MqttPort)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10));

        if (!string.IsNullOrWhiteSpace(_settings.MqttUsername))
        {
            builder.WithCredentials(_settings.MqttUsername, _settings.GetMqttPassword());
        }

        if (_settings.MqttUseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        var options = builder.Build();

        if (_role == CompanionRuntimeRole.Service)
        {
            options.WillTopic = ServiceStateTopic;
            options.WillPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(BuildServiceStatus(online: false), JsonOptions));
            options.WillQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
            options.WillRetain = true;
        }

        return options;
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_client is not { IsConnected: true })
        {
            return;
        }

        var builder = _factory.CreateSubscribeOptionsBuilder();
        var hasSubscriptions = false;

        if (_role == CompanionRuntimeRole.Service)
        {
            if (_settings.ServiceCommands.Count > 0)
            {
                builder.WithTopicFilter(ServiceCommandTopic, MqttQualityOfServiceLevel.AtMostOnce);
                await _client.SubscribeAsync(builder.Build(), cancellationToken);
            }

            return;
        }

        if (_settings.MqttNotificationsEnabled)
        {
            builder.WithTopicFilter($"hass.agent/notifications/{_settings.DeviceName}", MqttQualityOfServiceLevel.AtMostOnce);
            hasSubscriptions = true;
        }

        if (_settings.MqttMediaPlayerEnabled)
        {
            builder.WithTopicFilter($"hass.agent/media_player/{_settings.DeviceName}/cmd", MqttQualityOfServiceLevel.AtMostOnce);
            hasSubscriptions = true;
        }

        if (_settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0)
        {
            builder.WithTopicFilter($"hass.agent/buttons/{_settings.DeviceName}/cmd", MqttQualityOfServiceLevel.AtMostOnce);
            hasSubscriptions = true;
        }

        if (!hasSubscriptions)
        {
            _log.Info("No MQTT subscriptions enabled for tray app role.");
            return;
        }

        await _client.SubscribeAsync(builder.Build(), cancellationToken);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = ReadPayload(args.ApplicationMessage);

        try
        {
            if (topic == $"hass.agent/notifications/{_settings.DeviceName}")
            {
                var notification = JsonSerializer.Deserialize<NotificationPayload>(payload, JsonOptions);
                if (notification is not null && !string.IsNullOrWhiteSpace(notification.Message))
                {
                    _notificationSink.ShowNotification(notification);
                }

                return;
            }

            if (topic == $"hass.agent/media_player/{_settings.DeviceName}/cmd")
            {
                var command = JsonSerializer.Deserialize<MediaCommand>(payload, JsonOptions);
                if (command is not null && _mediaSessionService is not null)
                {
                    await _mediaSessionService.HandleCommandAsync(command);
                }
            }

            if (topic == $"hass.agent/buttons/{_settings.DeviceName}/cmd")
            {
                var command = JsonSerializer.Deserialize<SystemCommandMessage>(payload, JsonOptions);
                if (command is not null)
                {
                    var commandName = GetSystemCommandName(command);
                    if (commandName is null || !_settings.IsTrayAppCommandEnabled(commandName))
                    {
                        _log.Warning($"Unsupported tray app command received: {commandName ?? "(empty)"}");
                        return;
                    }

                    await _systemCommandService.HandleCommandAsync(command);
                }
            }

            if (topic == ServiceCommandTopic)
            {
                var command = JsonSerializer.Deserialize<SystemCommandMessage>(payload, JsonOptions);
                if (command is not null)
                {
                    var commandName = GetSystemCommandName(command);
                    if (commandName is null || !_settings.IsServiceCommandEnabled(commandName))
                    {
                        _log.Warning($"Unsupported system service command received: {commandName ?? "(empty)"}");
                        return;
                    }

                    await _systemCommandService.HandleCommandAsync(command);
                }
            }
        }
        catch (JsonException ex)
        {
            _log.Warning($"Rejected malformed MQTT payload on {topic}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Warning($"Unable to process MQTT message on {topic}: {ex.Message}");
        }
    }

    private async Task PublishDiscoveryAsync(bool offline = false)
    {
        if (_role == CompanionRuntimeRole.Service)
        {
            await PublishJsonAsync(ServiceStateTopic, BuildServiceStatus(!offline), retain: true);
            return;
        }

        var systemSensorsEnabled = !offline && _settings.MqttSystemSensorsEnabled;
        var buttonsEnabled = !offline && _settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0;

        var apis = offline
            ? new ApiCapabilitiesResponse(false, false, false, false, false, [], [], [])
            : new ApiCapabilitiesResponse(
                _settings.MqttNotificationsEnabled,
                _settings.MqttMediaPlayerEnabled,
                buttonsEnabled,
                systemSensorsEnabled,
                true,
                buttonsEnabled ? BuildCommandDescriptors(_settings.TrayAppCommands) : [],
                systemSensorsEnabled ? BuildCustomSensorDescriptors(serviceRole: false) : [],
                systemSensorsEnabled ? BuildStandardSensorDescriptors(serviceRole: false) : []);

        await PublishJsonAsync(
            $"hass.agent/devices/{_settings.DeviceName}",
            new MqttDiscoveryMessage(
                _settings.SerialNumber,
                new DeviceInfoResponse(
                    _settings.DeviceName,
                    _settings.Manufacturer,
                    _settings.Model,
                    _settings.SoftwareVersion),
                apis),
            retain: _settings.MqttRetainDiscovery);

        if (!offline)
        {
            await PublishHomeAssistantUpdateDiscoveryAsync();
        }
    }

    private async Task PublishMediaStateAsync(MediaStateMessage state)
    {
        await PublishJsonAsync($"hass.agent/media_player/{_settings.DeviceName}/state", state, retain: false);
    }

    private async Task PublishUpdateLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(UpdateCheckInterval, cancellationToken);
            await PublishUpdateStateAsync();
        }
    }

    private async Task PublishUpdateStateAsync()
    {
        var update = await AppUpdateService.CheckAsync(_settings.SoftwareVersion);
        if (!string.IsNullOrWhiteSpace(update.Error))
        {
            _log.Warning($"Unable to publish update state: {update.Error}");
        }

        await PublishJsonAsync(UpdateStateTopic, update, retain: true);
    }

    private async Task PublishHomeAssistantUpdateDiscoveryAsync()
    {
        await PublishJsonAsync(
            $"homeassistant/update/{SanitizeDiscoveryId(_settings.SerialNumber)}/hass_agent_net10/config",
            new MqttUpdateDiscoveryConfig(
                Name: $"{AppIdentity.DisplayName} Update",
                UniqueId: $"{SanitizeDiscoveryId(_settings.SerialNumber)}_hass_agent_net10_update",
                Device: new MqttDiscoveryDevice(
                    Identifiers: [_settings.SerialNumber],
                    Name: _settings.DeviceName,
                    Manufacturer: _settings.Manufacturer,
                    Model: _settings.Model,
                    SoftwareVersion: _settings.SoftwareVersion),
                StateTopic: UpdateStateTopic,
                ValueTemplate: "{{ value_json.installed_version }}",
                LatestVersionTopic: UpdateStateTopic,
                LatestVersionTemplate: "{{ value_json.latest_version }}",
                TitleTopic: UpdateStateTopic,
                TitleTemplate: "{{ value_json.title }}",
                ReleaseUrlTopic: UpdateStateTopic,
                ReleaseUrlTemplate: "{{ value_json.release_url }}",
                JsonAttributesTopic: UpdateStateTopic),
            retain: _settings.MqttRetainDiscovery);
    }

    private async Task PublishSystemSensorsLoopAsync(CancellationToken cancellationToken)
    {
        var intervals = new Dictionary<SensorPollingProfile, TimeSpan>
        {
            [SensorPollingProfile.Fast] = TimeSpan.FromSeconds(_settings.FastSensorIntervalSeconds),
            [SensorPollingProfile.Normal] = TimeSpan.FromSeconds(_settings.NormalSensorIntervalSeconds),
            [SensorPollingProfile.Hourly] = TimeSpan.FromSeconds(_settings.HourlySensorIntervalSeconds),
            [SensorPollingProfile.Startup] = Timeout.InfiniteTimeSpan
        };

        var now = DateTimeOffset.UtcNow;
        var nextDue = new Dictionary<SensorPollingProfile, DateTimeOffset>
        {
            [SensorPollingProfile.Fast] = now,
            [SensorPollingProfile.Normal] = now,
            [SensorPollingProfile.Hourly] = now,
            [SensorPollingProfile.Startup] = now
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            now = DateTimeOffset.UtcNow;
            var dueProfiles = nextDue
                .Where(item => item.Value <= now)
                .Select(item => item.Key)
                .ToHashSet();

            if (dueProfiles.Count == 0)
            {
                var next = nextDue.Values.Min();
                var delay = next > now ? next - now : TimeSpan.FromSeconds(1);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            await PublishJsonAsync(
                $"hass.agent/sensors/{_settings.DeviceName}/state",
                _systemMetricsService?.Read(_settings.CustomSensors, _role == CompanionRuntimeRole.Service, dueProfiles) ?? throw new InvalidOperationException("System metrics service is not available."),
                retain: false);

            foreach (var profile in dueProfiles)
            {
                if (intervals[profile] == Timeout.InfiniteTimeSpan)
                {
                    nextDue[profile] = DateTimeOffset.MaxValue;
                    continue;
                }

                nextDue[profile] = now + intervals[profile];
            }
        }
    }

    private MqttServiceStatusMessage BuildServiceStatus(bool online)
    {
        var systemSensorsEnabled = online && _settings.MqttServiceSystemSensorsEnabled;

        return new MqttServiceStatusMessage(
            online,
            _role.Token(),
            online ? BuildCommandDescriptors(_settings.ServiceCommands) : [],
            systemSensorsEnabled,
            systemSensorsEnabled ? BuildCustomSensorDescriptors(serviceRole: true) : [],
            systemSensorsEnabled ? BuildStandardSensorDescriptors(serviceRole: true) : [],
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<SystemCommandDescriptor> BuildCommandDescriptors(IEnumerable<string> commands)
    {
        return commands
            .Select(command => SystemCommandCatalog.TryNormalizeName(command, out var normalized) ? normalized : string.Empty)
            .Where(command => !string.IsNullOrEmpty(command))
            .Select(command => new SystemCommandDescriptor(
                command,
                Localization.Strings.GetHa($"Cmd.{command}"),
                command is "shutdown" or "restart" ? Localization.Strings.GetHa($"Cmd.{command}_comment") : null))
            .ToList();
    }

    private IReadOnlyList<BuiltInSensorDescriptor> BuildStandardSensorDescriptors(bool serviceRole)
    {
        return _settings.BuiltInSensors
            .Where(sensor => serviceRole ? sensor.Service : sensor.TrayApp)
            .Select(sensor => BuiltInSensorCatalog.Find(sensor.Key))
            .Where(sensor => sensor is not null)
            .Select(sensor => new BuiltInSensorDescriptor(
                sensor!.Key,
                Localization.Strings.GetHa($"Sensor.{sensor.Key}"),
                SensorPollingProfiles.ToKey(sensor.PollingProfile),
                sensor.HasMultipleValues,
                sensor.DefaultAttributePath,
                sensor.AttributePaths))
            .ToList();
    }

    private IReadOnlyList<CustomSensorDescriptor> BuildCustomSensorDescriptors(bool serviceRole)
    {
        return _settings.CustomSensors
            .Where(sensor => sensor.Enabled && (serviceRole ? sensor.Service : sensor.TrayApp))
            .Select(sensor => new CustomSensorDescriptor(
                sensor.Id,
                sensor.Type,
                sensor.Name,
                sensor.Parameter,
                SensorPollingProfiles.NormalizeKey(sensor.PollingProfile, SensorPollingProfile.Normal),
                sensor.IsDiskFree ? "GiB" : null,
                null,
                sensor.IsDiskFree ? "measurement" : null,
                sensor.Type switch
                {
                    CustomSensorTypes.ProcessRunning => "mdi:application-cog",
                    CustomSensorTypes.ServiceStatus => "mdi:cog-sync",
                    CustomSensorTypes.DiskFree => "mdi:harddisk",
                    CustomSensorTypes.BuiltInAttribute => "mdi:table-column-plus-after",
                    _ => "mdi:gauge"
                }))
            .ToList();
    }

    private bool ShouldPublishSystemSensors()
    {
        return _role == CompanionRuntimeRole.Service
            ? _settings.MqttServiceSystemSensorsEnabled
            : _settings.MqttSystemSensorsEnabled;
    }

    private static string? GetSystemCommandName(SystemCommandMessage message)
    {
        return message.RestartCancel
            ? "restart_cancel"
            : message.Command?.Trim().ToLowerInvariant();
    }

    private async Task PublishJsonAsync(string topic, object payload, bool retain)
    {
        if (_client is not { IsConnected: true })
        {
            return;
        }

        var message = _factory.CreateApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(payload, JsonOptions))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message);
    }

    private async Task PublishRawAsync(string topic, byte[]? payload, bool retain)
    {
        if (_client is not { IsConnected: true })
        {
            return;
        }

        var message = _factory.CreateApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload ?? [])
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message);
    }

    private static string ReadPayload(MqttApplicationMessage message)
    {
        return Encoding.UTF8.GetString(message.Payload.ToArray());
    }

    private static string SanitizeDiscoveryId(string value)
    {
        var id = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(id) ? "hass_agent_net10" : id;
    }

    private string ServiceStateTopic => $"hass.agent/system/{_settings.DeviceName}/state";

    private string ServiceCommandTopic => $"hass.agent/system/{_settings.DeviceName}/cmd";

    private string UpdateStateTopic => $"hass.agent/update/{_settings.DeviceName}/state";
}

internal sealed record MqttDiscoveryMessage(
    [property: JsonPropertyName("serial_number")] string SerialNumber,
    [property: JsonPropertyName("device")] DeviceInfoResponse Device,
    [property: JsonPropertyName("apis")] ApiCapabilitiesResponse Apis);

internal sealed record NotificationActionMessage(
    [property: JsonPropertyName("device_name")] string DeviceName,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("input")] string? Input);

internal sealed record MqttServiceStatusMessage(
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("commands")] IReadOnlyList<SystemCommandDescriptor> Commands,
    [property: JsonPropertyName("system_sensors")] bool SystemSensors,
    [property: JsonPropertyName("custom_sensors")] IReadOnlyList<CustomSensorDescriptor> CustomSensors,
    [property: JsonPropertyName("standard_sensors")] IReadOnlyList<BuiltInSensorDescriptor> StandardSensors,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

internal sealed record MqttUpdateDiscoveryConfig(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unique_id")] string UniqueId,
    [property: JsonPropertyName("device")] MqttDiscoveryDevice Device,
    [property: JsonPropertyName("state_topic")] string StateTopic,
    [property: JsonPropertyName("value_template")] string ValueTemplate,
    [property: JsonPropertyName("latest_version_topic")] string LatestVersionTopic,
    [property: JsonPropertyName("latest_version_template")] string LatestVersionTemplate,
    [property: JsonPropertyName("title_topic")] string TitleTopic,
    [property: JsonPropertyName("title_template")] string TitleTemplate,
    [property: JsonPropertyName("release_url_topic")] string ReleaseUrlTopic,
    [property: JsonPropertyName("release_url_template")] string ReleaseUrlTemplate,
    [property: JsonPropertyName("json_attributes_topic")] string JsonAttributesTopic);

internal sealed record MqttDiscoveryDevice(
    [property: JsonPropertyName("identifiers")] IReadOnlyList<string> Identifiers,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("manufacturer")] string Manufacturer,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("sw_version")] string SoftwareVersion);
