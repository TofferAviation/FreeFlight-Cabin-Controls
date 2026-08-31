# vAMSYS Pilot API setup

FreeFlight Cabin Control uses the vAMSYS Pilot API Authorization Code flow with PKCE. It never asks for a pilot password and no client secret is embedded in the desktop application.

## VA owner registration

In Orwell, open **Settings → API → API v3**, create a **Pilot API / Authorization Code + PKCE** client, and register:

- Redirect URI: `freeflight-cabin-control://oauth/vamsys`
- Scopes: `identity:basic` and `pilot:read`
- Privacy policy: a public policy explaining the identity fields read by FreeFlight and its encrypted local token storage

The VA Owner must attest and activate the client. Enter the resulting public numeric client ID, VA name, and three-letter ICAO code in FreeFlight's Airliners → Configure vAMSYS dialog. Never enter or distribute a client secret.

## Data handling

- Access and refresh tokens are protected with Windows Data Protection for the current Windows user.
- FreeFlight requests the authenticated name, email, pilot callsign, airline-scoped profile, and rank.
- Email and other personal details are read-only in FreeFlight. The account button opens vAMSYS for identity, privacy, and pilot-profile changes.
- Local profile pictures and background images are copied into the FreeFlight local profile directory and are never uploaded to vAMSYS.
- Disconnecting removes the local tokens. Pilots can separately revoke consent from their vAMSYS account.

## Airline scope

A Pilot API OAuth client belongs to one Virtual Airline, and the `/profile` response is restricted to that client airline. One client cannot enumerate every VA a user has joined. Each participating VA must register and attest its own FreeFlight client if multiple airline profiles are to be supported later.

The live reference is [vAMSYS Pilot API](https://vamsys.io/docs/pilot), with client administration described in the [vAMSYS API guide](https://vamsys.co.uk/docs/orwell/api).
