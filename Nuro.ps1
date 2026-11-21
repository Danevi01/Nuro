# Nuro - startet einen kleinen lokalen Webserver und oeffnet die App in einem eigenen Fenster.
# Laeuft nur auf 127.0.0.1 (von aussen nicht erreichbar). Beendet sich, wenn das Fenster geschlossen wird.
param([int]$Port = 8765)

$ErrorActionPreference = 'Stop'
$Root      = Join-Path $PSScriptRoot 'app'
$Url       = "http://127.0.0.1:$Port/"
$ProfilDir   = Join-Path $env:LOCALAPPDATA 'Nuro\browser-profil'
$OllamaUrl = 'http://127.0.0.1:11434/api/version'

function Test-Ollama { try { Invoke-RestMethod -Uri $OllamaUrl -TimeoutSec 2 | Out-Null; $true } catch { $false } }

# 1) Ollama starten, falls es nicht laeuft
if (-not (Test-Ollama)) {
    $app = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama app.exe'
    $cli = Get-Command ollama -ErrorAction SilentlyContinue
    if (Test-Path $app)  { Start-Process $app }
    elseif ($cli)        { Start-Process $cli.Source -ArgumentList 'serve' -WindowStyle Hidden }
    for ($i = 0; $i -lt 20 -and -not (Test-Ollama); $i++) { Start-Sleep -Milliseconds 500 }
}

# 2) Browser fuer das App-Fenster suchen (Edge ist bei Windows dabei, Chrome als Ersatz)
function Find-Browser {
    $cands = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:LOCALAPPDATA\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    )
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { return $c } }
    return $null
}
$Browser = Find-Browser
function Open-Window {
    if ($Browser) {
        New-Item -ItemType Directory -Force -Path $ProfilDir | Out-Null
        Start-Process $Browser -ArgumentList @("--app=$Url", "--user-data-dir=`"$ProfilDir`"", '--window-size=1200,860', '--no-first-run', '--no-default-browser-check')
    } else {
        Start-Process $Url
    }
}

# 3) Server starten (oder nur Fenster oeffnen, falls die App schon laeuft)
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($Url)
try { $listener.Start() } catch { Open-Window; exit 0 }

$types = @{
    '.html' = 'text/html; charset=utf-8'; '.js' = 'application/javascript; charset=utf-8'; '.css' = 'text/css; charset=utf-8'
    '.svg'  = 'image/svg+xml'; '.png' = 'image/png'; '.ico' = 'image/x-icon'; '.json' = 'application/json'
}
$rootFull = [System.IO.Path]::GetFullPath($Root)

function Send-Response($ctx) {
    $res = $ctx.Response
    try {
        $rel = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
        if ([string]::IsNullOrEmpty($rel)) { $rel = 'index.html' }
        $path = [System.IO.Path]::GetFullPath((Join-Path $rootFull $rel))
        if (-not $path.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path $path -PathType Leaf)) {
            $res.StatusCode = 404; $bytes = [Text.Encoding]::UTF8.GetBytes('Nicht gefunden')
        } else {
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $ext = [System.IO.Path]::GetExtension($path).ToLower()
            $res.ContentType = $(if ($types[$ext]) { $types[$ext] } else { 'application/octet-stream' })
            $res.Headers['Cache-Control'] = 'no-cache'
        }
        $res.ContentLength64 = $bytes.Length
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
    } catch { } finally { try { $res.Close() } catch { } }
}

Open-Window

# 4) Anfragen bedienen; beenden, sobald kein App-Fenster mehr offen ist
$profileEsc = [Regex]::Escape($ProfilDir)
function Test-WindowOpen {
    if (-not $Browser) { return $true }
    $exe = [System.IO.Path]::GetFileName($Browser)
    $procs = Get-CimInstance Win32_Process -Filter "Name='$exe'" -ErrorAction SilentlyContinue
    return [bool]($procs | Where-Object { $_.CommandLine -match $profileEsc })
}

$task = $listener.GetContextAsync()
$lastCheck = Get-Date
$startTime = Get-Date
while ($true) {
    if ($task.Wait(400)) {
        Send-Response $task.Result
        $task = $listener.GetContextAsync()
    }
    if (((Get-Date) - $lastCheck).TotalSeconds -ge 4 -and ((Get-Date) - $startTime).TotalSeconds -ge 10) {
        $lastCheck = Get-Date
        if (-not (Test-WindowOpen)) { break }
    }
}
$listener.Stop()
