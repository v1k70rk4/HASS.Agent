# HASS.Agent .NET10 Technical README

Modern .NET 10 Windows client for the HASS.Agent Home Assistant integration.

This project is intentionally small. The first milestone supports the integration's Local API mode:

- `GET /info`
- `POST /notify`
- Windows tray lifetime
- local JSON configuration

MQTT discovery, notification actions, sensors, commands, and media player support can be layered on top without carrying over the legacy client surface area.

## Build

Install the .NET 10 SDK, then open PowerShell in the repository root and publish:

```powershell
dotnet publish .\src\HASS.Agent.NET10\HASS.Agent.NET10.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable is written to:

```text
src\HASS.Agent.NET10\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\HASS.Agent.NET10.exe
```

## Configuration

On first start the app creates:

```text
C:\ProgramData\HASS.Agent.NET10\settings.json
```

Default settings listen on all network interfaces, so Home Assistant can reach the Windows machine over the LAN:

```json
{
  "deviceName": "WINDOWS-PC",
  "serialNumber": "generated-on-first-run",
  "bindHost": "0.0.0.0",
  "port": 5115,
  "showStartupNotification": false,
  "manufacturer": "v1k70rk4",
  "model": "HASS.Agent .NET10",
  "softwareVersion": "10.0.0",
  "mqttEnabled": false,
  "mqttHost": "homeassistant.local",
  "mqttPort": 1883,
  "mqttUsername": "",
  "mqttPassword": "",
  "mqttUseTls": false,
  "mqttRetainDiscovery": true,
  "mqttNotificationsEnabled": true,
  "mqttMediaPlayerEnabled": true,
  "mqttButtonsEnabled": true,
  "mqttSystemSensorsEnabled": true,
  "systemSensorsIntervalSeconds": 15
}
```

The current Local API mode is intentionally compatible with the integration and does not add authentication yet. Restrict TCP port `5115` to your local network with Windows Firewall.

## Home Assistant

In the HASS.Agent integration, choose Local API setup:

- Host: the Windows machine LAN IP address, for example `192.168.1.42`
- Port: `5115`
- SSL: disabled

The integration will call `/info` during setup and `/notify` when sending notifications.

## Action Notifications

Use the integration-specific action when you want buttons:

```yaml
action: hass_agent.send_notification
target:
  entity_id: notify.rv_note_notifications
data:
  title: Home Assistant
  message: Action teszt
  data:
    actions:
      - action: lights_on
        title: Lampa be
      - action: lights_off
        title: Lampa ki
```

Button presses are published to:

```text
hass.agent/notifications/{deviceName}/actions
```

Payload:

```json
{
  "device_name": "RV-NOTE",
  "action": "lights_on",
  "created_at": "2026-06-06T00:00:00+00:00",
  "input": null
}
```

Example automation using the event entity:

```yaml
alias: HASS.Agent notification action
triggers:
  - trigger: state
    entity_id: event.rv_note_notification_actions
conditions:
  - condition: template
    value_template: "{{ trigger.to_state.attributes.event_type == 'action' }}"
actions:
  - choose:
      - conditions:
          - condition: template
            value_template: "{{ trigger.to_state.attributes.action == 'lights_on' }}"
        sequence:
          - action: light.turn_on
            target:
              entity_id: light.nappali
      - conditions:
          - condition: template
            value_template: "{{ trigger.to_state.attributes.action == 'lights_off' }}"
        sequence:
          - action: light.turn_off
            target:
              entity_id: light.nappali
