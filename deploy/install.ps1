<#
.SYNOPSIS
  Installs (or upgrades) the Helpdesk server on Windows with SQL Server.

.DESCRIPTION
  First install: asks for the SQL Server, the company and the first administrator, then
    1. creates the database and the account the application uses   (sqlserver\01-create-database.sql)
    2. creates the tables                                         (sqlserver\02-schema.sql)
    3. adds the roles and permissions                             (sqlserver\03-seed.sql)
    4. copies the application to -InstallDir and writes its settings (secrets generated here, file locked down)
    5. creates the first administrator
    6. installs and starts a Windows service, and checks that it answers.
  The web app is served by the same service, so no IIS, nginx or Docker is needed.

  Upgrade (-Upgrade): stops the service, replaces the program files, keeps the settings, starts it again.
  The database is brought up to date by the application itself when it starts.

  Run it from an elevated PowerShell (Run as administrator) in the folder you unpacked the package into.
  Needs the sqlcmd command-line tool (part of "SQL Server command line utilities") on the PATH.

.EXAMPLE
  .\install.ps1
  Asks for everything it needs.

.EXAMPLE
  .\install.ps1 -SqlServer SQL01 -Database helpdesk -AppAuth Windows -AppLogin 'CORP\svc-helpdesk' -ServiceAccount 'CORP\svc-helpdesk' -Port 8080
  Application signs in to SQL Server with a Windows account, and the service runs as that account.

.EXAMPLE
  .\install.ps1 -Upgrade
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Helpdesk',
    [string]$ServiceName = 'Helpdesk',
    [int]$Port = 8080,

    # SQL Server: SQL01, SQL01\SQLEXPRESS, 10.0.0.5,1433 or (localdb)\MSSQLLocalDB
    [string]$SqlServer,
    [string]$Database = 'helpdesk',
    # Sql = the application signs in with a SQL login this script creates; Windows = with a Windows account
    [ValidateSet('Sql', 'Windows')][string]$AppAuth = 'Sql',
    # Sql: the login to create (default helpdesk_app). Windows: the account, e.g. CORP\svc-helpdesk or 'NT SERVICE\Helpdesk'
    [string]$AppLogin,
    # The account that may create databases and logins, for this installation only. Leave empty to use the Windows sign-in of the person running the script.
    [string]$SqlAdminUser,
    [securestring]$SqlAdminPassword,
    [bool]$TrustServerCertificate = $true,

    [string]$CompanyName,
    # The address people type to open Helpdesk. Used for links in e-mails and to allow the browser to call the API.
    [string]$PublicUrl,
    # Windows account the service runs as. Default: the virtual account NT SERVICE\<ServiceName>.
    [string]$ServiceAccount,
    [securestring]$ServiceAccountPassword,

    [string]$AdminEmail,
    [string]$AdminFirstName,
    [string]$AdminLastName,
    [securestring]$AdminPassword,

    [switch]$OpenFirewall,
    # Do everything except the Windows service: start the program once to check it, then stop it. For trying the installer out.
    [switch]$SkipService,
    [switch]$Upgrade,
    # Fail instead of asking for anything that is missing.
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

function Say($text) { Write-Host $text }
function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host ''; Write-Host "ERROR: $text" -ForegroundColor Red; exit 1 }

function Read-Value($prompt, $default = $null) {
    if ($NonInteractive) { Fail "Missing value: $prompt (run without -NonInteractive, or pass it as a parameter)" }
    $suffix = if ($default) { " [$default]" } else { '' }
    $answer = Read-Host "$prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $default }
    return $answer.Trim()
}

function Read-Secret($prompt, [switch]$Confirm) {
    if ($NonInteractive) { Fail "Missing value: $prompt" }
    while ($true) {
        $first = Read-Host $prompt -AsSecureString
        if (-not $Confirm) { return $first }
        $second = Read-Host "$prompt (again)" -AsSecureString
        if ((Get-PlainText $first) -ceq (Get-PlainText $second)) { return $first }
        Write-Host 'The two entries are not the same. Try again.' -ForegroundColor Yellow
    }
}

