"""
Face-only identity for TuioDemo (no TUIO markers for login).

1. Registers on the hub as **face_auth** and sends **mediapipe_camera_active**.
2. Uses the main camera by default (same index as **MAIN_CAPTURE_INDEX**, default **4** via DirectShow).
3. **Known face** (embedding matches a profile whose id exists in users.json): after **FACE_KNOWN_MATCH_FRAMES**
   consecutive matching frames (default **1** = notify on first stable hit), sends **mediapipe_user_ready** with
   **exists: true**. TuioDemo then sends **face_auth_shutdown** and starts **reacTIVision** after a short delay.
4. **Unknown face** for **FACE_NEW_USER_TIMEOUT_SEC** (default 20s) with a stable face in view:
   allocates a synthetic **tuioId** (900001+), appends **users.json**, enrolls the face, sends
   **mediapipe_user_ready** with **exists: false** so TuioDemo opens the sign-up screen.

Hub lines without **target** are broadcast to every Python service; each service ignores events
it does not care about. Hub event **face_auth_shutdown** (target **face_auth**) tells this process to **exit** so the webcam is free for **reacTIVision** after login.

Run **notification_hub.py** and **TuioDemo.exe**, then:
  python face_auth_service.py

If the OpenCV window shows video but **never** a green box / "Unknown face":
  Set **FACE_CAPTURE_INDEX** / **CAMERA_FALLBACK_INDICES** (wrong device = wrong or black image).
  Try **FACE_HAAR_MIN_WIDTH=30** and **FACE_HAAR_MIN_NEIGHBORS=3** if the face is small or dim.
  Face the camera frontally; add room lighting. Press **e** to test enroll when a box appears.
  **CAMERA_MIRROR_HORIZONTAL** — 1 (default): mirror the frame before detection/preview (inverted webcam fix).
"""

from __future__ import annotations

import json
import os
import sys
import time
from datetime import datetime
from typing import Any, Dict, List, Optional, Tuple

_script_dir = os.path.dirname(os.path.abspath(__file__))
if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

import cv2
import numpy as np

from camera_utils import (
    default_face_capture_index,
    mirror_frame_horizontal_if_enabled,
    open_capture_with_fallback,
)

FACE_DB_FILE = os.environ.get("FACE_DB_FILE", os.path.join(_script_dir, "face_database.json"))
ADMIN_FACE_DB_FILE = os.environ.get("ADMIN_FACE_DB_FILE", os.path.join(_script_dir, "admin_face_database.json"))
FACE_RECOGNITION_TOLERANCE = float(os.environ.get("FACE_RECOGNITION_TOLERANCE", "0.6"))
NOTIFY_HOST = os.environ.get("HUB_HOST", os.environ.get("MEDIAPIPE_NOTIFY_HOST", "127.0.0.1"))
NOTIFY_PORT = int(os.environ.get("HUB_PORT", os.environ.get("MEDIAPIPE_NOTIFY_PORT", "5000")))

def _default_users_json_path() -> str:
    """Same file TuioDemo uses (next to TuioDemo.exe); prefer Debug then Release."""
    base = os.path.normpath(os.path.join(_script_dir, "..", "CSharp", "TUIO11_NET", "bin"))
    for cfg in ("Debug", "Release"):
        p = os.path.join(base, cfg, "net48", "users.json")
        if os.path.isfile(p):
            return p
    return os.path.join(base, "Debug", "net48", "users.json")


_env_users = os.environ.get("USERS_JSON_PATH", "").strip()
USERS_JSON_PATH = _env_users if _env_users else _default_users_json_path()


def _default_admins_json_path() -> str:
  base = os.path.normpath(os.path.join(_script_dir, "..", "CSharp", "TUIO11_NET", "bin"))
  for cfg in ("Debug", "Release"):
    p = os.path.join(base, cfg, "net48", "admins.json")
    if os.path.isfile(p):
      return p
  return os.path.join(base, "Debug", "net48", "admins.json")


_env_admins = os.environ.get("ADMINS_JSON_PATH", "").strip()
ADMINS_JSON_PATH = _env_admins if _env_admins else _default_admins_json_path()

