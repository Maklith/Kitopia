# Bundled FFmpeg Native Libraries

Kitopia uses FFmpeg.AutoGen 9.0.1.1 and unmodified BtbN LGPL shared libraries.
`manifest.json` pins binary URLs and SHA256 checksums. The build downloads
only precompiled binary archives into `.fallout/temp/ffmpeg`, verifies them, and extracts
the matching native DLLs into `artifacts/ffmpeg/<rid>/tools/ffmpeg`.

Debug builds, including Rider F5, prepare the matching bundle automatically
before MSBuild collects files for copying. Subsequent builds skip preparation
until the bundle inputs change or a required file is missing. IDE design-time
builds do not download. Windows publishing prepares the bundle automatically
as well. Only native DLLs are deployed; image compression uses AutoGen directly.
The installed app does not download FFmpeg or use an executable from PATH.

To prepare libraries manually, run `./build.cmd BundleFfmpeg`. Add
`--ffmpeg-runtime win-x64` or `--ffmpeg-runtime win-arm64` to prepare just one
architecture. Release builds outside the publishing script still require a
prepared bundle, including compression tests run in Release configuration.

Windows x64 and ARM64 are supported. Kitopia no longer builds x86 releases.
GPL builds must not be used as a fallback.

The bundle includes native DLLs, the LGPL license, the GPL text incorporated by
LGPL 3, and origin metadata. Source archives and build recipes are not downloaded
or copied to build or publish output. `bundle.json` records the binary origin.
The matching upstream sources remain available at:

- [FFmpeg source](https://github.com/FFmpeg/FFmpeg/tree/a35c8799920a93b99781eab6e70a5795ee7d182b)
- [BtbN build recipes](https://github.com/BtbN/FFmpeg-Builds/tree/9acad4a9ef1583096af7836cc1e9c8cbcb4d3950)

Local builds honor the standard system/environment proxy settings. For this
workstation, set `HTTPS_PROXY=http://localhost:1080` in the local shell before
running the build target. Release builds explicitly disable proxy use; no
workstation proxy is persisted in the repository or client.

When updating, select LGPL shared archives, update binary checksums and source
references together, and match AutoGen's library major versions. Run the image compression
tests against the new bundle, including quality, alpha, rotation and animations.
