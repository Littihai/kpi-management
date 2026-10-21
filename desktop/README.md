# Helpdesk for Windows

An Electron shell around the web frontend. It packs the built `frontend/` and talks to the same API as the website.

## Build the installer

```bash
cd desktop
npm install
npm run dist        # -> dist/Helpdesk-Setup-<version>.exe
```

`npm start` builds the web app and opens it in a window (no installer). `npm run pack` makes an unpacked folder for a quick check.

To point the app at another API, set `VITE_API_URL` before building (default: the deployed Render API).

## Releasing a new version (automatic updates)

```bash
git tag v1.1.0 && git push origin v1.1.0
```

The `Desktop release` workflow builds the installer on GitHub and publishes it as a release; the version is taken from the
tag (the `version` in `package.json` is only the local default and need not be bumped). Installed apps check for a new
release shortly after start and every ~4 hours, download it in the background and install it when the app is closed
(or right away via "รีสตาร์ตเพื่ออัปเดต"). Use a higher version than the previous release each time.

- The first installer that contains auto-update must be installed manually once; older installs cannot update themselves.
- The installer is not signed, so SmartScreen warns on first install (More info -> Run anyway).
- The repo is public, so anyone can download the installer from the Releases page.
- Updates are not tested until a second release exists to update to.

### Forcing old versions to update

The API exposes `GET /api/client-config` returning `minDesktopVersion` and `latestDesktopVersion`, from the settings
`Client:MinDesktopVersion` (default `1.0.0`) and `Client:LatestDesktopVersion` (env vars `Client__MinDesktopVersion`
etc. on Render). An app older than the minimum shows a blocking "update required" screen. If the API is unreachable,
nothing is blocked.

## The API must allow the app's origin

The window loads from `app://local`, so the API's CORS list has to include it. It is already in the default list in
`ServiceRegistration.AddFrontendCors`; on Render nothing needs to be set once this version is deployed.

## Notes

- The installer is not code-signed, so Windows SmartScreen shows "unknown publisher" (More info -> Run anyway). A code-signing certificate removes it.
- Installs per user (no admin rights needed), with Desktop and Start Menu shortcuts and a normal uninstaller.
- Requires an internet connection (the API is online).
