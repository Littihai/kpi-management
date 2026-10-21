// Builds the web frontend and copies it into desktop/app, which is what the installer packs.
// Set VITE_API_URL before running to point the app at a different API.
const { execSync } = require('node:child_process')
const fs = require('node:fs')
const path = require('node:path')

const frontend = path.resolve(__dirname, '..', '..', 'frontend')
const target = path.resolve(__dirname, '..', 'app')
const run = (cmd) => execSync(cmd, { cwd: frontend, stdio: 'inherit', env: { ...process.env, VITE_DESKTOP: '1' } })

if (!fs.existsSync(path.join(frontend, 'node_modules'))) run('npm ci')
run('npm run build')
fs.rmSync(target, { recursive: true, force: true })
fs.cpSync(path.join(frontend, 'dist'), target, { recursive: true })
console.log('web build copied to', target)
