"""
Laptop camera → TUIO UDP (OSC). Uses **TUIO_CAPTURE_INDEX** (default **2** SplitCam, DirectShow on Windows).
**main.py** uses **MAIN_CAPTURE_INDEX** for Camo. MediaPipe **Hands** and **Face Mesh** (gaze iris + optional facial mood)
read the **same** mirrored frames from that one capture after login (not the face-login stream).

Optional **TUIO_HUB_ENABLE**: subscribe to NotificationHub. **plane_pose** — object/YOLO pipeline
(via **main.py** relay). **tuio_resume_hand_camera** (broadcast from TuioDemo after face login) and
**login** (same broadcast) arm reopening the physical webcam on the **main thread** (safe with synthetic frames).
Used when **TUIO_SKIP_CAMERA=1** during face_auth, or after login if capture was not available at startup.
Merge policy: **TUIO_PLANE_SOURCE** = finger | yolo | merge | **laser** | **bottle_hand** (default **merge**).
With **TUIO_LASER_ENABLE=1** and **merge**, priority is **laser dot → finger → YOLO**.
**bottle_hand** (aliases ``cup_hand`` / ``cup`` / ``bottle``): YOLO **bottle** on the webcam when detected,
else MediaPipe **finger** (like hand tracking). Set ``OBJECT_TARGET_CLASSES=bottle``.
**bottle_signup**: bottle for sign-up dial/select/submit only; **finger** controls the plane in-game.

**Laser pointer** (same webcam, OpenCV blob): **TUIO_LASER_ENABLE** — **1** (default when using ``start-hci`` unset override) tracks a bright or colored spot. **TUIO_LASER_MODE** — ``bright`` (default), ``green``, or ``red``. **TUIO_LASER_VMIN** / **TUIO_LASER_SMAX** / **TUIO_LASER_MIN_AREA** / **TUIO_LASER_MAX_AREA** / **TUIO_LASER_BLUR** / **TUIO_LASER_SMOOTH** / **TUIO_LASER_HOLD_FRAMES** — see ``detect_laser_spot_norm_xy`` in code.

Env:
  CAMERA_MIRROR_HORIZONTAL — 1 (default): mirror each frame before MediaPipe / preview (fixes inverted webcam).
  TUIO_AUTO_FRAME_FACE — default **0** = normal full-frame (no digital zoom). Set **1** to auto zoom/center
  the face for **Face Mesh** (gaze and/or emotion) + Hands; does not move the physical camera.
  TUIO_AUTO_FRAME_MARGIN — extra padding around the face box (default **0.28**).
  TUIO_AUTO_FRAME_SMOOTH — how fast the crop follows the head (default **0.18**); lower = calmer framing.
  TUIO_AUTO_FRAME_MIN_WIDTH — minimum crop width as a fraction of frame width (default **0.42**); avoids over-zoom.
  TUIO_HOST / TUIO_PORT — UDP target for TuioDemo (default 127.0.0.1:3333)
  TUIO_HUB_ENABLE — 1 to connect to hub as service **tuio** (default 1)
  HUB_HOST / HUB_PORT — TCP hub (default 127.0.0.1:5000)
  TUIO_PLANE_SOURCE — finger | merge | laser | **bottle_hand** | **bottle_signup**
  PLANE_MAP_SCALE_X, PLANE_MAP_SCALE_Y, PLANE_MAP_OFFSET_X, PLANE_MAP_OFFSET_Y — calibration
  CAMERA_FALLBACK_INDICES — optional comma list (e.g. 0,1,2) tried after preferred index
  TUIO_WAIT_FOR_CAMERA — 1 = retry until a camera appears (blocks)
  TUIO_ALLOW_NO_CAMERA — 1 (default) = keep running with blank frames + hub/YOLO TUIO if camera missing
  TUIO_SKIP_CAMERA — 1 = never open a capture at startup (synthetic frames). Frees the webcam for face_auth;
  TuioDemo then sends **tuio_resume_hand_camera** so this service can open the device for gameplay.
  TUIO_GAZE_ENABLE — 1 (default) = while the **real** webcam is open, run Face Mesh iris gaze and accumulate a heatmap;
  on quit (Q or window close), show the heatmap window. Set 0 to disable.
  TUIO_GAZE_GAIN — iris→screen scale (default **3.45**); lower = calmer motion on screen.
  TUIO_GAZE_MEDIAN_W — median window before EMA (default **9**); larger = smoother, more lag.
  TUIO_GAZE_DISPLAY_EMA — base blend after median (default **0.20**); **lower** = calmer, **higher** = snappier.
  TUIO_GAZE_FACE_MIN_DET / TUIO_GAZE_FACE_MIN_TRACK — Face Mesh (default **0.44** / **0.44**); slightly higher = steadier, less jitter when the face is ambiguous.
  TUIO_GAZE_FLIP_X — extra horizontal flip of gaze math only (default 0). Usually unnecessary when
  **CAMERA_MIRROR_HORIZONTAL=1** mirrors the whole frame before MediaPipe.
  TUIO_GAZE_FLIP_Y — vertical flip of iris offset (default **1**): look up moves the dot up on screen;
  set **0** if your setup already matched vertical correctly.
  TUIO_GAZE_GRID_W / TUIO_GAZE_GRID_H — heatmap resolution (default 96 × 54).
  TUIO_GAZE_HEATMAP_BLUR_SIGMA — Gaussian blur on accumulated counts before coloring (default **2.25**);
  **0** disables blur. Larger = smoother heatmap (less blocky after upscale).
  TUIO_GAZE_EVERY_N — process face mesh every N frames (default 1).
  TUIO_GAZE_HEATMAP_SAVE — optional explicit PNG path, or a **folder** to save into (default file name is used).
  TUIO_GAZE_HEATMAP_AUTO_SAVE — 1 (default) = when gaze ran, save PNG under the **outside** folder (see below);
  set 0 to save only if TUIO_GAZE_HEATMAP_SAVE is set.
  TUIO_GAZE_HEATMAP_SHOW — 1 (default) = open a heatmap window after run; 0 = save-only / headless.
  TUIO_GAZE_OVERLAY — 1 (default) = stream gaze as TUIO symbol **11** for a soft dot on the game (TuioDemo).
  TUIO_GAZE_TUIO_SMOOTH — OSC blend toward filtered gaze (default **0.38**); **lower** = calmer pointer.
  TUIO_GAZE_TUIO_HOLD_S — after the face drops, keep sending symbol **11** at the last position up to this many seconds (default **0.85**; max **3**) so the client does not lose the dot every frame.
  TUIO_EMOTION_HUB — when **1** (default) and the hub + Face Mesh run, estimate **happy / sad / confused /
  neutral** from mouth shape (landmarks) and broadcast ``user_facial_emotion`` so TuioDemo can tint the GUI
  or show clues. Set **0** to disable.
  Uses the **same** ``VideoCapture`` frames as MediaPipe Hands (``TUIO_CAPTURE_INDEX`` / ``MAIN_CAPTURE_INDEX``);
  does not use the face-login camera. Face Mesh is opened when **TUIO_GAZE_ENABLE=1** *or* ``TUIO_EMOTION_HUB=1``.
  TUIO_EMOTION_STABLE_FRAMES — consecutive frames that must agree before the hub sees a change for **neutral**
  and other non‑happy labels except where overridden below (default **7**).
  TUIO_EMOTION_STABLE_FRAMES_SAD — consecutive **sad** / **confused** frames before broadcasting CALM/HINT mode
  (default **min(5, STABLE)**); lower = faster empathy hints on TuioDemo.
  TUIO_EMOTION_STABLE_FRAMES_HAPPY — consecutive **happy** frames required before broadcasting HAPPY MODE (default **max(12, STABLE+5)**);
  higher reduces false happy from resting faces / landmark noise.
  TUIO_EMOTION_MIN_INTERVAL_S — minimum seconds between hub emotion messages (default **0.35**).
  TUIO_FACE_PIPELINE_THREAD — when **1** (default), **Face Mesh** (face + gaze + emotion) runs on a **single worker
  thread** while **Hands** runs on the main thread on the **same** camera frame (RGB copy for Face Mesh). This keeps
  ``face_mesh.process`` off the main loop for smoother play. Set **0** for fully sequential processing. When
  **TUIO_AUTO_FRAME_FACE=1** (digital zoom), processing stays on the main thread (Face Mesh must run before Hands on
  the cropped frame).
  TUIO_PREVIEW — **1** (default) = OpenCV window ``TUIO-only (laptop)``. Set **0** for headless.
  TUIO_PREVIEW_FACE_BOX — **1** (default) = draw a **green** rectangle around the detected face (Face Mesh) on that preview;
  **cyan** text above it shows the **instant** heuristic emotion + score (same as hub logic, not debounced).

  Sign-up: with ``TUIO_PLANE_SOURCE=bottle_signup``, a **water bottle** drives dial (swipe L/R), select (hold center),
  submit (hold low). Otherwise MediaPipe **hand** swipe + fist/peace. Plane uses **finger** in-game only.
  Tune ``TUIO_BOTTLE_SIGNUP_*`` or ``TUIO_SIGNUP_*`` env vars (see ``signup_bottle_control.py``).

  Default save folder: parent of the repo folder that contains ``hci2``. If that folder has the same name as
  the inner folder (e.g. ``...\\NEWHCIV1\\NEWHCIV1\\hci2``), files go to the **outer** ``...\\NEWHCIV1``.
  Otherwise files go next to ``hci2`` (the inner project root).
"""

from __future__ import annotations

import os
import sys
import time
from collections import deque
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures import TimeoutError as FuturesTimeout
from typing import Any, Deque, Dict, Optional, Tuple

_script_dir = os.path.dirname(os.path.abspath(__file__))

import cv2
import numpy as np

# Import MediaPipe before inserting script dir first on sys.path (avoids accidental shadowing).
_mp_import_error: Optional[str] = None
mp = None  # type: ignore
try:
    import mediapipe as mp  # type: ignore

    if mp is not None and not hasattr(mp, "solutions"):
        _mp_import_error = (
            "mediapipe package has no 'solutions' (broken or incompatible install). "
            "Try: pip uninstall mediapipe -y && pip install 'mediapipe==0.10.14'"
        )
        mp = None
except ImportError as ex:
    _mp_import_error = str(ex)
    mp = None

if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

try:
    from mediapipe_tuio_bridge import MediapipeTuioBridge
except ImportError:
    MediapipeTuioBridge = None  # type: ignore

from camera_utils import (
    default_tuio_capture_index,
    mirror_frame_horizontal_if_enabled,
    open_capture_with_fallback,
    synthetic_frame_generator,
)


