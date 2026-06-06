using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Runtime;

namespace HASS.Agent.Companion.SystemService;

internal static class CompanionServiceManager
{
    public const string ServiceName = AppIdentity.ServiceName;
    private const string DisplayName = AppIdentity.ServiceDisplayName;
    private const string Description = AppIdentity.ServiceDescription;

    public static bool TryHandleCommandLine(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("--install-service" or "--uninstall-service" or "--start-service" or "--stop-service"))
        {
            return false;
        }

        var result = ExecuteControlCommand(command);
        MessageBox.Show(
            result.Message,
            AppIdentity.ServiceDisplayName,
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        return true;
    }

    public static void RunElevated(string command, FileLog log)
    {
        try
        {
            var executable = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = command,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to start elevated service command '{command}': {ex.Message}");
            MessageBox.Show(ex.Message, AppIdentity.ServiceDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static string GetStatusText()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return $"Telepítve: igen{Environment.NewLine}Állapot: {controller.Status}{Environment.NewLine}Indítás: {GetStartModeText()}";
        }
        catch (InvalidOperationException)
        {
            return "Telepítve: nem";
        }
        catch (Exception ex)
        {
            return $"Service status hiba: {ex.Message}";
        }
    }

    private static ControlCommandResult ExecuteControlCommand(string command)
    {
        if (command == "--install-service" && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return new ControlCommandResult(false, $"A {AppIdentity.DisplayName} telepítéséhez Windows 10 2004 (build 19041) vagy újabb rendszer szükséges. Windows 11 ajánlott.");
        }

        return command switch
        {
            "--install-service" => Install(),
            "--uninstall-service" => Uninstall(),
            "--start-service" => RunSc("start", Quote(ServiceName)),
            "--stop-service" => RunSc("stop", Quote(ServiceName)),
            _ => new ControlCommandResult(false, $"Ismeretlen service parancs: {command}")
        };
    }

    private static ControlCommandResult Install()
    {
        var executable = Environment.ProcessPath ?? Application.ExecutablePath;
        var binaryPath = $"{Quote(executable)} --service";

        if (IsInstalled())
        {
            TryStopExistingService();

            var delete = RunSc("delete", Quote(ServiceName));
            if (!delete.Success)
            {
                return delete;
            }

            if (!WaitUntilNotInstalled(TimeSpan.FromSeconds(10)))
            {
                return new ControlCommandResult(false, "A regi service meg torles alatt all. Varj par masodpercet, majd probald ujra.");
            }
        }

        var create = RunSc(
            "create",
            $"{Quote(ServiceName)} binPath= {Quote(binaryPath)} start= auto DisplayName= {Quote(DisplayName)}");

        if (!create.Success)
        {
            return create;
        }

        _ = RunSc("description", $"{Quote(ServiceName)} {Quote(Description)}");
        _ = RunSc("failure", $"{Quote(ServiceName)} reset= 86400 actions= restart/60000/restart/60000/none/0");

        var start = RunSc("start", Quote(ServiceName));
        if (!start.Success)
        {
            return new ControlCommandResult(false, $"A service települt, de nem indult el.{Environment.NewLine}{start.Message}");
        }

        return new ControlCommandResult(true, $"A {AppIdentity.ServiceDisplayName} telepítve és elindítva.");
    }

    private static ControlCommandResult Uninstall()
    {
        if (!IsInstalled())
        {
            return new ControlCommandResult(true, "A service nincs telepítve.");
        }

        _ = RunSc("stop", Quote(ServiceName));
        Thread.Sleep(750);

        return RunSc("delete", Quote(ServiceName));
    }

    private static bool IsInstalled()
    {
        return ServiceController.GetServices().Any(service => service.ServiceName == ServiceName);
    }

    private static void TryStopExistingService()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                return;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        }
        catch
        {
            // The sc.exe delete call below will return a useful error if stopping failed.
        }
    }

    private static bool WaitUntilNotInstalled(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsInstalled())
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return !IsInstalled();
    }

    private static string GetStartModeText()
    {
        var query = RunSc("qc", Quote(ServiceName));
        if (!query.Success)
        {
            return "ismeretlen";
        }

        var line = query.Message
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase));

        return line?.Trim() ?? "ismeretlen";
    }

    private static ControlCommandResult RunSc(string command, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"{command} {arguments}",
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            });

            if (process is null)
            {
                return new ControlCommandResult(false, "Nem sikerült elindítani az sc.exe-t.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var message = string.Join(
                Environment.NewLine,
                new[] { output.Trim(), error.Trim() }.Where(item => !string.IsNullOrWhiteSpace(item)));

            return new ControlCommandResult(process.ExitCode == 0, string.IsNullOrWhiteSpace(message) ? "OK" : message);
        }
        catch (Exception ex)
        {
            return new ControlCommandResult(false, ex.Message);
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record ControlCommandResult(bool Success, string Message);
}
