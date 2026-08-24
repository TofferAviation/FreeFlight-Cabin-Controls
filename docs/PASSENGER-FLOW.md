# Passenger Flow preview

Passenger Flow is an original FreeFlight cabin-operations view. It does not copy Passenger2 artwork, layouts, code, or interface assets.

## Current simulator-free behavior

- Uses a deterministic 311-position FF777 profile whose marker coordinates cover every individual position represented by the current cabin schematic: 36 First, 35 Business in its drawn 2–3–2 blocks, and 240 Economy in its drawn 3–4–3 blocks.
- Accepts a user-selected mapped-passenger count from 1 to 311, while preserving a larger authoritative booked count imported from SimBrief.
- Starts with L2 open and L1 closed so single-door routing is immediately testable.
- Routes every newly entering passenger through the only open door.
- With both doors open, the assigned cabin on the passenger's boarding ticket controls the entry: First uses L1 while Business and Economy use L2.
- Opening both passenger doors increases the spawn and in-cabin flow limits, producing a faster boarding operation than either door alone.
- Uses separate upper and lower aisle lanes, selected from the assigned seat, so passengers move along an aisle before crossing into their row.
- Holds new passengers when every door is closed, then resumes through whichever door opens.
- Keeps passengers already inside the aircraft moving to their seats if an entry door closes.
- Keeps moving passengers class-coloured, changes a passenger to orange while the seat is being occupied, and changes the marker to green only after the passenger is seated and secured.
- Supports an optional real-operations pace targeting a 30–45 minute full one-door boarding, plus 1×, 2×, and 4× accelerated previews, pause/resume, reset, progress, ETA, and recent activity.
- Calls and releases boarding groups in numeric order from 1 through 8. The live cabin header shows the group currently boarding, and the manifest sorts by group before passenger number.
- Turns the primary action into **Start Deboarding** after boarding completes. Passengers leave their assigned seats through the same two aisle lanes, First routes to L1 when both doors are open, and Business/Economy routes to L2. Closing every door holds the operation until a door reopens.
- Generates a stable fictional profile for every preview passenger, including a name, age, nationality, purpose of travel, Executive Club tier, checked-bag count, assistance note, and booking reference.
- Provides a complete scrollable manifest with live status. A passenger's extended information is hidden until the passenger dot or manifest row is selected.
- Can import the passenger count, flight number, origin, and destination from the user's latest generated SimBrief OFP using their numeric Pilot ID. No SimBrief password is requested or stored.

## SimBrief boundary

The desktop application reads the latest generated OFP from SimBrief's documented fetcher with JSON output. The user enters the numeric Pilot ID shown in SimBrief Account Settings. A manual **Sync OFP** action is always available, and optional auto-sync runs when the Passenger Flow view model starts.

Only operational OFP data is consumed. SimBrief does not provide the real-world passenger identities used here; the manifest profiles remain deterministic fictional preview records. The current 311-position FlightFactor profile maps the 302-passenger test OFP completely and leaves nine positions empty. An imported passenger count above 311 still remains the authoritative **Booked** value, with the difference clearly reported as unmapped until a compatible cabin layout is selected.

## Cabin layout profiles

Aircraft Settings and the Passenger Flow Live Cabin card expose three stable profile IDs: `flightfactor.777v2`, `british-airways.777-200er`, and `british-airways.777-300`. The FlightFactor profile drives the operational Passenger Flow coordinates today. The supplied British Airways maps are installed only as private airline-seat-map references; the Passenger page displays dedicated horizontal crops with the original top/front at the left and bottom/tail at the right. Their IDs establish the selection and future matching boundary without committing third-party imagery to the public repository.

Selecting a British Airways reference hides the FlightFactor markers, door controls, and operational legend, and pauses an active operation. This prevents the application from suggesting that FlightFactor seat coordinates are valid for a different cabin. Returning to the FlightFactor profile restores the coded live simulation.

The user can persist a manual profile selection now. Once the X-Plane adapter exists, detected aircraft identity and verified variant metadata can select the matching profile automatically. Ambiguous or unknown aircraft must fall back to an explicit user choice rather than guessing the cabin.

## Future bridge boundary

The boarding engine is implemented in `FreeFlight.CabinControl.Core` and has no WPF or X-Plane dependency. The desktop view currently calls `SetDoorOpen` from its manual L1/L2 switches. A future FlightFactor 777 adapter can call the same method from live door datarefs.

The aircraft adapter must map FlightFactor door identifiers or X-Plane door-array indexes to semantic door names such as L1 and L2, and map verified aircraft/variant identifiers to the stable cabin-layout profile IDs. Passenger count can be supplied by the existing manual manifest control or the current SimBrief latest-OFP integration. A future vAMSYS integration can feed the same configuration boundary.

Actual 3D passenger objects are a later rendering layer. The current top-down passenger positions and assigned seats already provide the state needed to drive that layer without replacing the manifest or routing system.
