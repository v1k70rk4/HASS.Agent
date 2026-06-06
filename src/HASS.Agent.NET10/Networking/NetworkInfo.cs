using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HASS.Agent.Companion.Networking;

internal static class NetworkInfo
{
    public static IReadOnlyList<string> GetLanUrls(int port)
    {
        var urls = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address))
            .Select(address => $"http://{address.Address}:{port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            urls.Add($"http://{Environment.MachineName}:{port}");
        }

        return urls;
    }

    public static string GetPreferredLanUrl(int port)
    {
        return GetLanUrls(port)[0];
    }
}
