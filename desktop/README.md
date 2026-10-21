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

## The API must allow the app's origin

The window loads from `app://local`, so the API's CORS list has to include it. It is already in the default list in
`ServiceRegistration.AddFrontendCors`; on Render nothing needs to be set once this version is deployed.

## Notes

- The installer is not code-signed, so Windows SmartScreen shows "unknown publisher" (More info -> Run anyway). A code-signing certificate removes it.
- Installs per user (no admin rights needed), with Desktop and Start Menu shortcuts and a normal uninstaller.
- Requires an internet connection (the API is online).
