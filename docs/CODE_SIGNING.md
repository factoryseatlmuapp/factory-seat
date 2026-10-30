# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## What gets signed

Only official releases: the Windows app (`FactorySeat.exe`) and its installer (`FactorySeat-Setup-<version>.exe`),
as published on this repository's [Releases](https://github.com/factoryseatlmuapp/factory-seat/releases) page. They are built by GitHub Actions
([release workflow](https://github.com/factoryseatlmuapp/factory-seat/blob/main/.github/workflows/release.yml)) from the tagged commit in this public repository, and nothing
built on a personal machine is ever signed.

## Team roles

| Role | Who |
|---|---|
| Committers and reviewers | [factoryseatlmuapp](https://github.com/factoryseatlmuapp) (maintainer) |
| Approvers | [factoryseatlmuapp](https://github.com/factoryseatlmuapp) (maintainer) |

Contributions from others (translations, fixes) arrive as pull requests and are reviewed by the maintainer before
they are merged. Every signing request for a release is approved by hand.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it, with one exception: when it starts, it asks GitHub whether a newer release
exists (a plain request to GitHub's public releases API, carrying no personal data). This check can be turned off in
Settings. The app collects no data and has no accounts or servers of its own.

The app's window is drawn by Microsoft's WebView2 runtime, a Windows component that may contact Microsoft for its own
updates, as it does for every app that uses it.