# Default index 4 (OBS Virtual Camera; same feed as main.py)
FACE_CAPTURE_INDEX = default_face_capture_index()

# Default 1: first frame where the matched id exists in users.json notifies the hub so the game can
# release the face camera and start reacTIVision immediately. Set to 3–4 if you get false logins.
FACE_KNOWN_MATCH_FRAMES = int(os.environ.get("FACE_KNOWN_MATCH_FRAMES", "1"))
FACE_NEW_USER_TIMEOUT_SEC = float(os.environ.get("FACE_NEW_USER_TIMEOUT_SEC", "20"))
FACE_PROVISION_STABLE_FRAMES = int(os.environ.get("FACE_PROVISION_STABLE_FRAMES", "30"))

RESERVED_TUIO_IDS = {0, 1, 2, 4, 5, 6, 7, 10}

is_paused = False
shutdown_requested = False


def on_hub_command(msg):
    global is_paused, shutdown_requested
    event = msg.get("event")
    target = msg.get("target")
    tnorm = str(target).strip().lower() if target is not None else None
    # Accept broadcast (no target / all) or messages explicitly for this service.
    if tnorm not in (None, "", "all", "face_auth"):
        return
    if event == "pause_auth":
        is_paused = True
        print("[face_auth] PAUSED (user is in-game)")
    elif event == "resume_auth":
        is_paused = False
        print("[face_auth] RESUMED (back to login screen)")
    elif event == "face_auth_shutdown":
        shutdown_requested = True
        print("[face_auth] SHUTDOWN requested — exiting to release camera (e.g. for reacTIVision).")

# Haar detector (env overrides if faces are missed)
FACE_HAAR_SCALE_FACTOR = float(os.environ.get("FACE_HAAR_SCALE_FACTOR", "1.1"))
FACE_HAAR_MIN_NEIGHBORS = int(os.environ.get("FACE_HAAR_MIN_NEIGHBORS", "6"))
FACE_HAAR_MIN_WIDTH = int(os.environ.get("FACE_HAAR_MIN_WIDTH", "60"))
FACE_HAAR_MIN_HEIGHT = int(os.environ.get("FACE_HAAR_MIN_HEIGHT", "60"))
FACE_HAAR_EQUALIZE = os.environ.get("FACE_HAAR_EQUALIZE", "1").strip().lower() not in ("0", "false", "no")


def save_face_profiles(profiles: List[dict]) -> bool:
    try:
        with open(FACE_DB_FILE, "w", encoding="utf-8") as f:
            json.dump(profiles, f, ensure_ascii=False, indent=2)
        return True
    except Exception as ex:
        print("[face_auth] Failed to save face DB:", ex)
        return False


def load_face_profiles(db_path: Optional[str] = None) -> List[dict]:
    path = db_path or FACE_DB_FILE
    if not os.path.isfile(path):
        return []
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception as ex:
        print(f"[face_auth] Failed to read face DB {path}:", ex)
        return []
    if not isinstance(data, list):
        return []
    out: List[dict] = []
    for row in data:
        if not isinstance(row, dict):
            continue
        enc = row.get("encoding")
        if not isinstance(enc, list):
            continue
        out.append(row)
    return out


def load_admins_by_tuio_id() -> Dict[int, Dict[str, Any]]:
    path = ADMINS_JSON_PATH
    if not os.path.isfile(path):
        return {}
    try:
        with open(path, "r", encoding="utf-8") as f:
            root = json.load(f)
    except Exception:
        return {}
    admins = root.get("admins") if isinstance(root, dict) else None
    if not isinstance(admins, list):
        return {}
    by_id: Dict[int, Dict[str, Any]] = {}
    for a in admins:
        if not isinstance(a, dict):
            continue
        try:
            tid = int(a.get("tuioId", 0))
        except (TypeError, ValueError):
            continue
        if tid > 0:
            by_id[tid] = a
    return by_id