def _wrap_hint_line(
    text: str,
    font: int,
    scale: float,
    thickness: int,
    max_px: int,
) -> list[str]:
    words = str(text).split()
    if not words:
        return [""]
    lines: list[str] = []
    current = ""
    for word in words:
        trial = f"{current} {word}".strip()
        (tw, _), _ = cv2.getTextSize(trial, font, scale, thickness)
        if tw <= max_px or not current:
            current = trial
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def draw_hint_panel(
    frame: np.ndarray,
    lines: list[str],
    *,
    margin: int = 8,
    pad: int = 10,
    line_gap: int = 5,
    font_scale: float = 0.45,
    thickness: int = 1,
    fg: Tuple[int, int, int] = (220, 235, 255),
    accent: Tuple[int, int, int] = (255, 200, 0),
    bg: Tuple[int, int, int] = (42, 48, 62),
) -> None:
    """Draw a bottom-left hint box sized to fit all lines (word-wrapped)."""
    h, w = frame.shape[:2]
    if h < 8 or w < 8:
        return
    font = cv2.FONT_HERSHEY_SIMPLEX
    max_text_w = max(80, int(w * 0.94) - 2 * pad)
    wrapped: list[str] = []
    for line in lines:
        if not str(line).strip():
            continue
        wrapped.extend(_wrap_hint_line(str(line).strip(), font, font_scale, thickness, max_text_w))
    if not wrapped:
        return

    line_sizes = [cv2.getTextSize(ln, font, font_scale, thickness)[0] for ln in wrapped]
    box_w = min(w - 2 * margin, max(sz[0] for sz in line_sizes) + 2 * pad)
    line_h = max(sz[1] for sz in line_sizes)
    box_h = min(h - 2 * margin, len(wrapped) * line_h + (len(wrapped) - 1) * line_gap + 2 * pad)
    x0 = margin
    y0 = h - margin - box_h
    x1 = x0 + box_w
    y1 = y0 + box_h

    overlay = frame.copy()
    cv2.rectangle(overlay, (x0, y0), (x1, y1), bg, -1, lineType=cv2.LINE_AA)
    cv2.addWeighted(overlay, 0.82, frame, 0.18, 0, frame)
    cv2.rectangle(frame, (x0, y0), (x1, y1), accent, 1, lineType=cv2.LINE_AA)

    ty = y0 + pad + line_h
    for i, ln in enumerate(wrapped):
        color = accent if i == 0 else fg
        cv2.putText(
            frame,
            ln,
            (x0 + pad, ty + i * (line_h + line_gap)),
            font,
            font_scale,
            color,
            thickness,
            cv2.LINE_AA,
        )


# Refined iris rings (5 points each) — centroid tracks the pupil better than a single landmark.
_LEFT_IRIS_RING = (468, 469, 470, 471, 472)
_RIGHT_IRIS_RING = (473, 474, 475, 476, 477)
_L_EYE_OUTER, _L_EYE_INNER = 263, 362
_R_EYE_OUTER, _R_EYE_INNER = 33, 133
# Vertical aperture: several lid / corner points per eye (more stable than two lids only).
_L_EYE_Y_ANCHORS = (263, 362, 386, 385, 384, 374, 380, 381)
_R_EYE_Y_ANCHORS = (33, 133, 159, 160, 161, 145, 144, 163)


def _iris_ring_centroid(lm: Any, indices: Tuple[int, ...]) -> Optional[Tuple[float, float]]:
    try:
        pts = np.array([[lm[i].x, lm[i].y] for i in indices], dtype=np.float64)
    except (IndexError, AttributeError):
        return None
    if pts.shape[0] < 2:
        return None
    c = pts.mean(axis=0)
    return float(c[0]), float(c[1])


def _iris_offset_weighted(
    lm: Any,
    iris_ring: Tuple[int, ...],
    outer: int,
    inner: int,
    y_anchors: Tuple[int, ...],
) -> Optional[Tuple[float, float, float]]:
    """
    Iris centroid vs eye opening → (hx, hy) ~ (-1..1) and a weight (larger = more reliable).
    """
    ic = _iris_ring_centroid(lm, iris_ring)
    if ic is None:
        return None
    ix, iy = ic
    try:
        ox, oy = float(lm[outer].x), float(lm[outer].y)
        nx, ny = float(lm[inner].x), float(lm[inner].y)
        ys = np.array([float(lm[i].y) for i in y_anchors], dtype=np.float64)
    except (IndexError, AttributeError):
        return None
    ymin, ymax = float(ys.min()), float(ys.max())
    ecx = 0.5 * (ox + nx)
    ecy = 0.5 * (ymin + ymax)
    half_w = max(0.5 * abs(nx - ox), 1e-4)
    half_h = max(0.5 * abs(ymax - ymin), 1e-4)
    hx = (ix - ecx) / half_w
    hy = (iy - ecy) / half_h
    weight = min(4.0, half_w * 22.0) * min(4.0, half_h * 28.0)
    return hx, hy, weight


def _gaze_norm_xy_from_face_landmarks(
    face_lms: Any, gain: float, flip_x: bool, flip_y: bool
) -> Optional[Tuple[float, float]]:
    """Binocular iris rings + confidence-weighted fusion → normalized [0,1] gaze."""
    lm = face_lms.landmark
    lo = _iris_offset_weighted(lm, _LEFT_IRIS_RING, _L_EYE_OUTER, _L_EYE_INNER, _L_EYE_Y_ANCHORS)
    ro = _iris_offset_weighted(lm, _RIGHT_IRIS_RING, _R_EYE_OUTER, _R_EYE_INNER, _R_EYE_Y_ANCHORS)
    if lo is None and ro is None:
        return None
    if lo is None:
        hx, hy = ro[0], ro[1]
    elif ro is None:
        hx, hy = lo[0], lo[1]
    else:
        wl, wr = max(1e-4, lo[2]), max(1e-4, ro[2])
        wsum = wl + wr
        hx = (wl * lo[0] + wr * ro[0]) / wsum
        hy = (wl * lo[1] + wr * ro[1]) / wsum
    if flip_x:
        hx = -hx
    if flip_y:
        hy = -hy
    k = 0.5 * float(gain)
    gx = 0.5 + hx * k
    gy = 0.5 + hy * k
    return max(0.0, min(1.0, gx)), max(0.0, min(1.0, gy))


# Mouth / lip indices (MediaPipe Face Mesh 468) — geometry-only happy vs sad heuristic.
_LIP_UPPER_MID = 12
_LIP_LOWER_MID = 15
_MOUTH_LEFT_CORNER = 61
_MOUTH_RIGHT_CORNER = 291
# Chin / forehead — vertical span for pitch‑robust mouth cues (same 468 model).
_F_MESH_FOREHEAD = 10
_F_MESH_CHIN = 152


def _emotion_label_from_face_landmarks(face_lms: Any) -> Optional[Tuple[str, float]]:
    """
    Map lip geometry → (happy|sad|confused|neutral, confidence).

    ``confused`` is a heuristic for uneven / uncertain mouth without a clear smile or deep frown
    (asymmetric corners, skeptical twist). Uses only Face Mesh landmarks.
    """
    try:
        lm = face_lms.landmark
        y_l = float(lm[_MOUTH_LEFT_CORNER].y)
        y_r = float(lm[_MOUTH_RIGHT_CORNER].y)
        y_u = float(lm[_LIP_UPPER_MID].y)
        y_lo = float(lm[_LIP_LOWER_MID].y)
        x_l = float(lm[_MOUTH_LEFT_CORNER].x)
        x_r = float(lm[_MOUTH_RIGHT_CORNER].x)
        y_fore = float(lm[_F_MESH_FOREHEAD].y)
        y_chin = float(lm[_F_MESH_CHIN].y)
    except (IndexError, AttributeError):
        return None
    mid_lip_y = 0.5 * (y_u + y_lo)
    mid_corner_y = 0.5 * (y_l + y_r)
    # Corners higher on the image (smaller y) than lip midline → smile-like uplift.
    uplift = mid_lip_y - mid_corner_y
    downturn = max(0.0, float(-uplift))
    face_h = float(y_chin - y_fore)
    face_h = max(0.13, min(0.82, face_h))
    rel_uplift = uplift / face_h
    rel_downturn = max(0.0, float(-rel_uplift))

    mouth_w = float(np.hypot(x_r - x_l, y_r - y_l))
    mouth_h = abs(y_lo - y_u) + 1e-6
    aspect = mouth_w / mouth_h
    open_smile = uplift > 0.012 and aspect < 3.22
    mouth_aperture = mouth_h / (mouth_w + 1e-6)
    open_grin = uplift > 0.011 and mouth_aperture > 0.31

    aspect_sad_weight = max(0.0, 3.85 - aspect) * 0.36
    if uplift > 0.008:
        aspect_sad_weight *= 0.17
    elif abs(uplift) < 0.0045:
        aspect_sad_weight *= 0.48

    happy_raw = uplift * 22.0 + max(0.0, aspect - 3.25) * 0.35
    if open_smile or open_grin:
        happy_raw += 0.18
    if downturn >= 0.004:
        happy_raw -= min(0.45, downturn * 36.0)
    if rel_downturn >= 0.008:
        happy_raw -= min(0.38, (rel_downturn - 0.008) * 42.0)

    # Pixel + shape‑normalized frown (either cue can trigger CALM MODE).
    sad_raw = downturn * 58.0 + rel_downturn * 26.0 + aspect_sad_weight
    if downturn >= 0.0045:
        sad_raw += 0.11 + min(0.16, (downturn - 0.0045) * 34.0)
    if rel_downturn >= 0.009:
        sad_raw += 0.08 + min(0.14, (rel_downturn - 0.009) * 26.0)

    clear_frown = downturn >= 0.0032 or rel_downturn >= 0.0078
    weak_frown_hint = downturn >= 0.0024 or rel_downturn >= 0.0059

    happy_threshold = 0.59
    if aspect >= 3.45 and not (open_smile or open_grin):
        happy_threshold = 0.66
    win_margin = 0.26
    if weak_frown_hint:
        win_margin += 0.13

    def _sad_conf(sf: float) -> float:
        return max(0.35, min(1.0, 0.45 + 0.55 * min(1.0, (sad_raw - sf) / 0.48)))

    # Prefer sad when geometry shows downturn, even if aspect still feeds happy_raw a little.
    if clear_frown:
        sf = 0.22 if rel_downturn >= 0.0115 else (0.26 if downturn >= 0.0055 else 0.29)
        tie_eps = 0.035 if rel_downturn >= 0.010 else 0.02
        if sad_raw > sf and sad_raw + tie_eps >= happy_raw:
            return "sad", _sad_conf(sf)

    sad_margin = 0.06 if weak_frown_hint else 0.09
    sad_floor = 0.30
    if downturn >= 0.0085 or rel_downturn >= 0.012:
        sad_floor = 0.21
    elif downturn >= 0.006 or rel_downturn >= 0.0095:
        sad_floor = 0.24
    elif downturn >= 0.0045 or rel_downturn >= 0.008:
        sad_floor = 0.27

    if sad_raw > sad_floor and sad_raw > happy_raw + sad_margin:
        return "sad", _sad_conf(sad_floor)

    if happy_raw > happy_threshold and happy_raw > sad_raw + win_margin:
        ok_uplift = (
            ((open_smile or open_grin) and uplift >= 0.0095)
            or ((not open_smile and not open_grin) and uplift >= 0.014)
        )
        if ok_uplift:
            conf = max(0.35, min(1.0, 0.45 + 0.55 * min(1.0, (happy_raw - happy_threshold) / 0.52)))
            return "happy", conf

    # Confused / uncertain: uneven mouth without qualifying as full sad or happy.
    vert_asym = abs(y_l - y_r) / face_h
    slant_asym = abs(y_l - y_r) / max(mouth_w, 1e-6)
    not_peak = sad_raw < 0.58 and happy_raw < 0.50
    relaxed_mouth = -0.0048 < uplift < 0.013
    if not_peak and relaxed_mouth and not clear_frown:
        if vert_asym > 0.014:
            c_conf = max(0.38, min(1.0, 0.40 + vert_asym * 26.0))
            return "confused", c_conf
        if slant_asym > 0.38 and vert_asym > 0.0095:
            c_conf = max(0.38, min(1.0, 0.36 + slant_asym * 0.55))
            return "confused", c_conf

    return "neutral", max(0.2, 1.0 - max(happy_raw, sad_raw))


