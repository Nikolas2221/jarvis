# Release

Jarvis uses GitHub Releases for installation packages and online updates.

## Publish a new version

1. Update `<Version>` in `Jarvis.csproj`.
2. Commit and push changes.
3. Create and push a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions will build:

- `Jarvis-{version}-win-x64.zip` - installed app package.
- `JarvisInstaller-{version}.exe` - downloader/installer.
- `update-manifest.json` - online update manifest.

## Update flow

Installed copies contain `update-settings.json` with:

```json
{
  "manifestUrl": "https://github.com/Nikolas2221/jarvis/releases/latest/download/update-manifest.json"
}
```

The app checks that manifest from the `Update` button and installs a newer zip in place.
