# FreeFlight Cabin Control

FreeFlight Cabin Control is a Windows desktop application with a live X-Plane 12 bridge for managing a simulated aircraft cabin. Development initially targets the FlightFactor 777 v2 while keeping the core aircraft- and airline-neutral.

## Current baseline

Version `0.4.x` is the current application line. GitHub release builds receive an automatically increasing patch number and are offered through the in-application updater. It provides:

- operational Overview, Gate Desk, Iport DCS, Passenger Manifest, Boarding Passes, Cabin, Settings, Airliners, Cabin Area Control Panel, Audio, and Diagnostics navigation;
- a Real Tracker community section with the supplied FlightLogger presentation and an explicit browser link to `https://flightlogger.app/`; it is an external destination and receives no simulator, passenger, or unfinished-flight data from FreeFlight;
- a local dummy staff-login page with a live operations clock, station selector, non-persistent preview credentials, a signed-in session screen, a repaired high-contrast British Airways wordmark, and a locked Gate Operations navigation group that reveals Iport DCS, Gate Desk, Passenger Manifest, Boarding Passes, and Cabin only after sign-in; Overview is read-only for gate state and only links into this protected workspace;
- a shared gate-operations model: opening and closing the gate, checking in a passenger, loading baggage, printing a boarding pass, and boarding from either Gate Desk or Iport DCS all update the same manifest and live cabin state;
- a coded, reference-led Iport DCS workspace with role switching, live flight list and header, passenger lookup and check-in, quick/manual boarding, graphical seat selection, calculated load-control figures and envelope, flight monitoring, gate actions, and Windows-printer boarding-pass output;
- installed and connected Windows-printer discovery with default-printer selection, refresh, offline handling, and real dummy boarding-pass output from Gate Desk, Boarding Passes, or Iport DCS; no print job is sent until the user explicitly presses a print action;
- a coded British Airways-style thermal boarding-pass preview with monochrome coupon typography, a perforated passenger stub, unique stacked barcode, booking reference, ticket number, sequence number, seat, group, and class-correct First, Club World, World Traveller Plus, or World Traveller label for every fictional passenger;
- a searchable operational manifest with live check-in, boarding, baggage, and assistance states plus a private detail pane for the selected fictional passenger;
- a gate-focused Settings screen for SimBrief, automatic timing, boarding rules, deterministic passenger generation, printer defaults, sound alerts, and flight defaults;
- a functional flight-readiness Overview with a live local operations clock, a SimBrief-driven scheduled departure, a configurable 60-minute turnaround timeline, passenger and baggage totals, cabin distribution, and a navigation-only link into the protected gate workspace;
- deterministic departure and arrival gate profiles that read the SimBrief aircraft ICAO (or the selected cabin layout offline), show `DEP → ARR` in the shared header, allocate compatible gates at Heathrow T5, JFK T8 and Oslo, and retain separate user-editable fallbacks for airports not yet profiled;
- three operational cabin-layout choices shared by Settings and Cabin: the 311-position FlightFactor 777 v2 cabin, 280-position British Airways 777-200ER, and 266-position British Airways 777-300, all rendered horizontally with the nose left and tail right and without distorting their source aspect ratio;
- a searchable local airline catalog with persistent selection and custom airline profiles;
- an original simulator-free Passenger Flow page with profile-specific seat coordinates, mixed partial-load allocation, boarding-group calls with randomized within-group flow, varied passenger walking and entry timing, congestion slowdowns, complete boarding/deboarding, boarding-ticket-based L1/L2 routing, two-aisle movement, optional 30–45 minute real operations, accelerated previews, and a selected passenger's destination-seat highlight;
- a full passenger manifest with deterministic fictional names and profiles, live operational status, seat and booking details revealed only when a passenger dot or manifest row is selected;
- stable in-flight passenger movement with activity-specific colors, routed lavatory trips, seat-belt-triggered returns, and a pre-departure Champagne/orange-juice service for First Class and the first 12 Club World seats;
- two-stage cabin-crew rest with a 3.5-hour first block and two-hour second block, no rest inside three hours of landing, full arrival preparation in the final hour, and crew markers constrained to the aircraft interior;
- optional SimBrief latest-OFP import using the user's numeric Pilot ID, giving the planned OFP passenger count priority over manual load controls and synchronizing flight number, route, and scheduled off-block time without storing a SimBrief password;
- ICAO-driven airline-logo resolution with BAW and NOZ starter mappings and letter fallbacks;
- a fully coded Cabin Area Control Panel built on reference-locked 1040×812 instrument geometry and a 716×512 live LCD coordinate system, with a CSCP hierarchy and 15 live operational screens that preserve the supplied FF777 proportions without using the page renders at runtime;
- a British Airways 2024 safety-video mode with a 70% black “Announcement in progress” overlay, local MP4 playback directly inside the Safety Video card, future-aircraft queue staging, and no browser or YouTube dependency;
- live Audio-page safety-demonstration controls: both play buttons start/stop the shared MP4 session, the Safety Demonstration slider controls its real audio level, the switch mutes/unmutes it, and an amber page-wide banner marks an active announcement;
- navigation-persistent safety-video playback, so switching application pages does not stop, restart, or lose the active preview position;
- four installed British Airways Boarding Music programs on the coded 777 panel, with local playback/looping, live volume, and credited CC0/Creative Commons editions of each requested composition;
- an embedded 26 August 2026 catalog of 616 researched passenger-jet operators, with 453 ICAO-matched bundled branding assets and explicit fallbacks for operators without a verified code or logo;
- Audio-page boarding controls that choose a new installed program at random for each session, with live mute/volume and shared Now Playing state; exact program selection remains on the Cabin Panel;
- a Display Controls brightness bar whose numeric value, filled range, and pointer move together;
- a safe vAMSYS authorization entry point, pending an approved Pilot API client registration;
- enumeration and persistent selection of active Windows playback endpoints;
- automatic X-Plane 12.1.1+ connection through the simulator's built-in local Web API, with API-version negotiation, live dataref discovery, WebSocket telemetry, automatic retry, aircraft identity, flight phase, altitude, ground speed, vertical speed, on-ground state, engine state, illuminated and aircraft-specific seatbelt-sign discovery, a manual seatbelt fail-safe, and optional standard L1/L2 door synchronization;
- a loopable local passenger-ambience audio channel with independent enable and volume controls when a redistribution-cleared recording is installed through its content-pack slot;
- persistent local application and audio settings;
- a safe, versioned airline-content-pack model;
- real process CPU and memory sampling for the desktop application;
- stable application logging under `%LOCALAPPDATA%\\FreeFlight\\CabinControl\\logs`, with a non-fatal temporary-directory fallback if that location is locked or inaccessible;
- no bundled photographic CACP page renders or airline safety video; the boarding alternatives and limited identifying wordmarks are distributed only under their separately documented licences.

