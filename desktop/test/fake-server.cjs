// Stands in for KPI.API.exe in the tests: listens where ASPNETCORE_URLS says and shows what it was started with.
const http = require('node:http')

const mode = process.env.FAKE_MODE || 'ok'
if (mode === 'exit') process.exit(3)

const base = new URL(process.env.ASPNETCORE_URLS)
http.createServer((req, res) => {
  if (mode === 'silent') return   // never answers
  res.setHeader('Content-Type', 'application/json')
  if (req.url === '/env') {
    return res.end(JSON.stringify({
      configFile: process.env.Setup__ConfigFile,
      keyLength: (process.env.Jwt__Key || '').length,
      key: process.env.Jwt__Key,
      parent: process.env.HELPDESK_PARENT_PID,
      environment: process.env.ASPNETCORE_ENVIRONMENT,
      pid: process.pid,
    }))
  }
  res.end(JSON.stringify({ status: 'setup' }))
}).listen(Number(base.port), base.hostname)
