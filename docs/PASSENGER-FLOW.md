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

## Future bridge boundary

The boarding engine is implemented in `FreeFlight.CabinControl.Core` and has no WPF or X-Plane dependency. The desktop view currently calls `SetDoorOpen` from its manual L1/L2 switches. A future FlightFactor 777 adapter can call the same method from live door datarefs.

The aircraft adapter must map FlightFactor door identifiers or X-Plane door-array indexes to semantic door names such as L1 and L2. Passenger count can later be supplied by a flight assignment, vAMSYS integration, SimBrief integration, or the existing manual manifest control.

Actual 3D passenger objects are a later rendering layer. The current top-down passenger positions and assigned seats already provide the state needed to drive that layer without replacing the manifest or routing system.
