using HASS.Agent.Companion.Logging;
using NAudio.CoreAudioApi;

namespace HASS.Agent.Companion.Media;

internal sealed class AudioEndpointService(FileLog log)
{
    public int GetVolume()
    {
        try
        {
            using var device = GetDefaultDevice();
            return Convert.ToInt32(Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100, 0));
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to read default audio volume: {ex.Message}");
            return 0;
        }
    }

    public bool GetMuted()
    {
        try
        {
            using var device = GetDefaultDevice();
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to read default audio mute state: {ex.Message}");
            return false;
        }
    }

    public string GetOutputDeviceName()
    {
        try
        {
            using var device = GetDefaultDevice(DataFlow.Render);
            return device.FriendlyName;
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to read default audio output device: {ex.Message}");
            return string.Empty;
        }
    }

    public bool? GetMicrophoneMuted()
    {
        try
        {
            using var device = GetDefaultDevice(DataFlow.Capture);
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to read default microphone mute state: {ex.Message}");
            return null;
        }
    }

    public void SetVolume(int volume)
    {
        try
        {
            using var device = GetDefaultDevice();
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 100) / 100f;
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to set default audio volume: {ex.Message}");
        }
    }

    public void SetMuted(bool muted)
    {
        try
        {
            using var device = GetDefaultDevice();
            device.AudioEndpointVolume.Mute = muted;
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to set default audio mute state: {ex.Message}");
        }
    }

    private static MMDevice GetDefaultDevice()
    {
        return GetDefaultDevice(DataFlow.Render);
    }

    private static MMDevice GetDefaultDevice(DataFlow dataFlow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
    }
}
