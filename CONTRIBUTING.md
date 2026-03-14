# Contributing to BugBeacon-unity

Thank you for your interest in contributing! This document outlines the process for reporting issues, proposing features, and submitting pull requests.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Branch Strategy](#branch-strategy)
- [How to Contribute](#how-to-contribute)
- [Commit Message Guidelines](#commit-message-guidelines)
- [Pull Request Process](#pull-request-process)
- [Reporting Bugs](#reporting-bugs)
- [Requesting Features](#requesting-features)

---

## Code of Conduct

Please be respectful and professional in all interactions. We are committed to maintaining a welcoming environment for everyone.

---

## Getting Started

1. **Fork** the repository on GitHub.
2. **Clone** your fork locally:
   ```bash
   git clone https://github.com/<your-username>/BugBeacon-unity.git
   ```
3. Add the upstream remote:
   ```bash
   git remote add upstream https://github.com/GaoZombie/BugBeacon-unity.git
   ```
4. Open the project in **Unity 2022.3 LTS** or later.

---

## Branch Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Stable, release-ready code |
| `develop` | Integration branch for ongoing work |
| `feature/<name>` | New features — branch from `develop` |
| `fix/<name>` | Bug fixes — branch from `develop` |
| `hotfix/<name>` | Critical fixes — branch from `main` |

All pull requests should target the **`develop`** branch, unless it is a hotfix.

---

## How to Contribute

1. Check existing [Issues](https://github.com/GaoZombie/BugBeacon-unity/issues) to avoid duplicates.
2. Create a new branch from `develop`:
   ```bash
   git checkout develop
   git pull upstream develop
   git checkout -b feature/your-feature-name
   ```
3. Make your changes, following the code style of the existing codebase.
4. Write or update tests where applicable.
5. Commit your changes (see [Commit Message Guidelines](#commit-message-guidelines)).
6. Push to your fork and open a Pull Request targeting `develop`.

---

## Commit Message Guidelines

Use clear, descriptive commit messages. Format:

```
<type>: <short summary>

[optional body]
[optional footer]
```

**Types:**

| Type | When to use |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `refactor` | Code refactoring (no feature/fix) |
| `test` | Adding or updating tests |
| `chore` | Build process or tooling changes |

**Example:**
```
feat: add screenshot capture to bug reporter overlay

Captures the current frame before opening the overlay so the
screenshot reflects the exact state at the time of reporting.
```

---

## Pull Request Process

1. Ensure your branch is up to date with `develop` before opening a PR.
2. Fill out the [Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md) completely.
3. Link any related issues using `Closes #<issue-number>`.
4. Request a review from at least one maintainer.
5. Address all review comments before the PR can be merged.
6. PRs are merged using **Squash and Merge** to keep the history clean.

---

## Reporting Bugs

Use the [Bug Report template](.github/ISSUE_TEMPLATE/bug_report.md) to file a new issue. Please include:

- Unity version and target platform
- Steps to reproduce
- Expected vs. actual behavior
- Logs or screenshots if available

---

## Requesting Features

Use the [Feature Request template](.github/ISSUE_TEMPLATE/feature_request.md). Please describe:

- The problem you are trying to solve
- Your proposed solution
- Any alternatives you have considered

---

Thank you for helping make BugBeacon-unity better!
