# ToLiss A320 cabin datarefs

FreeFlight Cabin Control v0.4.8 adds an aircraft-specific ToLiss A320-family adapter to both the app's X-Plane Web API fallback and the native FreeFlight Cabin Bridge plugin.

## Aircraft recognition

The adapter requires an A320/A20N identity together with `ToLiss` in the aircraft description or relative ACF path. This prevents it from claiming unrelated A320 implementations.

## Passenger doors

ToLiss exposes passenger door modes through the integer/float array `AirbusFBW/PaxDoorModeArray`:

- index 0: front-left door 1L, exposed to FreeFlight as L1
- index 2: rear-left door 2L, exposed to FreeFlight as L2
- value 0: closed
- value 1: automatic/armed mode, not physically open
- value 2: open

The plugin samples these positions at 10 Hz and publishes normalized 0/1 ratios. Manual app commands write 0 for closed or 2 for open. Standard `sim/flightmodel2/misc/door_open_ratio` values remain fallback signals.

## Passenger signs

The actual standard X-Plane illuminated output `sim/cockpit2/annunciators/fasten_seatbelt` remains authoritative. `sim/cockpit2/switches/fasten_seat_belts` is used as a control/fallback, while `ckpt/oh/seatbelts/anim` is read-only and deliberately lower priority because a selector position does not always equal the illuminated cabin sign.

## Verification basis

The mappings were checked against the installed ToLiss A320 `smartcopilot.cfg` and cockpit object animation references. ToLiss documents the `PaxDoorModeArray` encoding as 0 closed, 1 automatic, and 2 open in its official aircraft changelog.
