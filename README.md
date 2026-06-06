# HASS.Agent .NET10

![Windows](https://img.shields.io/badge/Windows-10%202004%2B%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Version](https://img.shields.io/badge/version-10.0.0-brightgreen)
![Home Assistant](https://img.shields.io/badge/Home%20Assistant-MQTT%20%2B%20Custom%20Integration-41BDF5?logo=homeassistant&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-blue)

<img src="images/hass_agent_companion_modern_icon.png" align="right" width="128" alt="HASS.Agent .NET10 icon">

A modern Windows companion app for Home Assistant.

This fork refreshes the classic HASS.Agent idea into **HASS.Agent .NET10**, a lightweight .NET 10 client built for current Windows desktops. The original client was a .NET 6-era application; this version focuses on a smaller, cleaner runtime, MQTT-first Home Assistant integration, Windows 11-friendly UX, and a split tray app/system service model.

It is designed for Windows PCs you want to observe and control from Home Assistant: media playback, notifications, sensors, shutdown/restart, command buttons, and rich machine state.

The modern .NET10 line starts at **version 10.0.0**. The pre-.NET10 client remains available on the `legacy` branch for users who do not want to migrate yet.

## What Changed

- Rebuilt the companion client as a modern `.NET 10` Windows app.
- Added a Windows tray app for interactive user-session features.
- Added a Windows service for system-level features that should work without a logged-in user.
- Renamed the modern client to **HASS.Agent .NET10** so it is clearly separate from the legacy app.
- Moved shared settings/logs to `C:\ProgramData\HASS.Agent.NET10`.
- Added MQTT discovery and dynamic Home Assistant entities.
- Added a role matrix so features can be handled by `Service`, `Tray app`, or both.
- Added a configurable sensor catalog and custom sensors.
- Added service-aware shutdown/restart/restart-cancel support.
- Added a new Windows 11-style icon.

## Requirements

- Windows 10 version 2004 / build 19041 or newer
- Windows 11 recommended
- x64 Windows
- Home Assistant with MQTT
- The companion Home Assistant integration:
  [v1k70rk4/HASS.Agent-Integration](https://github.com/v1k70rk4/HASS.Agent-Integration)

Windows versions older than Windows 10 2004 are intentionally blocked. The app targets `net10.0-windows10.0.19041.0` and uses modern Windows APIs for notifications, media sessions, services, sensors, and desktop state.

If you download a published self-contained build, you do **not** need to install the .NET runtime separately. If you want to build from source, install the **.NET 10 SDK**.

## Features

### Notifications

Receive Home Assistant notifications on Windows.

Supports actionable notifications: buttons in the popup can publish an action event back to Home Assistant, so automations can react to user choices.

### Media Player

Expose the active Windows media session to Home Assistant:

- current title/artist/app
- play/pause
- next/previous
- seek
- volume and mute

### System Commands

Control the PC from Home Assistant:

- lock
- sleep
- monitor off
- volume up/down
- toggle mute
- shutdown
- restart
- cancel pending shutdown/restart

Shutdown and restart support:

- delay time
- force mode
- translated comments
- script/service calls through Home Assistant

### Windows Service

The same executable can run as a tray app or as a Windows service.

The service is useful for commands and sensors that should work even when nobody is logged in:

- shutdown
- restart
- restart cancel
- system metrics
- selected service-safe sensors
- custom process/service/disk sensors

The tray app handles interactive user-session features:

- notifications
- media player
- active window/process
- clipboard state
- monitor/session details
- audio endpoint details

### Sensor Catalog

The sensor UI has two sections:

- `Basic sensors`: built-in sensors without parameters, selectable per role.
- `Custom sensors`: parameterized sensors you can add multiple times.

Built-in sensors include:

- CPU usage
- memory usage and free memory
- system drive free space
- uptime and boot time
- active window/process/application title
- LAN IP with adapter attributes
- monitor state and display attributes
- audio output device
- volume and mute
- microphone mute
- battery level and remaining time
- power status
- idle time
- session locked/user present/session state
- logged-in user and logged-in user count
- RDP session count
- VPN connected
- Wi-Fi SSID and signal
- Bluetooth enabled
- pending reboot
- Windows Update pending
- recent Event Log errors
- last shutdown reason

Custom sensors currently support:

- `process_running`
- `service_status`
- `disk_free`

### Sensor Attributes

Some sensors keep a simple state but expose richer details as attributes:

- `LAN IP`: all active IPv4 addresses by adapter
- `Active displays`: display name, primary flag, size, and position
- `Recent Event Log errors`: recent event details
- `Last shutdown reason`: reason, event id, timestamp, message

## Home Assistant Integration

Install the matching custom integration:

[v1k70rk4/HASS.Agent-Integration](https://github.com/v1k70rk4/HASS.Agent-Integration)

The integration listens to the MQTT topics published by the Windows app and creates Home Assistant entities dynamically.

Main MQTT topics:

```text
hass.agent/devices/{deviceName}
hass.agent/system/{deviceName}/state
hass.agent/sensors/{deviceName}/state
hass.agent/media_player/{deviceName}/state
hass.agent/notifications/{deviceName}/actions
```

Command topics:

```text
hass.agent/buttons/{deviceName}/cmd
hass.agent/system/{deviceName}/cmd
```

## Quick Start

1. Install the Home Assistant integration:
   [v1k70rk4/HASS.Agent-Integration](https://github.com/v1k70rk4/HASS.Agent-Integration)
2. Download a published self-contained build or build it from source.
3. Start `HASS.Agent.NET10.exe`.
4. Open the tray menu.
5. Configure MQTT in `MQTT beállítások`.
6. Choose feature ownership in `Kepessegek / szerepkorok`.
7. Configure sensors in `Szenzorok`.
8. Install/update the service from `System service -> Telepítés / frissítés`.
9. Restart or reload the Home Assistant integration.

## Build

Install the .NET 10 SDK, then publish:

```powershell
dotnet publish .\src\HASS.Agent.NET10\HASS.Agent.NET10.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output executable is named `HASS.Agent.NET10.exe`.

## GitHub Actions

This repository includes a Windows GitHub Actions workflow for the .NET 10 client:

- `dotnet restore`
- `dotnet build -c Release`
- self-contained `win-x64` publish
- downloadable build artifact from manual workflow runs

The technical developer notes live here:

[src/HASS.Agent.NET10/README.md](src/HASS.Agent.NET10/README.md)

## Status

This is a modernization branch, not the original LAB02 release line.

The goal is a focused Windows/Home Assistant companion that keeps the useful HASS.Agent ideas, drops legacy weight, and moves the client toward a cleaner .NET 10 + MQTT + service/tray architecture. The old client line is kept separately on the `legacy` branch.

## License

MIT
