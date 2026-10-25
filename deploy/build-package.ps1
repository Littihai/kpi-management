<#
.SYNOPSIS
  Builds the server package: helpdesk-server-<version>.zip, the thing an administrator unpacks and installs.

.DESCRIPTION
  The package holds
    api\          the program (self-contained for Windows x64: no .NET needs to be installed) with the web app in api\wwwroot
    sqlserver\    the three SQL scripts
    install.ps1   the installer
    README-th.md  the guide
  Needs the .NET SDK and Node.js on the machine that builds it.

.EXAMPLE
  .\build-package.ps1 -Version 1.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = 'dev',
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) ("helpdesk-package-" + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $work 'helpdesk-server'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'api') | Out-Null

function Run($command, $arguments, $directory) {
    Push-Location $directory
    try { & $command @arguments; if ($LASTEXITCODE -ne 0) { throw "$command $($arguments -join ' ') failed ($LASTEXITCODE)" } } finally { Pop-Location }
}

Write-Host '== Program (self-contained, win-x64)'
Run 'dotnet' @('publish', 'KPI.API/KPI.API.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', (Join-Path $root 'api')) (Join-Path $repo 'backend')

Write-Host '== Settings (the development settings of the repository stay out of the package)'
# The repository's appsettings.json holds the settings of the developers' own deployment (a connection string, keys). A package
# goes to other companies and must carry none of that: keep only what is not secret. install.ps1 writes each company's own
# settings (appsettings.Production.json), and a server started without any waits on the setup page.
$apiOut = Join-Path $root 'api'
$source = Get-Content (Join-Path $apiOut 'appsettings.json') -Raw | ConvertFrom-Json
$clean = [ordered]@{}
foreach ($name in @('Logging', 'AllowedHosts')) { if ($source.PSObject.Properties.Name -contains $name) { $clean[$name] = $source.$name } }
$clean['Jwt'] = [ordered]@{ Issuer = $source.Jwt.Issuer; Audience = $source.Jwt.Audience }
$cleanJson = $clean | ConvertTo-Json -Depth 8
if ($cleanJson -match 'ConnectionStrings|"Key"|ApiKey|Password|Host=|Server=') { throw 'The package settings still look like they hold a secret. Stopping.' }
[IO.File]::WriteAllText((Join-Path $apiOut 'appsettings.json'), $cleanJson, (New-Object Text.UTF8Encoding($false)))
Remove-Item (Join-Path $apiOut 'appsettings.Example.json') -ErrorAction SilentlyContinue
# dotnet publish copies a developer's local settings file when there is one; it must never travel in a package.
Remove-Item (Join-Path $apiOut 'appsettings.Development.json') -ErrorAction SilentlyContinue

Write-Host '== Web app (same address as the API, so no API address is baked in)'
$frontend = Join-Path $repo 'frontend'
if (-not (Test-Path (Join-Path $frontend 'node_modules'))) { Run 'npm' @('ci') $frontend }
# An empty VITE_API_URL means "the page's own address". It has to come from a file: an empty environment variable does not exist in PowerShell.
$envFile = Join-Path $frontend '.env.production.local'
$hadEnvFile = Test-Path $envFile
$previous = if ($hadEnvFile) { Get-Content $envFile -Raw } else { $null }
try {
    Set-Content -Path $envFile -Value "VITE_API_URL=`n" -Encoding ASCII
    Run 'npm' @('run', 'build') $frontend
}
finally {
    if ($hadEnvFile) { Set-Content -Path $envFile -Value $previous -NoNewline } else { Remove-Item $envFile -ErrorAction SilentlyContinue }
}
$bakedIn = Select-String -Path (Join-Path $frontend 'dist\assets\*.js') -Pattern 'onrender\.com' -List
if ($bakedIn) { throw "The web build still contains an API address ($($bakedIn.Path)). VITE_API_URL was not applied." }
Copy-Item (Join-Path $frontend 'dist') (Join-Path $root 'api\wwwroot') -Recurse

Write-Host '== Scripts and guide'
Copy-Item (Join-Path $PSScriptRoot 'sqlserver') (Join-Path $root 'sqlserver') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $root
Copy-Item (Join-Path $PSScriptRoot 'README-th.md') $root
Set-Content -Path (Join-Path $root 'VERSION.txt') -Value $Version -Encoding ASCII

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$zip = Join-Path $Output "helpdesk-server-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $root -DestinationPath $zip
Remove-Item $work -Recurse -Force
Write-Host ''
Write-Host ("Built {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
