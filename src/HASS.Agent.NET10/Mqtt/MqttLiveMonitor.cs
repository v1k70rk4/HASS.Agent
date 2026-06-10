using System.Buffers;
using System.Text;
using HASS.Agent.Companion.Configuration;
using MQTTnet;
using MQTTnet.Protocol;

namespace HASS.Agent.Companion.Mqtt;

/// <summary>
/// Live tap on the hass.agent/# topics for the Danger Zone monitor.
/// Owns its own MQTT connection so the main service is untouched.
/// </summary>
internal sealed class MqttLiveMonitor : IDisposable
{
    private IMqttClient? _client;

    public bool IsRunning => _client is { IsConnected: true };

    public async Task StartAsync(CompanionSettings settings, Action<string, string, bool> onMessage, CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return;
        }

        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();

        client.ApplicationMessageReceivedAsync += args =>
        {
            var message = args.ApplicationMessage;
            var payload = Encoding.UTF8.GetString(message.Payload.ToArray());
            onMessage(message.Topic, payload, message.Retain);
            return Task.CompletedTask;
        };

        await client.ConnectAsync(MqttRetainedBrowser.BuildClientOptions(settings, factory, "monitor"), cancellationToken);

        var subscribe = factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter("hass.agent/#", MqttQualityOfServiceLevel.AtMostOnce)
            .Build();
        await client.SubscribeAsync(subscribe, cancellationToken);

        _client = client;
    }

    public async Task StopAsync()
    {
        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        try { await client.DisconnectAsync(); } catch { }
        client.Dispose();
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
