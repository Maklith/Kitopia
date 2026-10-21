# Bundled FFmpeg Tools and Native Libraries

Kitopia uses FFmpeg.AutoGen 9.0.1.1 and unmodified BtbN LGPL shared libraries.
`manifest.json` pins binary locations, URLs and SHA256 checksums. The build checks
for local precompiled archives (under `build/Ffmpeg/` or configured via `path` in `manifest.json`)
first before downloading into `.fallout/temp/ffmpeg`, verifies SHA256 checksums, and extracts
the matching native DLLs, `ffmpeg.exe` and `ffprobe.exe` into
`artifacts/ffmpeg/<rid>/tools/ffmpeg`.

Debug builds, including Rider F5, prepare the matching bundle automatically
before MSBuild collects files for copying. Subsequent builds skip preparation
until the bundle inputs change or a required file is missing. IDE design-time
builds do not download. Preparation reuses a complete extracted bundle when its
recorded manifest matches, checking native architectures and the LGPL license.
Documentation and project-license changes refresh the bundled copies without
downloading the binary archive again, even when the ZIP cache is absent.
Missing native files or a changed binary manifest require a verified cached ZIP
or a download. Windows publishing prepares the bundle automatically
as well. Image compression uses AutoGen directly; video compression uses the
bundled tools for probing, transcoding and lossless container conversion.
The installed app does not download FFmpeg or use an executable from PATH.
HEVC SDR uses the bundled Kvazaar software encoder. Preserving HDR with HEVC
uses Main10 through NVIDIA NVENC, Intel QSV or AMD AMF after a one-frame encoding
probe; it requires a compatible GPU and driver. An unavailable Main10 encoder
is reported without silently converting HDR to SDR. Container conversion with
the codec unchanged copies the existing HEVC HDR stream without an encoder.

To prepare libraries manually, run `./build/build.cmd BundleFfmpeg`. Add
`--ffmpeg-runtime win-x64` or `--ffmpeg-runtime win-arm64` to prepare just one
architecture. Release builds outside the publishing script still require a
prepared bundle, including compression tests run in Release configuration.

Windows x64 and ARM64 are supported. Kitopia no longer builds x86 releases.
GPL builds must not be used as a fallback.

The bundle includes native DLLs and tools, the LGPL license, the GPL text incorporated by
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
references together, and match AutoGen's library major versions. Run the image and
video compression tests against the new bundle, including quality, alpha, rotation,
animations, audio, HDR, container conversion and cancellation.
