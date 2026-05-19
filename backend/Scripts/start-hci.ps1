# Requires: Python on PATH, dependencies from backend/Python/requirements.txt
#
# Optional switches (e.g. from repo root start-play.ps1):
#   -WithFaceAuth      Launch face_auth_service.py (face login / new user -> hub)
#   -LaunchTuioDemo   Launch TuioDemo.exe after a short delay (needs hub first)
#   -Minimal          Only hub (+ face + game if those switches set). Skips workers, main.py, tuio_only_service.
#   -SingleWebcam     Set MAIN_SKIP_CAMERA / TUIO_SKIP_CAMERA so only face_auth opens the physical webcam (Windows).
#   -LaunchReacTIVision  Start hci2\reacTIVision-1.5.1-win64\reacTIVision.exe (TUIO markers -> same UDP port as TuioDemo).

param(
    [switch] $WithFaceAuth,
    [switch] $LaunchTuioDemo,
    [switch] $Minimal,
    [switch] $SingleWebcam,
    [switch] $PerfTestGame,
    [switch] $LaunchReacTIVision
)

$ErrorActionPreference = "Stop"
$BackendRoot = Split-Path -Parent $PSScriptRoot
$PyRoot = Join-Path $BackendRoot "Python"
$ServerHub = Join-Path $BackendRoot "Server\notification_hub.py"

if (-not (Test-Path $PyRoot)) {
    Write-Error "Python folder not found: $PyRoot"
}

# MediaPipe Hands needs mp.solutions (mediapipe==0.10.14 on Python 3.10-3.12).
$PyLauncher = "py"
$PyVersionArg = "-3.11"
$PyExePath = $null

if (Get-Command py -ErrorAction SilentlyContinue) {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        & py -3.11 -c "import mediapipe as mp; import sys; sys.exit(0 if hasattr(mp, 'solutions') else 1)" 2>&1 | Out-Null
    }
    finally {
        $ErrorActionPreference = $prevEap
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Warning '[start-hci] py -3.11 + mediapipe==0.10.14 not ready. Run: py -3.11 -m pip install -r backend\Python\requirements.txt'
    }
}
elseif (Test-Path "C:\Program Files\Python311\python.exe") {
    $PyExePath = "C:\Program Files\Python311\python.exe"
    $PyLauncher = $null
    $PyVersionArg = $null
}
elseif (Get-Command python -ErrorAction SilentlyContinue) {
    $PyLauncher = "python"
    $PyVersionArg = $null
    Write-Warning '[start-hci] Using generic python on PATH — prefer Python 3.11 + mediapipe==0.10.14.'
}
else {
    Write-Error "Neither 'py' nor 'python' was found on PATH. Install Python 3.11."
}

if ($PyExePath) {
    Write-Host ('[start-hci] Using Python: ' + $PyExePath) -ForegroundColor DarkGray
}
else {
    Write-Host ('[start-hci] Using Python: ' + $PyLauncher + ' ' + $PyVersionArg) -ForegroundColor DarkGray
}

function Get-PythonInvokeFragment {
    if ($PyExePath) {
        return "& '$PyExePath'"
    }
    if ($PyVersionArg) {
        return "& $PyLauncher $PyVersionArg"
    }
    return "& $PyLauncher"
}

$env:PYTHONPATH = $PyRoot
$env:PIPELINE_SERVICE_HOST = if ($env:PIPELINE_SERVICE_HOST) { $env:PIPELINE_SERVICE_HOST } else { "127.0.0.1" }
# SplitCam (2) → tuio_only + reacTIVision XML; OBS Virtual Camera (4) → main + face_auth
$env:MAIN_CAPTURE_INDEX = if ($env:MAIN_CAPTURE_INDEX) { $env:MAIN_CAPTURE_INDEX } else { "4" }
$env:TUIO_CAPTURE_INDEX = if ($env:TUIO_CAPTURE_INDEX) { $env:TUIO_CAPTURE_INDEX } else { "2" }
$env:FACE_CAPTURE_INDEX = if ($env:FACE_CAPTURE_INDEX) { $env:FACE_CAPTURE_INDEX } else { "4" }
if (-not $env:CAMERA_CAPTURE_BACKEND) { $env:CAMERA_CAPTURE_BACKEND = "dshow" }
if (-not $env:TUIO_LASER_ENABLE) { $env:TUIO_LASER_ENABLE = "1" }

