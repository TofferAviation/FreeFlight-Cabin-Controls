# Ember for X-Plane on macOS

The macOS automation builds a self-contained `.app` bundle and a separate DMG
for Apple Silicon (`arm64`) and Intel (`x64`) Macs. Each package contains the
matching `mac.xpl` X-Plane cabin bridge under `xplane-plugin/`.

The CI workflow deliberately produces test artifacts only. A public macOS
release must be code-signed and notarized using the British Airways Virtual
Apple Developer credentials before it is offered to pilots; otherwise macOS
Gatekeeper will warn users about an unknown developer.
