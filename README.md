# Rekon — Bug Capture SDK for Unity

**English** | [한국어](./README.ko.md)

### When the bug hit, it was already recording.

The moment you press the hotkey, the last **~60 seconds of video, logs, and performance data** are already on disk.
A rolling buffer is always running — so pressing it *after* the bug happens is never too late.

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-1.0.0-brightgreen.svg)](CHANGELOG.md)

<!-- DEMO GIF (replace before launch): 30s capture of hotkey → last-60s video + FPS graph + Console error aligned on one timeline -->
<!-- Until the GIF is ready, the spec text below stands in as the fallback -->

> In Play Mode, press `Ctrl/Cmd + Shift + B` → the last ~60s of video + screenshots + logs + game state (Scene/FPS/memory)
> is captured in one shot and lands on the web dashboard. Filing a Jira issue from there is one click.

```
# UPM Git URL (Package Manager > Add package from git URL...)
https://github.com/RekonOps/Rekon-unity.git#v1.0.0
```

---

## Sound familiar?

A non-technical QA writes in the ticket — **"The character looks weird."**

Holding one screenshot and that single line, you start debugging on guesswork.
Which scene it was, whether the FPS dropped, what hit the Console — nothing.
It won't reproduce, and the ticket gets closed as "cannot reproduce."

Rekon turns debugging that starts with guesses into **debugging that starts with evidence**.

---

## Why it's different

Existing tools try to find "the moment the bug happened" after the fact. Rekon starts from the premise that the moment is **already captured**.

### 1. Press after the fact — the last 60 seconds are still there

There is no record button. While you're in Play Mode, a rolling (ring) buffer keeps the most recent window at all times.
Even if you press the hotkey *after* seeing the bug, the last ~60 seconds of video are already on disk. (default 15fps · 1280×720)

> "I forgot to hit record" is structurally impossible.

### 2. Video and performance share one timeline

At any point in the captured video — that instant's **FPS drop**, **memory spike**, and **Console error** line up together.
The frame where the video "hitched" maps straight onto what the graphs were doing at that moment.

The 1-in-100 intermittent crash, the "only on that one device" bug — you see the moment on screen and in numbers, at the same time.

### 3. Unity context, in full

Scene name, device info, Unity version, frame rate, recent logs — the game state at the moment of capture is attached automatically.
This is Play Mode context that generic SDKs don't have.

---

## Invisible by design

Rekon is designed to be forgotten. No always-on overlay, no dashboard living inside your game.
It only appears when summoned by hotkey; otherwise it quietly keeps the rolling buffer turning. It doesn't break your flow.

---

## Installation

In Unity, open **Window > Package Manager > +  > Add package from git URL...** and enter:

```
https://github.com/RekonOps/Rekon-unity.git#v1.0.0
```

Or add it directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.rekonops.rekon": "https://github.com/RekonOps/Rekon-unity.git#v1.0.0"
  }
}
```

> Check [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases) for the latest version.

---

## Usage

Video capture is encoded with **FFmpeg**. It is not bundled with the package, so install it once per OS if you want video capture (PC/Mac — not supported on mobile):

| OS | Install |
|----|---------|
| **macOS** | `brew install ffmpeg` |
| **Windows** | `choco install ffmpeg` or `winget install ffmpeg` |
| **Linux** | `sudo apt install ffmpeg` (per your distro) |

> Rekon automatically searches PATH plus the default brew/choco/winget install locations (works even when the Unity editor doesn't inherit your shell PATH). Without FFmpeg, capture still works — screenshots and logs, just no video.

Once that's done, all you need is the hotkey.

1. A bug happens in **Play Mode**
2. **`Ctrl/Cmd + Shift + B`** — the last ~60 seconds are captured (configurable in Settings)
3. Enter a title and description → **[Save to Web]** → it lands on the web dashboard

Open the report on the dashboard and create the issue with the **[Send to Jira]** button.

> **Honest boundaries**: the Unity plugin only does *capture and save*. Connect Jira once from the web dashboard (`/settings/jira`). Unity never talks to Jira directly.

<details>
<summary>Web login / Jira integration internals (expand)</summary>

**Web login** — `Project Settings > Rekon > [Web Login]`
1. The plugin sends a `device_id` to the backend and receives a one-time login URL.
2. Your browser opens automatically and you sign in.
3. On completion, the token and workspace are saved to Settings automatically.
4. Settings shows **"Connected (workspace name)"**.

**Jira** — authenticate Jira Cloud OAuth in the web dashboard under `Settings > Jira`.
After that, the [Send to Jira] button is enabled on report detail pages.

</details>

---

## Settings (`Project Settings > Rekon`)

| Setting | Description | Default |
|---------|-------------|---------|
| Hotkey | Capture shortcut | `Ctrl/Cmd+Shift+B` |
| Video FPS | Frame capture rate | 15 |
| Video resolution | Capture resolution | 1280×720 |
| Log buffer | Number of recent logs kept | last N |
| Bundle retention | Max local bundle count/size | auto-pruned |
| Web connection | Login status and workspace name | disconnected |

---

## Why we built this

We've had tickets we couldn't close because the bug wouldn't reproduce.
We've closed tickets where QA wrote "the character looks weird" — without ever once seeing that moment.

The truth of that moment evaporates in 60 seconds. So we built something that keeps the evidence alive.

---

## Nothing gets lost

- **Offline auto-retry** — if the network drops, captures are stored locally (`pending/`) and retried in the background once you're back online (up to 3 attempts, exponential backoff). No data slips through the cracks.
- **Sensitive data masking** — emails, IPs, and tokens in logs are masked automatically.
- **Integrity verification** — every Release ships with SHA-256 checksums and a CycloneDX SBOM. You can verify the tarball yourself → [SECURITY.md](./SECURITY.md).
- **MIT OSS** — the code is open. Your evidence is never locked behind someone else's wall.

---

## Requirements

| Item | Requirement |
|------|-------------|
| Unity | 2022.3 LTS or newer |
| .NET | Standard 2.1 |
| FFmpeg | Required for video capture on PC/Mac — **not supported on mobile** |

> Video capture does not work in mobile builds. We're telling you up front.

---

## Contributing

Rekon is open source for transparency and distribution. **We're not accepting external pull requests at this time** — but bug reports and feature requests via [Issues](https://github.com/RekonOps/Rekon-unity/issues) are very welcome.

For security vulnerabilities, please use the private channels described in [SECURITY.md](SECURITY.md) instead of public issues.

## License

**MIT License** — see [LICENSE](LICENSE).

Copyright 2026 RekonOps