mode: single
```

## MQTT Discovery

MQTT mode lets Home Assistant discover the device automatically and enables the media player entity.

Open the tray icon menu and choose **MQTT beállítások**. The password is stored with Windows DPAPI protection for the current Windows user, with the generated serial number used as additional entropy.

Manual settings still live here:

```text
C:\ProgramData\HASS.Agent.NET10\settings.json
```

Example:

```json
{
  "mqttEnabled": true,
  "mqttHost": "192.168.1.10",
  "mqttPort": 1883,
  "mqttUsername": "mqtt-user",
  "mqttPasswordProtected": "encrypted-by-windows",
  "mqttUseTls": false,
  "mqttNotificationsEnabled": true,
  "mqttMediaPlayerEnabled": true,
  "mqttButtonsEnabled": true,
  "mqttSystemSensorsEnabled": true,
  "systemSensorsIntervalSeconds": 15
}
```

Changes saved through the UI restart the MQTT connection automatically.

The app publishes:

```text
hass.agent/devices/{deviceName}
hass.agent/notifications/{deviceName}/actions
hass.agent/media_player/{deviceName}/state
hass.agent/sensors/{deviceName}/state
```

The app subscribes to:

```text
hass.agent/notifications/{deviceName}
hass.agent/media_player/{deviceName}/cmd
hass.agent/buttons/{deviceName}/cmd
```

The media player uses Windows global media sessions for play/pause/next/previous/seek metadata and the default Windows audio endpoint for volume/mute.

## Buttons

When MQTT buttons are enabled, Home Assistant can send predefined command payloads to:

```text
hass.agent/buttons/{deviceName}/cmd
```

Current buttons:

- lock
- sleep
- monitor_off
- volume_up
- volume_down
- toggle_mute
- shutdown
- restart
- restart_cancel

The `shutdown` and `restart` button entities use a 60 second delay so the `restart_cancel` button can stop an accidental press. Scripts and automations can call the integration service directly with any delay:

```yaml
action: hass_agent.execute_command
data:
  device_name: RV-NOTE
  command: restart
  force: true
  time: 30
  comment: Home Assistantból újraindítva
```

Cancel a pending shutdown or restart:

```yaml
action: hass_agent.execute_command
data:
  device_name: RV-NOTE
  restart_cancel: true
```

The comment is passed to Windows as `shutdown.exe /c "..."`.

## System Sensors

When MQTT system sensors are enabled, the app publishes Windows 11 system metrics every `systemSensorsIntervalSeconds` seconds:

```text
hass.agent/sensors/{deviceName}/state
```

Current metrics:

- CPU usage
- memory usage
- available memory
- system drive free percentage
- system drive free space
- uptime
- active window
- active process
- volume
- muted
- battery level
- power status
- monitor power state
- LAN IP

## Windows Service

The same executable can run either as the tray app or as a Windows service.

Normal launch starts the tray app. Service launch uses:

```powershell
.\HASS.Agent.NET10.exe --service
```

Use the tray menu **System service** to install, start, stop, or uninstall the service. These actions request elevation through UAC.

Use the tray menu **Kepessegek / szerepkorok** to choose which role handles each feature or command. The initial matrix is intentionally conservative:

- notifications: tray app
- media player: tray app
- system sensors: tray app
- shutdown/restart/cancel: service and tray app
- interactive session commands like lock, monitor off and volume: tray app

Settings and logs are stored in the shared machine location:

```text
C:\ProgramData\HASS.Agent.NET10
```

On first launch after upgrading, an existing settings file is copied from `C:\ProgramData\HASS.Agent.Companion` or `%AppData%\HASS.Agent.Companion` if the new ProgramData settings file does not exist yet. MQTT passwords are re-protected with machine-scope DPAPI so the service can read them.

The service currently owns system-safe MQTT commands that should work without a logged-in user:

- shutdown
- restart
- restart_cancel

When the service is online, Home Assistant routes those commands to:

```text
hass.agent/system/{deviceName}/cmd
```

The service publishes its retained online state to:

```text
hass.agent/system/{deviceName}/state
```

If the service is not online, Home Assistant falls back to the tray app command topic.

## Windows Firewall

The app listens on TCP port `5115`. Allow Home Assistant to reach it from your local network:

```powershell
New-NetFirewallRule `
  -DisplayName "HASS.Agent .NET10 Local API" `
  -Direction Inbound `
  -Action Allow `
  -Protocol TCP `
  -LocalPort 5115 `
  -Profile Private
```

Keep your Windows network profile set to Private for your home LAN. Avoid opening this port on Public networks.

## Minimal Development Setup

You do not need Visual Studio for this project.

Required:

- .NET 10 SDK for Windows x64
- PowerShell

Optional:

- Visual Studio Code
- C# Dev Kit extension

Useful commands:

```powershell
dotnet nuget list source
dotnet --list-sdks
dotnet build .\src\HASS.Agent.NET10\HASS.Agent.NET10.csproj -c Release
dotnet run --project .\src\HASS.Agent.NET10\HASS.Agent.NET10.csproj -c Release
```

If `dotnet nuget list source` says `No sources found`, add the official NuGet feed:

```powershell
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
```

After the app is running, test from the Windows machine:

```powershell
Invoke-RestMethod http://localhost:5115/info
```

Test from another machine on the LAN by replacing the IP:

```powershell
Invoke-RestMethod http://192.168.1.42:5115/info
```
