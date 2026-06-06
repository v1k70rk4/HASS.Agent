namespace HASS.Agent.Companion.Http;

internal sealed class NullNotificationSink : INotificationSink
{
    public void ShowNotification(NotificationPayload notification)
    {
    }
}
