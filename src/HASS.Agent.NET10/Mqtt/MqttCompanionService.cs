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
    private HaWebSocketService? _haWs;
    private bool _isOnWebSocket;

    // Connection state tracking — when only capabilities/sensors change,
    // we refresh discovery without restarting the whole MQTT connection.
    private string? _connectedHost;
    private int _connectedPort;
    private string? _connectedUser;
    private string? _connectedPassword;
    private bool _connectedTls;
    private string? _connectedDeviceName;
    private bool _connectedHaApiEnabled;
    private string? _connectedHaApiUrl;
    private string? _connectedHaApiToken;
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

        if (!_settings.MqttEnabled && !_settings.HaApiEnabled)
        {
            _log.Info("MQTT and HA API both disabled.");
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
            if (!_settings.MqttEnabled && !_settings.HaApiEnabled)
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
                await PublishLegacyTopicCleanupAsync();
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
            || _connectedHaApiEnabled != _settings.HaApiEnabled
            || _connectedHaApiUrl != _settings.HaApiUrl
            || _connectedHaApiToken != _settings.GetHaApiToken()
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

        if (_isOnWebSocket && _haWs is not null)
        {
            await _haWs.PublishNotificationActionAsync(trimmedAction, _cts?.Token ?? CancellationToken.None);
            return;
        }

        await PublishJsonAsync(
            $"hass.agent/notifications/{TopicId}/actions",
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

        if (_haWs is not null)
        {
            await _haWs.DisconnectAsync();
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
        _isOnWebSocket = false;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _haWs?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _haWs?.Dispose();
        _haWs = null;

        // Initialize HA WebSocket service if configured.
        if (_settings.HaApiEnabled && !string.IsNullOrWhiteSpace(_settings.HaApiUrl))
        {
            _haWs = new HaWebSocketService(_settings, _log);
            _haWs.NotificationReceived += notification => _notificationSink.ShowNotification(notification);
            _haWs.MediaCommandReceived += command =>
            {
                if (_mediaSessionService is not null)
                {
                    _ = _mediaSessionService.HandleCommandAsync(command);
                }
            };
            _haWs.ButtonCommandReceived += command =>
            {
                var commandName = GetSystemCommandName(command);
                var enabled = _role == CompanionRuntimeRole.Service
                    ? commandName is not null && _settings.IsServiceCommandEnabled(commandName)
                    : commandName is not null && _settings.IsTrayAppCommandEnabled(commandName);
                if (enabled)
                {
                    _ = _systemCommandService.HandleCommandAsync(command);
                    return;
                }

                _log.Warning($"Unsupported {_role.Token()} WebSocket command received: {commandName ?? "(empty)"}");
            };
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            CancellationTokenSource? connectionCts = null;
            Task? systemSensorsTask = null;
            Task? updateTask = null;

            try
            {
                if (!_settings.MqttEnabled)
                {
                    // MQTT disabled — go straight to WebSocket if available.
                    if (_haWs is not null)
                    {
                        _log.Info("MQTT disabled, using HA WebSocket API as primary transport.");
                        await RunWebSocketFailoverAsync(tryMqttReconnect: false, cancellationToken);
                    }

                    // If WS also exits, wait and retry.
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                _client?.Dispose();
                _client = _factory.CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += HandleMessageAsync;

                var options = BuildOptions();
                _log.Info($"Connecting MQTT to {_settings.MqttHost}:{_settings.MqttPort}.");
                _isOnWebSocket = false;
                await _client.ConnectAsync(options, cancellationToken);

                _log.Info("MQTT connected.");
                await SubscribeAsync(cancellationToken);
                await PublishDiscoveryAsync();
                await PublishLegacyTopicCleanupAsync();
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
                _connectedHaApiEnabled = _settings.HaApiEnabled;
                _connectedHaApiUrl = _settings.HaApiUrl;
                _connectedHaApiToken = _settings.GetHaApiToken();
                _subscribedNotifications = _settings.MqttNotificationsEnabled;
                _subscribedMediaPlayer = _settings.MqttMediaPlayerEnabled;
                _subscribedButtons = _settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0;

                connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                if (_role == CompanionRuntimeRole.App && _settings.MqttMediaPlayerEnabled && _mediaSessionService is not null)
                {
                    await _mediaSessionService.StartAsync(
                        PublishMediaStateAsync,
                        thumbnail => PublishRawAsync(
                            $"hass.agent/media_player/{TopicId}/thumbnail",
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

                // MQTT failed — try WebSocket failover if configured.
                if (_haWs is not null && !cancellationToken.IsCancellationRequested)
                {
                    _log.Info("Switching to HA WebSocket API failover.");
                    try
                    {
                        await RunWebSocketFailoverAsync(tryMqttReconnect: true, cancellationToken);
                        // If we get here, MQTT reconnected — loop will retry MQTT.
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception wsEx)
                    {
                        _log.Warning($"HA WebSocket failover also failed: {wsEx.Message}");
                    }
                }

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

    /// <summary>
    /// Runs on the WebSocket transport. Publishes discovery & sensors via HA events.
    /// If <paramref name="tryMqttReconnect"/> is true, periodically checks if MQTT is back
    /// and returns to let the main loop reconnect.
    /// </summary>
    private async Task RunWebSocketFailoverAsync(bool tryMqttReconnect, CancellationToken cancellationToken)
    {
        _isOnWebSocket = true;
        await _haWs!.ConnectAsync(cancellationToken);
        _log.Info("HA WebSocket connected as failover transport.");

        using var wsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Publish discovery via WebSocket.
        await PublishDiscoveryViaWebSocketAsync(wsCts.Token);

        // Start the receive loop (handles incoming commands).
        var receiveTask = Task.Run(() => _haWs.ReceiveLoopAsync(wsCts.Token), wsCts.Token);

        // Start media player on WS transport.
        if (_role == CompanionRuntimeRole.App && _settings.MqttMediaPlayerEnabled && _mediaSessionService is not null)
        {
            await _mediaSessionService.StartAsync(
                state => _haWs.PublishMediaStateAsync(state, wsCts.Token),
                thumbnail => _haWs.PublishMediaThumbnailAsync(thumbnail, wsCts.Token),
                wsCts.Token);
        }

        // Start sensor publishing on WS transport.
        Task? sensorTask = null;
        if (ShouldPublishSystemSensors() && _systemMetricsService is not null)
        {
            sensorTask = Task.Run(() => PublishSystemSensorsViaWebSocketLoopAsync(wsCts.Token), wsCts.Token);
        }

        try
        {
            if (tryMqttReconnect)
            {
                // Periodically try MQTT reconnection in background.
                while (!cancellationToken.IsCancellationRequested && _haWs.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                    if (await TryMqttPingAsync(cancellationToken))
                    {
                        _log.Info("MQTT broker is back online — switching back from WebSocket.");
                        break;
                    }
                }
            }
            else
            {
                // WS-only mode: just wait until connection drops or cancel.
                while (!cancellationToken.IsCancellationRequested && _haWs.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            _isOnWebSocket = false;
            await wsCts.CancelAsync();

            if (sensorTask is not null)
            {
                try { await sensorTask; } catch (OperationCanceledException) { }
            }

            try { await receiveTask; } catch (OperationCanceledException) { }

            if (_mediaSessionService is not null)
            {
                await _mediaSessionService.StopAsync();
            }

            await _haWs.DisconnectAsync();
        }
    }

    private async Task PublishDiscoveryViaWebSocketAsync(CancellationToken cancellationToken)
    {
        var systemSensorsEnabled = _settings.MqttSystemSensorsEnabled;
        var buttonsEnabled = _settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0;

        var apis = new ApiCapabilitiesResponse(
            _settings.MqttNotificationsEnabled,
            _settings.MqttMediaPlayerEnabled,
            buttonsEnabled,
            systemSensorsEnabled,
            true,
            buttonsEnabled ? BuildCommandDescriptors(_settings.TrayAppCommands) : [],
            systemSensorsEnabled ? BuildCustomSensorDescriptors(serviceRole: false) : [],
            systemSensorsEnabled ? BuildStandardSensorDescriptors(serviceRole: false) : []);

        await _haWs!.PublishDeviceDiscoveryAsync(new
        {
            serial_number = _settings.SerialNumber,
            device = new
            {
                name = _settings.DeviceName,
                manufacturer = _settings.Manufacturer,
                model = _settings.Model,
                sw_version = _settings.SoftwareVersion
            },
            apis
        }, cancellationToken);
    }

    private async Task PublishSystemSensorsViaWebSocketLoopAsync(CancellationToken cancellationToken)
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

            var sensorData = _systemMetricsService?.Read(
                _settings.CustomSensors,
                _role == CompanionRuntimeRole.Service,
                dueProfiles);

            if (sensorData is not null)
            {
                await _haWs!.PublishSensorStateAsync(new
                {
                    serial_number = _settings.SerialNumber,
                    sensors = sensorData
                }, cancellationToken);
            }

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

    /// <summary>
    /// Tries a quick MQTT connect/disconnect to see if the broker is reachable.
    /// Returns true if the broker responds, false otherwise.
    /// </summary>
    private async Task<bool> TryMqttPingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var pingClient = _factory.CreateMqttClient();
            var options = BuildOptions();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await pingClient.ConnectAsync(options, timeoutCts.Token);
            await pingClient.DisconnectAsync();
            return true;
        }
        catch
        {
            return false;
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
            builder.WithTopicFilter($"hass.agent/notifications/{TopicId}", MqttQualityOfServiceLevel.AtMostOnce);
            hasSubscriptions = true;
        }

        if (_settings.MqttMediaPlayerEnabled)
        {
            builder.WithTopicFilter($"hass.agent/media_player/{TopicId}/cmd", MqttQualityOfServiceLevel.AtMostOnce);
            hasSubscriptions = true;
        }

        if (_settings.MqttButtonsEnabled && _settings.TrayAppCommands.Count > 0)
        {
            builder.WithTopicFilter($"hass.agent/buttons/{TopicId}/cmd", MqttQualityOfServiceLevel.AtMostOnce);
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
            if (topic == $"hass.agent/notifications/{TopicId}")
            {
                var notification = JsonSerializer.Deserialize<NotificationPayload>(payload, JsonOptions);
                if (notification is not null && !string.IsNullOrWhiteSpace(notification.Message))
                {
                    _notificationSink.ShowNotification(notification);
                }

                return;
            }

            if (topic == $"hass.agent/media_player/{TopicId}/cmd")
            {
                var command = JsonSerializer.Deserialize<MediaCommand>(payload, JsonOptions);
                if (command is not null && _mediaSessionService is not null)
                {
                    await _mediaSessionService.HandleCommandAsync(command);
                }
            }

            if (topic == $"hass.agent/buttons/{TopicId}/cmd")
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
            $"hass.agent/devices/{TopicId}",
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

    private async Task PublishLegacyTopicCleanupAsync()
    {
        var legacyTopicId = _settings.DeviceName;
        if (string.IsNullOrWhiteSpace(legacyTopicId) ||
            string.Equals(legacyTopicId, TopicId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await PublishRawAsync($"hass.agent/devices/{legacyTopicId}", null, retain: true);
        await PublishRawAsync($"hass.agent/system/{legacyTopicId}/state", null, retain: true);
        await PublishRawAsync($"hass.agent/update/{legacyTopicId}/state", null, retain: true);
    }

    private async Task PublishMediaStateAsync(MediaStateMessage state)
    {
        await PublishJsonAsync($"hass.agent/media_player/{TopicId}/state", state, retain: false);
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
                $"hass.agent/sensors/{TopicId}/state",
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

    private string TopicId => _settings.SerialNumber;

    private string ServiceStateTopic => $"hass.agent/system/{TopicId}/state";

    private string ServiceCommandTopic => $"hass.agent/system/{TopicId}/cmd";

    private string UpdateStateTopic => $"hass.agent/update/{TopicId}/state";
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
