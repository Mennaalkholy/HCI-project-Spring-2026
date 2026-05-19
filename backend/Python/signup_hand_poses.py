"""
Sign-up gestures for MediaPipe hands (no DollarPy).

Static poses (palm toward camera, mirrored selfie typical):
  - **Peace** (index + middle extended, ring + pinky curled) → ``submit``
  - **Fist** (index–pinky curled) → ``select``

Horizontal **swipe** (wrist landmark 0, sliding window over frames):
  - Wrist moves toward +x (image right) → ``rotateRight`` (👉 swipe right)
  - Wrist moves toward -x (image left) → ``rotateLeft`` (👈 swipe left)

Swipes are suppressed while a peace/fist pose is held (same frame static classification).

Env:
  TUIO_SIGNUP_POSE_STABLE_FRAMES — consecutive frames before peace/fist fires (default 7)
  TUIO_SIGNUP_POSE_COOLDOWN_S — min seconds between peace/fist actions (default 0.55)
  TUIO_SIGNUP_FINGER_MARGIN — y-gap for extended vs curled (default 0.034)
  TUIO_SIGNUP_SWIPE_WINDOW — wrist samples in sliding window (default 12)
  TUIO_SIGNUP_SWIPE_MIN_DX — min wrist delta (newest − oldest, norm 0–1) to count as swipe (default 0.12)
  TUIO_SIGNUP_SWIPE_COOLDOWN_S — min seconds between swipe rotations (default 0.48)
"""

from __future__ import annotations

import os
import time
from collections import deque
from typing import Any, Optional


def _lm_xy(lm: Any, idx: int) -> tuple[float, float]:
    p = lm.landmark[idx]
    return float(p.x), float(p.y)


def _extended_y(lm: Any, tip: int, pip: int, margin: float) -> bool:
    return float(lm.landmark[tip].y) < float(lm.landmark[pip].y) - margin


def _curled_y(lm: Any, tip: int, pip: int, margin: float) -> bool:
    return float(lm.landmark[tip].y) > float(lm.landmark[pip].y) - margin


def classify_signup_static_pose(lm: Any, *, finger_margin: float) -> Optional[str]:
    """
    Return ``submit`` (peace), ``select`` (fist), or None.
    """
    if (
        _extended_y(lm, 8, 6, finger_margin)
        and _extended_y(lm, 12, 10, finger_margin)
        and _curled_y(lm, 16, 14, finger_margin * 0.6)
        and _curled_y(lm, 20, 18, finger_margin * 0.6)
    ):
        return "submit"

    if all(
        _curled_y(lm, tip, pip, finger_margin * 0.45)
        for tip, pip in ((8, 6), (12, 10), (16, 14), (20, 18))
    ):
        return "select"

    return None


def classify_signup_pose(lm: Any, *, finger_margin: float) -> Optional[str]:
    """Alias for :func:`classify_signup_static_pose` (legacy name)."""
    return classify_signup_static_pose(lm, finger_margin=finger_margin)


class SignupSwipeDetector:
    """Horizontal wrist motion → rotateRight / rotateLeft with cooldown."""

    def __init__(self, window: int, min_dx: float, cooldown_s: float) -> None:
        self._window = max(5, int(window))
        self._min_dx = max(0.05, float(min_dx))
        self._cooldown_s = max(0.15, float(cooldown_s))
        self._xs: deque[float] = deque(maxlen=self._window)
        self._last_fire = 0.0

    def reset(self) -> None:
        self._xs.clear()

    def push(self, lm: Any, static_pose: Optional[str]) -> Optional[str]:
        now = time.monotonic()
        if now - self._last_fire < self._cooldown_s:
            return None
        if static_pose in ("submit", "select"):
            self._xs.clear()
            return None

        wx, _ = _lm_xy(lm, 0)
        self._xs.append(wx)
        if len(self._xs) < self._window:
            return None

        oldest = self._xs[0]
        newest = self._xs[-1]
        dx = newest - oldest
        if dx >= self._min_dx:
            self._xs.clear()
            self._last_fire = now
            return "rotateRight"
        if dx <= -self._min_dx:
            self._xs.clear()
            self._last_fire = now
            return "rotateLeft"
        return None


def make_swipe_detector_from_env() -> SignupSwipeDetector:
    try:
        w = int(os.environ.get("TUIO_SIGNUP_SWIPE_WINDOW", "12"))
    except ValueError:
        w = 12
    try:
        mdx = float(os.environ.get("TUIO_SIGNUP_SWIPE_MIN_DX", "0.12"))
    except ValueError:
        mdx = 0.12
    try:
        cd = float(os.environ.get("TUIO_SIGNUP_SWIPE_COOLDOWN_S", "0.48"))
    except ValueError:
        cd = 0.48
    return SignupSwipeDetector(w, mdx, cd)


class SignupPoseGate:
    """Hold-stable + cooldown so peace/fist do not spam the hub."""

    def __init__(self, stable_frames: int, cooldown_s: float) -> None:
        self._stable_frames = max(3, int(stable_frames))
        self._cooldown_s = max(0.15, float(cooldown_s))
        self._current: Optional[str] = None
        self._count = 0
        self._last_fire = 0.0

    def reset(self) -> None:
        self._current = None
        self._count = 0

    def push(self, pose: Optional[str]) -> Optional[str]:
        now = time.monotonic()
        if now - self._last_fire < self._cooldown_s:
            return None
        if not pose:
            self._current = None
            self._count = 0
            return None
        if pose == self._current:
            self._count += 1
        else:
            self._current = pose
            self._count = 1
        if self._count >= self._stable_frames:
            self._last_fire = now
            self._current = None
            self._count = 0
            return pose
        return None


def make_pose_gate_from_env() -> SignupPoseGate:
    try:
        n = int(os.environ.get("TUIO_SIGNUP_POSE_STABLE_FRAMES", "7"))
    except ValueError:
        n = 7
    try:
        cd = float(os.environ.get("TUIO_SIGNUP_POSE_COOLDOWN_S", "0.55"))
    except ValueError:
        cd = 0.55
    return SignupPoseGate(n, cd)


def finger_margin_from_env() -> float:
    try:
        m = float(os.environ.get("TUIO_SIGNUP_FINGER_MARGIN", "0.034"))
    except ValueError:
        m = 0.034
    return max(0.015, min(0.08, m))