function Get-PlainText([securestring]$secure) {
    if ($null -eq $secure) { return $null }
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function New-RandomText([int]$length) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-Health($url, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { return $true } } catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

# ── where things are ─────────────────────────────────────────────────────────────────────────────────────────────
$packageApi = Join-Path $PSScriptRoot 'api'
$sqlDir = Join-Path $PSScriptRoot 'sqlserver'
$exeName = 'KPI.API.exe'
if (-not (Test-Path (Join-Path $packageApi $exeName))) { Fail "The program files were not found at $packageApi. Run this script from the unpacked package." }
if (-not $SkipService -and -not (Test-Admin)) { Fail 'Run PowerShell as administrator (right-click, Run as administrator), then run this script again.' }

$apiDir = Join-Path $InstallDir 'api'
$settingsFile = Join-Path $apiDir 'appsettings.Production.json'
$healthUrl = "http://localhost:$Port/health/ready"

# ── upgrade ──────────────────────────────────────────────────────────────────────────────────────────────────────
if ($Upgrade) {
    if (-not (Test-Path $settingsFile)) { Fail "No installation found in $InstallDir (there is no $settingsFile). Run the script without -Upgrade." }
    Step 'Upgrade'
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') { Say 'Stopping the service...'; Stop-Service -Name $ServiceName -Force; $service.WaitForStatus('Stopped', '00:01:00') }
    $backup = Join-Path $InstallDir ("api.backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Say "Keeping the old program files in $backup"
    Copy-Item $apiDir $backup -Recurse
    $keep = Get-Content $settingsFile -Raw
    Get-ChildItem $apiDir -Force | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $packageApi '*') $apiDir -Recurse -Force
    Set-Content -Path $settingsFile -Value $keep -Encoding UTF8
    if ($service) { Say 'Starting the service...'; Start-Service -Name $ServiceName }
    if ($service -and -not (Wait-Health $healthUrl 120)) { Fail "The service did not answer at $healthUrl. The previous files are in $backup." }
    Say "Upgraded. The database is brought up to date by the application when it starts."
    exit 0
}

# ── what we need to know ─────────────────────────────────────────────────────────────────────────────────────────
Step 'Checking this computer'
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Fail 'sqlcmd was not found. Install "Microsoft SQL Server command line utilities" (sqlcmd) and open a new PowerShell window.'
}
if (Test-Path $settingsFile) { Fail "Helpdesk is already installed in $InstallDir. To install a newer version over it, run: .\install.ps1 -Upgrade" }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { Fail "A service named '$ServiceName' already exists. Use -Upgrade, or -ServiceName to pick another name." }
Say 'sqlcmd found.'

if (-not $SqlServer) { $SqlServer = Read-Value 'SQL Server (name, name\instance, or address,port)' }
if (-not $SqlServer) { Fail 'The SQL Server is needed.' }
if (-not $CompanyName) { $CompanyName = Read-Value 'Company name' 'My Company' }
if (-not $PublicUrl) { $PublicUrl = Read-Value 'Address people will use to open Helpdesk' ("http://" + $env:COMPUTERNAME.ToLower() + ":$Port") }
$PublicUrl = $PublicUrl.TrimEnd('/')
if ($PublicUrl -notmatch '^https?://') { Fail 'The address must start with http:// or https://' }

if (-not $AppLogin) { $AppLogin = if ($AppAuth -eq 'Sql') { 'helpdesk_app' } else { "NT SERVICE\$ServiceName" } }
if ($AppLogin -match "[\]\[';`"]") { Fail 'The login name must not contain quote marks, semicolons or square brackets.' }
if ($Database -notmatch '^[A-Za-z0-9_]{1,100}$') { Fail 'The database name may only contain letters, digits and underscores.' }

if (-not $AdminEmail) { $AdminEmail = Read-Value 'First administrator: e-mail (used to sign in)' }
if ($AdminEmail -notmatch '^[^@\s]+@[^@\s]+$') { Fail 'The administrator e-mail address is not valid.' }
if (-not $AdminFirstName) { $AdminFirstName = Read-Value 'First administrator: first name' 'Administrator' }
if (-not $AdminLastName) { $AdminLastName = Read-Value 'First administrator: last name' '' }
if (-not $AdminPassword) { $AdminPassword = Read-Secret 'First administrator: password (at least 8 characters)' -Confirm }
if ((Get-PlainText $AdminPassword).Length -lt 8) { Fail 'The administrator password must have at least 8 characters.' }

