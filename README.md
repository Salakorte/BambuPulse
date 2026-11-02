# BambuLab Status Widget

A compact, dark Windows companion for keeping an eye on a Bambu Lab printer. It brings print progress, temperatures, AMS slots, and a personal filament library together in one calm desktop view.

## Features

- Live printer status and job progress through Bambu Cloud.
- No always-on-top behavior; optional compact widget mode with settings in the system tray.
- Nozzle and bed temperature gauges, remaining time, and AMS spool overview.
- Personal filament library with colors, remaining weight, AMS slot assignments, and print history.
- Automatic filament usage lookup when Bambu Cloud provides it, with a manual history entry when it does not.
- Read-only printer monitoring; this app does not send print-control commands.

## Requirements

- Windows 10 or 11.
- .NET 8 SDK.
- A Bambu Lab account and a printer linked to that account.

## Run from source

```powershell
dotnet run
```

Open **Settings**, enter the email for your Bambu Lab account, and verify the one-time code sent by email. Select the linked printer if your account has more than one.

## Local data and privacy

Account and printer configuration, preferences, and the filament library are stored under `%LOCALAPPDATA%\BambuP1SWidget`. They are per-user local files and are excluded from Git by `.gitignore`. The cloud access token is protected with Windows DPAPI for the current Windows user. No account, token, printer serial number, or inventory data is required in this repository.

The app connects to Bambu Cloud and uses MQTT for printer status. Some authentication and print-history endpoints are community-observed rather than part of a documented public API, so Bambu may change them. Filament consumption estimates may be unavailable or differ from actual usage.

## Build

```powershell
dotnet build
```

## Project status

This is an independent community project and is not affiliated with or endorsed by Bambu Lab. UI references are credited in [DESIGN.md](DESIGN.md).

