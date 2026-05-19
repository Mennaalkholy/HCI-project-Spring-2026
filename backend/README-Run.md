# HCI backend — run order

Repo layout is **`hci2`** → **`backend`** → `Python`, `CSharp`, `Server`, `Scripts`, `Models`, etc.

If a folder path contains **spaces**, always **quote** paths in PowerShell:

```powershell
Set-Location -LiteralPath "C:\Users\YourName\Desktop\HCIVNEWSTYLE\NEWHCIV1\hci2\backend\Python"
```

**`start-play.ps1`** is the single stack launcher; it lives in the **top-level `NEWHCIV1` folder** (the one that contains the nested `NEWHCIV1\hci2`). From there run **`.\start-play.ps1 ...`**. If your current directory is **`hci2`**, run **`& "..\..\start-play.ps1"`** or use **VS Code → Run Task** (tasks call that script).

From the **`hci2`** repo root you can also run helpers (they quote paths internally):

- **`..\..\start-play.ps1 -SkipFaceAuth`** (from **`hci2`**) — no **`face_auth_service.py`**; TuioDemo uses **`TUIO_DEMO_SKIP_AUTH`** and **reacTIVision** auto-starts shortly after the game loads (no face step). Same as **`-PerfTest`** but without the face window. **`..\..\start-play.ps1 -SkipFaceAuth -Minimal`** = hub + TuioDemo only.
- `.\run-main.ps1` — runs `backend/Python/main.py`
- `.\run-tuio.ps1` — runs `tuio_only_service.py`

1. **Install** (once): `pip install -r Python/requirements.txt` (run inside `Python`, or pass the full quoted path to `-r`).
2. **Hub**: `python Server/notification_hub.py` (default `127.0.0.1:5000`) — run from **`backend`** with `Set-Location -LiteralPath "...\hci2\backend"`.
3. **Workers** (five terminals or use `Scripts/start-hci.ps1`):  
   `python -m pipeline.worker --name <gaze|face|object|mediapipe|bluetooth> --port <5111-5115>`  
   Run from directory `Python` with `PYTHONPATH` set to that folder (the launcher script does this).
4. **Camo pipeline**: `python main.py` from `Python` — env `MAIN_CAPTURE_INDEX` for Camo (not `hci2\main.py`; use `run-main.ps1` from repo root if unsure).
5. **TUIO / finger bridge (Camo by default)**: `python tuio_only_service.py` — defaults **`TUIO_CAPTURE_INDEX`** to **`MAIN_CAPTURE_INDEX`** (Camo **2**); UDP `TUIO_HOST`/`TUIO_PORT` must match `TuioDemo`. reacTIVision TUIO is separate (no Camo).
6. **Game**: run `TuioDemo.exe` from `CSharp/TUIO11_NET` — SocketClient uses hub port **5000** by default. **reacTIVision** (in `hci2/reacTIVision-1.5.1-win64/`) sends **TUIO UDP** to **127.0.0.1:3333** (`reacTIVision.xml`). After **successful face login**, TuioDemo sends **`face_auth_shutdown`** so **`face_auth_service.py`** exits and frees the webcam, then starts **reacTIVision** (~2.3 s later) unless **`TUIO_DEMO_AUTO_REACTIVISION=0`**. For a **second login** in the same session, restart **`face_auth_service.py`** manually (or relaunch **`start-play.ps1`**). **`tuio_only_service.py`** uses synthetic TUIO session IDs so it can run alongside marker TUIO.
**Skip only game login/sign-up (full stack still runs)**: set **`TUIO_DEMO_SKIP_AUTH=1`** before starting `TuioDemo.exe`, or use **`..\..\start-play.ps1 -PerfTest`** from **`hci2`** (or **`.\start-play.ps1`** from the outer **`NEWHCIV1`** folder). **reacTIVision** is started by **TuioDemo** after successful **face login** (hub event **`face_auth_shutdown`**); set **`TUIO_DEMO_AUTO_REACTIVISION=0`** on TuioDemo to disable that. Optional: **`TUIO_DEMO_PERF_AGE_GROUP`**, **`TUIO_DEMO_PERF_FROM_LEVEL`**, **`REACTIVISION_EXE`** (full path to `reacTIVision.exe`).

**Automated tests**: `pip install -r Python/requirements-dev.txt`, then either:

- From repo root **`hci2`**: `python -m pytest` (uses root [`pytest.ini`](../pytest.ini)), or  
- From `Python`: `python -m pytest`, or  
- From repo root: `.\run-tests.ps1` / `.\run-tests.ps1 tests/test_protocol.py::test_send_framed_jpeg_roundtrip -v`

