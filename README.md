# FreeFlight Cabin Control

FreeFlight Cabin Control is a Windows desktop application and planned X-Plane 12 bridge for managing a simulated aircraft cabin. Development initially targets the FlightFactor 777 v2 while keeping the core aircraft- and airline-neutral.

## Current baseline

Version `0.1.0-dev` is the first application-shell milestone. It provides:

- operational Overview, Gate Desk, Passenger Manifest, Boarding Passes, Cabin, Settings, Airliners, Cabin Area Control Panel, Audio, and Diagnostics navigation;
- a shared gate-operations model: opening and closing the gate, checking in a passenger, loading baggage, printing a preview boarding pass, and boarding from the Gate Desk all update the same manifest and live cabin state;
- a coded British Airways-style thermal boarding-pass preview with monochrome coupon typography, a perforated passenger stub, unique stacked barcode, booking reference, ticket number, sequence number, seat, group, and class-correct First, Club World, World Traveller Plus, or World Traveller label for every fictional passenger;
- a searchable operational manifest with live check-in, boarding, baggage, and assistance states plus a private detail pane for the selected fictional passenger;
- a gate-focused Settings screen for SimBrief, automatic timing, boarding rules, deterministic passenger generation, preview printers, sound alerts, and flight defaults;
- a functional flight-readiness Overview with a live local operations clock, a SimBrief-driven scheduled departure, a configurable 60-minute turnaround timeline, passenger and baggage totals, cabin distribution, and gate controls;
- a deterministic Heathrow Terminal 5 gate profile that reads the SimBrief aircraft ICAO (or the selected cabin layout offline), assigns wide-body/heavy aircraft such as the 777 to B/C satellite gates and narrow-body aircraft such as the A320 family to A gates, and retains a user-editable manual fallback for airports not yet profiled;
- three operational cabin-layout choices shared by Settings and Cabin: the 311-position FlightFactor 777 v2 cabin, 280-position British Airways 777-200ER, and 266-position British Airways 777-300, all rendered horizontally with the nose left and tail right and without distorting their source aspect ratio;
- a searchable local airline catalog with persistent selection and custom airline profiles;
- an original simulator-free Passenger Flow page with profile-specific seat coordinates, mixed partial-load allocation, boarding-group calls with randomized within-group flow, varied passenger walking and entry timing, congestion slowdowns, complete boarding/deboarding, boarding-ticket-based L1/L2 routing, two-aisle movement, optional 30–45 minute real operations, accelerated previews, and a selected passenger's destination-seat highlight;
- a full passenger manifest with deterministic fictional names and profiles, live operational status, seat and booking details revealed only when a passenger dot or manifest row is selected;
- optional SimBrief latest-OFP import using the user's numeric Pilot ID, giving the planned OFP passenger count priority over manual load controls and synchronizing flight number, route, and scheduled off-block time without storing a SimBrief password;
- ICAO-driven airline-logo resolution with BAW and NOZ starter mappings and letter fallbacks;
- a fully coded Cabin Area Control Panel built on reference-locked 1040×812 instrument geometry and a 716×512 live LCD coordinate system, with a CSCP hierarchy and 15 live operational screens that preserve the supplied FF777 proportions without using the page renders at runtime;
- a British Airways 2024 safety-video mode with a 70% black “Announcement in progress” overlay, local MP4 playback directly inside the Safety Video card, future-aircraft queue staging, and no browser or YouTube dependency;
- live Audio-page safety-demonstration controls: both play buttons start/stop the shared MP4 session, the Safety Demonstration slider controls its real audio level, the switch mutes/unmutes it, and an amber page-wide banner marks an active announcement;
- navigation-persistent safety-video playback, so switching application pages does not stop, restart, or lose the active preview position;
- four installed British Airways Boarding Music programs on the coded 777 panel, with local playback/looping, live volume, and credited CC0/Creative Commons editions of each requested composition;
- Audio-page boarding controls that choose a new installed program at random for each session, with live mute/volume and shared Now Playing state; exact program selection remains on the Cabin Panel;
- a Display Controls brightness bar whose numeric value, filled range, and pointer move together;
- a safe vAMSYS authorization entry point, pending an approved Pilot API client registration;
- enumeration and persistent selection of active Windows playback endpoints;
- honest disconnected/preview states until an X-Plane bridge exists;
- persistent local application and audio settings;
- a safe, versioned airline-content-pack model;
- real process CPU and memory sampling for the desktop application;
- stable application logging under `%LOCALAPPDATA%\\FreeFlight\\CabinControl\\logs`, with a non-fatal temporary-directory fallback if that location is locked or inaccessible;
- no bundled photographic CACP page renders or airline safety video; the boarding alternatives and limited identifying wordmarks are distributed only under their separately documented licences.

The X-Plane plugin, FlightFactor integration, embedded/in-aircraft audio-video playback, vAMSYS OAuth exchange, physical gate-printer adapter, and in-aircraft screens are not implemented in this baseline. Boarding-pass and bag-tag printing is an explicitly labelled local preview. Cabin-panel controls update safe local preview state, while media and future bridge actions enter a local pre-bridge event queue. Passenger Flow therefore uses manual L1/L2 door controls today; SimBrief supplies OFP-level flight, passenger-count, and departure-time data. A future aircraft adapter will replace the local operations clock, manual doors, and layout selection with live X-Plane telemetry without replacing the passenger or gate engine.

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
