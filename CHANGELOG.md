# Changelog

All notable changes are recorded here. User settings and unfinished-flight state are stored outside the installation directory and remain intact across updates.

## [0.5.28] - 2026-09-22

### Added

- Ember now protects the shared Fleet pool from forgotten unused reservations. If a reserved registration has not begun operating or started ACARS, and Ember cannot receive telemetry or maintain a simulator connection for 10 minutes, it is automatically released for another pilot.
- A clear countdown is shown in Fleet Management while Ember is waiting for the simulator. Recovering telemetry immediately preserves the reservation; a release that cannot be confirmed by the Fleet service is never assumed and is retried safely.

## [0.5.27] - 2026-09-22

### Added

- Ember now shows a pilot's private BAV Operations updates in the BAV Account page. PIREP submission confirmations, staff PIREP decisions, transfer-credit decisions and career-credit updates use the same secure notification feed as the pilot website.
- Notices refresh when Ember signs in, restores its secure device session, refreshes the BAV account or completes an ACARS flight. A temporary notice-service interruption can never prevent a confirmed ACARS flight from completing.

## [0.5.26] - 2026-09-21

### Fixed

- Live Cabin now projects passenger seats and passenger-entry doors through the same artwork geometry as the drawn aircraft. The correction covers every British Airways narrow- and long-haul cabin map, including the Airbus A320 family, A319, A321, A321neo and Embraer 190.
- A320 passenger locations now match the supplied cabin artwork row-by-row, and boarding begins from the visible open door instead of an offset virtual point.
- Cabin crew now remain at the forward entry and aft welcome stations during boarding. Changing between long-haul and short-haul layouts also clears a previous crew-rest rotation immediately.

### Verification

- Added automated geometry checks for every supported cabin layout and a visual A320neo check for the forward and aft crew stations.

## [0.5.22] - 2026-09-20

### Changed

- Ember and the British Airways Virtual website now use the approved pilot-rank artwork files directly for Cadet, Second Officer, First Officer, Senior First Officer, Captain, Senior Captain and Training Captain. The image assets are preserved unchanged from the supplied rank set.

## [0.5.21] - 2026-09-20

### Changed

- The BAV Account page now uses the full British Airways Virtual shoulder-board design for pilot ranks. Ember now matches the website’s navy board, stitched detail, gold button and rank-specific stripes, speedmarque, senior-captain laurel and training-captain star.

## [0.5.20] - 2026-09-20

### Added

- Ember now shows each signed-in pilot’s live British Airways Virtual rank in the account header and BAV Account page.
- The account page includes a compact navy-and-gold shoulder-board insignia that reflects Cadet through Training Captain, including rank stripes and senior/training distinctions.

### Changed

- A BAV profile refresh now updates the pilot’s rank alongside their name, profile picture and pilot number, so an automatic promotion appears in Ember without a reinstall.

## [0.5.19] - 2026-09-18

### Added

- Ember now creates a local crash report for unexpected user-interface exceptions, unobserved background-task errors and fatal process exceptions. Reports record the app version, runtime, operating system, exception and stack trace without uploading anything.
- Crash reports are kept under `%LOCALAPPDATA%\FreeFlight\CabinControl\crash-reports`; Ember retains the newest 20 reports and tells the pilot where to find a report when it can recover from a user-interface error.

### Security

- Common password, token, authorization, bearer-token, cookie, secret and API-key values are redacted before a crash report is written.

## [0.5.18] - 2026-09-14

### Fixed

- Fleet Management now visibly reports reservation progress, acceptance, and the exact server-side reason if a selected airframe cannot be reserved. This prevents a failed reservation from appearing to do nothing.

## [0.5.17] - 2026-09-14

### Fixed

- ACARS now starts from a valid BAV assignment at engine start or pushback even if Fleet airframe accounting is temporarily unavailable. This keeps BA-Radar tracking independent from Fleet recovery.
- End-of-flight ACARS completion is no longer blocked by a Fleet accounting failure.

## [0.5.16] - 2026-09-14

### Changed

- Ember now opens on a dedicated BAV secure sign-in screen. The navigation, flight header and all operational controls remain hidden until a pilot has authenticated.

## [0.5.15] - 2026-09-14

### Fixed

- Ember now reasserts the BAV account gateway after the main window loads and whenever an account session clears, preventing an unauthenticated launch from exposing the operational workspace.

## [0.5.14] - 2026-09-14

### Fixed

- Windows executable and installer now use the established Ember application icon instead of the legacy FreeFlight icon.