if ($SqlAdminUser -and -not $SqlAdminPassword) { $SqlAdminPassword = Read-Secret "Password of $SqlAdminUser" }

$appPassword = $null
if ($AppAuth -eq 'Sql') { $appPassword = New-RandomText 28; $appPassword = $appPassword + 'Aa1' }
$rng = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rng); $jwtKey = [Convert]::ToBase64String($rng)

# ── SQL Server ───────────────────────────────────────────────────────────────────────────────────────────────────
function Invoke-SqlFile([string]$file, [string]$databaseName, [hashtable]$variables) {
    $arguments = @('-S', $SqlServer, '-b', '-f', '65001', '-i', $file, '-d', $databaseName)
    if ($TrustServerCertificate) { $arguments += '-C' }
    if ($SqlAdminUser) { $arguments += @('-U', $SqlAdminUser) } else { $arguments += '-E' }
    # sqlcmd reads scripting variables and the password from the environment, so none of it shows up in a process list
    $names = @(); $oldPassword = $env:SQLCMDPASSWORD
    try {
        foreach ($key in $variables.Keys) { Set-Item -Path "Env:$key" -Value $variables[$key]; $names += $key }
        if ($SqlAdminUser) { $env:SQLCMDPASSWORD = (Get-PlainText $SqlAdminPassword) }
        $output = & sqlcmd @arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        foreach ($key in $names) { Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue }
        if ($null -eq $oldPassword) { Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue } else { $env:SQLCMDPASSWORD = $oldPassword }
    }
    $output | ForEach-Object { Say "   $_" }
    if ($code -ne 0) { Fail "sqlcmd failed on $(Split-Path $file -Leaf) (exit code $code). See the message above." }
}

Step 'Database'
$vars = @{ DbName = $Database; AuthMode = $AppAuth.ToUpper(); AppLogin = $AppLogin; AppPassword = $(if ($appPassword) { $appPassword } else { 'unused' }) }
Say "1/3 Create the database '$Database' and the account '$AppLogin'"
Invoke-SqlFile (Join-Path $sqlDir '01-create-database.sql') 'master' $vars
Say '2/3 Create the tables'
Invoke-SqlFile (Join-Path $sqlDir '02-schema.sql') $Database @{}
Say '3/3 Add the roles and permissions'
Invoke-SqlFile (Join-Path $sqlDir '03-seed.sql') $Database @{}

# ── the program and its settings ─────────────────────────────────────────────────────────────────────────────────
Step 'Program files'
New-Item -ItemType Directory -Force -Path $apiDir | Out-Null
Copy-Item (Join-Path $packageApi '*') $apiDir -Recurse -Force
Say "Copied to $apiDir"

$trust = if ($TrustServerCertificate) { 'True' } else { 'False' }
$connection = if ($AppAuth -eq 'Sql') {
    "Server=$SqlServer;Database=$Database;User Id=$AppLogin;Password=$appPassword;TrustServerCertificate=$trust;Encrypt=True"
} else {
    "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=$trust;Encrypt=True"
}
$settings = [ordered]@{
    Urls              = "http://0.0.0.0:$Port"
    Database          = [ordered]@{ Provider = 'SqlServer'; AutoMigrate = $true }
    ConnectionStrings = [ordered]@{ DefaultConnection = $connection }
    Jwt               = [ordered]@{ Key = $jwtKey; Issuer = 'Helpdesk'; Audience = 'Helpdesk' }
    App               = [ordered]@{ BaseUrl = $PublicUrl; CompanyName = $CompanyName }
    Cors              = [ordered]@{ Origins = @($PublicUrl) }
    # E-mail is set up in the app (Settings > E-mail). This key only keeps the unused Resend option quiet.
    Resend            = [ordered]@{ ApiKey = 'not-configured' }
}
$json = $settings | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($settingsFile, $json, (New-Object Text.UTF8Encoding($false)))
$serviceIdentity = if ($ServiceAccount) { $ServiceAccount } else { "NT SERVICE\$ServiceName" }
& icacls $settingsFile /inheritance:r /grant:r 'BUILTIN\Administrators:(F)' 'NT AUTHORITY\SYSTEM:(F)' | Out-Null
if ($SkipService) { & icacls $settingsFile /grant:r "${env:USERDOMAIN}\${env:USERNAME}:(R)" | Out-Null }   # a trial run uses the signed-in person, not the service
Say "Settings written to $settingsFile (readable only by administrators and the service; it holds the secrets)."

