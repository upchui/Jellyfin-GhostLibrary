<div align="center">
  <img src="img/image.png" alt="GhostLibrary" width="180" />
  <h1>GhostLibrary</h1>

  [![Build](https://github.com/upchui/Jellyfin-GhostLibrary/actions/workflows/build.yml/badge.svg)](https://github.com/upchui/Jellyfin-GhostLibrary/actions/workflows/build.yml)
  [![Release](https://github.com/upchui/Jellyfin-GhostLibrary/actions/workflows/release.yml/badge.svg)](https://github.com/upchui/Jellyfin-GhostLibrary/actions/workflows/release.yml)
  [![Latest Release](https://img.shields.io/github/v/release/upchui/Jellyfin-GhostLibrary)](https://github.com/upchui/Jellyfin-GhostLibrary/releases/latest)
  [![Jellyfin](https://img.shields.io/badge/Jellyfin-10.9%2B-blue)](https://jellyfin.org)

  <p>A <a href="https://jellyfin.org">Jellyfin</a> server plugin that hides selected media libraries from the client home screen and library list — without blocking access for other plugins or the filesystem.</p>
</div>

## Why?

Jellyfin's built-in way to restrict a library is through user policies, which also blocks filesystem access. This breaks plugins like **Cinema Mode** that rely on direct library access to work.

GhostLibrary intercepts the API response before it reaches the client and silently removes the selected libraries from the list. Internally, everything keeps working as normal.

**What it hides:**
- The library from the home screen / library list (e.g. Android TV, web UI)
- The library from `/Users/{userId}/Views` and `/Items` API responses

**What it does NOT touch:**
- `ILibraryManager` — Cinema Mode and any other server-side plugin still see the library
- File system access — paths and permissions are unchanged
- User policies — no database modifications

## Installation

### Via Plugin Repository (recommended)

1. Open the Jellyfin dashboard
2. Go to **Plugins → Repositories**
3. Click **+** and add this URL:
   ```
   https://raw.githubusercontent.com/upchui/Jellyfin-GhostLibrary/main/manifest.json
   ```
4. Go to **Plugins → Catalog**, find **GhostLibrary** and install it
5. Restart Jellyfin

### Manual

1. Download the latest `GhostLibrary_x.x.x.x.zip` from [Releases](https://github.com/upchui/Jellyfin-GhostLibrary/releases)
2. Extract `Jellyfin.Plugin.GhostLibrary.dll` into your Jellyfin plugins directory:
   ```
   # Linux
   ~/.config/jellyfin/plugins/GhostLibrary_1.0.0.1/

   # Windows
   %AppData%\Jellyfin\plugins\GhostLibrary_1.0.0.1\
   ```
3. Restart Jellyfin

## Configuration

1. Go to **Dashboard → Plugins → GhostLibrary**
2. Your libraries are loaded automatically — tick every library you want to hide
3. Click **Save**

Multiple libraries can be hidden at the same time.

> **Android TV / first use:** After saving, clear the Jellyfin app cache once so the app discards its old cached library list.

## How It Works

GhostLibrary registers a global ASP.NET Core `IAsyncActionFilter` via `PostConfigure<MvcOptions>`. The filter runs after every controller action and checks whether the response contains a `QueryResult<BaseItemDto>`. If it does, any `CollectionFolder` items matching the configured library IDs are removed before the response is serialized.

The filter also replaces the `ETag` response header with a hash of the filtered result, ensuring clients that support conditional HTTP requests do not serve stale cached responses.

## Build from Source

No local .NET SDK required — Docker handles the build:

```bash
# Clone
git clone https://github.com/upchui/Jellyfin-GhostLibrary.git
cd Jellyfin-GhostLibrary

# Build (output lands in ./dist/)
docker run --rm \
  -v "$(pwd)":/src \
  -v "$(pwd)/dist":/output \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet publish -c Release -o /output \
    /p:DebugType=None /p:DebugSymbols=false
```

## Compatibility

| Jellyfin | Plugin | .NET |
|----------|--------|------|
| 10.9.x   | 1.x    | 8.0  |

## License

MIT
