using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using HASS.Agent.Companion.Configuration;
using MQTTnet;
using MQTTnet.Protocol;

namespace HASS.Agent.Companion.Mqtt;

internal sealed record RetainedMqttMessage(string Topic, int PayloadSize, string Preview, bool IsOwnDevice);

/// <summary>
/// Scans the broker for retained HASS.Agent messages and deletes them on request.
/// Retained messages are discovered by subscribing to wildcard topics: the broker
/// immediately replays every retained message on a fresh subscription.
/// </summary>
internal static class MqttRetainedBrowser
{
    private static readonly TimeSpan CollectWindow = TimeSpan.FromSeconds(3);

    public static async Task<List<RetainedMqttMessage>> ScanAsync(CompanionSettings settings, CancellationToken cancellationToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var messages = new ConcurrentDictionary<string, RetainedMqttMessage>();
        var serial = settings.SerialNumber;
        var sanitizedSerial = SanitizeId(serial);

        client.ApplicationMessageReceivedAsync += args =>
        {
            var message = args.ApplicationMessage;
            if (!message.Retain)
            {
                return Task.CompletedTask;
            }

            var topic = message.Topic;
            var payload = message.Payload.ToArray();
            if (payload.Length == 0)
            {
                return Task.CompletedTask;
            }

            var preview = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 200));

            // homeassistant/.../config topics from other integrations are not ours;
            // keep only entries that reference a HASS.Agent device.
            if (topic.StartsWith("homeassistant/", StringComparison.OrdinalIgnoreCase) &&
                !topic.Contains("hass_agent", StringComparison.OrdinalIgnoreCase) &&
                !preview.Contains("hass.agent", StringComparison.OrdinalIgnoreCase) &&
                !preview.Contains("hass_agent", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            var isOwn =
                topic.Contains($"/{serial}", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains($"/{sanitizedSerial}/", StringComparison.OrdinalIgnoreCase) ||
                preview.Contains(serial, StringComparison.OrdinalIgnoreCase);

            messages[topic] = new RetainedMqttMessage(topic, payload.Length, preview, isOwn);
            return Task.CompletedTask;
        };

        await client.ConnectAsync(BuildClientOptions(settings, factory, "dangerzone"), cancellationToken);
        try
        {
            var subscribe = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter("hass.agent/#", MqttQualityOfServiceLevel.AtMostOnce)
                .WithTopicFilter("homeassistant/+/+/config", MqttQualityOfServiceLevel.AtMostOnce)
                .WithTopicFilter("homeassistant/+/+/+/config", MqttQualityOfServiceLevel.AtMostOnce)
                .Build();
            await client.SubscribeAsync(subscribe, cancellationToken);

            await Task.Delay(CollectWindow, cancellationToken);
        }
        finally
        {
            try { await client.DisconnectAsync(); } catch { }
        }

        return messages.Values
            .OrderByDescending(m => m.IsOwnDevice)
            .ThenBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task DeleteAsync(CompanionSettings settings, IReadOnlyList<string> topics, CancellationToken cancellationToken)
    {
        if (topics.Count == 0)
        {
            return;
        }

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        await client.ConnectAsync(BuildClientOptions(settings, factory, "dangerzone"), cancellationToken);
        try
        {
            foreach (var topic in topics)
            {
                // Publishing an empty retained payload clears the retained message.
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Array.Empty<byte>())
                    .WithRetainFlag()
                    .Build();
                await client.PublishAsync(message, cancellationToken);
            }
        }
        finally
        {
            try { await client.DisconnectAsync(); } catch { }
        }
    }

    internal static MqttClientOptions BuildClientOptions(CompanionSettings settings, MqttClientFactory factory, string role)
    {
        var builder = factory.CreateClientOptionsBuilder()
            .WithClientId(settings.GetMqttClientId(role))
            .WithTcpServer(settings.MqttHost, settings.MqttPort)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(10));

        if (!string.IsNullOrWhiteSpace(settings.MqttUsername))
        {
            builder.WithCredentials(settings.MqttUsername, settings.GetMqttPassword());
        }

        if (settings.MqttUseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }

    private static string SanitizeId(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
    }
}
