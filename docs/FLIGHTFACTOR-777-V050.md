# FlightFactor 777 cabin-panel preparation for v0.5.0

v0.4.2 introduces the aircraft-neutral `IAircraftCabinAdapter` boundary and a
FlightFactor 777 v2 identity profile. Door and seat-belt semantics are already
represented through this boundary. The remaining cabin-panel features stay
disabled until their FlightFactor datarefs and commands have been verified in a
running, licensed aircraft.

## Discovery session

1. Select the X-Plane executable or root folder in FreeFlight Settings.
2. Load the FlightFactor 777 v2 and open FreeFlight Diagnostics.
3. Exercise one control at a time: L1, L2, seat-belt sign, safety video,
   boarding music, lighting, temperature, PA and cabin display page.
4. Compare the before/after values using X-Plane DataRefEditor or DataRefTool and
   retain the relevant `Log.txt`/FreeFlight diagnostics output.
5. Record whether each signal is readable, writable, command-driven, scalar or
   array-based, including its array index and value range.

## Acceptance requirements

- No unverified FlightFactor private dataref is shipped as a hard-coded guess.
- Every write is attempted off X-Plane's main thread and fails back to the local
  FreeFlight control without blocking the simulator.
- The adapter is selected using ICAO, aircraft description and active ACF path.
- All mappings are rediscovered by name each X-Plane session; numeric Web API
  IDs are never persisted.
- Cabin-panel outputs are opt-in and cannot operate cargo/service doors when L1
  or L2 is requested.
- A simulator-free fake adapter covers reads, writes, rejected writes and
  disconnect recovery before v0.5.0 is released.

## Requested evidence

For the v0.5.0 mapping pass, provide the FlightFactor 777 v2 `.acf` path and a
dataref/command capture from the discovery session. The aircraft files
themselves do not need to be redistributed with FreeFlight.
