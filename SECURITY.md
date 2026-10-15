# Security

## Reporting a problem

If you find a security problem in Factory Seat, or a download that doesn't look right, **please report it privately**, not in a public issue:

1. Go to the [Security tab](https://github.com/factoryseatlmuapp/factory-seat/security).
2. Press **Report a vulnerability** and describe what you found.

Only the maintainers can see the report. You'll get an answer there.

## Getting the real app

Factory Seat is only published in two places:

- [GitHub Releases](https://github.com/factoryseatlmuapp/factory-seat/releases)
- Its OverTake listing, which links back here

Each release's notes list the installer's **SHA-256 hash** and its **VirusTotal** result. If a copy from anywhere else has a different hash, don't run it.

## What the app does on your PC

- It reads LMU's results files and race weekend saves. It never changes LMU's files.
- It saves your careers and settings in `%AppData%\LmuCareer`.
- Its only online request is an optional check for a newer release on GitHub, which you can turn off in Settings. It collects no data and has no accounts or servers. The [README](README.md) has the details.
