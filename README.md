# RAM.FPS.Light

A tiny always-on-top glass overlay that shows RAM usage and FPS.

No installer, no dependencies, no build step. Download the .exe and run it.

## Download

**https://github.com/lowkeyhawk/RAM.FPS.Light/releases/latest**

Grab `RamFpsOverlay.exe` from the latest release. It is a self-contained
x64 build, so the .NET runtime is already inside it — nothing to install.

Current release: **v1.0.0**
`RamFpsOverlay.exe` — 146 MB — SHA256 `ef58813e747f47f5121e1a3a0db5ef3498c46bd89442cc7b1f41adc6db0e2f9e`

Windows Defender SmartScreen may show a one-time warning because the
publisher is unrecognised. Click **More info**, then **Run anyway**.

## Usage

- The panel sits in the bottom-right corner, above the taskbar.
- **Drag** it with the left mouse button to reposition.
- **Double-click** the tray icon to show or hide the panel.
- **Right-click** the tray icon for **Exit**.
- Top line is RAM (`used / total MB` and a percentage).
- Bottom line is FPS, read from the DWM compositor, so it tracks desktop,
  browser and game activity. It reads `0` when nothing on screen is moving.

## Building from source

Requires the .NET 8 SDK with the Windows Desktop workload.

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Run it with `publish\RamFpsOverlay.exe`.

Stop the app before republishing — a running instance locks the output file
and the publish step fails with `MSB4018 ... file ... is being used by
another process`.

Note: trimming is not available for Windows Forms builds (`NETSDK1175`), so
the self-contained output stays around 146 MB.

## How it works

- RAM: `GlobalMemoryStatusEx` for total physical memory, plus
  `PerformanceCounter("Memory", "Available MBytes")` for what is free.
  The first `NextValue()` call always returns 0, so it is primed during
  initialisation.
- FPS: `DwmGetCompositionTimingInfo` (dwmapi.dll) gives the compositor frame
  count. FPS is the frame delta over elapsed time.
- Rendering: a layered window (`WS_EX_LAYERED` + `UpdateLayeredWindow`) with
  the whole panel drawn into a 32bpp ARGB bitmap, which gives true per-pixel
  alpha without depending on DWM acrylic.
