using System.Runtime.InteropServices;

namespace HASS.Agent.Companion.Media;

internal static class MediaKeySender
{
    private const int KeyEventExtendedKey = 1;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPreviousTrack = 0xB1;
    private const byte VkMediaStop = 0xB2;
    private const byte VkMediaPlayPause = 0xB3;
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;

    public static void VolumeUp() => Send(VkVolumeUp);

    public static void VolumeDown() => Send(VkVolumeDown);

    public static void MuteToggle() => Send(VkVolumeMute);

    public static void PlayPause() => Send(VkMediaPlayPause);

    public static void Stop() => Send(VkMediaStop);

    public static void Next() => Send(VkMediaNextTrack);

    public static void Previous() => Send(VkMediaPreviousTrack);

    private static void Send(byte virtualKey)
    {
        keybd_event(virtualKey, 0, KeyEventExtendedKey, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
