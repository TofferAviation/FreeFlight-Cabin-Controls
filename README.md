# FreeFlight Cabin Control

FreeFlight Cabin Control is a Windows desktop application and planned X-Plane 12 bridge for managing a simulated aircraft cabin. Development initially targets the FlightFactor 777 v2 while keeping the core aircraft- and airline-neutral.

## Current baseline

Version `0.1.0-dev` is the first application-shell milestone. It provides:

- Dashboard, Airliners, Cabin Area Control Panel, Audio, Performance, and Settings navigation;
- the detailed FlightFactor 777 v2 cabin-layout reference on the Dashboard;
- a searchable local airline catalog with persistent selection and custom airline profiles;
- ICAO-driven airline-logo resolution with BAW and NOZ starter mappings and letter fallbacks;
- a Boeing 777-inspired cabin-area panel host covering lighting, chimes, temperature, water/waste, passenger address, doors, displays, boarding music, and special functions;
- a safe vAMSYS authorization entry point, pending an approved Pilot API client registration;
- enumeration and persistent selection of active Windows playback endpoints;
- honest disconnected/preview states until an X-Plane bridge exists;
- persistent local application and audio settings;
- a safe, versioned airline-content-pack model;
- real process CPU and memory sampling for the desktop application;
- stable application logging under `%LOCALAPPDATA%\\FreeFlight\\CabinControl\\logs`;
- no bundled third-party airline audio or video media; the limited identifying wordmarks are documented under `Assets/AirlineLogos/SOURCES.md`.

The X-Plane plugin, FlightFactor integration, actual audio/video playback, vAMSYS OAuth exchange, and in-aircraft screens are not implemented in this baseline. Cabin-panel actions currently enter a local pre-bridge event queue.

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

Airline recordings, safety videos, music, logos, and other third-party media must not be committed unless redistribution rights have been documented. Private development content belongs under `content-packs/private/`, which Git ignores.

No open-source licence has been selected yet. The repository owner retains all rights until a licence is added.