if (-not $env:TUIO_PLANE_SOURCE) { $env:TUIO_PLANE_SOURCE = "bottle_hand" }
$planeSrc = if ($env:TUIO_PLANE_SOURCE) { $env:TUIO_PLANE_SOURCE.Trim().ToLower() } else { "" }
if ($planeSrc -in @("bottle_hand", "bottle", "cup_hand", "cup")) {
    if (-not $env:OBJECT_TARGET_CLASSES) { $env:OBJECT_TARGET_CLASSES = "bottle" }
    $env:TUIO_PLANE_SOURCE = "bottle_hand"
    # Gameplay uses bottle+finger; laser still runs when hub sends mediapipe_signup_pointer (admin/name keyboard).
    if (-not $env:TUIO_LASER_ENABLE) { $env:TUIO_LASER_ENABLE = "0" }
    $env:TUIO_CUP_LOCAL_YOLO = "1"
    $env:TUIO_BOTTLE_CLEAR_FALLBACK = "0"
    $env:TUIO_BOTTLE_CAP_FALLBACK = "0"
    $env:TUIO_BOTTLE_CAP_REQUIRES_YOLO = "1"
    $env:TUIO_BOTTLE_REQUIRE_NEAR_HAND = "1"
    if (-not $env:TUIO_BOTTLE_HAND_MAX_DIST) { $env:TUIO_BOTTLE_HAND_MAX_DIST = "0.38" }
    if (-not $env:OBJECT_CONF_THRESHOLD) { $env:OBJECT_CONF_THRESHOLD = "0.18" }
    if (-not $env:YOLO_IMGSZ) { $env:YOLO_IMGSZ = "640" }
    if (-not $env:TUIO_CUP_YOLO_EVERY_N) { $env:TUIO_CUP_YOLO_EVERY_N = "1" }
    if (-not $env:TUIO_CUP_ON_FRAMES) { $env:TUIO_CUP_ON_FRAMES = "2" }
    if (-not $env:TUIO_CUP_SMOOTH) { $env:TUIO_CUP_SMOOTH = "0.22" }
    if (-not $env:TUIO_BOTTLE_PLANE_SMOOTH) { $env:TUIO_BOTTLE_PLANE_SMOOTH = "0.16" }
    Write-Host '[start-hci] bottle_hand: bottle near hand (ignores faces/shirts); conf 0.18.' -ForegroundColor DarkCyan
}
if ($planeSrc -eq "bottle_signup") {
    if (-not $env:OBJECT_TARGET_CLASSES) { $env:OBJECT_TARGET_CLASSES = "bottle" }
    $env:TUIO_PLANE_SOURCE = "bottle_signup"
    $env:TUIO_LASER_ENABLE = "0"
    $env:TUIO_CUP_LOCAL_YOLO = "1"
    $env:TUIO_BOTTLE_CLEAR_FALLBACK = "1"
    $env:TUIO_BOTTLE_CAP_FALLBACK = "1"
    $env:TUIO_BOTTLE_REQUIRE_NEAR_HAND = "1"
    $env:TUIO_BOTTLE_HOLD_GEOMETRY = "1"
    $env:TUIO_BOTTLE_HAND_ANCHOR = "palm"
    if (-not $env:OBJECT_CONF_THRESHOLD) { $env:OBJECT_CONF_THRESHOLD = "0.15" }
    if (-not $env:TUIO_BOTTLE_MIN_CONF) { $env:TUIO_BOTTLE_MIN_CONF = "0.20" }
    if (-not $env:TUIO_BOTTLE_HAND_MIN_CONF) { $env:TUIO_BOTTLE_HAND_MIN_CONF = "0.15" }
    if (-not $env:TUIO_BOTTLE_HAND_MAX_DIST) { $env:TUIO_BOTTLE_HAND_MAX_DIST = "0.28" }
    if (-not $env:YOLO_IMGSZ) { $env:YOLO_IMGSZ = "640" }
    if (-not $env:TUIO_CUP_YOLO_EVERY_N) { $env:TUIO_CUP_YOLO_EVERY_N = "1" }
    if (-not $env:TUIO_CUP_ON_FRAMES) { $env:TUIO_CUP_ON_FRAMES = "2" }
    if (-not $env:TUIO_CUP_SMOOTH) { $env:TUIO_CUP_SMOOTH = "0.22" }
    if (-not $env:TUIO_BOTTLE_PLANE_SMOOTH) { $env:TUIO_BOTTLE_PLANE_SMOOTH = "0.16" }
    Write-Host '[start-hci] bottle_signup: palm anchor, cap+cup fallback, YOLO conf 0.15.' -ForegroundColor DarkCyan
}

