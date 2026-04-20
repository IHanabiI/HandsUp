# Hands Up

`Hands Up` (`举手手`) is a utility mod for Slay the Spire II.

It adds a single `Hands Up` entry to the ESC menu and provides several rollback and restart tools for testing routes, replaying rooms, and recovering from mistakes during a run.

## Main features

- Restart
- Soft Restart
- Return to Previous Floor
- Return to Previous Step

## Dependencies

- BaseLib

This project depends on BaseLib and follows the Slay the Spire II modding workflow built around BaseLib, Harmony, Godot 4.5.1 Mono, and .NET 9.

## Project structure

- `HandsUpCode/`
  Main source code for the mod.
- `HandsUp/`
  Mod assets.
- `tools/`
  Small helper scripts used during development.

## Local build requirements

To build this project locally, you need:

- Slay the Spire II
- Godot 4.5.1 Mono
- .NET 9 SDK
- BaseLib

The project uses `Sts2PathDiscovery.props` to detect the game install path automatically. If automatic detection does not work on your machine, create your own local build configuration instead of committing machine-specific paths into the repository.

## Notes

- This repository is the source project.
- Release packages for end users should be distributed separately.
- The current release line is `v0.1.0-beta`.