## [0.5.13] - 2026-09-14

### Added

- Ember now opens at the BAV account gateway and protects every operational page until the pilot has authenticated.
- Pilots can choose **Remember this BAV account on this Windows PC**. Ember stores a revocable device credential with Windows data protection; it never stores the website password or long-lived bearer token.

### Changed

- A remembered device is verified and its credential rotated by the live BAV website at launch. **Sign out** revokes it server-side and deletes its protected local copy.

## [0.5.12] - 2026-09-14

### Changed

- Restored the British Airways Virtual logo to the application navigation shell.
- Restored the established FreeFlight app mark for the Windows executable, installer and Gate Operations sign-in view; **Ember** remains the product name.

## [0.5.11] - 2026-09-14

### Changed

- Rebranded the desktop application as **Ember**, with the supplied Ember mark used in the app shell and Windows application icon.
- Installer, Start Menu, desktop shortcut, release assets, product metadata, updater language, documentation and licence now use the Ember name while preserving existing local settings and update compatibility.

## [0.5.10] - 2026-09-14

### Added

- Cabin Control now restores the pilot's active aircraft reservation or operation from the authoritative BAV Fleet API after sign-in or an app restart.

### Changed

- A reserved registration is selected automatically on Fleet refresh, preventing a locally remembered tail from masking the aircraft actually assigned to the pilot's current flight.

## [0.5.9] - 2026-09-14

### Added

- A proprietary FreeFlightLTD licence is now included in every installation and shown during installer setup.
- Settings provides a visible copyright notice and a **View Licence** action to open the installed terms.

### Changed

- Application and installer publisher metadata now identifies FreeFlightLTD and records the 2026 all-rights-reserved notice.

## [0.5.8] - 2026-09-13

### Added

- **Refresh BAV flight and profile** now recognizes a newly selected BAV assignment and safely imports its matching SimBrief passenger list when the latest OFP matches the BAV flight number and route.
- The BAV assignment aircraft type now selects the corresponding live-cabin layout before the OFP arrives, including Airbus A319/A320/A321/A350, Boeing 777/787 and Embraer 190 variants.
- Fleet Management refreshes its live aircraft list before selecting the first matching, in-service airframe for the new BAV flight. Reserving that registration remains an explicit pilot action.

### Changed

- Overlapping Fleet refresh requests are serialized, ensuring a new BAV flight selects against the latest website fleet data rather than a stale or empty list.
- An active boarding or ACARS operation is never overwritten by a later BAV refresh.

## [0.5.7] - 2026-09-13

### Added

- Cabin Control now displays the signed-in pilot's British Airways Virtual profile photo as a circular avatar in the account page and operational header.
- The app retrieves the photo from the protected BAV account API at sign-in and whenever **Refresh BAV flight and profile** is selected, keeping it aligned with the website profile.

## [0.5.6] - 2026-09-13

### Changed

- Cabin Control now ships permanently connected to `https://britishairwaysva.co.uk`; historical localhost, preview, and manually entered Fleet endpoints are replaced safely on launch.
- Fleet refreshes, aircraft records, cabin-defect reports, and hard-landing assessments now use the signed-in pilot's BAV account session. Pilots no longer need a shared Fleet device key.
- The Fleet workspace refreshes automatically when a pilot signs in, while Settings shows the fixed live website address.

## [0.5.5] - 2026-09-12

### Added

- Shared British Airways Virtual pilot-account sign-in, active aircraft reservation, and ACARS session recovery between Cabin Control and the website.
- Live simulator telemetry for Microsoft Flight Simulator and X-Plane, including public BA-Radar positioning, recent trails, and completed-flight recovery.
- Fleet hard-landing assessment, visual-inspection and maintenance workflows, plus controlled cabin-defect reporting and resolution actions.

### Changed

- Rebranded the desktop shell for British Airways Virtual and added the pilot identity, callsign and employee-ID presentation to the operational header.
- Fleet photos, assignment state, defect state and operational availability now remain synchronised with the protected website API.

## [0.5.4] - 2026-09-11

### Added

- Fleet Management is now part of the published application: a native Fleet workspace, protected website API connection settings, selectable aircraft records, live availability status, and optional automatic synchronisation.
- The desktop client communicates only with the protected Fleet website API; it never stores Supabase or database credentials.

### Changed

- Fleet connection preferences and selected aircraft are included in the application settings round-trip checks.

## [0.5.3] - 2026-09-11