def _draw_face_bbox_on_frame(
    frame: np.ndarray,
    face_lms: Any,
    emotion_pair: Optional[Tuple[str, float]] = None,
) -> None:
    """Axis-aligned box from all Face Mesh landmarks (normalized) → green BGR overlay on ``frame``."""
    try:
        pts = face_lms.landmark
    except AttributeError:
        return
    h, w = frame.shape[:2]
    if h < 4 or w < 4:
        return
    xs: list[float] = []
    ys: list[float] = []
    for p in pts:
        try:
            xs.append(float(p.x) * float(w))
            ys.append(float(p.y) * float(h))
        except (AttributeError, TypeError, ValueError):
            continue
    if len(xs) < 40:
        return
    pad = max(4, int(0.022 * min(w, h)))
    x0 = int(max(0, min(xs) - pad))
    y0 = int(max(0, min(ys) - pad))
    x1 = int(min(w - 1, max(xs) + pad))
    y1 = int(min(h - 1, max(ys) + pad))
    if x1 <= x0 + 3 or y1 <= y0 + 3:
        return
    green = (0, 220, 0)
    cv2.rectangle(frame, (x0, y0), (x1, y1), green, 2, lineType=cv2.LINE_AA)
    font = cv2.FONT_HERSHEY_SIMPLEX
    if emotion_pair is not None:
        lab, sc = emotion_pair[0], float(emotion_pair[1])
        emo_txt = f"{lab} {sc:.2f}"
        ty_emo = max(22, y0 - 26)
        for ox, oy in ((1, 1), (-1, -1), (1, -1), (-1, 1)):
            cv2.putText(frame, emo_txt, (x0 + ox, ty_emo + oy), font, 0.52, (0, 0, 0), 2, cv2.LINE_AA)
        cv2.putText(frame, emo_txt, (x0, ty_emo), font, 0.52, (0, 255, 255), 1, cv2.LINE_AA)
    cv2.putText(
        frame,
        "Face",
        (x0, max(14, y0 - 6)),
        font,
        0.55,
        green,
        2,
        lineType=cv2.LINE_AA,
    )


class _EmotionHubDebouncer:
    """Require stable labels before emitting hub events (reduces GUI flicker)."""

    def __init__(
        self,
        stable_n: int,
        min_interval_s: float,
        stable_n_happy: Optional[int] = None,
        stable_n_sad: Optional[int] = None,
    ) -> None:
        self._stable_n = max(3, int(stable_n))
        self._stable_n_happy = max(
            self._stable_n + 3,
            int(stable_n_happy) if stable_n_happy is not None else max(self._stable_n + 5, 12),
        )
        # Faster path for sad/confused so CALM/HINT modes + gaze hints trigger without flicker.
        self._stable_n_sad = (
            max(3, min(int(stable_n_sad), self._stable_n))
            if stable_n_sad is not None
            else max(3, min(5, self._stable_n))
        )
        self._buf_max = max(self._stable_n, self._stable_n_happy)
        self._buf: Deque[Tuple[str, float]] = deque(maxlen=self._buf_max)
        self._min_interval_s = max(0.08, float(min_interval_s))
        self._last_sent: Optional[str] = None
        self._last_sent_ts = 0.0

    def reset(self) -> None:
        self._buf.clear()

    def _emit_if_changed(self, label: str, conf: float) -> Optional[Tuple[str, float]]:
        now = time.monotonic()
        if label == self._last_sent:
            return None
        if self._last_sent is not None and (now - self._last_sent_ts) < self._min_interval_s:
            return None
        if self._last_sent is None and label == "neutral":
            self._last_sent = "neutral"
            self._last_sent_ts = now
            return None
        self._last_sent = label
        self._last_sent_ts = now
        return label, conf

    def push(self, pair: Optional[Tuple[str, float]]) -> Optional[Tuple[str, float]]:
        """Returns (emotion, score) to send on the hub when the stable label *changes*."""
        if pair is None:
            self._buf.clear()
            return None
        self._buf.append(pair)

        # Happy stays strict (long tail) to avoid false positives.
        if len(self._buf) >= self._stable_n_happy:
            tail_h = list(self._buf)[-self._stable_n_happy :]
            if all(p[0] == "happy" for p in tail_h):
                conf = float(np.mean([p[1] for p in tail_h]))
                out = self._emit_if_changed("happy", conf)
                if out is not None:
                    return out
                return None

        # Sad / confused: shorter tail so TuioDemo can show empathy + flag hints sooner.
        if len(self._buf) >= self._stable_n_sad:
            tail_s = list(self._buf)[-self._stable_n_sad :]
            lab0 = tail_s[0][0]
            if lab0 in ("sad", "confused") and all(p[0] == lab0 for p in tail_s):
                conf = float(np.mean([p[1] for p in tail_s]))
                out = self._emit_if_changed(lab0, conf)
                if out is not None:
                    return out
                return None

        if len(self._buf) < self._stable_n:
            return None
        tail_short = list(self._buf)[-self._stable_n :]
        if not all(p[0] == tail_short[0][0] for p in tail_short):
            return None
        label = tail_short[0][0]
        if label == "happy":
            return None
        conf = float(np.mean([p[1] for p in tail_short]))
        return self._emit_if_changed(label, conf)


class _GazeStabilizer:
    """Median over a short window + adaptive EMA to reject spikes while tracking saccades."""

    def __init__(self, window: int, base_alpha: float) -> None:
        self._window = max(1, int(window))
        self._base_alpha = max(0.05, min(1.0, float(base_alpha)))
        self._buf: Deque[Tuple[float, float]] = deque(maxlen=self._window)
        self._fx = 0.5
        self._fy = 0.5
        self._has = False

    def reset(self) -> None:
        self._buf.clear()
        self._has = False

    def push(self, gx: float, gy: float) -> Tuple[float, float]:
        self._buf.append((gx, gy))
        arr = np.array(list(self._buf), dtype=np.float64)
        mx = float(np.median(arr[:, 0]))
        my = float(np.median(arr[:, 1]))
        if not self._has:
            self._fx, self._fy = mx, my
            self._has = True
            return mx, my
        err = abs(mx - self._fx) + abs(my - self._fy)
        alpha = min(1.0, self._base_alpha + 0.28 * err)
        self._fx += alpha * (mx - self._fx)
        self._fy += alpha * (my - self._fy)
        return max(0.0, min(1.0, self._fx)), max(0.0, min(1.0, self._fy))


class _AutoFaceFrameState:
    """
    Smooth face-centered crop on the full mirrored frame, then resize back to WxH.
    Improves hand + iris tracking without moving the physical webcam.
    """

    def __init__(self, margin: float, smooth: float, min_width_frac: float) -> None:
        self.margin = max(0.05, min(0.55, float(margin)))
        self.smooth = max(0.04, min(0.6, float(smooth)))
        self.min_width_frac = max(0.28, min(0.92, float(min_width_frac)))
        self.rx0, self.ry0, self.rx1, self.ry1 = 0.0, 0.0, 1.0, 1.0

    def update(self, face_lms: Any) -> None:
        xs = [float(p.x) for p in face_lms.landmark]
        ys = [float(p.y) for p in face_lms.landmark]
        if not xs:
            return
        mn_x, mx_x = min(xs), max(xs)
        mn_y, mx_y = min(ys), max(ys)
        span_x = max(1e-4, mx_x - mn_x)
        span_y = max(1e-4, mx_y - mn_y)
        nx0 = max(0.0, mn_x - self.margin * span_x)
        nx1 = min(1.0, mx_x + self.margin * span_x)
        ny0 = max(0.0, mn_y - self.margin * span_y)
        ny1 = min(1.0, mx_y + self.margin * span_y)
        nw, nh = nx1 - nx0, ny1 - ny0
        if nw < self.min_width_frac:
            cx = 0.5 * (nx0 + nx1)
            nx0 = max(0.0, cx - 0.5 * self.min_width_frac)
            nx1 = min(1.0, nx0 + self.min_width_frac)
            if nx1 >= 1.0:
                nx0 = max(0.0, 1.0 - self.min_width_frac)
                nx1 = 1.0
        if nh < self.min_width_frac * 0.85:
            cy = 0.5 * (ny0 + ny1)
            half = 0.5 * self.min_width_frac * 0.85
            ny0 = max(0.0, cy - half)
            ny1 = min(1.0, ny0 + 2 * half)
            if ny1 >= 1.0:
                ny0 = max(0.0, 1.0 - 2 * half)
                ny1 = 1.0
        s = self.smooth
        self.rx0 = (1.0 - s) * self.rx0 + s * nx0
        self.ry0 = (1.0 - s) * self.ry0 + s * ny0
        self.rx1 = (1.0 - s) * self.rx1 + s * nx1
        self.ry1 = (1.0 - s) * self.ry1 + s * ny1

    def on_no_face(self) -> None:
        s = self.smooth * 0.22
        self.rx0 = (1.0 - s) * self.rx0 + s * 0.0
        self.ry0 = (1.0 - s) * self.ry0 + s * 0.0
        self.rx1 = (1.0 - s) * self.rx1 + s * 1.0
        self.ry1 = (1.0 - s) * self.ry1 + s * 1.0

    def apply(self, frame: np.ndarray) -> np.ndarray:
        h, w = frame.shape[:2]
        x0 = int(max(0, min(w - 2, round(self.rx0 * w))))
        x1 = int(max(x0 + 2, min(w, round(self.rx1 * w))))
        y0 = int(max(0, min(h - 2, round(self.ry0 * h))))
        y1 = int(max(y0 + 2, min(h, round(self.ry1 * h))))
        crop = frame[y0:y1, x0:x1]
        if crop.size == 0 or crop.shape[0] < 2 or crop.shape[1] < 2:
            return frame
        return cv2.resize(crop, (w, h), interpolation=cv2.INTER_AREA)


def _gaze_heatmap_save_folder() -> str:
    """
    Directory for default gaze heatmap PNGs: 'outside' the inner ``.../NAME/NAME/hci2`` tree when duplicated,
    else the folder that directly contains ``hci2`` (project root).
    """
    hci2 = os.path.abspath(os.path.join(_script_dir, "..", ".."))
    inner = os.path.dirname(hci2)
    if not inner:
        return os.getcwd()
    outer = os.path.dirname(inner)
    try:
        if (
            outer
            and os.path.isdir(inner)
            and os.path.isdir(outer)
            and os.path.basename(inner).lower() == os.path.basename(outer).lower()
        ):
            return outer
    except OSError:
        pass
    return inner