FlightFactor-specific custom-dataref mappings, embedded/in-aircraft audio-video playback, vAMSYS OAuth exchange, physical bag-tag printing, and in-aircraft screens are not implemented in this baseline. Boarding-pass printing uses the selected installed Windows queue, while bag tags remain an explicitly labelled local preview. The live bridge reads X-Plane's standard datarefs and synchronizes L1/L2 when the standard door array is exposed; manual controls remain available when an aircraft omits those values. A future verified FlightFactor adapter can add custom doors, automatic cabin-variant selection, simulator audio buses, and screen rendering without replacing the passenger or gate engine.

## X-Plane connection

1. Run X-Plane 12.1.1 or newer on the same Windows computer.
2. In X-Plane **Settings → Network**, do not select **Disable Incoming Traffic**.
3. Start FreeFlight Cabin Control. It probes `127.0.0.1:8086` and reconnects automatically.
4. If X-Plane was launched with a custom `--web_server_port`, enter that port under **Settings → X-Plane 12 Live Connection** and choose **Retry Connection**.

No separate X-Plane plugin is required for this telemetry layer. The Web API is loopback-only, so the app does not expose the simulator to another computer. Diagnostics shows the active aircraft, flight phase, and age of the latest telemetry frame. See the [official X-Plane Web API reference](https://developer.x-plane.com/article/x-plane-web-api/).

## Build

Requirements:

- Windows 10 or later
- .NET 10 SDK

```powershell
dotnet build FreeFlight.CabinControl.slnx
dotnet run --project tests/FreeFlight.CabinControl.Core.Tests
dotnet run --project src/FreeFlight.CabinControl.App
```

## Repository and content policy

Source filenames remain stable. Releases are identified by Git tags and `CHANGELOG.md`, not renamed project files.

Airline recordings, safety videos, music, logos, seat maps, and other third-party media must not be committed unless redistribution rights have been documented. Private development content belongs under `content-packs/private/`, which Git ignores. The supplied BA 777 seat-map previews follow that private-content path. Bundled boarding recordings retain the separate licences and attribution listed in `content-packs/british-airways/audio/boarding/ATTRIBUTION.md`.

No open-source licence has been selected yet. The repository owner retains all rights until a licence is added.

The BA-first local media input, program filenames, publish paths, and recording-attribution requirements are documented in `docs/BAW-MEDIA.md`.
