// Publishes the Helpdesk server (the program a company installs as a Windows service) into desktop/api, which the installer packs as the
// app's built-in server. Self-contained for Windows x64, so no .NET has to be installed on the person's PC. Needs the .NET SDK.
//
// Like deploy/build-package.ps1 it keeps the repository's own settings out: only what is not secret stays in appsettings.json, and the
// developer's local settings file is removed, because dotnet publish copies it when it exists.
const { execFileSync } = require('node:child_process')
const fs = require('node:fs')
const path = require('node:path')

const project = path.resolve(__dirname, '..', '..', 'backend', 'KPI.API', 'KPI.API.csproj')
const target = path.resolve(__dirname, '..', 'api')

fs.rmSync(target, { recursive: true, force: true })
execFileSync('dotnet', [
  'publish', project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
  '-p:DebugType=None', '-p:DebugSymbols=false', '-o', target,
], { stdio: 'inherit' })

const settings = path.join(target, 'appsettings.json')
const source = JSON.parse(fs.readFileSync(settings, 'utf8'))
const clean = {}
for (const name of ['Logging', 'AllowedHosts']) if (name in source) clean[name] = source[name]
clean.Jwt = { Issuer: source.Jwt && source.Jwt.Issuer, Audience: source.Jwt && source.Jwt.Audience }
const text = JSON.stringify(clean, null, 2)
if (/ConnectionStrings|"Key"|ApiKey|Password|Host=|Server=/.test(text)) throw new Error('The built-in server settings still look like they hold a secret. Stopping.')
fs.writeFileSync(settings, text)
for (const extra of ['appsettings.Example.json', 'appsettings.Development.json']) fs.rmSync(path.join(target, extra), { force: true })

if (!fs.existsSync(path.join(target, 'KPI.API.exe'))) throw new Error('KPI.API.exe was not produced')
console.log('built-in server published to', target)