def load_users_by_tuio_id() -> Dict[int, Dict[str, Any]]:
    path = USERS_JSON_PATH
    if not os.path.isfile(path):
        return {}
    try:
        with open(path, "r", encoding="utf-8") as f:
            root = json.load(f)
    except Exception:
        return {}
    users = root.get("users") if isinstance(root, dict) else None
    if not isinstance(users, list):
        return {}
    by_id: Dict[int, Dict[str, Any]] = {}
    for u in users:
        if not isinstance(u, dict):
            continue
        try:
            tid = int(u.get("tuioId", 0))
        except (TypeError, ValueError):
            continue
        if tid > 0:
            by_id[tid] = u
    return by_id


def append_user_json(path: str, tuio_id: int, name: Optional[str] = None) -> bool:
    try:
        with open(path, "r", encoding="utf-8") as f:
            root = json.load(f)
    except Exception:
        root = {"users": []}
    if not isinstance(root, dict):
        root = {"users": []}
    users = root.get("users")
    if not isinstance(users, list):
        users = []
        root["users"] = users
    now = datetime.now().isoformat()
    users.append(
        {
            "tuioId": tuio_id,
            "name": name or f"User {tuio_id}",
            "gender": "unspecified",
            "ageGroup": "adult",
            "maxCompletedLevel": 0,
            "createdAt": now,
        }
    )
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(root, f, ensure_ascii=False, indent=2)
        return True
    except Exception as ex:
        print("[face_auth] append_user_json failed:", ex)
        return False


def embedding_from_face(face_bgr: np.ndarray) -> List[float]:
    gray = cv2.cvtColor(face_bgr, cv2.COLOR_BGR2GRAY)
    gray = cv2.resize(gray, (128, 128), interpolation=cv2.INTER_AREA)
    hog = cv2.HOGDescriptor(
        _winSize=(128, 128),
        _blockSize=(16, 16),
        _blockStride=(8, 8),
        _cellSize=(8, 8),
        _nbins=9,
    )
    vec = hog.compute(gray).flatten().astype(np.float32)
    n = np.linalg.norm(vec)
    if n > 1e-6:
        vec = vec / n
    return vec.tolist()


def crop_face(frame_bgr: np.ndarray, detector: cv2.CascadeClassifier) -> Tuple[Optional[np.ndarray], Optional[Tuple[int, int, int, int]]]:
    gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
    if FACE_HAAR_EQUALIZE:
        gray = cv2.equalizeHist(gray)
    mw, mh = max(20, FACE_HAAR_MIN_WIDTH), max(20, FACE_HAAR_MIN_HEIGHT)
    faces = detector.detectMultiScale(
        gray,
        scaleFactor=FACE_HAAR_SCALE_FACTOR,
        minNeighbors=FACE_HAAR_MIN_NEIGHBORS,
        minSize=(mw, mh),
    )
    # Second pass: more sensitive (small / distant faces, softer light)
    if len(faces) == 0:
        faces = detector.detectMultiScale(gray, scaleFactor=1.05, minNeighbors=2, minSize=(28, 28))
    if len(faces) == 0:
        return None, None
    x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
    x1, y1, x2, y2 = x, y, x + w, y + h
    if x2 <= x1 or y2 <= y1:
        return None, None
    return frame_bgr[y1:y2, x1:x2], (x1, y1, x2, y2)


def euclidean_distance(a: List[float], b: List[float]) -> float:
    va = np.array(a, dtype=np.float32)
    vb = np.array(b, dtype=np.float32)
    if va.size == 0 or vb.size == 0 or va.size != vb.size:
        return float("inf")
    return float(np.linalg.norm(va - vb))


def find_best_match(live_encoding: List[float], profiles: List[dict]) -> Tuple[Optional[dict], float, float]:
    best_profile: Optional[dict] = None
    best_distance = float("inf")
    for p in profiles:
        enc = p.get("encoding")
        if not isinstance(enc, list):
            continue
        distance = euclidean_distance(enc, live_encoding)
        if distance < best_distance:
            best_distance = distance
            best_profile = p

    if best_distance == float("inf"):
        return None, float("inf"), 0.0

    confidence = max(0.0, 1.0 - best_distance)
    if best_distance <= FACE_RECOGNITION_TOLERANCE:
        return best_profile, best_distance, confidence
    return None, best_distance, confidence


