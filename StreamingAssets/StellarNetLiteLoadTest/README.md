# StellarNetLiteLoadTest

Standalone load test tool for StellarNetLite.

## Current features

- Supports `kcp` and `tcp`
- Supports multiple rooms
- Supports multiple clients per room
- First client in each room creates the room
- Other clients join the same room
- Auto-completes the minimal `Ready` / `StartGame` flow needed by the room framework
- Simulates normal clients after room start: small-range wandering, pauses, and occasional action/chat
- Supports runtime room control: `addroom`, `removeroom`, `endroom`, `status`
- Runs until stopped when `--duration 0`

## Build

```powershell
dotnet build -c Release
```

## Example

```powershell
dotnet run -c Release -- --transport kcp --host 127.0.0.1 --port 7777 --rooms 5 --clients-per-room 20 --duration 0 --move-rate 8
```

## Parameters

- `--transport kcp|tcp`
- `--host 127.0.0.1`
- `--port 7777`
- `--rooms 5`
- `--clients-per-room 20`
- `--connect-rate 10`
- `--duration 0`
- `--move-rate 8`
- `--room-name LoadTestRoom`
- `--account-prefix bot`
- `--client-version 0.0.1`
- `--log-interval 5`

## Notes

- `duration = 0` means run until you stop it manually.
- `move-rate` is an upper bound while a bot is walking. Bots do not move continuously.
- Runtime commands can be typed into the tool process or sent from the Unity Editor window.
- Unity Editor window is available from `StellarNetLite/Load Test`.
