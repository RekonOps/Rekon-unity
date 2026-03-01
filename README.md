# Bug-OneTouch-unity

> A local-first, in-game bug reporting plugin for Unity — with seamless Jira Cloud integration.

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/Status-In%20Development-orange.svg)]()

---

## Overview

**Bug-OneTouch-unity** empowers QA testers and developers to report bugs directly from within a running Unity game — without ever leaving the play session. Reports are captured locally first and synced to Jira Cloud when connectivity is available, ensuring no bug report is ever lost.

---

## Key Features

- **One-Touch In-Game Reporting** — Open a lightweight overlay at any time during play to capture screenshots, device metadata, and reproduction steps with a single gesture.
- **Local-First Queue** — All reports are persisted to local storage immediately, then synced to Jira Cloud in the background (offline-safe).
- **Automatic Context Capture** — Automatically attaches the current scene name, device info, Unity version, frame rate, and the last N log entries to every report.
- **Jira Cloud Integration** — Creates Jira issues directly via the Jira REST API v3, supporting custom fields, priority levels, and project/issue-type configuration.
- **Configurable UI & Hotkey** — The reporter overlay, trigger hotkey, and Jira project settings are all configurable through a scriptable-object-based settings asset, with no code changes required.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Game Engine | Unity 2022.3 LTS+ |
| Language | C# (.NET Standard 2.1) |
| Backend / Sync | Jira Cloud REST API v3 |
| Local Persistence | Unity `Application.persistentDataPath` + JSON |
| Auth | Jira API Token (Basic Auth over HTTPS) |
| Optional Backend | Supabase (for multi-device sync & dashboard) |
| Distribution | Unity Package Manager (UPM) via Git URL |

---

## Installation

### Via Unity Package Manager (UPM) — Git URL

1. Open Unity and navigate to **Window > Package Manager**.
2. Click the **+** button in the top-left corner and select **Add package from git URL…**
3. Enter the following URL:

```
https://github.com/RekonOps/Bug-OneTouch-unity.git#main
```

4. Click **Add**. Unity will download and import the package automatically.

### Configuration

After installation, create a settings asset:

1. In the **Project** window, right-click and select **Create > Bug OneTouch > Settings**.
2. Fill in your **Jira Cloud URL**, **Email**, **API Token**, and **Project Key**.
3. Assign the settings asset in the `BugReporter` component on your desired GameObject.

---

## Quick Start

```csharp
// Trigger the bug reporter programmatically
BugReporter.Instance.Show();

// Or use the default hotkey (configurable): F12
```

---

## Project Structure

```
Assets/
  BugOneTouch/
    Runtime/          # Core C# scripts (reporter, queue, Jira client)
    Editor/           # Unity Editor tooling & settings inspector
    UI/               # Default overlay prefabs & UI assets
    Tests/            # EditMode & PlayMode unit tests
docs/                 # PRD and design documents
.github/              # PR and Issue templates
```

---

## Roadmap

See the [CHANGELOG](CHANGELOG.md) for version history and upcoming work.

---

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a pull request.

---

## License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

Copyright 2026 RekonOps