def _resolve_gaze_heatmap_save_path(raw: str) -> str:
    """``raw`` is a file path, or an existing directory → timestamped PNG inside it."""
    path = os.path.expanduser(raw.strip())
    if os.path.isdir(path):
        name = time.strftime("gaze_heatmap_%Y%m%d_%H%M%S.png", time.localtime())
        return os.path.join(path, name)
    parent = os.path.dirname(os.path.abspath(path))
    if parent and not os.path.isdir(parent):
        try:
            os.makedirs(parent, exist_ok=True)
        except OSError:
            pass
    return path


def _default_gaze_heatmap_save_path() -> str:
    folder = _gaze_heatmap_save_folder()
    try:
        os.makedirs(folder, exist_ok=True)
    except OSError:
        pass
    name = time.strftime("gaze_heatmap_%Y%m%d_%H%M%S.png", time.localtime())
    return os.path.join(folder, name)


def _render_gaze_heatmap(
    hist: np.ndarray,
    out_path: Optional[str],
    show_window: bool,
) -> None:
    """Show and optionally save a colored heatmap from accumulated gaze counts."""
    total = float(hist.sum())
    if total <= 0:
        print("[TUIO-only] Gaze heatmap: no samples recorded (no face / camera off).")
        return
    try:
        blur_sigma = float(os.environ.get("TUIO_GAZE_HEATMAP_BLUR_SIGMA", "2.25"))
    except ValueError:
        blur_sigma = 2.25
    blur_sigma = max(0.0, min(12.0, blur_sigma))
    h0, w0 = hist.shape[:2]
    work = hist.astype(np.float32)
    if blur_sigma > 1e-6:
        ksz = max(3, min(max(h0, w0) | 1, int(round(blur_sigma * 5)) | 1))
        work = cv2.GaussianBlur(work, (ksz, ksz), blur_sigma, borderType=cv2.BORDER_REFLECT)
    flat = work.reshape(-1)
    nz = flat[flat > 1e-6]
    lo = float(np.percentile(nz, 5)) if nz.size else 0.0
    hi = float(np.percentile(nz, 99)) if nz.size else float(work.max())
    if hi <= lo:
        hi = lo + 1.0
    norm = np.clip((work - lo) / (hi - lo), 0.0, 1.0)
    colored = cv2.applyColorMap((norm * 255).astype(np.uint8), cv2.COLORMAP_INFERNO)
    h, w = colored.shape[:2]
    display = cv2.resize(colored, (w * 4, h * 4), interpolation=cv2.INTER_LANCZOS4)
    if out_path:
        try:
            cv2.imwrite(out_path, display)
            print("[TUIO-only] Gaze heatmap saved:", os.path.abspath(out_path))
        except Exception as ex:
            print("[TUIO-only] Gaze heatmap save failed:", ex)
    if show_window:
        win = "Gaze heatmap (press any key to close)"
        try:
            cv2.imshow(win, display)
            cv2.waitKey(0)
            try:
                cv2.destroyWindow(win)
            except Exception:
                pass
        except Exception as ex:
            print("[TUIO-only] Gaze heatmap window failed:", ex)


def _hub_enabled() -> bool:
    return os.environ.get("TUIO_HUB_ENABLE", "1").strip().lower() not in ("0", "false", "no")


def _map_plane(cx: float, cy: float) -> Tuple[float, float]:
    try:
        sx = float(os.environ.get("PLANE_MAP_SCALE_X", "1"))
        sy = float(os.environ.get("PLANE_MAP_SCALE_Y", "1"))
        ox = float(os.environ.get("PLANE_MAP_OFFSET_X", "0"))
        oy = float(os.environ.get("PLANE_MAP_OFFSET_Y", "0"))
    except ValueError:
        sx = sy = 1.0
        ox = oy = 0.0
    px = max(0.0, min(1.0, cx * sx + ox))
    py = max(0.0, min(1.0, cy * sy + oy))
    return px, py


def detect_laser_spot_norm_xy(
    frame_bgr: np.ndarray,
    mode: str = "bright",
    blur_ksize: int = 11,
    vmin: int = 218,
    smax: int = 255,
    min_area: float = 4.0,
    max_area: float = 900.0,
) -> Optional[Tuple[float, float]]:
    """
    Bright or colored blob (laser dot) → normalized (x, y) in [0, 1], same convention as MediaPipe
    (origin top-left). Returns None if no suitable contour.
    """
    if frame_bgr is None or frame_bgr.size < 64:
        return None
    h, w = frame_bgr.shape[:2]
    if h < 4 or w < 4:
        return None
    k = max(3, int(blur_ksize) | 1)
    blurred = cv2.GaussianBlur(frame_bgr, (k, k), 0)
    m = str(mode or "bright").strip().lower()
    if m == "red":
        hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)
        mask = cv2.bitwise_or(
            cv2.inRange(hsv, (0, 70, 80), (10, 255, 255)),
            cv2.inRange(hsv, (160, 70, 80), (180, 255, 255)),
        )
    elif m == "green":
        hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, (38, 50, 50), (92, 255, 255))
    else:
        gray = cv2.cvtColor(blurred, cv2.COLOR_BGR2GRAY)
        _, mask = cv2.threshold(gray, int(max(1, min(255, vmin))), 255, cv2.THRESH_BINARY)
        if int(smax) < 255:
            hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)
            sat = hsv[:, :, 1]
            mask = cv2.bitwise_and(mask, cv2.inRange(sat, 0, int(max(0, min(255, smax)))))

    mask = cv2.erode(mask, None, iterations=1)
    mask = cv2.dilate(mask, None, iterations=2)
    cnts, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not cnts:
        return None
    c = max(cnts, key=cv2.contourArea)
    area = float(cv2.contourArea(c))
    if area < float(min_area) or area > float(max_area):
        return None
    M = cv2.moments(c)
    m00 = float(M.get("m00", 0.0))
    if m00 < 1e-3:
        return None
    cx = float(M["m10"]) / m00
    cy = float(M["m01"]) / m00
    return max(0.0, min(1.0, cx / float(w))), max(0.0, min(1.0, cy / float(h)))


def resolve_plane_output(
    plane_source: str,
    *,
    laser_enable: bool,
    laser_ok: bool,
    lx: float,
    ly: float,
    tip_ok: bool,
    tx: float,
    ty: float,
    yolo_active: bool,
    yolo_cx: float,
    yolo_cy: float,
    map_plane: Any,
) -> Tuple[float, float, bool]:
    """
    Pick plane TUIO position from the configured source.

    **bottle_hand** / **cup_hand**: YOLO plane when ``yolo_active``, else MediaPipe finger.
    **bottle_signup**: finger for plane; bottle only during ``mediapipe_signup_gestures``.
    **merge**: laser (if enabled) → finger → YOLO.
    """
    ps = str(plane_source or "merge").strip().lower()

    if ps == "finger":
        return float(tx), float(ty), bool(tip_ok)
    if ps == "yolo":
        if yolo_active:
            px, py = map_plane(float(yolo_cx), float(yolo_cy))
            return px, py, True
        return float(tx), float(ty), False
    if ps == "laser":
        px, py = map_plane(float(lx), float(ly))
        return px, py, bool(laser_ok)
    if ps == "bottle_signup":
        return float(tx), float(ty), bool(tip_ok)
    if ps in ("bottle_hand", "bottle", "cup_hand", "cup"):
        if yolo_active:
            px, py = map_plane(float(yolo_cx), float(yolo_cy))
            return px, py, True
        return float(tx), float(ty), bool(tip_ok)

    # merge (default)
    if laser_enable:
        if laser_ok:
            px, py = map_plane(float(lx), float(ly))
            return px, py, True
        if tip_ok:
            return float(tx), float(ty), True
        if yolo_active:
            px, py = map_plane(float(yolo_cx), float(yolo_cy))
            return px, py, True
        return float(tx), float(ty), bool(tip_ok)
    if yolo_active:
        px, py = map_plane(float(yolo_cx), float(yolo_cy))
        return px, py, True
    return float(tx), float(ty), bool(tip_ok)


def _bottle_hand_anchor_xy(lm: Any, mp_hands_mod: Any) -> Tuple[float, float]:
    """Palm center for bottle proximity (fingertip is often far from the bottle body)."""
    mode = os.environ.get("TUIO_BOTTLE_HAND_ANCHOR", "palm").strip().lower()
    if mode == "wrist":
        p = lm.landmark[mp_hands_mod.HandLandmark.WRIST]
        return float(p.x), float(p.y)
    if mode in ("tip", "finger"):
        p = lm.landmark[mp_hands_mod.HandLandmark.INDEX_FINGER_TIP]
        return float(p.x), float(p.y)
    ids = (
        mp_hands_mod.HandLandmark.WRIST,
        mp_hands_mod.HandLandmark.INDEX_FINGER_MCP,
        mp_hands_mod.HandLandmark.MIDDLE_FINGER_MCP,
        mp_hands_mod.HandLandmark.RING_FINGER_MCP,
        mp_hands_mod.HandLandmark.PINKY_MCP,
    )
    xs: list[float] = []
    ys: list[float] = []
    for idx in ids:
        p = lm.landmark[idx]
        xs.append(float(p.x))
        ys.append(float(p.y))
    return sum(xs) / len(xs), sum(ys) / len(ys)


class _CupControlStabilizer:
    """
    Debounced object lock: require consecutive YOLO hits before switching to bottle control,
    hold position through brief drop-outs, then require consecutive misses before hand mode.
    """

    def __init__(
        self,
        alpha: float,
        hold_frames: int,
        on_frames: int,
        off_frames: int,
    ) -> None:
        self._alpha = max(0.05, min(1.0, float(alpha)))
        self._hold = max(0, int(hold_frames))
        self._on_need = max(1, int(on_frames))
        self._off_need = max(1, int(off_frames))
        self._x = 0.5
        self._y = 0.5
        self._bbox: Optional[Any] = None
        self._cup_mode = False
        self._hit_streak = 0
        self._miss_streak = 0
        self._hold_left = 0

    def push(
        self, spot: Optional[Tuple[float, float]], bbox_norm: Optional[Any] = None
    ) -> Tuple[float, float, bool, Optional[Any]]:
        if spot is not None:
            sx, sy = spot
            self._hit_streak += 1
            self._miss_streak = 0
            if not self._cup_mode and self._hit_streak < self._on_need:
                return self._x, self._y, False, None
            if not self._cup_mode:
                self._cup_mode = True
            a = self._alpha
            if self._hold_left <= 0 and self._hit_streak == self._on_need:
                self._x, self._y = sx, sy
            else:
                self._x = (1.0 - a) * self._x + a * sx
                self._y = (1.0 - a) * self._y + a * sy
            if bbox_norm is not None:
                self._bbox = bbox_norm
            self._hold_left = self._hold
            return self._x, self._y, True, self._bbox

        self._hit_streak = 0
        self._miss_streak += 1
        if not self._cup_mode:
            return self._x, self._y, False, None

        if self._hold_left > 0:
            self._hold_left -= 1
            return self._x, self._y, True, self._bbox

        if self._miss_streak >= self._off_need:
            self._cup_mode = False
            self._bbox = None
            return self._x, self._y, False, None

        return self._x, self._y, True, self._bbox

    def tick(self) -> Tuple[float, float, bool, Optional[Any]]:
        """Between YOLO frames: keep smoothed bottle position without counting a miss."""
        if self._cup_mode:
            return self._x, self._y, True, self._bbox
        return self._x, self._y, False, None


