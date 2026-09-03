# Architecture baseline

## Components

### Cabin Core application

The Windows application owns configuration, cabin state, passenger simulation, content-pack selection, scheduling, diagnostics, and the operator interface.

The gate-operations view model is the shared presentation boundary for Overview, Gate Desk, Passenger Manifest, and Boarding Passes. It wraps the core passenger engine instead of copying passenger records per page. A passenger boarded from the Gate Desk is therefore placed in the same assigned seat used by Cabin, while SimBrief manifest rebuilds propagate to every gate page.

Boarding-pass identities and personal details are deterministic fictional preview data generated locally from a user-controlled seed. The current printer controls update a simulated print state only. A future Windows printer adapter must sit behind a separate service boundary so UI state and passenger logic do not depend on printer drivers.

### X-Plane bridge

The Windows application connects to X-Plane through its built-in loopback Web API. REST discovers the simulator version and session-scoped dataref IDs; WebSockets stream values at up to 10 Hz. The bundled FreeFlight Cabin Bridge plugin uses the native SDK to normalize aircraft-specific L1/L2 door and seat-belt outputs into stable `freeflight/cabin/*` datarefs. The app prioritizes these stable signals, then falls back to adaptive Web API discovery and manual cabin controls.

The native C++ plugin caches resolved dataref handles, samples at 10 Hz, and performs no file or network I/O in X-Plane's flight loop. Future simulator audio-bus playback or simulator-side screen rendering must remain outside this small cabin-state bridge unless separately profiled and verified.

### Aircraft adapter

Each supported aircraft receives a declarative `IAircraftCabinAdapter` containing verified datarefs, commands, capabilities, and cabin-layout mappings. Stable layout IDs distinguish the operational FlightFactor 777 v2, British Airways 777-200ER, and British Airways 777-300 passenger-coordinate profiles. The FlightFactor adapter prioritizes the verified custom L1/L2 animation outputs and actual illuminated passenger-sign output, while preserving standard fallbacks. Private cabin-panel mappings remain gated on an instrumented v0.5.0 discovery session. FlightFactor-specific behavior must not leak into the passenger engine.

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
