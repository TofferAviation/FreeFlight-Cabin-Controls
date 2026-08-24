# FreeFlight Cabin Control

FreeFlight Cabin Control is a Windows desktop application and planned X-Plane 12 bridge for managing a simulated aircraft cabin. Development initially targets the FlightFactor 777 v2 while keeping the core aircraft- and airline-neutral.

## Current baseline

Version `0.1.0-dev` is the first application-shell milestone. It provides:

- Dashboard, Airliners, Cabin Area Control Panel, Audio, Performance, and Settings navigation;
- the detailed FlightFactor 777 v2 cabin-layout reference on the Dashboard;
- a searchable local airline catalog with persistent selection and custom airline profiles;
- an original simulator-free Passenger Flow page with seat-centred FF777 passenger markers, complete boarding and deboarding runs, boarding-ticket-based L1/L2 routing, two-aisle movement, an optional 30–45 minute real-operations pace, accelerated previews, progress, ETA, pause/resume, and live rerouting when doors change;
- a full passenger manifest with deterministic fictional names and profiles, live operational status, seat and booking details revealed only when a passenger dot or manifest row is selected;
- optional SimBrief latest-OFP import using the user's numeric Pilot ID, synchronizing passenger count, flight number, and route without storing a SimBrief password;
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

The X-Plane plugin, FlightFactor integration, embedded/in-aircraft audio-video playback, vAMSYS OAuth exchange, and in-aircraft screens are not implemented in this baseline. Cabin-panel controls update safe local preview state, while media and future bridge actions enter a local pre-bridge event queue. Passenger Flow therefore uses manual L1/L2 door controls today; SimBrief supplies only OFP-level flight and passenger-count data, and a future aircraft adapter will replace manual door inputs with live telemetry without replacing the passenger engine.

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

Airline recordings, safety videos, music, logos, and other third-party media must not be committed unless redistribution rights have been documented. Private development content belongs under `content-packs/private/`, which Git ignores. Bundled boarding recordings retain the separate licences and attribution listed in `content-packs/british-airways/audio/boarding/ATTRIBUTION.md`.

No open-source licence has been selected yet. The repository owner retains all rights until a licence is added.

The BA-first local media input, program filenames, publish paths, and recording-attribution requirements are documented in `docs/BAW-MEDIA.md`.
