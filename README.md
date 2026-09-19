# Jellyfin.Plugin.MyEpisodes

[![Build & Release Plugin](https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/actions/workflows/release.yml/badge.svg)](https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/actions/workflows/release.yml)
[![Jellyfin Minimum Version](https://img.shields.io/badge/Jellyfin-v10.9.0%2B-blue.svg)](https://jellyfin.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Jellyfin.Plugin.MyEpisodes** is a Jellyfin server plugin that automatically syncs TV episode watch progress and library acquisition state with [MyEpisodes.net](https://www.myepisodes.com/).

---

## ✨ Features

- 📺 **Automatic Watched Sync**: Automatically marks episodes as watched on MyEpisodes when completed in Jellyfin.
- 📥 **Acquired Status Sync**: Updates episode status to acquired when new episodes are added to Jellyfin libraries.
- 👥 **Multi-User Configuration**: Per-user mapping between Jellyfin users and their MyEpisodes API keys.
- ⚙️ **Customizable Preferences**: Enable or disable Watched or Acquired synchronization toggles per user.

---

## 🚀 Installation

### Option 1: Jellyfin Plugin Repository (Recommended)

1. Open your Jellyfin Web Interface.
2. Go to **Dashboard** → **Plugins** → **Repositories**.
3. Click **Add Repository** (`+`).
4. Enter the repository details:
   - **Repository Name**: `MyEpisodes Plugin`
   - **Repository URL**: 
     ```
     https://raw.githubusercontent.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/main/manifest.json
     ```
5. Click **Save** and then **Refresh Repositories**.
6. Go to **Catalog**, search for **MyEpisodes**, and click **Install**.
7. Restart your Jellyfin server.

### Option 2: Manual Installation

1. Download `MyEpisodes.zip` from the latest [GitHub Release](https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/releases).
2. Extract the contents into your Jellyfin `plugins/MyEpisodes` directory:
   - **Linux / Docker / Home Assistant**: `/config/plugins/MyEpisodes/` or `/share/jellyfin/plugins/MyEpisodes/`
   - **Windows**: `%AppData%\jellyfin\plugins\MyEpisodes\`
3. Restart your Jellyfin server.

For detailed Home Assistant / Raspberry Pi setup steps, check out [PluginInstallation.md](file:///Users/peti/Developer/Repos/Jellyfin.Plugin.MyEpisodes/docs/PluginInstallation.md).

---

## ⚙️ Configuration

1. In Jellyfin, navigate to **Dashboard** → **Plugins** → **MyEpisodes**.
2. Select your Jellyfin user profile.
3. Enter your **MyEpisodes API Key** / credentials.
4. Toggle your synchronization preferences:
   - **Sync Watched Progress**: Send watched status updates when playback completes.
   - **Sync Acquired Status**: Mark episodes as acquired when downloaded/added to media libraries.
5. Click **Save**.

---

## 🛠️ Development & Building

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)

### Building Locally

```bash
# Clone the repository
git clone https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes.git
cd Jellyfin.Plugin.MyEpisodes

# Restore dependencies
dotnet restore

# Build release binary
dotnet publish Jellyfin.Plugin.MyEpisodes/Jellyfin.Plugin.MyEpisodes.csproj -c Release -f net9.0 -o ./publish
```

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!  
Feel free to check the [issues page](https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/issues).

---

## 📜 License

Distributed under the MIT License. See `LICENSE` for more information.
