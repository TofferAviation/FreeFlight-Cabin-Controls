# FlightFactor 777 v2 cabin datarefs

These mappings were verified from the locally installed FlightFactor 777-(200ER)(300ER)(EE) v2 Ultimate aircraft files on 2026-09-03. They cover the cabin controls currently consumed by FreeFlight Cabin Control.

| Function | Dataref | Values | Use |
| --- | --- | --- | --- |
| L1 passenger door position | `1-sim/anim/doorL1` | `0.0` closed, transitional values while moving, `1.0` open | Authoritative read output |
| L2 passenger door position | `1-sim/anim/doorL2` | `0.0` closed, transitional values while moving, `1.0` open | Authoritative read output |
| Illuminated seat-belt sign | `1-sim/anim/seatbeltLight` | `0` off, `1` illuminated | Authoritative read output |
| Seat-belt selector | `1-sim/ckpt/passSignsSeatbeltsSwitch/anim` | `0` OFF, `1` AUTO, `2` ON | Selector read and write target |

## Evidence

- `objects/777/fuselage1.obj` drives the L1 and L2 door geometry from `doorL1` and `doorL2`, with animation keys spanning `0.0` to `1.0`.
- Cabin and seat object files use `seatbeltLight` directly for the visible passenger-seat sign illumination.
- `objects/777/ckpt/knobs.obj`, `data/vrFoMainDo.txt`, and both `data/soundZones_B772.txt` and `data/soundZones_B77W.txt` identify the selector and document `OFF(0)`, `AUTO(1)`, and `ON(2)`.
- Prior FreeFlight runtime discovery logs confirm that all four custom datarefs are registered by the aircraft plugin while the FlightFactor 777 is loaded.

The FreeFlight bridge publishes these aircraft-specific signals through stable `freeflight/cabin/*` datarefs. Standard X-Plane door and passenger-sign datarefs remain fallback mappings for other aircraft.
