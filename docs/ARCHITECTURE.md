# Architecture baseline

## Components

### Cabin Core application

The Windows application owns configuration, cabin state, passenger simulation, content-pack selection, scheduling, diagnostics, and the operator interface.

The gate-operations view model is the shared presentation boundary for Overview, Gate Desk, Passenger Manifest, and Boarding Passes. It wraps the core passenger engine instead of copying passenger records per page. A passenger boarded from the Gate Desk is therefore placed in the same assigned seat used by Cabin, while SimBrief manifest rebuilds propagate to every gate page.

Boarding-pass identities and personal details are deterministic fictional preview data generated locally from a user-controlled seed. The current printer controls update a simulated print state only. A future Windows printer adapter must sit behind a separate service boundary so UI state and passenger logic do not depend on printer drivers.

### X-Plane bridge

The Windows application connects directly to X-Plane 12.1.1 or newer through X-Plane's built-in loopback Web API. REST discovers the simulator version and session-scoped dataref IDs; WebSockets streams the selected values at up to 10 Hz. The bridge reconnects automatically, treats a missing simulator as a normal offline state, and never blocks the WPF thread.

A later native C++ plugin is only needed for capabilities the Web API does not provide, such as simulator audio-bus playback or simulator-side screen rendering. If added, it must remain lightweight and must not block X-Plane's main thread.

### Aircraft adapter

Each supported aircraft receives a declarative `IAircraftCabinAdapter` containing verified datarefs, commands, capabilities, and cabin-layout mappings. Stable layout IDs distinguish the operational FlightFactor 777 v2, British Airways 777-200ER, and British Airways 777-300 passenger-coordinate profiles. The v0.4.2 FlightFactor identity adapter carries only standard verified fallbacks; private cabin-panel mappings remain gated on an instrumented v0.5.0 discovery session. FlightFactor-specific behavior must not leak into the passenger engine.

### Airline content pack

Airline presentation and media are data. A pack may declare branding, languages, aircraft compatibility, trigger metadata, and relative asset paths. A pack cannot contain executable extensions or load arbitrary code.

## Communication boundary

The first Windows implementation uses X-Plane's HTTP/WebSocket server on `127.0.0.1`, default port `8086`. X-Plane dataref IDs are rediscovered on every simulator session because they are not stable across restarts. The application converts updates into immutable `CabinTelemetrySnapshot` records before any view model receives them. Disconnects fail safe to manual cabin controls.

## Delivery order

1. Visually approved application shell.
2. Settings and content-pack foundation.
3. Built-in Web API handshake and live telemetry. ✓
4. Flight-phase state machine. ✓
5. X-Plane audio playback and announcement scheduler.
6. Cabin zones and passenger simulation.
7. Single-screen in-aircraft rendering feasibility test.
8. Scaled IFE and custom-cabin work after performance and licensing review.
