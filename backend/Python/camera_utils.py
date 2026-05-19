"""
OpenCV camera helpers: Windows DirectShow first, index probing, optional synthetic frames.

Split-camera defaults (DirectShow on Windows):

- **MAIN_CAPTURE_INDEX** / **FACE_CAPTURE_INDEX** → **4** (OBS Virtual Camera) — main.py, face_auth
- **TUIO_CAPTURE_INDEX** → **2** (SplitCam) — tuio_only_service; reacTIVision uses ``camera.xml`` id **2**

Override any index or set **CAMERA_CAPTURE_BACKEND** (``dshow`` | ``msmf``).

**CAMERA_MIRROR_HORIZONTAL** — default ``1``: horizontally flip frames in Python (selfie-style) so
MediaPipe and on-screen preview match an inverted front camera. Set to ``0`` for raw sensor order.
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path
from typing import Iterator, List, Optional, Tuple


def load_dotenv_if_present() -> None:
    """Load backend/Python/.env into os.environ (does not override existing vars)."""
    env_path = Path(__file__).resolve().parent / ".env"
    if not env_path.is_file():
        return
    try:
        for raw in env_path.read_text(encoding="utf-8").splitlines():
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            if "=" not in line:
                continue
            key, _, val = line.partition("=")
            key = key.strip()
            val = val.strip().strip('"').strip("'")
            if key and key not in os.environ:
                os.environ[key] = val
    except OSError:
        pass


load_dotenv_if_present()

import cv2
import numpy as np

# Index 4: OBS Virtual Camera → main.py / face_auth (MediaPipe, YOLO).
DEFAULT_CAPTURE_INDEX = 4
# Index 2: SplitCam → tuio_only_service (hands / bottle; reacTIVision uses camera.xml id 2).
DEFAULT_TUIO_CAPTURE_INDEX = 2


def default_capture_index() -> int:
    """MAIN_CAPTURE_INDEX / CAMO_CAMERA_INDEX, else 4 (OBS Virtual Camera)."""
    return int(
        os.environ.get(
            "MAIN_CAPTURE_INDEX",
            os.environ.get("CAMO_CAMERA_INDEX", str(DEFAULT_CAPTURE_INDEX)),
        )
    )


def default_tuio_capture_index() -> int:
    """TUIO_CAPTURE_INDEX for tuio_only_service, else 2 (SplitCam)."""
    return int(os.environ.get("TUIO_CAPTURE_INDEX", str(DEFAULT_TUIO_CAPTURE_INDEX)))


def default_face_capture_index() -> int:
    """FACE_CAPTURE_INDEX, else same as default_capture_index() (OBS / index 4)."""
    return int(os.environ.get("FACE_CAPTURE_INDEX", str(default_capture_index())))


def _gray_mean_std(frame: np.ndarray) -> Tuple[float, float]:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    return float(np.mean(gray)), float(np.std(gray))


def _frame_looks_usable(frame: np.ndarray) -> bool:
    """
    Reject captures that open successfully but return black/flat buffers (wrong device or dead stream).
    Tune with CAMERA_MIN_MEAN_GRAY / CAMERA_MIN_STD_GRAY if your room is very dark.
    """
    if frame is None or frame.size == 0:
        return False
    min_mean = float(os.environ.get("CAMERA_MIN_MEAN_GRAY", "10"))
    min_std = float(os.environ.get("CAMERA_MIN_STD_GRAY", "3"))
    m, s = _gray_mean_std(frame)
    return m >= min_mean and s >= min_std


def _backend_specs_for_platform() -> List[Tuple[Optional[int], str]]:
    """
    Pairs of (cv2.CAP_* or None for default, label for logs).
    On Windows we try DSHOW then MSMF then default — same index can work on one API and black-screen on another.
    CAMERA_CAPTURE_BACKEND=msmf|dshow limits to that API first (default still tried after if needed).
    """
    if sys.platform != "win32":
        return [(None, "default")]
    raw = os.environ.get("CAMERA_CAPTURE_BACKEND", "").strip().lower()
    if raw == "msmf":
        return [(cv2.CAP_MSMF, "MSMF"), (None, "default")]
    if raw == "dshow":
        return [(cv2.CAP_DSHOW, "DSHOW"), (None, "default")]
    # Default on Windows: DirectShow first (matches VideoCapture(index, cv2.CAP_DSHOW)).
    if sys.platform == "win32":
        return [(cv2.CAP_DSHOW, "DSHOW"), (None, "default")]
    return [(None, "default")]


def _apply_optional_resolution(cap: cv2.VideoCapture) -> None:
    w = os.environ.get("CAMERA_FRAME_WIDTH", "").strip()
    h = os.environ.get("CAMERA_FRAME_HEIGHT", "").strip()
    if not w or not h:
        return
    try:
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, float(w))
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, float(h))
    except Exception:
        pass


def _open_one_backend(index: int, api: Optional[int]) -> Optional[cv2.VideoCapture]:
    cap: Optional[cv2.VideoCapture] = None
    try:
        cap = cv2.VideoCapture(index, api) if api is not None else cv2.VideoCapture(index)
        if cap is not None and cap.isOpened():
            _apply_optional_resolution(cap)
            return cap
    except Exception:
        pass
    try:
        if cap is not None:
            cap.release()
    except Exception:
        pass
    return None


def _read_smoke_test(cap: cv2.VideoCapture, *, label: str, index: int) -> bool:
    """
    Discard warmup frames, then require several frames with real image content (not all-black).
    """
    if os.environ.get("CAMERA_SKIP_FRAME_CHECK", "").strip().lower() in ("1", "true", "yes"):
        for _ in range(8):
            ok, frame = cap.read()
            if ok and frame is not None:
                return True
            time.sleep(0.04)
        return False

    warmup = int(os.environ.get("CAMERA_WARMUP_FRAMES", "12"))
    for _ in range(max(0, warmup)):
        cap.read()

    need = int(os.environ.get("CAMERA_SMOKE_GOOD_FRAMES", "5"))
    good = 0
    last_m, last_s = 0.0, 0.0
    for _ in range(max(24, need + 10)):
        ok, frame = cap.read()
        if not ok or frame is None:
            time.sleep(0.04)
            continue
        last_m, last_s = _gray_mean_std(frame)
        if _frame_looks_usable(frame):
            good += 1
            if good >= need:
                return True
        time.sleep(0.03)
    print(
        f"[{label}] Camera index {index}: frames look unusable (last mean_gray={last_m:.1f}, std={last_s:.1f}). "
        "Try another index, CAMERA_CAPTURE_BACKEND=msmf, or lower CAMERA_MIN_MEAN_GRAY if the room is dark."
    )
    return False


def _parse_fallback_indices(preferred: int) -> List[int]:
    raw = os.environ.get("CAMERA_FALLBACK_INDICES", "").strip()
    if raw:
        out: List[int] = []
        for p in raw.split(","):
            p = p.strip()
            if not p:
                continue
            try:
                out.append(int(p))
            except ValueError:
                pass
        if preferred not in out:
            return [preferred] + out
        return out
    order = [preferred, 0, 1, 2, 3, 4, 5, 6, 7]
    seen = set()
    unique: List[int] = []
    for x in order:
        if x not in seen:
            seen.add(x)
            unique.append(x)
    return unique


def open_capture_with_fallback(
    *,
    preferred_index: int,
    label: str,
    auto_fallback: bool = True,
    wait_forever: bool = False,
) -> Tuple[Optional[cv2.VideoCapture], int]:
    """
    Try to open a working capture. When auto_fallback is True, probe fallback indices
    (MAIN_CAPTURE_INDEX / TUIO_CAPTURE_INDEX plus COMMON_FALLBACK).

    If wait_forever is True, block retrying every 2s until a camera appears (Ctrl+C to abort).
    """
    indices = _parse_fallback_indices(preferred_index) if auto_fallback else [preferred_index]

    def _once() -> Tuple[Optional[cv2.VideoCapture], int]:
        backends = _backend_specs_for_platform()
        for idx in indices:
            for api, api_name in backends:
                cap = _open_one_backend(idx, api)
                if cap is None:
                    continue
                if not _read_smoke_test(cap, label=label, index=idx):
                    try:
                        cap.release()
                    except Exception:
                        pass
                    continue
                print(f"[{label}] Opened camera index {idx} via {api_name} (preferred was {preferred_index})")
                return cap, idx
        return None, -1

    while True:
        cap, idx = _once()
        if cap is not None:
            return cap, idx
        print(
            f"[{label}] No camera from indices {indices}. "
            f"Set CAMERA_FALLBACK_INDICES (comma list) or *_CAPTURE_INDEX. "
            f"Windows: close other apps using the webcam."
        )
        if not wait_forever:
            return None, -1
        print(f"[{label}] Waiting 2s for a camera (Ctrl+C to stop)…")
        time.sleep(2.0)


def mirror_frame_horizontal_if_enabled(frame: Optional[np.ndarray]) -> Optional[np.ndarray]:
    """
    Flip webcam frames left/right (selfie-style) when CAMERA_MIRROR_HORIZONTAL is enabled.
    Default is **on** (1) so MediaPipe / preview match a typical mirrored front camera.
    Set CAMERA_MIRROR_HORIZONTAL=0 to use the raw sensor orientation.
    """
    if frame is None or frame.size == 0:
        return frame
    raw = os.environ.get("CAMERA_MIRROR_HORIZONTAL", "1").strip().lower()
    if raw in ("0", "false", "no"):
        return frame
    return cv2.flip(frame, 1)


def synthetic_frame_generator(width: int, height: int) -> Iterator[np.ndarray]:
    """BGR blank frames for pipeline keep-alive when no camera is available."""
    blank = np.zeros((height, width, 3), dtype=np.uint8)
    while True:
        yield blank.copy()
