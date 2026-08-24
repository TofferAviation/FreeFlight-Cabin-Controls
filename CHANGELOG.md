# Changelog

All notable changes are recorded here. Filenames remain stable between releases.

## [0.1.0] - Unreleased

### Added

- Initial .NET 10 WPF application foundation.
- Dashboard, Audio, Performance, and Settings application shell.
- Airliners page with search, filters, persistent airline selection, and custom local profiles.
- Passenger Flow page with a simulator-free seat-aligned FF777 preview, animated passenger movement, configurable manifests, L1/L2 routing, boarding groups, progress/ETA, pause/resume/reset, and live door rerouting.
- Three stable cabin-layout profile IDs in Aircraft Settings: the operational FlightFactor 777 v2 cabin and private British Airways 777-200ER/777-300 seat-map references, with readable scrollable previews and persisted manual selection ahead of adapter-driven auto-matching.
- Passenger markers now land at the visual centre of each schematic seat, turn orange while the passenger settles in, and turn green once seated and secured; a 30–45 minute real-operations pace is available alongside accelerated previews.
- Two-door boarding now follows the assigned ticket cabin (First via L1, Business/Economy via L2), while single-door operation routes everyone through the available door; passenger paths now use separate upper and lower aisle lanes.
- Complete deboarding operations with live passenger movement, L1/L2 ticket routing, progress, ETA, pause/resume, and an empty-cabin completion state.
- A full interactive passenger manifest with deterministic fictional names and profiles, seat assignments, booking references, baggage, assistance notes, loyalty tiers, and live boarding/deboarding status; personal details stay hidden until a passenger is selected.
- Optional SimBrief latest-OFP sync using a numeric Pilot ID, importing passenger count, flight number, origin, and destination while persisting the user's sync preference.
- Ordered boarding calls from Group 1 through Group 8, an active-group status tab, strict group sequencing, and group-first manifest sorting.
- Master Audio now scales the real safety-video and boarding-music output, with animated left/right VU meters that respond to playback activity and effective output volume.
- Windows playback-endpoint discovery and output-device selection.
- FlightFactor 777 v2 cabin-layout reference and transparent FreeFlight sidebar branding.
- Safe vAMSYS connection setup boundary pending approved OAuth application credentials.
- Cabin Area Control Panel with a faithful CSCP-to-cabin-controls hierarchy, 15 coded FF777 operational screens, working page navigation, local control state, and a separate bridge-ready media queue.
- Fully coded CACP rendering derived from the ten supplied FF777 references, with reference-locked instrument and LCD geometry, live WPF controls, a clean bezel-only presentation, and darker pressed-key feedback.
- British Airways 2024 safety-video test preview with embedded corner playback, stop and external-browser controls, and future-aircraft staging using the configured source.
- BA-first native offline video playback with the private `BA_Safety_Video.mp4` input automatically copied into development and published builds when present.
- Four stable British Airways Boarding Music program slots with native local playback, looping, live volume, and missing-recording feedback.
- A credited CC BY 3.0 Philip Milman Flower Duet alternative for Boarding Music Program 4.
- Credited redistribution-safe Dvořák, Brahms, and Tchaikovsky editions for Boarding Music Programs 1–3, so all four programs work in a clean installation.
- Audio-page Boarding Music playback with a random installed program per session, live mute and volume control, synchronized Now Playing details, and Cabin Panel manual selection.
- Audio-page controls for starting/stopping the shared safety MP4, changing its live audio volume, muting/unmuting it, and showing a page-wide amber “Announcement in progress” banner.
- Safety-video, passenger-address, display, boarding-music, lighting, temperature, chime, door, and service controls in preview mode.
- ICAO-based airline-logo resolution with BAW and NOZ starter assets and an offline letter fallback.
- Metallic CABIN CONTROL sidebar treatment matched to the FreeFlight brand mark.
- Local JSON settings persistence.
- Airline content-pack manifest and validation model.
- Generic, redistributable example content pack.
- Core application self-tests without third-party test dependencies.
- Stable startup, shutdown, settings-error, and unhandled-error logging.

### Fixed

- SimBrief planned passenger counts now remain authoritative instead of being silently clamped to the visual map. Loads above the selected map's capacity are reported as requiring a compatible layout.
- Expanded the FlightFactor schematic from 219 repeated symbols to 311 individual seat positions: 36 First, 35 Business in the drawn 2–3–2 arrangement, and 240 Economy in the drawn 3–4–3 arrangement. A 302-passenger SimBrief load now maps and boards all 302 passengers, leaving nine seats empty.
- Display-brightness telemetry is now one-way UI data, preventing the CACP Display Controls page from attempting to write to a read-only property.
- The Display Controls pointer now tracks every brightness change across the progress bar.
- Diagnostic logging now falls back safely when the normal log file is locked or inaccessible, preventing the error handler itself from causing native Windows exception `0xe0434352`.
- Settings storage now selects the first writable location across Local AppData, Roaming AppData, and the temporary directory, avoiding startup dialogs when a profile folder has unusable permissions.

### Changed

- Removed the photographic wall and raster page dependency so panel controls and state can map directly to the future X-Plane aircraft bridge.
- Replaced responsive approximations with fixed reference coordinates so the bezel, LCD, keys, rules, readouts, and navigation preserve the original proportions when uniformly scaled.
- Added the unique operational pages from the readable B777 reference archive as code-only screens; the supplied PNG pages remain design references and are not runtime assets.
- Replaced the light safety-video treatment with a 70% black panel overlay and centered white “Announcement in progress” text.
- Removed the YouTube/WebView player and external-browser action; safety video playback is now strictly local MP4 inside the application.
- Moved active video playback into the Safety Video card so it replaces the `LOCAL MP4` placeholder instead of opening a floating lower-right preview.
- Kept application page views alive during navigation so active safety video and cabin audio continue uninterrupted in the background.
