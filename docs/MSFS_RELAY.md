# Ember MSFS Relay

Microsoft Flight Simulator and SimConnect run on Windows. Ember MSFS Relay is a small
Windows companion that lets a pilot's Ember X-Plane client on macOS or Linux view the
MSFS flight telemetry from that Windows computer over the same private network.

It is not a cloud service and does not contact BAV, Supabase, Render, or another
third-party relay. It is deliberately a local, opt-in bridge.

## Start it on the Windows MSFS computer

Use the included **Start Ember MSFS Relay** shortcut, or run the following from the
`msfs-relay` folder in a Windows Ember installation:

```powershell
.\Ember.MsfsRelay.exe --allow-lan --port 52187
```

The relay refuses to open a network listener unless `--allow-lan` is supplied. Keep
its console open while it is in use. It displays local HTTPS addresses, a SHA-256
certificate fingerprint, and a six-digit one-time pairing code.

If Windows asks about firewall access, allow it only for **Private** networks that you
trust. Never make the relay publicly reachable and never forward its port from a
router.

## Pair from Ember on macOS or Linux

1. In the Ember X-Plane client, use **MSFS on a Windows PC**.
2. Enter the relay's HTTPS address, fingerprint, and current pairing code.
3. Choose **Pair MSFS relay**.

The client pins the supplied certificate fingerprint, so it will not silently connect
to a different server. The pairing code is accepted once per new Ember session. The
client discards its resulting access token on exit; the Windows relay holds its matching
token only in memory for at most eight hours, or until the relay closes. Ember stores
only the chosen relay address and certificate fingerprint to make the next connection
easier; it never saves the code or token.

## Current scope

The relay provides read-only aircraft identity, flight phase, altitude, on-ground
state, seat-belt status, and fresh-frame time. MSFS cabin controls remain in the
Windows Ember application for now. The relay does not record microphone, camera,
screen, simulator, or account data.
