# Ember for X-Plane on Linux

The release workflow publishes one `x86_64.AppImage` for the Linux X-Plane
client. It contains the self-contained .NET runtime, the Ember desktop client,
and the matching Linux X-Plane cabin bridge under `xplane-plugin/`.

Pilots download the AppImage, mark it executable, and launch it directly. The
embedded desktop entry and icon allow normal desktop integration where their
Linux desktop supports it. The package does not include or use any Windows
SimConnect component.
