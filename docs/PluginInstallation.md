# Installing **Jellyfin.Plugin.MyEpisodes**

This document covers two scenarios:

1. **Direct installation** – copy the plugin files manually to a Jellyfin instance (e.g., a Home‑Assistant add‑on on a Raspberry Pi).
2. **GitHub‑hosted distribution** – publish the plugin as a GitHub release and let Jellyfin discover it automatically via a manifest.

---

## 📦 Direct (manual) installation

### 1️⃣ Build the plugin locally
```bash
cd /Users/peti/Developer/Repos/Jellyfin.Plugin.MyEpisodes
# Restore NuGet packages (once)
dotnet restore
# Publish a Release build for .NET 9 targeting the specific plugin project
dotnet publish Jellyfin.Plugin.MyEpisodes/Jellyfin.Plugin.MyEpisodes.csproj -c Release -f net9.0 -o ./publish
```
The `./publish` folder now contains:
```
Jellyfin.Plugin.MyEpisodes.dll
Jellyfin.Plugin.MyEpisodes.deps.json
Jellyfin.Plugin.MyEpisodes.runtimeconfig.json
```

### 2️⃣ Determine where the Jellyfin add‑on expects plugins
* Home Assistant OS typically mounts the add‑on data at **/config** inside the container and exposes it on the host at **/share/jellyfin** (or directly under **/config/plugins**).  Verify the exact path by opening a terminal on the Pi and listing the directory:
```bash
ls -l /share/jellyfin/plugins   # or /config/plugins
```
The folder that contains a `plugins` sub‑directory is the one you will use.

### 3️⃣ Copy the built files to the Pi
```bash
# Adjust the remote host/IP and path discovered in step 2
scp -r ./publish/* pi@raspberrypi.local:/share/jellyfin/plugins/MyEpisodes/
```
*(If you prefer a zip, copy `MyEpisodes-plugin.zip` instead.)*

### 4️⃣ Fix ownership / permissions (Home Assistant runs add‑ons as UID 1000)
```bash
ssh pi@raspberrypi.local "chown -R 1000:1000 /share/jellyfin/plugins/MyEpisodes && chmod -R 755 /share/jellyfin/plugins/MyEpisodes"
```

### 5️⃣ Restart the Jellyfin add‑on
*Via UI*: **Supervisor → Add‑on → Jellyfin → Restart**
*Or via CLI*: `ha addons restart core_jellyfin`

### 6️⃣ Verify the plugin loads
Open the Jellyfin UI (e.g. `http://<pi‑ip>:8096`), go to **Dashboard → Plugins → MyEpisodes**.  You should see a configuration page.  The server log will contain a line like:
```
[INFO] Loading plugin MyEpisodes v1.0.0
```
If the plugin does not appear, repeat steps 4‑5 and check the log for permission or path errors.

---

## 🌐 GitHub‑hosted distribution (automatic install via Jellyfin UI)

