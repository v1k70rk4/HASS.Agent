using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using System.Windows.Forms;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Media;

namespace HASS.Agent.Companion.SystemStatus;

internal sealed class SystemMetricsService : IDisposable
{
    private readonly object _cpuLock = new();
    private readonly AudioEndpointService? _audioEndpointService;
    private readonly MonitorPowerStateService? _monitorPowerStateService;
    private readonly bool _includeInteractiveMetrics;
    private ulong? _lastIdleTime;
    private ulong? _lastKernelTime;
    private ulong? _lastUserTime;

    public SystemMetricsService(FileLog log, MonitorPowerStateService? monitorPowerStateService, bool includeInteractiveMetrics = true)
    {
        _audioEndpointService = includeInteractiveMetrics ? new AudioEndpointService(log) : null;
        _monitorPowerStateService = monitorPowerStateService;
        _includeInteractiveMetrics = includeInteractiveMetrics;
    }

    public SystemMetricsMessage Read(IReadOnlyList<CustomSensorDefinition>? customSensors = null, bool serviceRole = false)
    {
        var memory = ReadMemory();
        var drive = ReadSystemDrive();
        var activeWindow = _includeInteractiveMetrics ? ReadActiveWindow() : (Title: string.Empty, ProcessName: string.Empty);
        var power = ReadPowerStatus();
        var session = ReadSessionStatus();
        var wifi = ReadWifiStatus();
        var sessions = ReadSessionCounts();
        var sessionLocked = _includeInteractiveMetrics ? ReadSessionLocked() : null;
        var networkAddresses = ReadNetworkAddresses();
        var displays = _includeInteractiveMetrics ? ReadDisplays() : [];
        var recentErrors = ReadRecentEventLogErrors(TimeSpan.FromHours(1));
        var lastShutdown = ReadLastShutdownInfo();

        return new SystemMetricsMessage(
            CpuUsage: ReadCpuUsage(),
            MemoryUsage: memory.UsagePercent,
            MemoryAvailableMb: memory.AvailableMb,
            SystemDriveFreePercent: drive.FreePercent,
            SystemDriveFreeGb: drive.FreeGb,
            UptimeSeconds: Math.Max(0, Environment.TickCount64 / 1000),
            ActiveWindow: _includeInteractiveMetrics ? LimitState(activeWindow.Title) : null,
            ActiveProcess: _includeInteractiveMetrics ? LimitState(activeWindow.ProcessName) : null,
            ForegroundAppTitle: _includeInteractiveMetrics ? BuildForegroundAppTitle(activeWindow) : null,
            Volume: _audioEndpointService?.GetVolume(),
            Muted: _audioEndpointService?.GetMuted(),
            AudioOutputDevice: _includeInteractiveMetrics ? LimitState(_audioEndpointService?.GetOutputDeviceName() ?? string.Empty) : null,
            MicrophoneMuted: _includeInteractiveMetrics ? _audioEndpointService?.GetMicrophoneMuted() : null,
            BatteryLevel: power.BatteryLevel,
            PowerStatus: power.Status,
            BatteryTimeRemaining: power.TimeRemainingSeconds,
            MonitorPowerState: _monitorPowerStateService?.State,
            ActiveDisplay: _includeInteractiveMetrics ? FormatDisplayState(displays) : null,
            NetworkAddress: networkAddresses.FirstOrDefault()?.Address ?? string.Empty,
            VpnConnected: ReadVpnConnected(),
            WifiSsid: wifi.Ssid,
            WifiSignal: wifi.Signal,
            IdleTimeSeconds: _includeInteractiveMetrics ? ReadIdleTimeSeconds() : null,
            SessionLocked: sessionLocked,
            UserPresent: _includeInteractiveMetrics ? session.State == "active" && sessionLocked is false && !string.IsNullOrWhiteSpace(session.User) : null,
            ClipboardTextAvailable: _includeInteractiveMetrics ? ReadClipboardTextAvailable() : null,
            SessionState: session.State,
            LoggedInUser: session.User,
            LoggedInUsers: sessions.LoggedInUsers,
            RdpSessions: sessions.RdpSessions,
            PendingReboot: ReadPendingReboot(),
            WindowsUpdatePending: ReadWindowsUpdatePending(),
            BluetoothEnabled: ReadBluetoothEnabled(),
            EventLogErrorsRecent: recentErrors.Count,
            LastShutdownReason: lastShutdown.Summary,
            Attributes: BuildAttributes(networkAddresses, displays, recentErrors, lastShutdown),
            BootTime: DateTimeOffset.Now.AddMilliseconds(-Environment.TickCount64),
            CustomSensors: ReadCustomSensors(customSensors ?? [], serviceRole),
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
    }

    private double ReadCpuUsage()
    {
        lock (_cpuLock)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return 0;
            }

            var idleTime = idle.ToUInt64();
            var kernelTime = kernel.ToUInt64();
            var userTime = user.ToUInt64();

            if (_lastIdleTime is null || _lastKernelTime is null || _lastUserTime is null)
            {
                _lastIdleTime = idleTime;
                _lastKernelTime = kernelTime;
                _lastUserTime = userTime;
                return 0;
            }

            var idleDelta = idleTime - _lastIdleTime.Value;
            var kernelDelta = kernelTime - _lastKernelTime.Value;
            var userDelta = userTime - _lastUserTime.Value;
            var totalDelta = kernelDelta + userDelta;

            _lastIdleTime = idleTime;
            _lastKernelTime = kernelTime;
            _lastUserTime = userTime;

            if (totalDelta == 0)
            {
                return 0;
            }

            var usage = (totalDelta - idleDelta) * 100d / totalDelta;
            return Math.Round(Math.Clamp(usage, 0, 100), 1);
        }
    }

    private static (double UsagePercent, long AvailableMb) ReadMemory()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0);
        }

        return (
            Math.Round((double)status.MemoryLoad, 1),
            Convert.ToInt64(status.AvailablePhysical / 1024 / 1024));
    }

    private static (double FreePercent, double FreeGb) ReadSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return (0, 0);
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return (0, 0);
            }

            return (
                Math.Round(drive.AvailableFreeSpace * 100d / drive.TotalSize, 1),
                Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 1));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (string Title, string ProcessName) ReadActiveWindow()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return (string.Empty, string.Empty);
        }

        var titleBuilder = new StringBuilder(512);
        _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);

        var processName = string.Empty;
        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                processName = string.Empty;
            }
        }

        return (titleBuilder.ToString(), processName);
    }

    private static string BuildForegroundAppTitle((string Title, string ProcessName) activeWindow)
    {
        if (string.IsNullOrWhiteSpace(activeWindow.ProcessName))
        {
            return LimitState(activeWindow.Title);
        }

        if (string.IsNullOrWhiteSpace(activeWindow.Title))
        {
            return LimitState(activeWindow.ProcessName);
        }

        return LimitState($"{activeWindow.ProcessName} - {activeWindow.Title}");
    }

    private static (int? BatteryLevel, string Status, long? TimeRemainingSeconds) ReadPowerStatus()
    {
        var status = SystemInformation.PowerStatus;
        long? timeRemaining = status.BatteryLifeRemaining >= 0 ? status.BatteryLifeRemaining : null;
        if (status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
        {
            return (null, "no_battery", null);
        }

        int? batteryLevel = status.BatteryLifePercent >= 0
            ? Convert.ToInt32(Math.Clamp(Math.Round(status.BatteryLifePercent * 100), 0, 100))
            : null;

        if (status.PowerLineStatus == PowerLineStatus.Online &&
            status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging))
        {
            return (batteryLevel, "charging", timeRemaining);
        }

        return status.PowerLineStatus switch
        {
            PowerLineStatus.Online => (batteryLevel, "plugged_in", timeRemaining),
            PowerLineStatus.Offline => (batteryLevel, "battery", timeRemaining),
            _ => (batteryLevel, "unknown", timeRemaining)
        };
    }

    private static IReadOnlyList<NetworkAddressInfo> ReadNetworkAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                .Where(address =>
                    address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address) &&
                    !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => new NetworkAddressInfo(adapter.Name, adapter.Description, address.Address.ToString())))
            .ToList();
    }

    private static IReadOnlyList<DisplayInfo> ReadDisplays()
    {
        try
        {
            return Screen.AllScreens
                .Select(screen => new DisplayInfo(
                    screen.DeviceName,
                    screen.Primary,
                    screen.Bounds.Width,
                    screen.Bounds.Height,
                    screen.Bounds.X,
                    screen.Bounds.Y))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string FormatDisplayState(IReadOnlyList<DisplayInfo> displays)
    {
        return LimitState(string.Join(
            ", ",
            displays.Select(display =>
                $"{(display.Primary ? "primary:" : string.Empty)}{display.Name} {display.Width}x{display.Height}")));
    }

    private static bool ReadVpnConnected()
    {
        string[] vpnHints = ["vpn", "wireguard", "tailscale", "zerotier", "tap", "tun", "openvpn", "nord", "proton", "surfshark"];
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Any(adapter =>
                vpnHints.Any(hint => adapter.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)) ||
                vpnHints.Any(hint => adapter.Description.Contains(hint, StringComparison.OrdinalIgnoreCase)));
    }

    private static (string Ssid, int? Signal) ReadWifiStatus()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "wlan show interfaces",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            });

            if (process is null)
            {
                return (string.Empty, null);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            if (process.ExitCode != 0)
            {
                return (string.Empty, null);
            }

            var ssid = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var signalText = Regex.Match(output, @"^\s*(Signal|Jel)\s*:\s*(\d+)%", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[2].Value;
            int? signal = int.TryParse(signalText, out var parsedSignal) ? parsedSignal : null;
            return (LimitState(ssid), signal);
        }
        catch
        {
            return (string.Empty, null);
        }
    }

    private static long? ReadIdleTimeSeconds()
    {
        var input = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref input))
        {
            return null;
        }

        var idleMilliseconds = Environment.TickCount64 - input.Time;
        return Math.Max(0, idleMilliseconds / 1000);
    }

    private static (string State, string User) ReadSessionStatus()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
        {
            return ("none", string.Empty);
        }

        var state = QuerySessionConnectState(sessionId);
        var userName = QuerySessionString(sessionId, WtsInfoClass.UserName);
        var domain = QuerySessionString(sessionId, WtsInfoClass.DomainName);

        var user = string.IsNullOrWhiteSpace(userName)
            ? string.Empty
            : string.IsNullOrWhiteSpace(domain) ? userName : $"{domain}\\{userName}";

        return (state, LimitState(user));
    }

    private static bool? ReadClipboardTextAvailable()
    {
        bool? result = null;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = Clipboard.ContainsText();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMilliseconds(750)))
        {
            return null;
        }

        return exception is null ? result : null;
    }

    private static (int LoggedInUsers, int RdpSessions) ReadSessionCounts()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessionsPointer, out var sessionCount))
        {
            return (0, 0);
        }

        try
        {
            var loggedInUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rdpSessions = 0;
            var itemSize = Marshal.SizeOf<WtsSessionInfo>();

            for (var index = 0; index < sessionCount; index++)
            {
                var itemPointer = IntPtr.Add(sessionsPointer, index * itemSize);
                var session = Marshal.PtrToStructure<WtsSessionInfo>(itemPointer);
                if (session.State != WtsConnectState.Active)
                {
                    continue;
                }

                var userName = QuerySessionString(session.SessionId, WtsInfoClass.UserName);
                if (!string.IsNullOrWhiteSpace(userName))
                {
                    var domain = QuerySessionString(session.SessionId, WtsInfoClass.DomainName);
                    loggedInUsers.Add(string.IsNullOrWhiteSpace(domain) ? userName : $"{domain}\\{userName}");
                }

                var clientProtocol = QuerySessionInt(session.SessionId, WtsInfoClass.ClientProtocolType);
                if (clientProtocol == 2)
                {
                    rdpSessions++;
                }
            }

            return (loggedInUsers.Count, rdpSessions);
        }
        finally
        {
            WTSFreeMemory(sessionsPointer);
        }
    }

    private static bool? ReadSessionLocked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            return false;
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    private static string QuerySessionConnectState(uint sessionId)
    {
        if (!WTSQuerySessionInformation(
            IntPtr.Zero,
            sessionId,
            WtsInfoClass.ConnectState,
            out var buffer,
            out var bytesReturned))
        {
            return "unknown";
        }

        try
        {
            if (bytesReturned < sizeof(int))
            {
                return "unknown";
            }

            var state = (WtsConnectState)Marshal.ReadInt32(buffer);
            return state switch
            {
                WtsConnectState.Active => "active",
                WtsConnectState.Connected => "connected",
                WtsConnectState.ConnectQuery => "connect_query",
                WtsConnectState.Shadow => "shadow",
                WtsConnectState.Disconnected => "disconnected",
                WtsConnectState.Idle => "idle",
                WtsConnectState.Listen => "listen",
                WtsConnectState.Reset => "reset",
                WtsConnectState.Down => "down",
                WtsConnectState.Init => "init",
                _ => "unknown"
            };
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string QuerySessionString(uint sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static int? QuerySessionInt(uint sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytesReturned))
        {
            return null;
        }

        try
        {
            return bytesReturned >= sizeof(int) ? Marshal.ReadInt32(buffer) : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static bool ReadPendingReboot()
    {
        return RegistryKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
            RegistryKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") ||
            RegistryValueExists(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
    }

    private static bool ReadWindowsUpdatePending()
    {
        return RegistryKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") ||
            RegistryKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Services\Pending") ||
            RegistryKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting");
    }

    private static bool ReadBluetoothEnabled()
    {
        try
        {
            using var controller = new ServiceController("bthserv");
            if (controller.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
            {
                return false;
            }

            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var devices = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
            return devices?.GetSubKeyNames().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<EventLogErrorInfo> ReadRecentEventLogErrors(TimeSpan window)
    {
        var since = DateTime.UtcNow - window;
        return ReadRecentLogErrors("System", since)
            .Concat(ReadRecentLogErrors("Application", since))
            .OrderByDescending(error => error.CreatedAt)
            .Take(20)
            .ToList();
    }

    private static IReadOnlyList<EventLogErrorInfo> ReadRecentLogErrors(string logName, DateTime sinceUtc)
    {
        try
        {
            var query = new EventLogQuery(
                logName,
                PathType.LogName,
                "*[System[(Level=1 or Level=2)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            var errors = new List<EventLogErrorInfo>();
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (record.TimeCreated is null || record.TimeCreated.Value.ToUniversalTime() < sinceUtc)
                    {
                        break;
                    }

                    errors.Add(new EventLogErrorInfo(
                        logName,
                        record.ProviderName ?? string.Empty,
                        record.Id,
                        record.LevelDisplayName ?? string.Empty,
                        record.TimeCreated.Value));
                }
            }

            return errors;
        }
        catch
        {
            return [];
        }
    }

    private static ShutdownInfo ReadLastShutdownInfo()
    {
        try
        {
            var query = new EventLogQuery(
                "System",
                PathType.LogName,
                "*[System[(EventID=1074 or EventID=6008 or EventID=41)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record is null)
            {
                return new ShutdownInfo(string.Empty, string.Empty, null, 0, string.Empty);
            }

            var message = string.Empty;
            try
            {
                message = record.FormatDescription() ?? string.Empty;
            }
            catch
            {
                // Some localized event messages cannot be formatted if the provider resources are unavailable.
            }

            var reason = record.Id switch
            {
                1074 => "planned",
                6008 => "unexpected",
                41 => "kernel_power",
                _ => "unknown"
            };

            var created = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown time";
            var summary = LimitState(string.IsNullOrWhiteSpace(message)
                ? $"{reason} at {created}"
                : $"{reason} at {created}: {message}");
            return new ShutdownInfo(summary, reason, record.TimeCreated, record.Id, LimitState(message));
        }
        catch
        {
            return new ShutdownInfo(string.Empty, string.Empty, null, 0, string.Empty);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> BuildAttributes(
        IReadOnlyList<NetworkAddressInfo> networkAddresses,
        IReadOnlyList<DisplayInfo> displays,
        IReadOnlyList<EventLogErrorInfo> recentErrors,
        ShutdownInfo lastShutdown)
    {
        return new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["network_address"] = new Dictionary<string, object?>
            {
                ["addresses"] = networkAddresses
            },
            ["active_display"] = new Dictionary<string, object?>
            {
                ["displays"] = displays
            },
            ["event_log_errors_recent"] = new Dictionary<string, object?>
            {
                ["window_minutes"] = 60,
                ["events"] = recentErrors
            },
            ["last_shutdown_reason"] = new Dictionary<string, object?>
            {
                ["reason"] = lastShutdown.Reason,
                ["event_id"] = lastShutdown.EventId,
                ["created_at"] = lastShutdown.CreatedAt,
                ["message"] = lastShutdown.Message
            }
        };
    }

    private static IReadOnlyList<CustomSensorState> ReadCustomSensors(IReadOnlyList<CustomSensorDefinition> sensors, bool serviceRole)
    {
        return sensors
            .Where(sensor => sensor.Enabled && (serviceRole ? sensor.Service : sensor.TrayApp))
            .Select(ReadCustomSensor)
            .ToList();
    }

    private static CustomSensorState ReadCustomSensor(CustomSensorDefinition sensor)
    {
        try
        {
            if (sensor.IsProcessRunning)
            {
                var processName = Path.GetFileNameWithoutExtension(sensor.Parameter.Trim());
                return new CustomSensorState(sensor.Id, Process.GetProcessesByName(processName).Length > 0);
            }

            if (sensor.IsServiceStatus)
            {
                using var controller = new ServiceController(sensor.Parameter.Trim());
                return new CustomSensorState(sensor.Id, controller.Status.ToString().ToLowerInvariant());
            }

            if (sensor.IsDiskFree)
            {
                var root = NormalizeDriveRoot(sensor.Parameter);
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return new CustomSensorState(sensor.Id, null);
                }

                return new CustomSensorState(sensor.Id, Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 1));
            }
        }
        catch
        {
            return new CustomSensorState(sensor.Id, null);
        }

        return new CustomSensorState(sensor.Id, null);
    }

    private static string NormalizeDriveRoot(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return $"{trimmed}:\\";
        }

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return $"{trimmed}\\";
        }

        return trimmed;
    }

    private static bool RegistryKeyExists(RegistryHive hive, string path)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool RegistryValueExists(RegistryHive hive, string path, string valueName)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            return key?.GetValue(valueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string LimitState(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 255 ? trimmed : $"{trimmed[..252]}...";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server,
        uint sessionId,
        WtsInfoClass infoClass,
        out IntPtr buffer,
        out uint bytesReturned);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(
        IntPtr server,
        uint reserved,
        uint version,
        out IntPtr sessions,
        out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public ulong ToUInt64()
        {
            return ((ulong)HighDateTime << 32) | LowDateTime;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private enum WtsInfoClass
    {
        UserName = 5,
        DomainName = 7,
        ConnectState = 8,
        ClientProtocolType = 16
    }

    private enum WtsConnectState
    {
        Active = 0,
        Connected = 1,
        ConnectQuery = 2,
        Shadow = 3,
        Disconnected = 4,
        Idle = 5,
        Listen = 6,
        Reset = 7,
        Down = 8,
        Init = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public uint SessionId;
        public IntPtr WinStationName;
        public WtsConnectState State;
    }

    private const uint DesktopSwitchDesktop = 0x0100;
}

internal sealed record SystemMetricsMessage(
    [property: JsonPropertyName("cpu_usage")] double CpuUsage,
    [property: JsonPropertyName("memory_usage")] double MemoryUsage,
    [property: JsonPropertyName("memory_available_mb")] long MemoryAvailableMb,
    [property: JsonPropertyName("system_drive_free_percent")] double SystemDriveFreePercent,
    [property: JsonPropertyName("system_drive_free_gb")] double SystemDriveFreeGb,
    [property: JsonPropertyName("uptime_seconds")] long UptimeSeconds,
    [property: JsonPropertyName("active_window")] string? ActiveWindow,
    [property: JsonPropertyName("active_process")] string? ActiveProcess,
    [property: JsonPropertyName("foreground_app_title")] string? ForegroundAppTitle,
    [property: JsonPropertyName("volume")] int? Volume,
    [property: JsonPropertyName("muted")] bool? Muted,
    [property: JsonPropertyName("audio_output_device")] string? AudioOutputDevice,
    [property: JsonPropertyName("microphone_muted")] bool? MicrophoneMuted,
    [property: JsonPropertyName("battery_level")] int? BatteryLevel,
    [property: JsonPropertyName("power_status")] string PowerStatus,
    [property: JsonPropertyName("battery_time_remaining")] long? BatteryTimeRemaining,
    [property: JsonPropertyName("monitor_power_state")] string? MonitorPowerState,
    [property: JsonPropertyName("active_display")] string? ActiveDisplay,
    [property: JsonPropertyName("network_address")] string NetworkAddress,
    [property: JsonPropertyName("vpn_connected")] bool VpnConnected,
    [property: JsonPropertyName("wifi_ssid")] string WifiSsid,
    [property: JsonPropertyName("wifi_signal")] int? WifiSignal,
    [property: JsonPropertyName("idle_time_seconds")] long? IdleTimeSeconds,
    [property: JsonPropertyName("session_locked")] bool? SessionLocked,
    [property: JsonPropertyName("user_present")] bool? UserPresent,
    [property: JsonPropertyName("clipboard_text_available")] bool? ClipboardTextAvailable,
    [property: JsonPropertyName("session_state")] string SessionState,
    [property: JsonPropertyName("logged_in_user")] string LoggedInUser,
    [property: JsonPropertyName("logged_in_users")] int LoggedInUsers,
    [property: JsonPropertyName("rdp_sessions")] int RdpSessions,
    [property: JsonPropertyName("pending_reboot")] bool PendingReboot,
    [property: JsonPropertyName("windows_update_pending")] bool WindowsUpdatePending,
    [property: JsonPropertyName("bluetooth_enabled")] bool BluetoothEnabled,
    [property: JsonPropertyName("event_log_errors_recent")] int EventLogErrorsRecent,
    [property: JsonPropertyName("last_shutdown_reason")] string LastShutdownReason,
    [property: JsonPropertyName("boot_time")] DateTimeOffset BootTime,
    [property: JsonPropertyName("custom_sensors")] IReadOnlyList<CustomSensorState> CustomSensors,
    [property: JsonPropertyName("attributes")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Attributes,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

internal sealed record NetworkAddressInfo(
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("address")] string Address);

internal sealed record DisplayInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("primary")] bool Primary,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

internal sealed record EventLogErrorInfo(
    [property: JsonPropertyName("log")] string Log,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("event_id")] int EventId,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

internal sealed record ShutdownInfo(
    string Summary,
    string Reason,
    DateTime? CreatedAt,
    int EventId,
    string Message);
