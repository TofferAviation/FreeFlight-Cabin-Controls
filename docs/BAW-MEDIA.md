# British Airways first-release media

The application has a stable private input slot for the British Airways safety video:

`content-packs/private/british-airways/media/BA_Safety_Video.mp4`

When that file exists, `dotnet build` and `dotnet publish` copy it into the application as:

`content-packs/british-airways/media/BA_Safety_Video.mp4`

The Cabin Area Control Panel detects the published file automatically and uses native offline playback in the lower-right preview. If it is absent, Start reports that the local MP4 is not installed; the application never opens or embeds YouTube.

Use an H.264 video with AAC audio in an MP4 container for broad Windows playback support. The private source directory and video extensions are ignored by Git. Only release a media file when its redistribution permission has been documented.
