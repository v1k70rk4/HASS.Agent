namespace HASS.Agent.Companion.Http;

internal interface INotificationSink
{
    void ShowNotification(NotificationPayload notification);
}