Running `pytest tests/...` only works when the current directory is `backend/Python` (or use `run-tests.ps1`).

Plane fusion: `main.py` relays YOLO plane poses to the hub (`HUB_PLANE_RELAY=1`); `tuio_only_service.py` subscribes as service `tuio`. Use `TUIO_PLANE_SOURCE=merge` for YOLO-when-detected else finger.

See `Python/.env.example` for variables.

### Face-only login (Camo) + hub broadcast

- **Identity**: TUIO **markers are not used** for login or sign-up. Only **`face_auth_service.py`** (defaults to Camo via **`MAIN_CAPTURE_INDEX`** / **`FACE_CAPTURE_INDEX`**, index **2**) decides who you are.
- **Known face** (profile id exists in **`face_database.json`** and **`users.json`**): sends **`mediapipe_user_ready`** with **`exists: true`** → greeting.
- **Unknown face** for **`FACE_NEW_USER_TIMEOUT_SEC`** (default **20** s), stable in frame: appends **`users.json`**, enrolls face, sends **`mediapipe_user_ready`** with **`exists: false`** → **sign-up** (gender / age) using **TUIO dials and/or MediaPipe virtual dial** together.
- **Gameplay**: reacTIVision TUIO, MediaPipe UDP, YOLO plane, etc. Hub lines **without** `"target"` are **broadcast**; each service ignores unrelated JSON.

[`users.json`](CSharp/TUIO11_NET/bin/Debug/net48/users.json) — override with **`USERS_JSON_PATH`**.

1. **`notification_hub.py`** → **`TuioDemo.exe`** → **`face_auth_service.py`**.
2. Optional: **`e`** in the face window = manual enroll to **`FACE_ENROLL_TUIO_ID`**.

Tune **`FACE_NEW_USER_TIMEOUT_SEC`**, **`FACE_KNOWN_MATCH_FRAMES`**, **`FACE_PROVISION_STABLE_FRAMES`** in env or in `face_auth_service.py`.

### Troubleshooting

- **`mediapipe` has no `solutions`** — Reinstall a compatible wheel: `pip uninstall mediapipe -y` then `pip install mediapipe==0.10.14` (matches `requirements.txt`). Until fixed, `tuio_only_service.py` keeps running using **hub/YOLO-only** TUIO (no finger overlay).

- **Camera index out of range / cannot open** — Set `MAIN_CAPTURE_INDEX` / `TUIO_CAPTURE_INDEX` or `CAMERA_FALLBACK_INDICES=0,1,2,3`. By default **`MAIN_ALLOW_NO_CAMERA=1`** keeps `main.py` streaming synthetic blank frames so workers stay alive; use **`MAIN_WAIT_FOR_CAMERA=1`** to block until a device appears.

- **face_auth: video works but no face / no green box** — Wrong index often looks black or like the wrong room: set **`FACE_CAPTURE_INDEX`** and **`CAMERA_FALLBACK_INDICES`**. If the image is fine but Haar never fires, lower **`FACE_HAAR_MIN_WIDTH`** / **`FACE_HAAR_MIN_NEIGHBORS`** (see `.env.example`). Use frontal lighting; the OpenCV window title is **Face auth (hub)**.

- **Black, noisy, or “snow” preview but OpenCV says the camera opened** — On Windows, DirectShow and MSMF behave differently per device. Set **`CAMERA_CAPTURE_BACKEND=msmf`** (or try **`dshow`**), scan indices with **`CAMERA_FALLBACK_INDICES=0,1,2,3`**, and close other apps using the webcam. See **`CAMERA_MIN_MEAN_GRAY`** / **`CAMERA_FRAME_WIDTH`** in `Python/.env.example`.

- **Grey window + padlock / “camera locked” on Face auth while another app shows live video** — Most laptop webcams allow **only one** program at a time. **`start-play.ps1`** (full stack) now passes **`-SingleWebcam`** to the backend launcher so **`main.py`** and **`tuio_only_service.py`** do not open the webcam (`MAIN_SKIP_CAMERA` / `TUIO_SKIP_CAMERA`); only **`face_auth_service.py`** uses the real camera. Close the **Windows Camera** app and any meeting software using the cam. To run the old behavior (each service captures), use **`..\..\start-play.ps1 -UsePipelineCamera`** from **`hci2`** (or **`.\start-play.ps1`** from the outer folder) and supply a second camera or virtual cam (e.g. Camo / OBS).
