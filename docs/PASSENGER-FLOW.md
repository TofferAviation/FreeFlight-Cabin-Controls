# Passenger Flow preview

Passenger Flow is an original FreeFlight cabin-operations view. It does not copy Passenger2 artwork, layouts, code, or interface assets.

## Current simulator-free behavior

- Uses a deterministic 256-seat FF777 preview profile.
- Accepts a user-selected booked-passenger count from 1 to 256.
- Starts with L2 open and L1 closed so single-door routing is immediately testable.
- Routes every newly entering passenger through the only open door.
- With both doors open, L1 handles First and forward Business while L2 handles the remaining cabin.
- Holds new passengers when every door is closed, then resumes through whichever door opens.
- Keeps passengers already inside the aircraft moving to their seats if an entry door closes.
- Supports 1×, 2×, and 4× preview speeds, pause/resume, reset, progress, ETA, and recent activity.

## Future bridge boundary

The boarding engine is implemented in `FreeFlight.CabinControl.Core` and has no WPF or X-Plane dependency. The desktop view currently calls `SetDoorOpen` from its manual L1/L2 switches. A future FlightFactor 777 adapter can call the same method from live door datarefs.

The aircraft adapter must map FlightFactor door identifiers or X-Plane door-array indexes to semantic door names such as L1 and L2. Passenger count can later be supplied by a flight assignment, vAMSYS integration, SimBrief integration, or the existing manual manifest control.

Actual 3D passenger objects are a later rendering layer. The current top-down passenger positions and assigned seats already provide the state needed to drive that layer without replacing the manifest or routing system.
