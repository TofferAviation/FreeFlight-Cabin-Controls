# Architecture baseline

## Components

### Cabin Core application

The Windows application owns configuration, cabin state, passenger simulation, content-pack selection, scheduling, diagnostics, and the operator interface.

The gate-operations view model is the shared presentation boundary for Overview, Gate Desk, Passenger Manifest, and Boarding Passes. It wraps the core passenger engine instead of copying passenger records per page. A passenger boarded from the Gate Desk is therefore placed in the same assigned seat used by Cabin, while SimBrief manifest rebuilds propagate to every gate page.

Boarding-pass identities and personal details are deterministic fictional preview data generated locally from a user-controlled seed. The current printer controls update a simulated print state only. A future Windows printer adapter must sit behind a separate service boundary so UI state and passenger logic do not depend on printer drivers.

### X-Plane bridge plugin

The future native C++ plugin will own X-Plane SDK access, simulator dataref sampling, X-Plane audio-bus playback, and simulator-side screen rendering. It must remain lightweight and must not block X-Plane's main thread.

### Aircraft adapter

Each supported aircraft receives a declarative adapter containing verified datarefs, commands, capabilities, and cabin-layout mappings. Stable layout IDs distinguish the operational FlightFactor 777 v2, British Airways 777-200ER, and British Airways 777-300 passenger-coordinate profiles. FlightFactor-specific behavior must not leak into the generic core.

### Airline content pack

Airline presentation and media are data. A pack may declare branding, languages, aircraft compatibility, trigger metadata, and relative asset paths. A pack cannot contain executable extensions or load arbitrary code.

## Communication boundary

The first Windows implementation will use local named pipes. The protocol will be versioned independently and will exchange immutable telemetry snapshots and explicit commands. Disconnects must fail safe on both sides.

## Delivery order

1. Visually approved application shell.
2. Settings and content-pack foundation.
3. Native bridge handshake and live telemetry.
4. Flight-phase state machine.
5. X-Plane audio playback and announcement scheduler.
6. Cabin zones and passenger simulation.
7. Single-screen in-aircraft rendering feasibility test.
8. Scaled IFE and custom-cabin work after performance and licensing review.
