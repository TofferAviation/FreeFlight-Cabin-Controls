# British Airways first-release media

The application has a stable private input slot for the British Airways safety video:

`content-packs/private/british-airways/media/BA_Safety_Video.mp4`

When that file exists, `dotnet build` and `dotnet publish` copy it into the application as:

`content-packs/british-airways/media/BA_Safety_Video.mp4`

The Cabin Area Control Panel detects the published file automatically and uses native offline playback inside the Safety Video card, replacing the `LOCAL MP4` placeholder while active. If it is absent, Start reports that the local MP4 is not installed; the application never opens or embeds YouTube.

The Audio page controls this same persistent playback session. Its Now Playing and Safety Demonstration play buttons start or stop the local MP4; the Safety Demonstration slider changes the MP4 audio level immediately; and its switch mutes or restores audio without stopping the video. While active, the Audio page shows a full-width amber `Announcement in progress` banner.

Use an H.264 video with AAC audio in an MP4 container for broad Windows playback support. The private source directory and video extensions are ignored by Git. Only release a media file when its redistribution permission has been documented.

## Boarding music programs

The coded 777 Boarding Music screen contains four stable British Airways program slots:

| Program | Reference title | Stable input filename |
| --- | --- | --- |
| 1 | Dvořák — *Serenade for Strings in E Major, Op. 22* | `BA_Boarding_Program_01_Dvorak.mp3` |
| 2 | Brahms — *Symphony No. 3 in F Major, Op. 90: III. Poco Allegretto* | `BA_Boarding_Program_02_Brahms.mp3` |
| 3 | Tchaikovsky — *Serenade for Strings in C Major, Op. 48: II. Waltz* | `BA_Boarding_Program_03_Tchaikovsky.mp3` |
| 4 | Delibes — *The Flower Duet from Lakmé* | `BA_Boarding_Program_04_Flower_Duet.mp3` |

Redistribution-safe alternatives are bundled for all four programs under:

`content-packs/british-airways/audio/boarding/`

Each recording's performer, source, licence, and modifications are documented in the adjacent `ATTRIBUTION.md`. These are editions of the requested compositions, not the protected commercial masters shown in the reference playlist.

An authorised private replacement can use the same stable filename under:

`content-packs/private/british-airways/audio/boarding/`

When present, the private file takes priority during build and publish. Installed recordings are copied to:

`content-packs/british-airways/audio/boarding/`

The panel reports any missing recording instead of attempting a web stream. `MUSIC ON` starts the selected local file, `MUSIC OFF` stops it, volume changes are live, and a completed track loops until stopped.