### 1️⃣ Add a **manifest.json** to the repository root
```jsonc
// manifest.json
{
  "name": "MyEpisodes",
  "guid": "ef8b2e7c-cb1a-4a5b-9f9c-123456789abc",
  "description": "Sync watch‑progress with MyEpisodes.net",
  "overview": "Provides automatic episode‑matching, position sync and localisation support for MyEpisodes.",
  "owner": "Novak‑Peter",
  "version": "1.0.0",
  "targetAbi": "10.9.0",
  "category": "Synchronization",
  "url": "https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes",
  "sourceUrl": "https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes",
  "imageUrl": "https://raw.githubusercontent.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/main/docs/icon.png",
  "changelog": "https://github.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/blob/main/CHANGELOG.md"
}
```
*The `guid` must be generated once (e.g., via https://www.guidgenerator.com) and never changed.*

### 2️⃣ Create a GitHub Actions workflow that builds & publishes a zip on every tag
Create `.github/workflows/release.yml` with the following contents:
```yaml
name: Build & Release Plugin

on:
  push:
    tags:
      - 'v*'   # tags like v1.0.0, v2.1.3

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore & Publish
        run: |
          dotnet restore
          dotnet publish Jellyfin.Plugin.MyEpisodes/Jellyfin.Plugin.MyEpisodes.csproj -c Release -f net9.0 -o ./publish

      - name: Prepare zip layout
        run: |
          mkdir -p ./publish/MyEpisodes
          cp ./publish/*.dll ./publish/*.deps.json ./publish/*.runtimeconfig.json ./publish/MyEpisodes/
          cp manifest.json ./publish/MyEpisodes/
          cd ./publish && zip -r MyEpisodes.zip MyEpisodes

      - name: Create GitHub Release with zip asset
        uses: softprops/action-gh-release@v2
        with:
          files: ./publish/MyEpisodes.zip
          generate_release_notes: true
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```
This workflow:
1. Triggers on any tag that starts with `v`.
2. Publishes the plugin binaries.
3. Packages them (including the `manifest.json`) into `MyEpisodes.zip`.
4. Attaches the zip to a GitHub **Release**.

### 3️⃣ Tag & push a new version
```bash
git tag v1.0.0   # bump as appropriate
git push origin v1.0.0
```
GitHub Actions will run and a new Release will appear with the zip asset.

### 4️⃣ Host the manifest for Jellyfin to discover
The raw file URL works out‑of‑the‑box:
```
https://raw.githubusercontent.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/main/manifest.json
```
If you prefer a *single repository list* (useful when you have many custom plugins), create `repositories.json` in the repo root:
```json
[
  "https://raw.githubusercontent.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/main/manifest.json"
]
```
Enable **GitHub Pages** (Settings → Pages → source: `main` branch / root) and the file will be served at:
```
https://Novak-Peter.github.io/Jellyfin.Plugin.MyEpisodes/repositories.json
```
You can give this URL to other Jellyfin installations.

### 5️⃣ Add the custom repository inside Jellyfin
1. Open **Dashboard → Plugins → Repositories**.
2. Click **Add Repository**.
3. Paste either the raw `manifest.json` URL **or** the `repositories.json` page URL.
4. Click **Save** and then **Refresh**.
5. The *MyEpisodes* plugin will appear in the catalog and can be installed with a single click.  Future tags that bump the `version` field will be offered as updates automatically.

---

## 📌 Quick reference cheat‑sheet
```bash
# --- Direct install ---
cd /Users/peti/Developer/Repos/Jellyfin.Plugin.MyEpisodes
dotnet publish Jellyfin.Plugin.MyEpisodes/Jellyfin.Plugin.MyEpisodes.csproj -c Release -f net9.0 -o ./publish
scp -r ./publish/* pi@raspberrypi.local:/share/jellyfin/plugins/MyEpisodes/
ssh pi@raspberrypi.local "chown -R 1000:1000 /share/jellyfin/plugins/MyEpisodes && chmod -R 755 /share/jellyfin/plugins/MyEpisodes"
ha addons restart core_jellyfin   # or restart via UI

# --- GitHub‑hosted workflow ---
# 1. Add manifest.json (see above)
# 2. Add .github/workflows/release.yml (see above)
# 3. Tag a new version
git tag v1.0.0
git push origin v1.0.0   # triggers CI and creates a Release with MyEpisodes.zip
# 4. In Jellyfin UI → Dashboard → Plugins → Repositories, add:
#    https://raw.githubusercontent.com/Novak-Peter/Jellyfin.Plugin.MyEpisodes/main/manifest.json
```

---

### 🎉 You’re all set!
* Use the **direct** method for quick testing on your Raspberry Pi.
* Switch to the **GitHub‑hosted** method once you want a clean, upgrade‑friendly distribution that any Jellyfin server can install automatically.

Feel free to open an issue on the GitHub repo if you run into trouble or want to add more documentation (screenshots, changelog, etc.). Happy syncing!