class _PlaneOutputSmoother:
    """Extra EMA on plane TUIO output every video frame (matches hand-like motion)."""

    def __init__(self, alpha: float) -> None:
        self._alpha = max(0.05, min(1.0, float(alpha)))
        self._x = 0.5
        self._y = 0.5
        self._active = False

    def push(self, x: float, y: float, active: bool) -> Tuple[float, float, bool]:
        if not active:
            self._active = False
            return float(x), float(y), False
        tx = max(0.0, min(1.0, float(x)))
        ty = max(0.0, min(1.0, float(y)))
        if not self._active:
            self._x, self._y = tx, ty
            self._active = True
            return self._x, self._y, True
        jump = abs(tx - self._x) + abs(ty - self._y)
        a = self._alpha
        if jump > 0.45:
            self._x, self._y = tx, ty
        else:
            self._x += (tx - self._x) * a
            self._y += (ty - self._y) * a
        return self._x, self._y, True


class _LaserSpotStabilizer:
    """EMA while locked; brief hold after drop-out."""

    def __init__(self, alpha: float, hold_frames: int) -> None:
        self._alpha = max(0.05, min(1.0, float(alpha)))
        self._hold = max(0, int(hold_frames))
        self._x = 0.5
        self._y = 0.5
        self._miss = 0
        self._has = False

    def push(self, spot: Optional[Tuple[float, float]]) -> Tuple[float, float, bool]:
        if spot is not None:
            sx, sy = spot
            self._miss = 0
            if not self._has:
                self._x, self._y = sx, sy
                self._has = True
            else:
                a = self._alpha
                self._x = (1.0 - a) * self._x + a * sx
                self._y = (1.0 - a) * self._y + a * sy
            return self._x, self._y, True
        self._miss += 1
        if self._has and self._miss <= self._hold:
            return self._x, self._y, True
        self._has = False
        return self._x, self._y, False


