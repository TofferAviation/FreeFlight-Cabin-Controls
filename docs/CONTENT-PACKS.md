# Airline content packs

Airline customization is built into the product boundary from the beginning. It is not hard-coded into the FlightFactor adapter.

## Manifest rules

- `schemaVersion` must be supported by the running application.
- `id` is a stable lowercase identifier.
- `version`, `displayName`, `author`, and `licence` are required.
- Asset paths are relative to the pack directory.
- Paths may not escape the pack directory.
- Executable files and scripts are rejected.
- A pack can list compatible aircraft adapters without changing application code.

## Content policy

Only media with documented redistribution permission may be committed or shipped. Free distribution does not itself grant permission to reproduce or redistribute protected recordings, video, music, or logos.

The public repository contains only a generic example manifest. Private development packs belong under `content-packs/private/` and are excluded by `.gitignore`.