if ($SingleWebcam) {
    $env:MAIN_SKIP_CAMERA = "1"
    $env:TUIO_SKIP_CAMERA = "1"
    Write-Host '[start-hci] SingleWebcam: MAIN_SKIP_CAMERA=1 TUIO_SKIP_CAMERA=1 (real camera left for face_auth).' -ForegroundColor DarkCyan
}

Write-Host ('[start-hci] PYTHONPATH=' + $PyRoot)

Write-Host '[start-hci] Cleaning up existing ports (5000, 5111-5115)...' -ForegroundColor Gray
Get-Process | Where-Object { $_.MainWindowTitle -like "worker*" -or $_.MainWindowTitle -eq "notification hub" } | Stop-Process -Force -ErrorAction SilentlyContinue
$ports = @(5000, 5111, 5112, 5113, 5114, 5115)
foreach ($p in $ports) {
    $pidToKill = Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -First 1
    if ($pidToKill) { Stop-Process -Id $pidToKill -Force -ErrorAction SilentlyContinue }
}

$pyFrag = Get-PythonInvokeFragment

function Start-PyWindow {
    param([string]$Title, [string]$Arguments)
    $innerCmd = "`$env:PYTHONPATH='$PyRoot'; `$Host.UI.RawUI.WindowTitle = '$Title'; Write-Host '--- $Title ---' -ForegroundColor Cyan; $pyFrag $Arguments"
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($innerCmd)
    $encoded = [Convert]::ToBase64String($bytes)
    Start-Process powershell -ArgumentList @('-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -WorkingDirectory $PyRoot
}

Write-Host '[start-hci] Launching notification hub...'
$hubInner = "`$Host.UI.RawUI.WindowTitle = 'notification hub'; Write-Host '--- Notification Hub ---' -ForegroundColor Cyan; $pyFrag '$ServerHub'"
$hubBytes = [System.Text.Encoding]::Unicode.GetBytes($hubInner)
$hubEncoded = [Convert]::ToBase64String($hubBytes)
Start-Process powershell -ArgumentList @('-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $hubEncoded) -WorkingDirectory $BackendRoot

Start-Sleep -Seconds 3

if (-not $Minimal) {
    $workers = @(
        @{ name = "gaze"; port = 5111 },
        @{ name = "face"; port = 5112 },
        @{ name = "object"; port = 5113 },
        @{ name = "mediapipe"; port = 5114 },
        @{ name = "bluetooth"; port = 5115 }
    )

    foreach ($w in $workers) {
        Write-Host ('[start-hci] Worker ' + $w.name + ' :' + $w.port)
        Start-PyWindow ('worker ' + $w.name) ('-m pipeline.worker --name ' + $w.name + ' --port ' + $w.port)
        Start-Sleep -Milliseconds 400
    }

    Start-Sleep -Seconds 1

    Write-Host ('[start-hci] Launching main.py (camera index ' + $env:MAIN_CAPTURE_INDEX + ', DSHOW) ...')
    Start-PyWindow 'main.py' 'main.py'

    Start-Sleep -Milliseconds 500

    Write-Host ('[start-hci] Launching tuio_only_service.py (camera index ' + $env:TUIO_CAPTURE_INDEX + ', DSHOW) ...')
    Start-PyWindow 'tuio_only_service.py' 'tuio_only_service.py'
}
else {
    Write-Host '[start-hci] -Minimal: skipping workers, main.py, tuio_only_service.'
}

if ($WithFaceAuth) {
    Start-Sleep -Milliseconds 600
    Write-Host '[start-hci] Launching face_auth_service.py (face login / new user) ...'
    Start-PyWindow 'face_auth_service.py' 'face_auth_service.py'
}

if ($LaunchTuioDemo) {
    $demoProjDir = Join-Path $BackendRoot "CSharp\TUIO11_NET"
    $demoDir = Join-Path $demoProjDir "bin\Debug\net48"
    $demoExe = Join-Path $demoDir "TuioDemo.exe"
    if (-not (Test-Path -LiteralPath $demoExe)) {
        Write-Host '[start-hci] TuioDemo.exe not found - attempting dotnet build...' -ForegroundColor Yellow
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            Write-Warning '[start-hci] dotnet not on PATH. Install the .NET SDK.'
        }
        else {
            Push-Location $demoProjDir
            try {
                & dotnet build TUIO_DEMO.csproj -c Debug --nologo -v minimal
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning ('[start-hci] dotnet build failed (exit code ' + $LASTEXITCODE + ').')
                }
            }
            finally {
                Pop-Location
            }
        }
    }
    if (-not (Test-Path -LiteralPath $demoExe)) {
        Write-Warning ('[start-hci] TuioDemo.exe still missing at: ' + $demoExe)
    }
    else {
        Write-Host '[start-hci] Launching TuioDemo.exe in 2s (hub must be accepting connections) ...'
        Start-Sleep -Seconds 2
        $hci2RootForRv = Split-Path -Parent $BackendRoot
        $rvExeAuto = Join-Path $hci2RootForRv "reacTIVision-1.5.1-win64\reacTIVision.exe"
        if (Test-Path -LiteralPath $rvExeAuto) {
            $env:REACTIVISION_EXE = $rvExeAuto
            Write-Host ('[start-hci] REACTIVISION_EXE set: ' + $env:REACTIVISION_EXE) -ForegroundColor DarkGray
        }
        if (-not $env:TUIO_DEMO_AUTO_REACTIVISION) {
            $env:TUIO_DEMO_AUTO_REACTIVISION = "1"
            Write-Host '[start-hci] TUIO_DEMO_AUTO_REACTIVISION=1 (reacTIVision after face login)' -ForegroundColor DarkGray
        }
        if ($PerfTestGame) {
            $env:TUIO_DEMO_SKIP_AUTH = "1"
            Write-Host '[start-hci] PerfTestGame: TUIO_DEMO_SKIP_AUTH=1' -ForegroundColor DarkCyan
        }
        Start-Process -FilePath $demoExe -WorkingDirectory $demoDir
    }
}

if ($LaunchReacTIVision) {
    $hci2Root = Split-Path -Parent $BackendRoot
    $rvRoot = Join-Path $hci2Root "reacTIVision-1.5.1-win64"
    $rvExe = Join-Path $rvRoot "reacTIVision.exe"
    if (-not (Test-Path -LiteralPath $rvExe)) {
        Write-Warning ('[start-hci] reacTIVision.exe not found: ' + $rvExe)
    }
    else {
        Write-Host '[start-hci] Launching reacTIVision in 2s ...' -ForegroundColor DarkCyan
        Start-Sleep -Seconds 2
        Start-Process -FilePath $rvExe -WorkingDirectory $rvRoot
    }
}

Write-Host '[start-hci] Done. Face: known user -> game greeting | new face (~12s) -> sign-up.'