### Fixed

- Cabin crew markers now update at the active animation cadence instead of the old 250 ms dashboard refresh. Their aisle movement is time-based and substantially slower, eliminating the large horizontal jumps that made them look like they were spinning.
- Cabin activity no longer invalidates the moving-passenger canvas a second time while boarding or deboarding is already refreshing it. Static dashboard and manifest updates now run at 2 Hz, preserving the 20 FPS Balanced animation budget.

## [0.5.2] - 2026-09-11

### Added

- Gate Desk now includes Jetway Operations. With X-Plane 12.1.4 or later connected, **Operate Jetways** activates the simulator's native jetway command from Cabin Controls and reports whether the request was accepted.

### Notes

- Native X-Plane selects the eligible bridge(s) and door(s). Per-bridge, per-door assignment and multi-bridge orchestration require a future FreeFlight jetway engine or compatible scenery adapter.

## [0.5.1] - 2026-09-04

### Added

- A complete five-part British Airways catering workspace covering Menu Selection, Service Progress, Inventory, Passenger Preferences, and Special Meals.
- Automatic catering-profile selection from the SimBrief route, aircraft family, scheduled departure time, and estimated flight duration.
- Long-haul First, Club World, World Traveller Plus, and World Traveller service structures, including Dine Anytime-style First choices, departure meals, mid-flight refreshments, and second services.
- Short-haul Club Europe and Euro Traveller profiles with time-sensitive breakfast, lunch, afternoon-tea and dinner service, plus complimentary refreshments and High Life Café buy-on-board choices.
- Individual passenger meal selections, deterministic special meals, limited meal quantities, low/out-of-stock states, and progressive service status.
- Date-versioned catering packs. A bundled seasonal pack is used by default, while newer JSON packs placed in the local `catering-packs` folder can replace it without rebuilding the application.

### Changed

- Live cabin meal and drink activities now follow the active catering phase and pause while the simulator seat-belt sign is on.
- Catering service progression, inventory use, passenger order status, and onboard spend are reconciled in real time.
- Vertical catering scroll bars are visually hidden while mouse-wheel, touchpad, and touch scrolling remain functional.
- Application and installer version advanced to 0.5.1.

## [0.5.0] - 2026-09-04

### Added

- Eleven additional built-in British Airways cabin layouts covering the A319, A321, A321neo 220M, A350, two 777-200 configurations, three 787 variants, 787-10, and Embraer 190. The application now ships sixteen operational layouts in total.
- A searchable offline Iport DCS station catalog covering British Airways destinations from the airline's published route network.
- Aircraft-aware L1-L5 and R1-R5 door models for wide-body cabins, with overwing exits explicitly separated as emergency exits on applicable narrow-body aircraft.
- A new Cabin parent menu with Live Cabin, Catering & Meal Service, and Onboard Menu pages, while Airliners now has its own parent section.
- Live catering inventory, ground-uplift progress, realistic GBP menu pricing, recorded passenger purchases, per-passenger onboard spend, crew service duties, and lavatory demand/queues.
- An app-only Diagnostics view showing current Cabin Control CPU percentage, memory in MB, managed heap, process detail, a 60-second graph, and measured peak impact.

### Changed

- SimBrief loads larger than the selected layout now activate a clear seat-map override and fill every mapped seat instead of reporting impossible passengers outside the cabin.
- Passenger cabin movement is frame-time limited, queues approach their positions smoothly, and service/lavatory activities retain their action-specific colors.
- Cabin crew receive moving service positions and selectable duty details for meal heating, food delivery, drink service, and tray collection.
- Application and installer version advanced to 0.5.0.

### Fixed

- Manual gate-desk boarding once again works before simulator door telemetry is available without falsely opening an aircraft door.
- Expanded fleet capacities, passenger routes, and emergency-exit classification are now covered by automated checks.

## [0.4.9] - 2026-09-04

### Fixed

- Removed the opaque white backdrop from both built-in British Airways 777 cabin maps so the aircraft artwork blends into the Live Cabin theme like the Airbus A320 layouts.
- Preserved the original 777 image dimensions and seat geometry so existing passenger, crew, aisle, and door coordinates remain aligned.

## [0.4.8] - 2026-09-03

### Added

- Built-in British Airways Airbus A320-200 and A320neo cabin maps with 156 usable mapped seats, Club Europe blocked-middle-seat rules, live passenger movement, and front/rear door routing.
- A dedicated ToLiss A320-family adapter reads and controls passenger doors through `AirbusFBW/PaxDoorModeArray`, using index 0 for 1L and index 2 for 2L.

