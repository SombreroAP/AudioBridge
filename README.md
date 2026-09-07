# AudioBridge

Route audio between a gaming PC and a streaming PC, so you can plug your headphones into
the streaming PC and still use your good microphone in games.

Two directions, independent, both over your LAN:

| Direction | Captured on | Played on | Needs a virtual device? |
|---|---|---|---|
| **Game audio** | Gaming PC (WASAPI loopback) | Streaming PC headphones | No |
| **Microphone** | Streaming PC (real mic) | Gaming PC, as a mic | Yes — VB-CABLE |

## Why the microphone direction needs VB-CABLE

For a remote mic to work in games and Discord, it has to appear to Windows as a *capture*
device. That means a kernel-mode audio driver, which means an EV certificate and WHQL
submission — months before anything ships.

So AudioBridge renders the incoming mic audio into **CABLE Input**, and you select
**CABLE Output** as your microphone in-game. Everything sits behind `IVirtualMicDevice`,
so if we ever ship our own signed driver it drops in without touching anything above it.

**AudioBridge does not bundle VB-CABLE.** If it isn't installed, setup credits VB-Audio and
sends you to [vb-cable.com](https://vb-cable.com) to install it yourself. See
[docs/licensing.md](docs/licensing.md).

## Layout

```
src/AudioBridge.Core     net10.0     wire protocol, jitter buffer, device interfaces
tests/AudioBridge.Core.Tests         runs anywhere, including macOS
```

Windows-specific projects (WASAPI capture/render via NAudio, VB-CABLE detection, and the
WPF setup wizard) come next and target `net10.0-windows`.

## Building

```
dotnet test          # core logic, any OS
dotnet build         # full solution, Windows only
```

Test builds are published as a self-contained single-file `.exe` to the shared
`AudioBridge` folder on Google Drive.