def main() -> None:
    # Default index 2 (SplitCam); reacTIVision reads the same device via camera.xml id 2.
    cap_idx = default_tuio_capture_index()
    tuio_host = os.environ.get("TUIO_HOST", "127.0.0.1")
    tuio_port = int(os.environ.get("TUIO_PORT", "3333"))
    plane_source = os.environ.get("TUIO_PLANE_SOURCE", "merge").strip().lower()
    laser_enable = os.environ.get("TUIO_LASER_ENABLE", "0").strip().lower() in ("1", "true", "yes")
    laser_mode = os.environ.get("TUIO_LASER_MODE", "bright").strip().lower()
    try:
        laser_vmin = int(os.environ.get("TUIO_LASER_VMIN", "218"))
    except ValueError:
        laser_vmin = 218
    try:
        laser_smax = int(os.environ.get("TUIO_LASER_SMAX", "255"))
    except ValueError:
        laser_smax = 255
    try:
        laser_min_a = float(os.environ.get("TUIO_LASER_MIN_AREA", "4"))
    except ValueError:
        laser_min_a = 4.0
    try:
        laser_max_a = float(os.environ.get("TUIO_LASER_MAX_AREA", "900"))
    except ValueError:
        laser_max_a = 900.0
    try:
        laser_blur = int(os.environ.get("TUIO_LASER_BLUR", "11"))
    except ValueError:
        laser_blur = 11
    try:
        laser_smooth = float(os.environ.get("TUIO_LASER_SMOOTH", "0.42"))
    except ValueError:
        laser_smooth = 0.42
    try:
        laser_hold = int(os.environ.get("TUIO_LASER_HOLD_FRAMES", "4"))
    except ValueError:
        laser_hold = 4
    if plane_source == "laser":
        laser_enable = True
    elif plane_source == "bottle_signup":
        plane_source = "bottle_signup"
    elif plane_source in ("cup", "cup_hand", "bottle", "bottle_hand"):
        plane_source = "bottle_hand"
    elif plane_source not in ("finger", "yolo", "merge", "laser", "bottle_signup", "bottle_hand"):
        print(f"[TUIO-only] Unknown TUIO_PLANE_SOURCE='{plane_source}', using 'merge'.")
        plane_source = "merge"
    bottle_signup_mode = plane_source == "bottle_signup"
    bottle_hand_mode = plane_source == "bottle_hand"
    object_hand_mode = bottle_signup_mode or bottle_hand_mode
    cup_local_yolo = object_hand_mode and os.environ.get("TUIO_CUP_LOCAL_YOLO", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )
    cup_hide_hands = object_hand_mode and os.environ.get(
        "TUIO_CUP_HIDE_HANDS_WHEN_CUP", "1"
    ).strip().lower() not in ("0", "false", "no")
    try:
        cup_yolo_every_n = max(1, int(os.environ.get("TUIO_CUP_YOLO_EVERY_N", "2")))
    except ValueError:
        cup_yolo_every_n = 2
    try:
        cup_smooth = float(os.environ.get("TUIO_CUP_SMOOTH", "0.22"))
    except ValueError:
        cup_smooth = 0.22
    try:
        bottle_plane_smooth = float(os.environ.get("TUIO_BOTTLE_PLANE_SMOOTH", "0.16"))
    except ValueError:
        bottle_plane_smooth = 0.16
    try:
        cup_hold = int(os.environ.get("TUIO_CUP_HOLD_FRAMES", "18"))
    except ValueError:
        cup_hold = 18
    try:
        cup_on_frames = int(os.environ.get("TUIO_CUP_ON_FRAMES", "4"))
    except ValueError:
        cup_on_frames = 4
    try:
        cup_off_frames = int(os.environ.get("TUIO_CUP_OFF_FRAMES", "12"))
    except ValueError:
        cup_off_frames = 12
    cup_stab = (
        _CupControlStabilizer(cup_smooth, cup_hold, cup_on_frames, cup_off_frames)
        if object_hand_mode
        else None
    )
    bottle_plane_smoother = _PlaneOutputSmoother(bottle_plane_smooth) if bottle_hand_mode else None
    bottle_signup_ctrl = None
    if bottle_signup_mode or bottle_hand_mode:
        from signup_bottle_control import make_bottle_signup_controller_from_env

        bottle_signup_ctrl = make_bottle_signup_controller_from_env()
    cup_frame_counter = 0
    laser_stab = _LaserSpotStabilizer(laser_smooth, laser_hold)
    laser_plane_smoother = _PlaneOutputSmoother(laser_smooth)
    wait_cam = os.environ.get("TUIO_WAIT_FOR_CAMERA", "0").strip().lower() in ("1", "true", "yes")
    allow_no_cam = os.environ.get("TUIO_ALLOW_NO_CAMERA", "1").strip().lower() not in ("0", "false", "no")

    hub: Any = None
    state: Dict[str, Any] = {
        "yolo_active": False,
        "cx": 0.5,
        "cy": 0.5,
        "cup_bbox_norm": None,
        "yolo_debug_hint": "",
        "pending_resume_camera": False,
        "resume_open_attempts": 0,
        "signup_gestures_active": False,
        "signup_pointer_active": False,
    }

    bridge = None
    if MediapipeTuioBridge is not None:
        try:
            bridge = MediapipeTuioBridge(host=tuio_host, port=tuio_port)
            bridge.set_mode("point8_only")
            print(f"[TUIO-only] UDP → {tuio_host}:{tuio_port}")
        except OSError as e:
            print("[TUIO-only] Bridge disabled:", e)

    mp_draw = None
    mp_hands_mod = None
    hands = None
    if mp is not None:
        mp_draw = mp.solutions.drawing_utils
        mp_hands_mod = mp.solutions.hands
        hands = mp_hands_mod.Hands(
            model_complexity=0,
            max_num_hands=2,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )
    else:
        print("[TUIO-only] MediaPipe Hands unavailable:", _mp_import_error or "unknown error")
        print("[TUIO-only] Continuing with hub/YOLO-driven TUIO only (no finger tracking).")

    gaze_enable = os.environ.get("TUIO_GAZE_ENABLE", "1").strip().lower() not in ("0", "false", "no")
    emotion_hub = os.environ.get("TUIO_EMOTION_HUB", "1").strip().lower() not in ("0", "false", "no")
    try:
        gaze_gain = float(os.environ.get("TUIO_GAZE_GAIN", "3.45"))
    except ValueError:
        gaze_gain = 3.45
    gaze_flip_x = os.environ.get("TUIO_GAZE_FLIP_X", "0").strip().lower() in ("1", "true", "yes")
    gaze_flip_y = os.environ.get("TUIO_GAZE_FLIP_Y", "1").strip().lower() in ("1", "true", "yes")
    try:
        gaze_median_w = max(1, int(os.environ.get("TUIO_GAZE_MEDIAN_W", "9")))
    except ValueError:
        gaze_median_w = 9
    try:
        gaze_display_ema = float(os.environ.get("TUIO_GAZE_DISPLAY_EMA", "0.20"))
    except ValueError:
        gaze_display_ema = 0.20
    gaze_display_ema = max(0.05, min(1.0, gaze_display_ema))
    try:
        gaze_face_min_det = float(os.environ.get("TUIO_GAZE_FACE_MIN_DET", "0.44"))
    except ValueError:
        gaze_face_min_det = 0.44
    try:
        gaze_face_min_track = float(os.environ.get("TUIO_GAZE_FACE_MIN_TRACK", "0.44"))
    except ValueError:
        gaze_face_min_track = 0.44
    gaze_face_min_det = max(0.1, min(0.95, gaze_face_min_det))
    gaze_face_min_track = max(0.1, min(0.95, gaze_face_min_track))
    gaze_stabilizer = _GazeStabilizer(gaze_median_w, gaze_display_ema)
    try:
        gaze_every_n = max(1, int(os.environ.get("TUIO_GAZE_EVERY_N", "1")))
    except ValueError:
        gaze_every_n = 1
    try:
        gaze_grid_w = max(8, int(os.environ.get("TUIO_GAZE_GRID_W", "96")))
        gaze_grid_h = max(8, int(os.environ.get("TUIO_GAZE_GRID_H", "54")))
    except ValueError:
        gaze_grid_w, gaze_grid_h = 96, 54
    gaze_heatmap_save_raw = os.environ.get("TUIO_GAZE_HEATMAP_SAVE", "").strip()
    gaze_heatmap_auto_save = os.environ.get("TUIO_GAZE_HEATMAP_AUTO_SAVE", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )
    gaze_heatmap_save: Optional[str] = None
    if gaze_heatmap_save_raw:
        gaze_heatmap_save = _resolve_gaze_heatmap_save_path(gaze_heatmap_save_raw)
    elif gaze_heatmap_auto_save and gaze_enable:
        gaze_heatmap_save = _default_gaze_heatmap_save_path()
        print("[TUIO-only] Gaze heatmap will auto-save to:", os.path.abspath(gaze_heatmap_save))

    face_mesh = None
    if mp is not None and (gaze_enable or emotion_hub):
        try:
            face_mesh = mp.solutions.face_mesh.FaceMesh(
                max_num_faces=1,
                refine_landmarks=True,
                min_detection_confidence=gaze_face_min_det,
                min_tracking_confidence=gaze_face_min_track,
            )
            if gaze_enable and emotion_hub:
                print(
                    "[TUIO-only] Face Mesh: iris gaze + facial emotion on the **same** frames as MediaPipe Hands "
                    f"(camera index from TUIO_CAPTURE_INDEX / MAIN_CAPTURE_INDEX) | det={gaze_face_min_det} "
                    f"track={gaze_face_min_track} median_w={gaze_median_w}"
                )
            elif gaze_enable:
                print(
                    "[TUIO-only] Gaze tracking: Face Mesh (iris rings, weighted fusion) active | "
                    f"det={gaze_face_min_det} track={gaze_face_min_track} median_w={gaze_median_w}"
                )
            else:
                print(
                    "[TUIO-only] Face Mesh: **emotion only** (TUIO_GAZE_ENABLE=0) — same camera stream as Hands | "
                    f"det={gaze_face_min_det} track={gaze_face_min_track}"
                )
        except Exception as ex:
            print("[TUIO-only] Face Mesh init failed:", ex)
            face_mesh = None
    elif not gaze_enable and not emotion_hub:
        print("[TUIO-only] Gaze + emotion Face Mesh off (TUIO_GAZE_ENABLE=0 and TUIO_EMOTION_HUB=0).")
    elif not gaze_enable:
        print("[TUIO-only] Gaze tracking disabled (TUIO_GAZE_ENABLE=0); Face Mesh kept for emotion (same camera as Hands).")

    gaze_hist = np.zeros((gaze_grid_h, gaze_grid_w), dtype=np.float32)
    gaze_frame_counter = 0
    gaze_overlay = os.environ.get("TUIO_GAZE_OVERLAY", "1").strip().lower() not in ("0", "false", "no")

    auto_frame_face = (
        bool(gaze_enable or emotion_hub)
        and face_mesh is not None
        and os.environ.get("TUIO_AUTO_FRAME_FACE", "0").strip().lower() in ("1", "true", "yes")
    )
    af_state: Optional[_AutoFaceFrameState] = None
    if auto_frame_face:
        try:
            af_margin = float(os.environ.get("TUIO_AUTO_FRAME_MARGIN", "0.28"))
        except ValueError:
            af_margin = 0.28
        try:
            af_smooth = float(os.environ.get("TUIO_AUTO_FRAME_SMOOTH", "0.18"))
        except ValueError:
            af_smooth = 0.18
        try:
            af_min_w = float(os.environ.get("TUIO_AUTO_FRAME_MIN_WIDTH", "0.42"))
        except ValueError:
            af_min_w = 0.42
        af_state = _AutoFaceFrameState(af_margin, af_smooth, af_min_w)
        print(
            "[TUIO-only] Auto face framing ON (TUIO_AUTO_FRAME_FACE=1) — software zoom/center for Face Mesh + Hands."
        )

    skip_cam = os.environ.get("TUIO_SKIP_CAMERA", "0").strip().lower() in ("1", "true", "yes")
    cap = None
    used_idx = -1
    if not skip_cam:
        cap, used_idx = open_capture_with_fallback(
            preferred_index=cap_idx,
            label="TUIO-only",
            auto_fallback=True,
            wait_forever=wait_cam,
        )
    synth_gen = None
    if skip_cam:
        synth_gen = synthetic_frame_generator(480, 320)
        print(
            "[TUIO-only] TUIO_SKIP_CAMERA=1 — not opening any device; blank preview; "
            "finger/MediaPipe-from-camera off unless you use another capture path."
        )
    elif cap is None and allow_no_cam:
        synth_gen = synthetic_frame_generator(480, 320)
        print("[TUIO-only] No camera — using blank frames; TUIO from hub/YOLO only. Ctrl+C to quit.")
    elif cap is None:
        print("[TUIO-only] No camera and TUIO_ALLOW_NO_CAMERA=0 — exit.")
        if hands is not None:
            hands.close()
        if face_mesh is not None:
            face_mesh.close()
        if bridge is not None:
            bridge.close()
        if hub is not None:
            hub.close()
        sys.exit(1)

    from signup_hand_poses import (
        classify_signup_static_pose,
        finger_margin_from_env,
        make_pose_gate_from_env,
        make_swipe_detector_from_env,
    )

    pose_gate = make_pose_gate_from_env()
    swipe_detector = make_swipe_detector_from_env()
    _pose_margin = finger_margin_from_env()
    if bottle_signup_mode or bottle_hand_mode:
        print(
            "[TUIO-only] Sign-up: slide bottle left/right to rotate dial (not twist); hold still=center select, low=submit."
            + (" Hand backup when bottle not detected." if bottle_hand_mode else "")
        )
    else:
        print("[TUIO-only] Sign-up: swipe L/R (dial) + fist/peace when hub sends mediapipe_signup_gestures.")

    def on_hub(msg: Dict[str, Any]) -> None:
        ev = str(msg.get("event", "")).strip().lower()
        if ev == "plane_pose":
            if cup_local_yolo:
                return
            active = bool(msg.get("active", False))
            state["yolo_active"] = active
            if active:
                try:
                    state["cx"] = float(msg.get("cx", 0.5))
                    state["cy"] = float(msg.get("cy", 0.5))
                except (TypeError, ValueError):
                    pass
        elif ev == "login":
            # Backup if tuio_resume_hand_camera is missed; main loop no-ops when cap is already open.
            state["pending_resume_camera"] = True
            state["resume_open_attempts"] = 0
            state["signup_gestures_active"] = False
            state["signup_pointer_active"] = False
            pose_gate.reset()
            swipe_detector.reset()
            print("[TUIO-only] Hub: login — arming physical webcam for MediaPipe hands (if needed).")
        elif ev == "tuio_resume_hand_camera":
            # Open capture on the main loop only (avoid racing next(synth_gen) vs synth_gen=None).
            state["pending_resume_camera"] = True
            state["resume_open_attempts"] = 0
            print("[TUIO-only] Hub: tuio_resume_hand_camera — opening webcam on main thread when device is free.")
        elif ev == "mediapipe_signup_gestures":
            state["signup_gestures_active"] = bool(msg.get("active", False))
            pose_gate.reset()
            swipe_detector.reset()
            if bottle_signup_ctrl is not None:
                bottle_signup_ctrl.reset()
            print(
                "[TUIO-only] Hub: mediapipe_signup_gestures active="
                + str(state["signup_gestures_active"])
                + (
                    " (bottle sign-up; finger plane off)"
                    if bottle_signup_mode
                    else " (swipe/fist/peace; finger plane off)"
                )
            )
        elif ev == "mediapipe_game_started":
            state["signup_gestures_active"] = False
            state["signup_pointer_active"] = False
            pose_gate.reset()
            swipe_detector.reset()
            if bottle_signup_ctrl is not None:
                bottle_signup_ctrl.reset()
            print(
                "[TUIO-only] Hub: mediapipe_game_started — finger plane on"
                + ("; bottle sign-up off." if bottle_signup_mode else "; signup poses off.")
            )
        elif ev == "mediapipe_signup_pointer":
            active_ptr = bool(msg.get("active", False))
            state["signup_pointer_active"] = active_ptr
            state["_keyboard_laser_hub"] = False
            if active_ptr:
                state["signup_gestures_active"] = False
                pose_gate.reset()
                swipe_detector.reset()
                if bottle_signup_ctrl is not None:
                    bottle_signup_ctrl.reset()
            print(
                "[TUIO-only] Hub: mediapipe_signup_pointer active="
                + str(active_ptr)
                + (
                    " (laser → TUIO plane for admin/name keyboard)"
                    if active_ptr
                    else " (resume finger/bottle plane)"
                )
            )

    if _hub_enabled():
        try:
            from hub_client import HubServiceClient

            hh = os.environ.get("HUB_HOST", "127.0.0.1")
            hp = int(os.environ.get("HUB_PORT", "5000"))
            hub = HubServiceClient("tuio", host=hh, port=hp, on_command=on_hub)
            hub.connect()
            print(f"[TUIO-only] Hub subscriber tuio @ {hh}:{hp}")
        except Exception as ex:
            print("[TUIO-only] Hub disabled:", ex)
            hub = None

    emotion_debouncer: Optional[_EmotionHubDebouncer] = None
    if emotion_hub and hub is not None and face_mesh is not None:
        try:
            _em_stab = max(3, int(os.environ.get("TUIO_EMOTION_STABLE_FRAMES", "7")))
        except ValueError:
            _em_stab = 7
        try:
            _em_happy = os.environ.get("TUIO_EMOTION_STABLE_FRAMES_HAPPY", "").strip()
            _em_happy_n = max(_em_stab + 3, int(_em_happy)) if _em_happy else None
        except ValueError:
            _em_happy_n = None
        try:
            _em_int = float(os.environ.get("TUIO_EMOTION_MIN_INTERVAL_S", "0.35"))
        except ValueError:
            _em_int = 0.35
        try:
            _em_sad_raw = os.environ.get("TUIO_EMOTION_STABLE_FRAMES_SAD", "").strip()
            _em_sad_n = max(3, int(_em_sad_raw)) if _em_sad_raw else None
        except ValueError:
            _em_sad_n = None
        emotion_debouncer = _EmotionHubDebouncer(
            _em_stab, _em_int, stable_n_happy=_em_happy_n, stable_n_sad=_em_sad_n
        )
        _happy_frames_resolved = (
            max(_em_stab + 3, _em_happy_n) if _em_happy_n is not None else max(_em_stab + 5, 12)
        )
        _sad_frames_resolved = _em_sad_n if _em_sad_n is not None else max(3, min(5, _em_stab))
        print(
            f"[TUIO-only] Facial emotion → hub (event user_facial_emotion); "
            f"stable_frames={_em_stab}, stable_frames_happy={_happy_frames_resolved}, "
            f"stable_frames_sad_confused={_sad_frames_resolved}."
        )
    elif not emotion_hub:
        print("[TUIO-only] Facial emotion hub broadcasts disabled (TUIO_EMOTION_HUB=0).")

    print(
        f"[TUIO-only] Camera index {used_idx if used_idx >= 0 else 'synthetic'} — "
        f"plane source '{plane_source}' — press Q to quit"
    )
    if bottle_hand_mode:
        print(
            "[TUIO-only] bottle_hand: water bottle YOLO (+ cap fallback for clear plastic), else finger."
            + (" (TUIO_CUP_LOCAL_YOLO=1)" if cup_local_yolo else " (hub plane_pose from main.py)")
        )
        if cup_hide_hands:
            print("[TUIO-only] Hand skeleton hidden while bottle controls the plane.")
        if cup_stab is not None:
            print(
                f"[TUIO-only] Bottle stabilizer: on={cup_on_frames} hold={cup_hold} off={cup_off_frames} "
                f"yolo_smooth={cup_smooth:.2f} plane_smooth={bottle_plane_smooth:.2f}."
            )
    elif bottle_signup_mode:
        print(
            "[TUIO-only] bottle_signup: bottle for gender/age sign-up only; finger controls the plane in-game."
            + (" (local YOLO)" if cup_local_yolo else "")
        )
        if cup_hide_hands:
            print("[TUIO-only] Hand skeleton hidden during bottle sign-up.")
        if cup_stab is not None:
            print(
                f"[TUIO-only] Bottle sign-up stabilizer: on={cup_on_frames} hold={cup_hold} "
                f"off={cup_off_frames} smooth={cup_smooth:.2f}."
            )
    elif laser_enable:
        print(
            f"[TUIO-only] Laser assist ON (mode={laser_mode}, TUIO_LASER_ENABLE); merge order: laser → finger → YOLO."
        )
    else:
        print("[TUIO-only] Laser assist OFF (set TUIO_LASER_ENABLE=1 for webcam laser dot → plane in merge).")
    mirror_cam = os.environ.get("CAMERA_MIRROR_HORIZONTAL", "1").strip().lower() not in ("0", "false", "no")
    if mirror_cam:
        print("[TUIO-only] CAMERA_MIRROR_HORIZONTAL=1 — video + MediaPipe use mirrored (selfie) orientation.")

    preview = os.environ.get("TUIO_PREVIEW", "1").strip().lower() not in ("0", "false", "no")
    preview_face_box = os.environ.get("TUIO_PREVIEW_FACE_BOX", "1").strip().lower() not in ("0", "false", "no")
    if preview and preview_face_box and face_mesh is not None:
        print(
            "[TUIO-only] Green face box + cyan emotion label (instant) on preview 'TUIO-only (laptop)' when a face is detected."
        )
    elif preview and preview_face_box and face_mesh is None:
        print(
            "[TUIO-only] Face box unavailable: no Face Mesh (set TUIO_GAZE_ENABLE=1 or TUIO_EMOTION_HUB=1)."
        )
    gaze_heatmap_show = os.environ.get("TUIO_GAZE_HEATMAP_SHOW", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )

    last_x, last_y = 0.5, 0.5
    miss = 0
    hold = 2

    use_parallel_face = (
        face_mesh is not None
        and hands is not None
        and cap is not None
        and not auto_frame_face
        and os.environ.get("TUIO_FACE_PIPELINE_THREAD", "1").strip().lower() not in ("0", "false", "no")
    )
    face_executor: Optional[ThreadPoolExecutor] = None
    if use_parallel_face:
        face_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="tuio_facemesh")
        print(
            "[TUIO-only] Face pipeline thread: Face Mesh (face / gaze / emotion) on worker, "
            "MediaPipe Hands on main thread — same camera frame (RGB copy)."
        )

    try:
        while True:
            if state["pending_resume_camera"] and cap is None:
                c, ui = open_capture_with_fallback(
                    preferred_index=cap_idx,
                    label="TUIO-only-post-login",
                    auto_fallback=True,
                    wait_forever=False,
                )
                if c is not None:
                    cap = c
                    used_idx = ui
                    synth_gen = None
                    state["pending_resume_camera"] = False
                    state["resume_open_attempts"] = 0
                    print(f"[TUIO-only] Webcam opened for MediaPipe hands (device index {used_idx}).")
                else:
                    state["resume_open_attempts"] = int(state["resume_open_attempts"]) + 1
                    time.sleep(0.15)
                    if state["resume_open_attempts"] >= 100:
                        state["pending_resume_camera"] = False
                        state["resume_open_attempts"] = 0
                        print(
                            "[TUIO-only] Could not open camera after login/resume (~15s). "
                            "Try TUIO_CAPTURE_INDEX / CAMERA_FALLBACK_INDICES, CAMERA_SKIP_FRAME_CHECK=1, "
                            "or lower CAMERA_MIN_MEAN_GRAY if the room is dark."
                        )
            elif state["pending_resume_camera"] and cap is not None:
                state["pending_resume_camera"] = False
                state["resume_open_attempts"] = 0

            if cap is not None:
                ok, frame = cap.read()
                if not ok or frame is None:
                    time.sleep(0.05)
                    continue
            else:
                frame = next(synth_gen)  # type: ignore
                ok = True
            frame = mirror_frame_horizontal_if_enabled(frame)
            frame_yolo = frame
            frame = cv2.resize(frame, (480, 320))
            frame_before_face_crop = frame

            fres_gaze = None
            rgb = np.ascontiguousarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            face_future = None
            if cap is not None and face_mesh is not None:
                gaze_frame_counter += 1
                if auto_frame_face and af_state is not None:
                    fa = face_mesh.process(rgb)
                    if fa.multi_face_landmarks:
                        af_state.update(fa.multi_face_landmarks[0])
                    else:
                        af_state.on_no_face()
                    frame = af_state.apply(frame)
                    rgb = np.ascontiguousarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                    fres_gaze = face_mesh.process(rgb)
                elif use_parallel_face and face_executor is not None:
                    face_future = face_executor.submit(
                        face_mesh.process, np.ascontiguousarray(rgb.copy())
                    )
                else:
                    fres_gaze = face_mesh.process(rgb)

            signup_pointer_now = bool(state.get("signup_pointer_active"))
            if signup_pointer_now:
                # Name keyboard + admin dashboard (phase 3 hci): laser only — skip MediaPipe Hands.
                class _R:
                    multi_hand_landmarks = None

                res = _R()
            elif hands is not None:
                res = hands.process(rgb)
            else:

                class _R:
                    multi_hand_landmarks = None

                res = _R()

            if face_future is not None:
                try:
                    fres_gaze = face_future.result(timeout=3.0)
                except FuturesTimeout:
                    fres_gaze = None
                except Exception as ex:
                    print("[TUIO-only] Face Mesh worker error:", repr(ex))
                    fres_gaze = None

            gaze_ok = False
            gaze_tx, gaze_ty = 0.5, 0.5
            current_emotion_pair: Optional[Tuple[str, float]] = None
            if cap is not None and face_mesh is not None and fres_gaze is not None:
                if fres_gaze.multi_face_landmarks:
                    current_emotion_pair = _emotion_label_from_face_landmarks(
                        fres_gaze.multi_face_landmarks[0]
                    )
                    if gaze_enable:
                        gz = _gaze_norm_xy_from_face_landmarks(
                            fres_gaze.multi_face_landmarks[0], gaze_gain, gaze_flip_x, gaze_flip_y
                        )
                        if gz is not None:
                            gaze_tx, gaze_ty = gaze_stabilizer.push(gz[0], gz[1])
                            gaze_ok = True
                            if gaze_frame_counter % gaze_every_n == 0:
                                gx, gy = gaze_tx, gaze_ty
                                ix = int(max(0.0, min(1.0, gx)) * (gaze_grid_w - 1))
                                iy = int(max(0.0, min(1.0, gy)) * (gaze_grid_h - 1))
                                gaze_hist[iy, ix] += 1.0
                        else:
                            gaze_stabilizer.reset()
                    if emotion_debouncer is not None and hub is not None:
                        ev_out = emotion_debouncer.push(current_emotion_pair)
                        if ev_out is not None:
                            elab, escore = ev_out
                            hub.send_event(
                                {
                                    "event": "user_facial_emotion",
                                    "emotion": elab,
                                    "score": round(float(escore), 3),
                                }
                            )
                else:
                    if gaze_enable:
                        gaze_stabilizer.reset()
                    if emotion_debouncer is not None:
                        emotion_debouncer.push(None)

            tip_ok = False
            tx, ty = last_x, last_y
            if res.multi_hand_landmarks:
                lm = res.multi_hand_landmarks[0]
                tip = lm.landmark[mp_hands_mod.HandLandmark.INDEX_FINGER_TIP]
                tx, ty = float(tip.x), float(tip.y)
                tip_ok = True
                miss = 0
            else:
                miss += 1
                if miss <= hold:
                    tip_ok = True
                    tx, ty = last_x, last_y
            if tip_ok:
                last_x, last_y = max(0.0, min(1.0, tx)), max(0.0, min(1.0, ty))
                tx, ty = last_x, last_y

            signup_active_now = bool(state.get("signup_gestures_active")) and not signup_pointer_now
            cup_active = False
            run_bottle_yolo = (
                cup_local_yolo
                and cap is not None
                and cup_stab is not None
                and not signup_pointer_now
                and (bottle_hand_mode or signup_active_now)
            )
            if run_bottle_yolo:
                cup_frame_counter += 1
                raw_spot: Optional[Tuple[float, float]] = None
                raw_bbox: Optional[Any] = None
                hand_hint: Optional[Tuple[float, float]] = None
                if tip_ok and res.multi_hand_landmarks and mp_hands_mod is not None:
                    if bottle_hand_mode and signup_active_now:
                        hand_hint = _bottle_hand_anchor_xy(res.multi_hand_landmarks[0], mp_hands_mod)
                    elif bottle_signup_mode:
                        hand_hint = _bottle_hand_anchor_xy(res.multi_hand_landmarks[0], mp_hands_mod)
                    elif bottle_hand_mode:
                        hand_hint = _bottle_hand_anchor_xy(
                            res.multi_hand_landmarks[0], mp_hands_mod
                        )
                    else:
                        hand_hint = _bottle_hand_anchor_xy(res.multi_hand_landmarks[0], mp_hands_mod)
                elif tip_ok:
                    hand_hint = (float(tx), float(ty))
                ran_yolo = cup_frame_counter % cup_yolo_every_n == 0
                if ran_yolo:
                    try:
                        from pipeline.handlers import detect_water_bottle_in_bgr, hand_bbox_norm_from_landmarks

                        # Same BGR frame + coords as MediaPipe Hands (not full-res frame_yolo).
                        det_frame = frame
                        hand_bbox_norm: Optional[List[float]] = None
                        if res.multi_hand_landmarks:
                            hand_bbox_norm = hand_bbox_norm_from_landmarks(
                                res.multi_hand_landmarks[0]
                            )
                        det = detect_water_bottle_in_bgr(
                            det_frame,
                            hand_xy=hand_hint,
                            hand_bbox_norm=hand_bbox_norm,
                        )
                        plane_d = det.get("plane") if isinstance(det.get("plane"), dict) else None
                        if bool(det.get("active")) and plane_d:
                            raw_spot = (
                                float(plane_d.get("cx", 0.5)),
                                float(plane_d.get("cy", 0.5)),
                            )
                            raw_bbox = plane_d.get("bbox_norm")
                            state["yolo_debug_hint"] = (
                                f"{plane_d.get('label', '?')} {float(plane_d.get('confidence', 0)):.2f}"
                            )
                        else:
                            dets = det.get("detections") if isinstance(det.get("detections"), list) else []
                            if dets:
                                bottles = [
                                    d
                                    for d in dets
                                    if str(d.get("label", "")).lower() == "bottle"
                                ]
                                if bottles and hand_hint:
                                    state["yolo_debug_hint"] = (
                                        "bottle seen, move closer to palm / hold in frame"
                                    )
                                else:
                                    top = sorted(
                                        dets,
                                        key=lambda d: float(d.get("confidence", 0)),
                                        reverse=True,
                                    )[:3]
                                    state["yolo_debug_hint"] = "seen: " + ", ".join(
                                        f"{d.get('label', '?')} {float(d.get('confidence', 0)):.2f}"
                                        for d in top
                                    )
                            else:
                                state["yolo_debug_hint"] = (
                                    (
                                        "hold bottle near hand — ignores faces/shirts"
                                        if bottle_hand_mode
                                        else "show open palm + bottle (cap toward camera)"
                                    )
                                    if hand_hint
                                    else "show hand + bottle to camera"
                                )
                    except Exception as ex:
                        state["yolo_debug_hint"] = f"YOLO error: {ex}"
                        if cup_frame_counter <= cup_yolo_every_n:
                            print("[TUIO-only] bottle YOLO error:", ex)
                    cx_c, cy_c, cup_active, cup_bbox = cup_stab.push(raw_spot, raw_bbox)
                else:
                    cx_c, cy_c, cup_active, cup_bbox = cup_stab.tick()
                state["yolo_active"] = cup_active
                state["cx"] = cx_c
                state["cy"] = cy_c
                state["cup_bbox_norm"] = cup_bbox if cup_active else None

                bottle_sig: Optional[str] = None
                use_bottle_signup_ctrl = (
                    bottle_signup_ctrl is not None
                    and cup_active
                    and (
                        bottle_signup_mode
                        or (bottle_hand_mode and signup_active_now)
                    )
                )
                if use_bottle_signup_ctrl:
                    # Smoothed position for stable hold-to-select/submit; hand-relative zones.
                    gx, gy = float(cx_c), float(cy_c)
                    bottle_sig = bottle_signup_ctrl.push(gx, gy, True, hand_hint)
                if hub is not None and bottle_sig:
                    print("[TUIO-only] bottle signup -> hub:", bottle_sig)
                    hub.send_event({"event": "mediapipe_signup_gesture", "signal": bottle_sig})
                elif (
                    hub is not None
                    and cup_active
                    and bottle_signup_mode
                    and res.multi_hand_landmarks
                    and bottle_signup_ctrl is not None
                ):
                    lm0 = res.multi_hand_landmarks[0]
                    hand_swipe = swipe_detector.push(lm0, None)
                    if hand_swipe in ("rotateLeft", "rotateRight"):
                        print("[TUIO-only] hand swipe (with bottle) -> hub:", hand_swipe)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": hand_swipe})
            elif bottle_signup_mode or (bottle_hand_mode and signup_active_now):
                state["yolo_active"] = False
                state["cup_bbox_norm"] = None
                if bottle_signup_ctrl is not None:
                    bottle_signup_ctrl.reset()

            px, py = float(tx), float(ty)
            active_out = bool(tip_ok)

            laser_xy_raw: Optional[Tuple[float, float]] = None
            laser_run = laser_enable or signup_pointer_now
            if laser_run:
                if cap is not None:
                    laser_xy_raw = detect_laser_spot_norm_xy(
                        frame_before_face_crop,
                        mode=laser_mode,
                        blur_ksize=laser_blur,
                        vmin=laser_vmin,
                        smax=laser_smax,
                        min_area=laser_min_a,
                        max_area=laser_max_a,
                    )
                lx, ly, laser_ok = laser_stab.push(laser_xy_raw)
            else:
                lx, ly, laser_ok = 0.5, 0.5, False

            if signup_pointer_now:
                px, py, active_out = resolve_plane_output(
                    "laser",
                    laser_enable=True,
                    laser_ok=laser_ok,
                    lx=lx,
                    ly=ly,
                    tip_ok=False,
                    tx=0.5,
                    ty=0.5,
                    yolo_active=False,
                    yolo_cx=0.5,
                    yolo_cy=0.5,
                    map_plane=_map_plane,
                )
            else:
                px, py, active_out = resolve_plane_output(
                    plane_source,
                    laser_enable=laser_enable,
                    laser_ok=laser_ok,
                    lx=lx,
                    ly=ly,
                    tip_ok=tip_ok,
                    tx=float(tx),
                    ty=float(ty),
                    yolo_active=bool(state["yolo_active"]),
                    yolo_cx=float(state["cx"]),
                    yolo_cy=float(state["cy"]),
                    map_plane=_map_plane,
                )
            if bottle_plane_smoother is not None and bool(state.get("yolo_active")):
                px, py, active_out = bottle_plane_smoother.push(px, py, active_out)
            if signup_pointer_now and active_out:
                px, py, active_out = laser_plane_smoother.push(px, py, active_out)
            signup_active = bool(state.get("signup_gestures_active")) and not signup_pointer_now
            if bridge is not None:
                if signup_active:
                    bridge.set_plane_position(0.5, 0.5, False)
                else:
                    bridge.set_plane_position(px, py, active_out)
                if gaze_enable and gaze_overlay and face_mesh is not None:
                    bridge.set_gaze_position(gaze_tx, gaze_ty, gaze_ok)
                else:
                    bridge.set_gaze_position(0.5, 0.5, False)
                bridge.tick()

            if signup_pointer_now and hub is not None and laser_ok and not state.get("_keyboard_laser_hub"):
                hub.send_event({"event": "keyboard_pointer_source", "source": "laser"})
                state["_keyboard_laser_hub"] = True

            if signup_active and hub is not None and bottle_signup_mode and not cup_active:
                if res.multi_hand_landmarks:
                    lm0 = res.multi_hand_landmarks[0]
                    static_pose = classify_signup_static_pose(lm0, finger_margin=_pose_margin)
                    swipe_sig = swipe_detector.push(lm0, static_pose)
                    if swipe_sig:
                        print("[TUIO-only] hand swipe (bottle lost) -> hub:", swipe_sig)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": swipe_sig})
            elif signup_active and hub is not None and bottle_hand_mode and not cup_active:
                if res.multi_hand_landmarks:
                    lm0 = res.multi_hand_landmarks[0]
                    static_pose = classify_signup_static_pose(lm0, finger_margin=_pose_margin)
                    fired_static = pose_gate.push(static_pose)
                    swipe_sig = swipe_detector.push(lm0, static_pose)
                    if fired_static:
                        print("[TUIO-only] signup pose -> hub:", fired_static)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": fired_static})
                    elif swipe_sig:
                        print("[TUIO-only] signup swipe -> hub:", swipe_sig)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": swipe_sig})
                else:
                    swipe_detector.reset()
            elif signup_active and hub is not None and not bottle_signup_mode and not bottle_hand_mode:
                if res.multi_hand_landmarks:
                    lm0 = res.multi_hand_landmarks[0]
                    static_pose = classify_signup_static_pose(lm0, finger_margin=_pose_margin)
                    fired_static = pose_gate.push(static_pose)
                    swipe_sig = swipe_detector.push(lm0, static_pose)
                    if fired_static:
                        print("[TUIO-only] signup pose -> hub:", fired_static)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": fired_static})
                    elif swipe_sig:
                        print("[TUIO-only] signup swipe -> hub:", swipe_sig)
                        hub.send_event({"event": "mediapipe_signup_gesture", "signal": swipe_sig})
                else:
                    swipe_detector.reset()

            hide_hands_draw = cup_hide_hands and bool(state.get("yolo_active")) and (
                bottle_hand_mode
                or (bottle_signup_mode and signup_active_now)
            )
            if (
                hands is not None
                and res.multi_hand_landmarks
                and mp_draw is not None
                and mp_hands_mod is not None
                and not hide_hands_draw
            ):
                for hand_lm in res.multi_hand_landmarks:
                    mp_draw.draw_landmarks(frame, hand_lm, mp_hands_mod.HAND_CONNECTIONS)

            if preview:
                cup_bbox = state.get("cup_bbox_norm")
                if isinstance(cup_bbox, (list, tuple)) and len(cup_bbox) >= 4:
                    hh, ww = frame.shape[:2]
                    cx_n, cy_n, bw_n, bh_n = [float(v) for v in cup_bbox[:4]]
                    x1 = int((cx_n - bw_n / 2.0) * ww)
                    y1 = int((cy_n - bh_n / 2.0) * hh)
                    x2 = int((cx_n + bw_n / 2.0) * ww)
                    y2 = int((cy_n + bh_n / 2.0) * hh)
                    cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)
                    cv2.putText(
                        frame,
                        "bottle",
                        (x1, max(0, y1 - 6)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 255, 0),
                        2,
                    )
                elif bottle_hand_mode:
                    hint = str(state.get("yolo_debug_hint") or "").strip()
                    if hint:
                        cv2.putText(
                            frame,
                            hint[:72],
                            (8, frame.shape[0] - 12),
                            cv2.FONT_HERSHEY_SIMPLEX,
                            0.45,
                            (0, 200, 255),
                            1,
                            cv2.LINE_AA,
                        )
                elif bottle_signup_mode and signup_active_now:
                    hint = str(state.get("yolo_debug_hint") or "bottle sign-up").strip()
                    draw_hint_panel(
                        frame,
                        [
                            hint,
                            "Slide bottle L/R to rotate (or swipe empty hand)",
                            "Hold still near palm = select | lower bottle below palm = submit",
                        ],
                    )
                if (
                    (laser_enable or signup_pointer_now)
                    and laser_ok
                    and cap is not None
                    and not auto_frame_face
                ):
                    hh, ww = frame.shape[:2]
                    cv2.circle(
                        frame,
                        (int(lx * max(1, ww - 1)), int(ly * max(1, hh - 1))),
                        8,
                        (0, 255, 255),
                        2,
                    )
                if (
                    preview_face_box
                    and fres_gaze is not None
                    and fres_gaze.multi_face_landmarks
                ):
                    _draw_face_bbox_on_frame(
                        frame, fres_gaze.multi_face_landmarks[0], current_emotion_pair
                    )
                cv2.imshow("TUIO-only (laptop)", frame)
                if cv2.waitKey(1) == ord("q"):
                    break
            else:
                time.sleep(0.001)
    finally:
        if face_executor is not None:
            try:
                face_executor.shutdown(wait=True)
            except Exception as ex:
                print("[TUIO-only] Face thread pool shutdown:", repr(ex))
            face_executor = None
        if hands is not None:
            hands.close()
        if cap is not None:
            cap.release()
        if preview:
            try:
                cv2.destroyAllWindows()
            except Exception:
                pass
        _render_gaze_heatmap(gaze_hist, gaze_heatmap_save, gaze_heatmap_show)
        if face_mesh is not None:
            face_mesh.close()
        if bridge is not None:
            bridge.close()
        if hub is not None:
            hub.close()


if __name__ == "__main__":
    main()
