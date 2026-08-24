# Passenger Flow preview

Passenger Flow is an original FreeFlight cabin-operations view. It does not copy Passenger2 artwork, layouts, code, or interface assets.

## Current simulator-free behavior

- Uses a deterministic 219-position FF777 profile whose marker coordinates match the visible seat centres in the current cabin schematic.
- Accepts a user-selected booked-passenger count from 1 to 219.
- Starts with L2 open and L1 closed so single-door routing is immediately testable.
- Routes every newly entering passenger through the only open door.
- With both doors open, the assigned cabin on the passenger's boarding ticket controls the entry: First uses L1 while Business and Economy use L2.
- Opening both passenger doors increases the spawn and in-cabin flow limits, producing a faster boarding operation than either door alone.
- Uses separate upper and lower aisle lanes, selected from the assigned seat, so passengers move along an aisle before crossing into their row.
- Holds new passengers when every door is closed, then resumes through whichever door opens.
- Keeps passengers already inside the aircraft moving to their seats if an entry door closes.
- Keeps moving passengers class-coloured, changes a passenger to orange while the seat is being occupied, and changes the marker to green only after the passenger is seated and secured.
- Supports an optional real-operations pace targeting a 30–45 minute full one-door boarding, plus 1×, 2×, and 4× accelerated previews, pause/resume, reset, progress, ETA, and recent activity.
- Turns the primary action into **Start Deboarding** after boarding completes. Passengers leave their assigned seats through the same two aisle lanes, First routes to L1 when both doors are open, and Business/Economy routes to L2. Closing every door holds the operation until a door reopens.
- Generates a stable fictional profile for every preview passenger, including a name, age, nationality, purpose of travel, Executive Club tier, checked-bag count, assistance note, and booking reference.
- Provides a complete scrollable manifest with live status. A passenger's extended information is hidden until the passenger dot or manifest row is selected.
- Can import the passenger count, flight number, origin, and destination from the user's latest generated SimBrief OFP using their numeric Pilot ID. No SimBrief password is requested or stored.

## SimBrief boundary

The desktop application reads the latest generated OFP from SimBrief's documented fetcher with JSON output. The user enters the numeric Pilot ID shown in SimBrief Account Settings. A manual **Sync OFP** action is always available, and optional auto-sync runs when the Passenger Flow view model starts.

Only operational OFP data is consumed. SimBrief does not provide the real-world passenger identities used here; the manifest profiles remain deterministic fictional preview records. An imported passenger count above the current 219-seat visual capacity is visibly limited to 219.

## Future bridge boundary

The boarding engine is implemented in `FreeFlight.CabinControl.Core` and has no WPF or X-Plane dependency. The desktop view currently calls `SetDoorOpen` from its manual L1/L2 switches. A future FlightFactor 777 adapter can call the same method from live door datarefs.

The aircraft adapter must map FlightFactor door identifiers or X-Plane door-array indexes to semantic door names such as L1 and L2. Passenger count can be supplied by the existing manual manifest control or the current SimBrief latest-OFP integration. A future vAMSYS integration can feed the same configuration boundary.

Actual 3D passenger objects are a later rendering layer. The current top-down passenger positions and assigned seats already provide the state needed to drive that layer without replacing the manifest or routing system.
