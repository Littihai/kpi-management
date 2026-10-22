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

## Which server the app talks to

The installer ships pointing at our hosted server. A company that runs its own server (see `deploy/README-th.md`) points the app at it in one of three ways; the first that applies wins:

1. **The person chooses it** on the sign-in page ("เปลี่ยน" next to "เซิร์ฟเวอร์") or in Settings > About. The address is tested before it is used (it must answer `/api/client-config` like a Helpdesk server), remembered on that computer, and changing it signs the person out. "Back to the default" undoes it.
2. **IT sets it for the computer**, so people never have to type it: the `HELPDESK_SERVER_URL` environment variable, or the first line of `%ProgramData%\Helpdesk\server.txt` (it survives app updates). Example: `http://helpdesk.company.local:8080`.
3. **The build** (`VITE_API_URL`), for a web build served by the API itself (empty means the page's own address).

An address is the server's origin only (`http(s)://host[:port]`, no path). `http` works (verified from the app to a non-local address) but is not encrypted, so the dialog warns about it; `https` needs a certificate the computer trusts (an internal CA installed in Windows works). The server must allow the app's origin (`app://local`), which it does by default.

In a web build the choice is hidden unless it was built with `VITE_ALLOW_SERVER_CHOICE=true`: a web page belongs to the server that serves it.

## The API must allow the app's origin

The window loads from `app://local`, so the API's CORS list has to include it. It is already in the default list in
`ServiceRegistration.AddFrontendCors`; on Render nothing needs to be set once this version is deployed.

## Notes

- The installer is not code-signed, so Windows SmartScreen shows "unknown publisher" (More info -> Run anyway). A code-signing certificate removes it.
- Installs per user (no admin rights needed), with Desktop and Start Menu shortcuts and a normal uninstaller.
- Requires an internet connection (the API is online).