# ── the first administrator ──────────────────────────────────────────────────────────────────────────────────────
Step 'First administrator'
$exe = Join-Path $apiDir $exeName
$oldEnv = @{}
foreach ($pair in @{ HELPDESK_ADMIN_EMAIL = $AdminEmail; HELPDESK_ADMIN_PASSWORD = (Get-PlainText $AdminPassword); HELPDESK_ADMIN_FIRST_NAME = $AdminFirstName; HELPDESK_ADMIN_LAST_NAME = $AdminLastName; ASPNETCORE_ENVIRONMENT = 'Production' }.GetEnumerator()) {
    $oldEnv[$pair.Key] = [Environment]::GetEnvironmentVariable($pair.Key)
    [Environment]::SetEnvironmentVariable($pair.Key, $pair.Value)
}
try {
    Push-Location $apiDir
    $result = & $exe create-admin 2>&1
    $code = $LASTEXITCODE
    Pop-Location
}
finally {
    foreach ($key in $oldEnv.Keys) { [Environment]::SetEnvironmentVariable($key, $oldEnv[$key]) }
}
$result | Where-Object { $_ -notmatch '^(info|warn|dbug):' -and $_ -notmatch '^\s+(Microsoft|Hangfire)' } | ForEach-Object { Say "   $_" }
if ($code -ne 0) { Fail "Creating the administrator failed (exit code $code)." }

# ── run it ───────────────────────────────────────────────────────────────────────────────────────────────────────
if ($SkipService) {
    Step 'Trying the program (no service was created)'
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $process = Start-Process -FilePath $exe -WorkingDirectory $apiDir -PassThru -WindowStyle Hidden
    try {
        if (-not (Wait-Health $healthUrl 90)) { Fail "The program did not answer at $healthUrl." }
        Say "It answers at $healthUrl"
    }
    finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
else {
    Step 'Windows service'
    $binary = "`"$exe`""
    if ($ServiceAccount -and $ServiceAccountPassword) {
        $credential = New-Object Management.Automation.PSCredential($ServiceAccount, $ServiceAccountPassword)
        New-Service -Name $ServiceName -BinaryPathName $binary -DisplayName 'Helpdesk' -Description 'Helpdesk server' -StartupType Automatic -Credential $credential | Out-Null
    }
    else {
        New-Service -Name $ServiceName -BinaryPathName $binary -DisplayName 'Helpdesk' -Description 'Helpdesk server' -StartupType Automatic | Out-Null
        & sc.exe config $ServiceName obj= $serviceIdentity | Out-Null
    }
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -Type MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production')
    # the service account must be able to read the program files and the settings (a virtual account only exists once the service does)
    & icacls $InstallDir /grant "${serviceIdentity}:(OI)(CI)(RX)" | Out-Null
    & icacls $settingsFile /grant:r "${serviceIdentity}:(R)" | Out-Null
    if ($OpenFirewall) {
        New-NetFirewallRule -DisplayName "Helpdesk (TCP $Port)" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        Say "Opened TCP port $Port in the Windows firewall."
    }
    Start-Service -Name $ServiceName
    Say 'Service started. Waiting for it to answer...'
    if (-not (Wait-Health $healthUrl 120)) {
        Fail "The service did not answer at $healthUrl. Look in Event Viewer > Windows Logs > Application (source: $ServiceName), and check the SQL Server address and account."
    }
}

Step 'Done'
Say "Open:        $PublicUrl"
Say "Sign in as:  $AdminEmail"
Say "Settings:    $settingsFile  (keep it private; it holds the database password and the signing key)"
Say ''
Say 'Next: Settings > E-mail (SMTP) to set up outgoing mail, then Settings > Users to add people.'
Say 'Back up the database regularly: attachments are stored in it. To install a newer version later: .\install.ps1 -Upgrade'
