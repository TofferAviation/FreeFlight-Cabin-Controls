# Passenger Flow preview

Passenger Flow is an original FreeFlight cabin-operations view. It does not copy Passenger2 artwork, layouts, code, or interface assets.

## Current cabin behavior

- Provides operational coordinate profiles for the 311-position FlightFactor schematic, 272-position British Airways 777-200ER, and 256-position British Airways 777-300.
- Accepts a user-selected mapped-passenger count up to the selected layout's capacity, while preserving a larger authoritative booked count imported from SimBrief.
- Selects occupied positions from the entire cabin for partial loads, then assigns each fictional passenger a unique boarding-pass seat. Seats therefore do not fill as a rigid tail-to-nose sequence.
- Starts with L2 open and L1 closed so single-door routing is immediately testable.
- Routes every newly entering passenger through the only open door.
- With both doors open, the assigned cabin on the passenger's boarding ticket controls the entry: First uses L1 while all other cabins use L2.
- Opening both passenger doors increases the spawn and in-cabin flow limits, producing a faster boarding operation than either door alone.
- Uses separate upper and lower aisle lanes, selected from the assigned seat, so passengers move along the correct aisle before crossing into their row.
- Varies entry gaps and walking speeds, randomizes order within each called group, and slows nearby passengers in congested aisle sections to produce controlled boarding/deboarding disorder without breaking boarding-pass assignments.
- Holds new passengers when every door is closed, then resumes through whichever door opens.
- Keeps passengers already inside the aircraft moving to their seats if an entry door closes.
- Keeps moving passengers class-coloured, changes a passenger to orange while the seat is being occupied, and changes the marker to green only after the passenger is seated and secured.
- Supports an optional real-operations pace targeting a 30–45 minute full one-door boarding, plus 1×, 2×, and 4× accelerated previews, pause/resume, reset, progress, ETA, and recent activity.
- Calls and releases boarding groups in numeric order from 1 through 8. The live cabin header shows the group currently boarding, and the manifest sorts by group before passenger number.
- Turns the primary action into **Start Deboarding** after boarding completes. Passengers leave their assigned seats through the same two aisle lanes, First routes to L1 when both doors are open, and every other cabin routes to L2. Closing every door holds the operation until a door reopens.
- Generates a stable fictional profile for every preview passenger, including a name, age, nationality, purpose of travel, Executive Club tier, checked-bag count, assistance note, and booking reference.
- Provides a complete scrollable manifest with live status. Selecting a live passenger marker highlights only that passenger's destination seat; selecting a manifest row opens the extended passenger information.
- Can import the passenger count, flight number, origin, and destination from the user's latest generated SimBrief OFP using their numeric Pilot ID. No SimBrief password is requested or stored.

## SimBrief boundary

The desktop application reads the latest generated OFP from SimBrief's documented fetcher with JSON output. The user enters the numeric Pilot ID shown in SimBrief Account Settings. A manual **Sync OFP** action is always available, and optional auto-sync runs when the Passenger Flow view model starts.

Only operational OFP data is consumed. SimBrief does not provide the real-world passenger identities used here; the manifest profiles remain deterministic fictional preview records. The current 311-position FlightFactor profile maps the 302-passenger test OFP completely and leaves nine positions empty. An imported passenger count above 311 still remains the authoritative **Booked** value, with the difference clearly reported as unmapped until a compatible cabin layout is selected.

## Cabin layout profiles

Aircraft Settings and the Passenger Flow Live Cabin card expose three stable profile IDs: `flightfactor.777v2`, `british-airways.777-200er`, and `british-airways.777-300`. Every profile drives its own operational seat coordinates, cabin classes, boarding groups, door positions, and aisle routing. Original FreeFlight-rendered BA 777-200ER and 777-300 schematics are compiled WPF resources, so clean installations do not depend on a private content pack or loose local files.

Changing layouts safely starts a fresh manifest against the selected capacity while preserving the booked SimBrief count, current manual door choices, and stable profile selection. A count above the chosen capacity is reported as unmapped rather than silently discarded.

The user can persist a manual profile selection. The X-Plane bridge now detects and reports aircraft identity, but it deliberately does not guess a cabin profile from an ambiguous description. A verified aircraft adapter can later map exact variant metadata to a stable profile ID; unknown aircraft continue to use the explicit user choice.

## X-Plane bridge boundary

The boarding engine is implemented in `FreeFlight.CabinControl.Core` and has no WPF or X-Plane dependency. Each layout owns its real doorway center and threshold, so passengers stay on the galley center line until they reach the assigned aisle. Manual L1/L2 changes request a non-blocking simulator write; confirmed live telemetry is applied through a separate path so it cannot echo back into X-Plane. When X-Plane exposes `sim/flightmodel2/misc/door_open_ratio`, indexes 0 and 1 remain safe standard fallbacks while changing aircraft-specific signals receive priority.

A FlightFactor adapter still needs verified mappings for custom door identifiers and exact aircraft/variant identifiers. Passenger count can be supplied by the existing manual manifest control or the current SimBrief latest-OFP integration. A future vAMSYS integration can feed the same configuration boundary.

Actual 3D passenger objects are a later rendering layer. The current top-down passenger positions and assigned seats already provide the state needed to drive that layer without replacing the manifest or routing system.
