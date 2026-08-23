# Changelog

All notable changes are recorded here. Filenames remain stable between releases.

## [0.1.0] - Unreleased

### Added

- Initial .NET 10 WPF application foundation.
- Dashboard, Audio, Performance, and Settings application shell.
- Airliners page with search, filters, persistent airline selection, and custom local profiles.
- Windows playback-endpoint discovery and output-device selection.
- FlightFactor 777 v2 cabin-layout reference and transparent FreeFlight sidebar branding.
- Safe vAMSYS connection setup boundary pending approved OAuth application credentials.
- Cabin Area Control Panel with a faithful main menu, nine coded FF777 system pages, working page navigation, local control state, and a separate bridge-ready media queue.
- Fully coded CACP rendering derived from the ten supplied FF777 references, with reusable live WPF controls, a clean bezel-only presentation, and darker pressed-key feedback.
- British Airways 2024 safety-video test preview and future-aircraft staging action using the configured YouTube source.
- Safety-video, passenger-address, display, boarding-music, lighting, temperature, chime, door, and service controls in preview mode.
- ICAO-based airline-logo resolution with BAW and NOZ starter assets and an offline letter fallback.
- Metallic CABIN CONTROL sidebar treatment matched to the FreeFlight brand mark.
- Local JSON settings persistence.
- Airline content-pack manifest and validation model.
- Generic, redistributable example content pack.
- Core application self-tests without third-party test dependencies.
- Stable startup, shutdown, settings-error, and unhandled-error logging.

### Fixed

- Display-brightness telemetry is now one-way UI data, preventing the CACP Display Controls page from attempting to write to a read-only property.

### Changed

- Removed the photographic wall and raster page dependency so panel controls and state can map directly to the future X-Plane aircraft bridge.
