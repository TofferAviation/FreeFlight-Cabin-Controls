# Changelog

All notable changes are recorded here. User settings and unfinished-flight state are stored outside the installation directory and remain intact across updates.

## [0.3.0] - 2026-08-29

### Added

- A GitHub Releases pipeline now publishes a versioned Windows package after every accepted change on `main`.
- Automatic update checks now run at startup and every 30 minutes, with a manual **Check GitHub Now** action in Settings.
- The update dialog and changelog load the latest GitHub release notes instead of relying only on the installed text file.
- A **Real Tracker** navigation group and FlightLogger community page now present the supplied promotional artwork and open the official real-world flight logbook in the user's browser without sharing FreeFlight session data.
- Rotating 3.5-hour cabin-crew rest blocks during cruise, with half-crew groups, live countdown/status, activity events, rest-state markers, and update-safe session restoration.

- Automatic X-Plane and Microsoft Flight Simulator 2024 detection through the X-Plane Web API and the official out-of-process SimConnect interface.
- A persistent simulator-status indicator that names the active simulator and reports whether telemetry comes from X-Plane Web API or MSFS 2024 SimConnect.
- Aircraft-specific X-Plane door-dataref discovery, active ACF path resolution, live seat-belt awareness, and passenger cabin activities.
- Unique fictional passenger email addresses and live passenger activity/seat-belt details.
- Functional iPort DCS F-key shortcuts matching the commands displayed in its footer.
- Live 60-second CPU and memory graphs, simulator process metrics, X-Plane FPS telemetry, and actionable performance recommendations.
- User-confirmed update notifications with release notes, flight-in-progress guidance, one-click Windows package staging, restart installation, and a separate bundled-changelog window.
- Atomic unfinished-flight persistence and next-launch restoration.
- Imported 616 global passenger-jet operators and 453 bundled ICAO-matched airline logos with documented provenance and trademark attribution.
- Denser live passenger flow with centered door-to-aisle crossings, top-down person markers, cabin activity summaries, and dark-blue cabin-crew markers that greet at entrances or move/secure according to the live seat-belt sign and flight phase.
- Live aircraft movement and inferred pushback status for X-Plane and MSFS 2024, with automatic wide-body safety-video playback two minutes after pushback begins.
- Simulator-synchronized operational time from X-Plane or MSFS telemetry.
- Departure-to-arrival Overview mode with live climb, cruise, descent, arrival and deboarding stages plus destination welcome and end-of-flight messages.
- Bundled British Airways B772/B77W cabin-load planning references, automatic SimBrief aircraft-to-layout matching, and an exportable final operational load sheet from the iPort printer control.

### Changed

- Replaced the abstract seatbelt indicator with a crisp buckle-arrow-buckle annunciator based on the supplied 777-style reference.
- Release builds now receive their actual version from the GitHub workflow, so the Settings version, update comparison, ZIP name, and changelog agree.
- Self-contained GitHub packages now restore the required Windows runtime packs from the official NuGet feed during release publication.
- Live boarding now permits a denser but congestion-aware passenger stream, and the Live Cabin header uses a two-row responsive layout so operational status remains readable.

- Cabin layout replacement now raises one collection reset instead of hundreds of individual UI updates.
- Simulator telemetry is coalesced to the newest frame so the UI dispatcher cannot be flooded during simulator or layout loading.
- The dedicated Updates page was replaced by a non-blocking update-available dialog and a permanent Open Changelog action.
- Update installation now forces an active-flight snapshot before staging, so a restarted updated app can resume the unfinished flight from its latest state.
- Simulator Connections now appears first in Settings with separate folder and `X-Plane.exe` selectors, an always-readable selected path, live source detail, and retry control.
- Resource Usage now shows the current CPU percentage and memory megabytes alongside its live graph; simulator memory is also reported in MB.
- Application version advanced to 0.3.0; GitHub builds add an automatically increasing patch number.

### Fixed

- Door state now initializes from live simulator telemetry instead of a hard-coded open L2 door.
- Passengers away from their seats return and fasten their belts when the simulator seat-belt sign is switched on.
- Closing Cabin Control no longer discards an unfinished flight.
- Operational status and door-routing controls now reflow within the available Live Cabin width instead of clipping their right-hand text.
- Unresolved passengers are converted to reconciled no-shows at the earlier of the ten-minute final-boarding grace limit or gate close, allowing boarding to finalize without impossible unchecked/boarded states.

## [0.1.0] - Unreleased

### Added

- Live X-Plane 12.1.1+ connection using the simulator's built-in local REST/WebSocket API, with version negotiation, session dataref discovery, automatic retry, live flight-phase and cabin telemetry, diagnostics, settings, and optional standard L1/L2 door synchronization.
- Initial .NET 10 WPF application foundation.
- Dashboard, Audio, Performance, and Settings application shell.
- New operational Overview, Gate Desk, Passenger Manifest, Boarding Passes, and gate-focused Settings pages based on one shared passenger and gate state rather than disconnected mock screens.
- Gate opening/closing, passenger check-in, baggage state, preview printing, and single-passenger boarding controls; Gate Desk boarding places the selected passenger directly into their assigned live-cabin seat and prevents duplicate boarding.
- Per-passenger British Airways-style thermal boarding passes with monochrome coupon typography, perforated passenger stubs, unique deterministic stacked barcodes, fictional ticket data, booking references, sequence numbers, seats and groups, plus cabin-correct First, Club World, World Traveller Plus, and World Traveller labels.
- Searchable and filterable manifest views with live check-in, boarding, baggage, and assistance state plus selected-passenger operational details.
- Persistent gate timing, route, generation seed, boarding-rule, preview-printer, sound-alert, and archive settings.
- Airliners page with search, filters, persistent airline selection, and custom local profiles.
- Passenger Flow page with a simulator-free seat-aligned FF777 preview, animated passenger movement, configurable manifests, L1/L2 routing, boarding groups, progress/ETA, pause/resume/reset, and live door rerouting.
- Three stable cabin-layout profile IDs in Aircraft Settings: the operational FlightFactor 777 v2 cabin and private British Airways 777-200ER/777-300 seat-map references, with readable scrollable previews and persisted manual selection ahead of adapter-driven auto-matching.
- Passenger Flow shares the cabin-layout selector and displays both British Airways maps horizontally in the Live Cabin card, with the aircraft front on the left and tail on the right. All three layouts now drive their own operational passenger coordinates.
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
- Added the missing application-wide metric-label style and corrected the Gate Desk progress binding to one-way, preventing the redesigned application from failing during window startup.
- British Airways horizontal seat maps now use uniform, aspect-preserving viewport scaling instead of stretching to the Live Cabin card.

### Changed

- Removed the photographic wall and raster page dependency so panel controls and state can map directly to the future X-Plane aircraft bridge.
- Replaced responsive approximations with fixed reference coordinates so the bezel, LCD, keys, rules, readouts, and navigation preserve the original proportions when uniformly scaled.
- Added the unique operational pages from the readable B777 reference archive as code-only screens; the supplied PNG pages remain design references and are not runtime assets.
- Replaced the light safety-video treatment with a 70% black panel overlay and centered white “Announcement in progress” text.
- Removed the YouTube/WebView player and external-browser action; safety video playback is now strictly local MP4 inside the application.
- Moved active video playback into the Safety Video card so it replaces the `LOCAL MP4` placeholder instead of opening a floating lower-right preview.
- Kept application page views alive during navigation so active safety video and cabin audio continue uninterrupted in the background.
