# QuickRoute2

QuickRoute2 is a GPS route analysis application primarily designed for orienteering. It overlays GPS tracks on map images and helps you analyze route choice, pace and performance.

QuickRoute2 is a cross-platform application for Windows and Linux.

It is based on [QuickRoute](https://github.com/matstroeng/quickroute) by Mats Troeng and Jörgen Ohlin. QuickRoute2 is an independent derivative work and is not an official release of the original project.

## Features

* Display GPS routes on map images
* Import routes from GPX, TCX, FIT, FRWD and Navilock files
* Open documents created by the original QuickRoute
* Adjust and crop routes
* Rotate maps
* Undo and redo route adjustments
* Color routes by pace and other values
* Display split times, momentary information and graphs
* Download split times from WinSplits and ORIS
* Export maps and routes to JPEG, PNG, KMZ, GPX and XML

## Documents

QuickRoute2 saves new documents using the `.qrtx` format.

A `.qrtx` file contains the QuickRoute2 document data together with its map image.

Documents created by the original QuickRoute (`.qrt`) can also be opened. Legacy `.qrt` files are imported safely and are not overwritten; saving creates a new `.qrtx` document.

## Supported files

### Import

* QuickRoute2 documents (`.qrtx`)
* QuickRoute documents (`.qrt`)
* GPX
* TCX
* FIT
* FRWD
* Navilock
* KMZ maps
* JPEG, PNG, GIF, BMP and WebP map images

### Export

* JPEG
* PNG
* KMZ
* GPX
* Route data XML

JPEG exports can contain embedded QuickRoute route data compatible with the original QuickRoute application.

## User data

QuickRoute2 stores its settings in the current user's configuration directory.

* Windows: `%APPDATA%\QuickRoute2`
* Linux: `$XDG_CONFIG_HOME/QuickRoute2` or `~/.config/QuickRoute2`

The application does not store personal settings or documents in its program directory.

## Building from source

QuickRoute2 requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build QuickRoute.sln
dotnet test QuickRoute.sln
dotnet run --project src/QuickRoute.App
```

QuickRoute2 uses [.NET 10](https://dotnet.microsoft.com/) and [Avalonia UI](https://avaloniaui.net/).

Some internal project and namespace names intentionally retain the `QuickRoute` prefix because they are required for compatibility with legacy `.qrt` documents.

## License

QuickRoute2 is licensed under the GNU General Public License version 3.

See [LICENSE](LICENSE) for the full license text.

The original QuickRoute is Copyright © Mats Troeng, Jörgen Ohlin and QuickRoute contributors.

QuickRoute2 is a modified version of QuickRoute, ported to .NET 10 and Avalonia in 2026.
