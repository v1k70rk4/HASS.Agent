# HASS.Agent .NET10 Technical README

Modern .NET 10 Windows client for the HASS.Agent Home Assistant integration.

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

Default settings listen on all network interfaces, so Home Assistant can reach the Windows machine over the LAN. An API key is auto-generated on first run.

```json
{
  "deviceName": "WINDOWS-PC",
  "serialNumber": "generated-on-first-run",
  "apiKey": "generated-on-first-run",
  "bindHost": "0.0.0.0",
  "port": 5115,
  "showStartupNotification": false,
  "autoStartOnLogin": false,
  "mqttEnabled": false,
  "mqttHost": "homeassistant.local",
  "mqttPort": 1883,
  "mqttUsername": "",
  "mqttPasswordProtected": "encrypted-by-windows",
  "mqttUseTls": false,
  "mqttRetainDiscovery": true,
  "haApiEnabled": false,
  "haApiUrl": "",
  "haApiTokenProtected": "encrypted-by-windows",
  "mqttNotificationsEnabled": true,
  "mqttMediaPlayerEnabled": true,
  "mqttButtonsEnabled": true,
  "mqttSystemSensorsEnabled": true,
  "fastSensorIntervalSeconds": 10,
  "normalSensorIntervalSeconds": 60,
  "hourlySensorIntervalSeconds": 3600
}
```

## Local HTTP API

The agent runs a small HTTP server on port `5115`. This is used by the Local HTTP API integration mode and for device info.

| Method | Path | Auth | Description |
|--------|------|:----:|-------------|
| `GET` | `/info` | | Device info and capabilities |
| `POST` | `/notify` | Bearer | Send a notification |

The `POST /notify` endpoint requires `Authorization: Bearer <api_key>`. The API key is shown on the General settings page and stored in `settings.json`.

`GET /info` is unauthenticated so the integration config flow can validate the connection during setup.

## Home Assistant Connection Modes

### MQTT (recommended)

Enable MQTT in the agent settings. The device is discovered automatically by the Home Assistant integration. All features work: notifications, media player, sensors, commands, update entity. Requires an MQTT broker on the local network.

### HA API (WebSocket)

Enable HA API in the agent settings. The agent connects directly to Home Assistant's WebSocket API using a long-lived access token. Works remotely (e.g. via Nabu Casa) without an MQTT broker. HTTPS is required for remote access.

Can be used standalone or as automatic failover when MQTT is unavailable — when both are enabled, the agent uses MQTT as primary and switches to WebSocket when the broker is unreachable, then switches back when MQTT recovers.

Compared to MQTT:
- no retained discovery/state (sensor values are lost until the agent reconnects after a restart)
- no MQTT Last Will (no automatic offline detection)
- media thumbnails are sent as base64 events (~33% larger)

The connection test also reads the installed `hass_agent` integration manifest through Home Assistant's WebSocket API and warns when the integration is missing, cannot be checked, or is older than the minimum supported version.

When using HA API, the agent publishes these Home Assistant event bus events:

```text
hass_agent_device_update
hass_agent_sensor_update
hass_agent_media_update
hass_agent_media_thumbnail
hass_agent_notification_action
```

The integration sends commands back via `hass_agent_command` events:

```json
{
  "serial_number": "agent-serial",
  "command_type": "notification | media_command | button_command",
  "payload": {}
}
```

All events and commands are targeted by `serial_number`, so renaming the device does not break routing.

### Local HTTP API

In the HASS.Agent integration, choose Local API setup:

- Host: the Windows machine LAN IP address, for example `192.168.1.42`
- Port: `5115`
- SSL: disabled
- API key: copy from the agent General settings page

Only notifications are supported. Use MQTT or HA API for full functionality.

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
hass.agent/notifications/{serialNumber}/actions
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

MQTT mode lets Home Assistant discover the device automatically and enables the media player, sensors, commands, and update entities.

Open the tray icon and go to the MQTT settings page. The password is stored with Windows DPAPI protection at machine scope, so both the tray app and the service can read it.

The app publishes:

```text
hass.agent/devices/{serialNumber}
hass.agent/notifications/{serialNumber}/actions
hass.agent/media_player/{serialNumber}/state
hass.agent/sensors/{serialNumber}/state
hass.agent/update/{serialNumber}/state
```

The app subscribes to:

```text
hass.agent/notifications/{serialNumber}
hass.agent/media_player/{serialNumber}/cmd
hass.agent/buttons/{serialNumber}/cmd
```

## Buttons

When MQTT buttons are enabled, Home Assistant can send predefined command payloads to:

```text
hass.agent/buttons/{serialNumber}/cmd
```

Current buttons: `lock`, `sleep`, `monitor_off`, `volume_up`, `volume_down`, `toggle_mute`, `shutdown`, `restart`, `restart_cancel`.

The `shutdown` and `restart` button entities use a 60 second delay so the `restart_cancel` button can stop an accidental press. Scripts and automations can call the integration service directly with any delay:

```yaml
action: hass_agent.execute_command
data:
  device_name: RV-NOTE
  command: restart
  force: true
  time: 30
  comment: Home Assistantbol ujrainditva
```

Cancel a pending shutdown or restart:

```yaml
action: hass_agent.execute_command
data:
  device_name: RV-NOTE
  restart_cancel: true
```

## Windows Service

The same executable can run either as the tray app or as a Windows service.

Normal launch starts the tray app. Service launch uses:

```powershell
.\HASS.Agent.NET10.exe --service
```

Use the Service page in the settings UI to install, start, stop, or uninstall the service. These actions request elevation through UAC.

The service publishes its retained online state to:

```text
hass.agent/system/{serialNumber}/state
```

When the service is online, Home Assistant routes shutdown/restart/restart_cancel commands to:

```text
hass.agent/system/{serialNumber}/cmd
```

If the service is not online, Home Assistant falls back to the tray app command topic.

Settings and logs are stored in the shared machine location:

```text
C:\ProgramData\HASS.Agent.NET10
```
