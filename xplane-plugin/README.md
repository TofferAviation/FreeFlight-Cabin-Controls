# FreeFlight Cabin Bridge

This lightweight X-Plane 12 plugin converts aircraft-specific door and passenger-sign outputs into a stable interface consumed by FreeFlight Cabin Control:

- `freeflight/cabin/plugin_online`
- `freeflight/cabin/seatbelt_available`
- `freeflight/cabin/seatbelt_sign`
- `freeflight/cabin/door_l1_available`
- `freeflight/cabin/door_l1_ratio`
- `freeflight/cabin/door_l2_available`
- `freeflight/cabin/door_l2_ratio`

The plugin samples at 10 Hz, caches all SDK dataref handles, performs no file or network I/O in the flight loop, and re-resolves aircraft datarefs when the loaded aircraft changes. Its writable FreeFlight datarefs forward manual app requests to writable aircraft or standard simulator controls when available.

For the FlightFactor 777 v2, the bridge gives authoritative priority to `1-sim/anim/doorL1`, `1-sim/anim/doorL2`, and the actual `1-sim/anim/seatbeltLight` output. App commands use `1-sim/ckpt/passSignsSeatbeltsSwitch/anim` with FlightFactor's `0=OFF`, `1=AUTO`, and `2=ON` selector encoding. See `docs/FLIGHTFACTOR_777_DATAREFS.md` for the local verification record.

For the ToLiss A320 family, the bridge uses `AirbusFBW/PaxDoorModeArray[0]` for the front-left 1L door and index `[2]` for the rear-left 2L door. ToLiss door modes are decoded as `0=Closed`, `1=Auto`, and `2=Open`; writable app requests send `0` or `2`. The standard X-Plane seat-belt annunciator remains the authoritative illuminated-sign output, with the ToLiss cockpit animation available only as a lower-priority fallback. See `docs/TOLISS_A320_DATAREFS.md`.

Build with the current X-Plane SDK:

```powershell
cmake -S xplane-plugin -B artifacts/xplane-plugin-build -A x64 -DXPLANE_SDK_ROOT=C:\path\to\XPSDK\SDK
cmake --build artifacts/xplane-plugin-build --config Release
```

Install `win.xpl` at `X-Plane 12/Resources/plugins/FreeFlightCabinBridge/64/win.xpl`, then restart X-Plane.