### Changed

- SimBrief aircraft codes `A320` and `A20N` now select their matching cabin profiles automatically.
- Narrow-body Airbus layouts use four cabin-crew members, one centre aisle, and short-haul crew-duty behavior while retaining the live-cabin passenger engine.
- Application version advanced to 0.4.8.

### Fixed

- ToLiss door mode values are translated from `0=Closed`, `1=Auto`, and `2=Open` into dependable open/closed app signals instead of being mistaken for percentages.

## [0.4.7] - 2026-09-03

### Added

- The British Airways 2024 safety video is now included with every installer and portable update for offline in-app playback.
- The repository records the owner's confirmation that British Airways granted redistribution permission for the bundled safety video and cabin-layout artwork.

### Changed

- The built-in 777-200ER and 777-300ER Live Cabin backgrounds now use the supplied British Airways horizontal seat maps.
- Application version advanced to 0.4.7.

### Fixed

- Clean installations no longer report that `BA_Safety_Video.mp4` is missing.

## [0.4.6] - 2026-09-03

### Added

- Original FreeFlight British Airways 777-200ER and 777-300 cabin schematics are now compiled into the application for every clean installation.

### Changed

- Settings previews and the Live Cabin page now load both British Airways layouts through assembly pack resources instead of optional private files.
- Application version advanced to 0.4.6.

### Fixed

- Public GitHub release builds no longer fall back to the generic FlightFactor schematic when a British Airways cabin profile is selected.

## [0.4.5] - 2026-09-03

### Changed

- The FlightFactor 777 v2 bridge now prioritizes its exact `1-sim/anim/doorL1` and `1-sim/anim/doorL2` animation outputs instead of relying on similarly named cabin and service-door candidates.
- Seat-belt synchronization now reads the actual `1-sim/anim/seatbeltLight` output. The three-position selector is retained as a command target with its verified `OFF=0`, `AUTO=1`, and `ON=2` encoding.
- The FlightFactor 777 adapter contract records the verified custom mappings and preserves standard X-Plane datarefs as fallbacks.

### Fixed

- FlightFactor selector position `AUTO` is no longer misreported as an illuminated seat-belt sign.
- Turning the seat-belt sign on from the app now sends FlightFactor selector value `2` rather than value `1` (`AUTO`).

## [0.4.4] - 2026-09-03

### Added

- Cabin-crew markers can be selected to open a crew profile with a stable fictional name, role, crew ID, current duty, and varied age between 22 and 56.
- A single lightweight FreeFlight Cabin Bridge plugin now publishes stable X-Plane datarefs for plugin availability, the passenger seat-belt sign, and L1/L2 door positions.
- Settings can install or update the bundled X-Plane plugin directly after the user selects their X-Plane folder or executable.
- GitHub releases now include a guided per-user Windows Setup executable with Start-menu integration, optional desktop shortcut, clean uninstall support, and preserved local settings.

### Changed

- The X-Plane bridge prioritizes the FreeFlight plugin interface for incoming and outgoing cabin controls, retaining automatic Web API discovery and manual controls as fallbacks.
- Native plugin sampling runs at 10 Hz with cached SDK handles and no file or network I/O in X-Plane's flight loop.
- Application version advanced to 0.4.4.

### Fixed

- Aircraft-specific door and seat-belt signals are normalized before reaching the app, preventing unrelated static datarefs from masking the active simulator output.

## [0.4.3] - 2026-09-02

### Added

- Flights and SimBrief imports can now be unloaded directly from both the overview and Live Cabin pages.
- A completed flight is automatically unloaded after the app has observed departure, landing, a stopped aircraft, and ten seconds of confirmed engine shutdown.

### Changed

- Passenger animation now refreshes only moving cabin markers at render speed while batching full manifest and metric updates, substantially reducing UI work on dense loads.
- Cabin crew positions use layout-specific aisle coordinates and safe horizontal bounds so every marker remains inside the aircraft schematic.
- Closing the app now completes and clears the active flight; an updater-initiated restart remains the sole exception so an in-progress flight can resume after installation.
- Application version advanced to 0.4.3.

### Fixed

- Seat-belt sign changes no longer teleport cabin crew to jumpseats; crew secure only for aircraft movement, pushback, taxi, climb, descent, or approach duties.
- Cabin crew greeting, service, rest, and arrival-preparation markers can no longer be placed beyond the visible airframe.

