# Airline logo sources

Airline marks are resolved by ICAO code. A mark is displayed only for a matching file in this folder; otherwise the interface falls back to the ICAO letters.

- `BAW.png` — British Airways wordmark supplied by the project owner; its solid background was removed to create the shared transparent application asset. British Airways remains a protected trademark of its owner.
- `NOZ.png` — Norwegian 2024 wordmark, sourced from [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Norwegian_Logo_2024.svg), originally attributed there to Norwegian Air Shuttle ASA. Norwegian remains a protected trademark of its owner.
- `RYR.png` — Ryanair wordmark, sourced from [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Ryanair_logo.svg), where the simple text logo is identified as public domain. Ryanair remains a protected trademark of its owner.

The marks identify the selected airline inside a flight-simulation utility. Their inclusion does not imply endorsement, sponsorship, or affiliation.

## Base branding package

The generated base package adds ICAO-matched PNG assets from the MIT-licensed
[imgmongelli/airlines-logos-dataset](https://github.com/imgmongelli/airlines-logos-dataset).
Only files whose ICAO filename matches an operator code in the supplied 26 August 2026
passenger-jet workbook are bundled. The dataset licence covers its repository packaging;
individual airline names and marks remain trademarks of their respective owners and are
used here only to identify an operator inside the flight-simulation application.
