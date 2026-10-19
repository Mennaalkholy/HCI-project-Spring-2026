# HCI Project (Spring 2026) — Multimodal Tangible Flag Game

An interactive Windows game controlled through multiple input modalities: face-recognition login, MediaPipe hand gestures, YOLO object tracking and reacTIVision TUIO markers. Players match country flags on a board, with two-tier hints and age-group profiles (child / teen / adult).

## Features
- **Face-only login & sign-up** (`face_auth_service.py`): known faces are greeted; an unknown face held stable in frame is enrolled and taken to sign-up (gender / age).
- **Gesture input** (`signup_hand_poses.py`): MediaPipe hand poses (peace = submit, fist = select) and horizontal swipes.
- **Object control** (`signup_bottle_control.py`): YOLO-tracked water bottle slid left/right as a controller.
- **Tangible markers**: reacTIVision TUIO markers and dials drive gameplay in the C# client.
- **Flag hints** (`CountryHintHelper.cs`): subtle tier-1 hint, clearer tier-2 hint after losing a life.

## Architecture
| Component | Path | Role |
|---|---|---|
| Notification hub | `backend/Server/notification_hub.py` | Message broker (default `127.0.0.1:5000`); broadcasts JSON events between services |
| Frame orchestrator | `backend/Python/main.py` | Captures camera frames and sends them over TCP to pipeline workers; relays YOLO plane poses |
| Pipeline workers | `backend/Python/pipeline/` | `gaze`, `face`, `object`, `mediapipe`, `bluetooth` workers (ports 5111–5115) |
| TUIO bridge | `backend/Python/tuio_only_service.py`, `mediapipe_tuio_bridge.py` | Converts finger/YOLO positions into TUIO UDP |
| Game client | `backend/CSharp/TUIO11_NET/` (`TuioDemo`) | C# / .NET Framework 4.8 game built on the TUIO 1.1 C# library |

## Tech Stack
C# (.NET Framework 4.8, WinForms), Python 3.10–3.12, OpenCV, MediaPipe 0.10.14, Ultralytics YOLO (`yolo11n.pt`), NumPy, TUIO / OSC, reacTIVision 1.5.1, pytest.

## Run (Windows)
```powershell
pip install -r backend/Python/requirements.txt
python backend/Server/notification_hub.py      # 1. hub
backend\Scripts\start-hci.ps1                  # 2. workers
.\run-main.ps1                                 # 3. camera pipeline
.\run-tuio.ps1                                 # 4. TUIO bridge
# 5. build & run TuioDemo from backend/CSharp/TUIO11_NET
```
See [`backend/README-Run.md`](backend/README-Run.md) for full run order, environment variables (`backend/Python/.env.example`) and troubleshooting.

## Tests
```powershell
pip install -r backend/Python/requirements-dev.txt
python -m pytest        # or .\run-tests.ps1
```
Tests in `backend/Python/tests` cover the frame protocol, handlers, hub client, camera utils, laser-spot detection, the MediaPipe→TUIO bridge and worker integration.