## [0.4.2] - 2026-09-02

### Added

- Seated passengers now react individually when the seat-belt sign illuminates: most respond within a short deterministic delay, immediate responders remain possible, and the live cabin reports how many are still securing themselves.
- X-Plane cabin controls are now bidirectional. Manual L1/L2 and seat-belt requests use the local Web API's writable dataref endpoint while rejected/read-only mappings fall back safely to the local UI.
- An aircraft-neutral cabin-adapter contract and FlightFactor 777 v2 identity profile prepare door, seat-belt and future cabin-panel semantics for the verified v0.5.0 mapping pass.

### Changed

- X-Plane cabin-dataref discovery ranks all matching aircraft signals instead of accepting the first arbitrary subset, learns which values actually change, and automatically rediscovers mappings after the active ACF changes.
- In-flight passenger movement refreshes in 125–500 ms slices according to performance mode, keeping persistent routes while removing the former one-to-two-second visual jumps.
- Application version advanced to 0.4.2.

### Fixed

- FlightFactor passenger entry and the L1/L2 controls now use the cropped schematic's real galley threshold instead of the lower British Airways-map coordinate, keeping passengers centered through the doorway.
- Static standard annunciators no longer permanently mask a changing aircraft-specific seat-belt signal.
- Live simulator door updates no longer echo back as outgoing user commands.

## [0.4.1] - 2026-09-01

### Added

- A real vAMSYS Pilot API client boundary using Authorization Code + PKCE, cryptographic state verification, the native FreeFlight callback URI, Windows-user encrypted token storage, automatic refresh, revoked-consent handling, and the minimum `identity:basic pilot:read` scopes.
- Connected vAMSYS pilots now receive a shared account control in the top-right application header with their name, pilot callsign, airline and optional local profile picture.
- The connected-account window keeps personal identity read-only and redirects those changes to vAMSYS, while allowing local FreeFlight profile pictures and custom page backgrounds.
- Background appearance preferences include 10–20 blur intensity, 10–20 percent image strength, and an option to apply the image across all parent pages.

### Changed

- The vAMSYS airline catalog now remains empty until a verified Pilot API profile is loaded, then contains only the airline authorized by that airline-scoped OAuth client.
- The shared flight header now uses proportional columns so the connected-account control remains usable without covering flight, route, gate, aircraft or SimBrief information.
- Release patch selection now counts an existing `.0` tag correctly instead of treating zero as an empty result, ensuring the updater receives a genuinely newer tag.
- Application version advanced to 0.4.1.

### Security

- FreeFlight never requests or stores a vAMSYS password or OAuth client secret. Access and refresh tokens are encrypted to the signed-in Windows user and local appearance images never leave the computer.

## [0.4.0] - 2026-08-30

### Added

- First Class and the first 12 Club World passengers now receive a pre-departure welcome-drink service once that cabin section has completed boarding, with Champagne or orange juice shown as the live activity.
- Passenger ambience now has a real loopable local-audio channel, independent volume and enable controls, Audio-page playback state, and a documented redistribution-safe content-pack slot.
- The seat-belt annunciator now has an explicit manual fail-safe: when simulator synchronization is disabled or no dependable simulator signal is available, the 777-style icon can be clicked to toggle cabin behaviour.

### Changed

- X-Plane seat-belt synchronization now prioritizes the illuminated annunciator, discovers aircraft-specific numeric seat-belt signals, and retains both standard cockpit switch datarefs as fallbacks.
- Passenger cabin movement now follows persistent seat-to-aisle-to-lavatory routes instead of selecting a new location every update; activity colors remain attached to each passenger throughout the movement.
- Cabin crew positions are clamped inside the aircraft, with dedicated positions for welcome-drink service, arrival preparation, entrance greeting, normal duty, secured seating, and crew rest.
- Crew rest now operates as a 3.5-hour first-shift block, a two-hour second-shift duty handover, and a two-hour second-shift rest block. Rest is prohibited inside three hours of landing, and all crew prepare the cabin inside the final hour.
- Application and GitHub release versioning advanced to the `0.4.x` line; the first release is `v0.4.0` and later accepted releases increment the patch tag.

### Fixed

- Passengers no longer teleport, disappear, or leave the cabin bounds during in-flight activity changes or when returning to their seats for the seat-belt sign.
- Boarding paths now preserve the exact center line through the selected aircraft door before crossing to the assigned cabin aisle.
- Cabin crew markers can no longer render outside the cabin plan.

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
