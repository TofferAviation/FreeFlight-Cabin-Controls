# FreeFlight Cabin Control

FreeFlight Cabin Control is a Windows desktop application and planned X-Plane 12 bridge for managing a simulated aircraft cabin. Development initially targets the FlightFactor 777 v2 while keeping the core aircraft- and airline-neutral.

## Current baseline

Version `0.1.0-dev` is the first application-shell milestone. It provides:

- the approved Dashboard, Audio, Performance, and Settings navigation;
- honest disconnected/preview states until an X-Plane bridge exists;
- persistent local application and audio settings;
- a safe, versioned airline-content-pack model;
- real process CPU and memory sampling for the desktop application;
- no bundled third-party airline media.

The X-Plane plugin, FlightFactor integration, audio playback, and in-aircraft screens are not implemented in this baseline.

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