def enroll_profile_for_tuio(tuio_id: int, face_crop: np.ndarray, profiles: List[dict]) -> bool:
    enc = embedding_from_face(face_crop)
    now_iso = datetime.now().isoformat()
    uid = str(tuio_id)
    existing = next((i for i, p in enumerate(profiles) if str(p.get("id", "")) == uid), None)
    row = {
        "id": uid,
        "name": f"User {tuio_id}",
        "encoding": enc,
        "created_at": now_iso,
        "last_seen": None,
        "recognition_count": 0,
    }
    if existing is None:
        profiles.append(row)
    else:
        profiles[existing].update(
            {
                "encoding": enc,
                "last_seen": now_iso,
                "recognition_count": int(profiles[existing].get("recognition_count", 0) or 0) + 1,
            }
        )
    return save_face_profiles(profiles)


def next_synthetic_tuio_id(users_map: Dict[int, Dict[str, Any]], profiles: List[dict]) -> int:
    used = set(users_map.keys())
    for p in profiles:
        try:
            used.add(int(str(p.get("id", "")).strip()))
        except (TypeError, ValueError):
            pass
    for tid in range(900001, 909999):
        if tid not in used and tid not in RESERVED_TUIO_IDS:
            return tid
    return 900001


def main() -> None:
    from hub_client import HubServiceClient

    enroll_tid = int(os.environ.get("FACE_ENROLL_TUIO_ID", "999001"))

    hub = HubServiceClient("face_auth", host=NOTIFY_HOST, port=NOTIFY_PORT, on_command=on_hub_command)
    try:
        hub.connect()
    except OSError as ex:
        print("[face_auth] Cannot connect to hub at", NOTIFY_HOST, NOTIFY_PORT, "— start notification_hub.py first:", ex)
        sys.exit(1)

    hub.send_event({"event": "mediapipe_camera_active", "active": True})
    print("[face_auth] Sent mediapipe_camera_active.")
    print("[face_auth] users.json:", os.path.abspath(USERS_JSON_PATH), "| exists:", os.path.isfile(USERS_JSON_PATH))
    print("[face_auth] admins.json:", os.path.abspath(ADMINS_JSON_PATH), "| exists:", os.path.isfile(ADMINS_JSON_PATH))
    print("[face_auth] admin faces:", os.path.abspath(ADMIN_FACE_DB_FILE), "| exists:", os.path.isfile(ADMIN_FACE_DB_FILE))

    cap, used_idx = open_capture_with_fallback(
        preferred_index=FACE_CAPTURE_INDEX,
        label="face_auth",
        auto_fallback=True,
        wait_forever=False,
    )
    if cap is None:
        print("[face_auth] No camera — exit.")
        hub.close()
        sys.exit(1)

    cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    detector = cv2.CascadeClassifier(cascade_path)
    if detector.empty():
        print("[face_auth] Missing Haar cascade.")
        cap.release()
        hub.close()
        sys.exit(1)

    notified_id: Optional[int] = None
    known_streak = 0
    unknown_streak = 0
    unknown_since: Optional[float] = None
    last_provision_ts = 0.0

    print("[face_auth] Camera index:", used_idx, "| Camo default via MAIN_CAPTURE_INDEX / FACE_CAPTURE_INDEX")
    print(
        "[face_auth] New user after",
        FACE_NEW_USER_TIMEOUT_SEC,
        "s unknown face (stable). Keys: q=quit | e=manual enroll id",
        enroll_tid,
    )
    print(
        "[face_auth] Haar:",
        f"scale={FACE_HAAR_SCALE_FACTOR} neighbors={FACE_HAAR_MIN_NEIGHBORS}",
        f"minSize=({FACE_HAAR_MIN_WIDTH},{FACE_HAAR_MIN_HEIGHT}) equalize={FACE_HAAR_EQUALIZE}",
    )

    _last_no_face_tip = 0.0
    _logged_frame_shape = False
    _last_no_match_log = 0.0
    _last_not_in_users_log = 0.0

    try:
        while True:
            if shutdown_requested:
                print("[face_auth] Shutting down (camera released).")
                break
            if is_paused:
                time.sleep(0.5)
                continue

            ok, frame = cap.read()
            if not ok or frame is None:
                continue
            frame = mirror_frame_horizontal_if_enabled(frame)
            if not _logged_frame_shape:
                _logged_frame_shape = True
                h0, w0 = frame.shape[:2]
                print(f"[face_auth] Video frame size: {w0}x{h0} (if black/wrong view, set FACE_CAPTURE_INDEX or CAMERA_FALLBACK_INDICES)")

            player_profiles = load_face_profiles(FACE_DB_FILE)
            admin_profiles = load_face_profiles(ADMIN_FACE_DB_FILE)
            users_map = load_users_by_tuio_id()
            admins_map = load_admins_by_tuio_id()

            face, box = crop_face(frame, detector)
            
            # Area check: ignore tiny detections that are likely background noise
            if box:
                x1, y1, x2, y2 = box
                h_f, w_f = frame.shape[:2]
                face_area = (x2 - x1) * (y2 - y1)
                frame_area = w_f * h_f
                # A real face at ~1m distance should be > 2.5% of frame
                if face_area < (frame_area * 0.025):
                    face = None
                    box = None
            
            if box:
                x1, y1, x2, y2 = box
                cv2.rectangle(frame, (x1, y1), (x2, y2), (80, 220, 80), 2)

            label = "No face"
            status = ""

            if face is None:
                notified_id = None
                known_streak = 0
                unknown_streak = 0
                unknown_since = None
                label = "No face — step into view"
                now = time.monotonic()
                if now - _last_no_face_tip > 8.0:
                    _last_no_face_tip = now
                    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                    m = float(np.mean(gray))
                    print(
                        f"[face_auth] No face detected (frame mean brightness={m:.1f}). "
                        "Try FACE_CAPTURE_INDEX=0,1,2… or CAMERA_FALLBACK_INDICES=0,1,2,3 | "
                        "FACE_HAAR_MIN_WIDTH=30 | better frontal light."
                    )
            else:
                live = embedding_from_face(face)

                # 1) Admin face DB + admins.json (never users.json)
                best_admin, dist_a, conf_a = find_best_match(live, admin_profiles)
                admin_tid = 0
                admin_matched = False
                if best_admin is not None:
                    uid_str = str(best_admin.get("id", "")).strip()
                    if uid_str.isdigit():
                        admin_tid = int(uid_str)
                        admin_matched = admin_tid in admins_map

                if best_admin is not None and admin_matched:
                    unknown_streak = 0
                    unknown_since = None
                    known_streak += 1
                    label = f"ADMIN {best_admin.get('name', '?')} id={admin_tid} conf={conf_a:.2f}"
                    if known_streak >= FACE_KNOWN_MATCH_FRAMES and notified_id != admin_tid:
                        a = admins_map.get(admin_tid)
                        payload: Dict[str, Any] = {
                            "event": "mediapipe_user_ready",
                            "exists": True,
                            "isAdmin": True,
                            "tuioId": admin_tid,
                            "name": str((a or {}).get("name") or best_admin.get("name") or "admin"),
                        }
                        hub.send_event(payload)
                        notified_id = admin_tid
                        known_streak = 0
                        status = "Sent admin login"
                        print("[face_auth]", status, payload)
                else:
                    # 2) Player face DB + users.json only
                    best, dist, conf = find_best_match(live, player_profiles)

                    uid_in_users = False
                    tid = 0
                    if best is not None:
                        uid_str = str(best.get("id", "")).strip()
                        if uid_str.isdigit():
                            tid = int(uid_str)
                            uid_in_users = tid in users_map and tid not in admins_map

                    if best is None:
                        nowm = time.monotonic()
                        if nowm - _last_no_match_log > 2.5:
                            _last_no_match_log = nowm
                            print(f"[face_auth] NO MATCH: dist={dist:.3f} conf={conf:.2f}")
                    elif not uid_in_users:
                        nowm = time.monotonic()
                        if nowm - _last_not_in_users_log > 4.0:
                            _last_not_in_users_log = nowm
                            print(
                                f"[face_auth] MATCH (not in users.json): id={best.get('id')} dist={dist:.3f} conf={conf:.2f}"
                            )

                    if best is not None and uid_in_users:
                        unknown_streak = 0
                        unknown_since = None
                        known_streak += 1
                        tid = int(str(best.get("id", "")).strip())
                        label = f"{best.get('name', '?')} id={tid} conf={conf:.2f}"
                        if known_streak >= FACE_KNOWN_MATCH_FRAMES and notified_id != tid:
                            print(
                                f"[face_auth] MATCH FOUND: id={tid} dist={dist:.3f} conf={conf:.2f} "
                                "— notifying hub (TuioDemo will close face camera and start reacTIVision)"
                            )
                            u = users_map.get(tid)
                            payload = {
                                "event": "mediapipe_user_ready",
                                "exists": True,
                                "isAdmin": False,
                                "tuioId": tid,
                                "name": str((u or {}).get("name") or best.get("name") or f"User {tid}"),
                                "ageGroup": str((u or {}).get("ageGroup") or "adult"),
                                "gender": str((u or {}).get("gender") or "unspecified"),
                            }
                            hub.send_event(payload)
                            notified_id = tid
                            known_streak = 0
                            status = "Sent known user"
                            print("[face_auth]", status, payload)
                    else:
                        known_streak = 0
                        notified_id = None
                        unknown_streak += 1
                        if unknown_since is None:
                            unknown_since = time.monotonic()
                        label = (
                            f"Unknown face dist={dist:.3f}"
                            if player_profiles
                            else "No profiles — will create user after timeout"
                        )
                        elapsed = time.monotonic() - (unknown_since or 0)
                        if (
                            elapsed >= FACE_NEW_USER_TIMEOUT_SEC
                            and unknown_streak >= FACE_PROVISION_STABLE_FRAMES
                            and time.monotonic() - last_provision_ts > 5.0
                        ):
                            print(
                                f"[face_auth] PROVISIONING: Streak={unknown_streak} Elapsed={elapsed:.1f}s. Creating new user..."
                            )
                            new_id = next_synthetic_tuio_id(users_map, player_profiles)
                            if append_user_json(USERS_JSON_PATH, new_id, f"User {new_id}"):
                                player_profiles = load_face_profiles(FACE_DB_FILE)
                                if enroll_profile_for_tuio(new_id, face, player_profiles):
                                    users_map = load_users_by_tuio_id()
                                    u = users_map.get(new_id)
                                    payload = {
                                        "event": "mediapipe_user_ready",
                                        "exists": False,
                                        "tuioId": new_id,
                                        "name": str((u or {}).get("name") or f"User {new_id}"),
                                        "ageGroup": "adult",
                                        "gender": "unspecified",
                                    }
                                    hub.send_event(payload)
                                    notified_id = new_id
                                    last_provision_ts = time.monotonic()
                                    unknown_since = None
                                    unknown_streak = 0
                                    status = f"Provisioned new user {new_id}"
                                    print("[face_auth]", status, payload)
                                else:
                                    print(
                                        "[face_auth] Enroll failed after append_user — remove orphan user from users.json if needed."
                                    )
                            else:
                                print("[face_auth] append_user_json failed")

            cv2.putText(frame, label[:80], (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (255, 220, 80), 2)
            if status:
                cv2.putText(frame, status[:80], (10, 52), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (120, 255, 120), 2)
            cv2.imshow("Face auth (hub)", frame)

            key = cv2.waitKey(1) & 0xFF
            if key == ord("q"):
                break
            if key == ord("e"):
                if face is None:
                    print("[face_auth] No face to enroll.")
                else:
                    profiles = load_face_profiles()
                    if enroll_profile_for_tuio(enroll_tid, face, profiles):
                        print(f"[face_auth] Enrolled tuioId={enroll_tid} into {FACE_DB_FILE}")
                    else:
                        print("[face_auth] Enroll save failed.")

    finally:
        cap.release()
        cv2.destroyAllWindows()
        hub.close()
        print("[face_auth] Exit.")


if __name__ == "__main__":
    main()
