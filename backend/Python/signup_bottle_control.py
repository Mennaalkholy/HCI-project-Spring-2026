"""
Sign-up control with a water bottle (YOLO position), not hand poses.

Maps bottle movement to the same hub signals TuioDemo expects:
  - **Slide** the bottle left/right in front of the camera (not twist/rotate in place)
  - Hold still in center → ``select``
  - Hold still low in frame → ``submit``

Swipes take priority over select/submit when horizontal motion is detected.

Env:
  TUIO_BOTTLE_SIGNUP_SWIPE_WINDOW, TUIO_BOTTLE_SIGNUP_SWIPE_MIN_DX, TUIO_BOTTLE_SIGNUP_SWIPE_COOLDOWN_S
  TUIO_BOTTLE_SIGNUP_SELECT_STABLE_FRAMES, TUIO_BOTTLE_SIGNUP_SUBMIT_STABLE_FRAMES
  TUIO_BOTTLE_SIGNUP_ACTION_COOLDOWN_S
  TUIO_BOTTLE_SIGNUP_HOLD_MAX_SPREAD — max movement while counting as "hold still"
  TUIO_BOTTLE_SIGNUP_SUBMIT_BELOW_HAND — bottle cy must be this far below palm to submit (default 0.08)
  TUIO_BOTTLE_SIGNUP_SELECT_NEAR_HAND — bottle must be within this distance of palm to select (default 0.20)
"""

from __future__ import annotations

import os
import time
from collections import deque
from typing import Deque, Optional, Tuple


class BottleSignupController:
    def __init__(
        self,
        swipe_window: int,
        swipe_min_dx: float,
        swipe_cooldown_s: float,
        select_stable_frames: int,
        submit_stable_frames: int,
        action_cooldown_s: float,
    ) -> None:
        self._swipe_window = max(5, int(swipe_window))
        self._swipe_min_dx = max(0.05, float(swipe_min_dx))
        self._swipe_cooldown_s = max(0.15, float(swipe_cooldown_s))
        self._select_need = max(5, int(select_stable_frames))
        self._submit_need = max(5, int(submit_stable_frames))
        self._action_cooldown_s = max(0.2, float(action_cooldown_s))
        self._xs: Deque[float] = deque(maxlen=self._swipe_window)
        self._ys: Deque[float] = deque(maxlen=self._swipe_window)
        self._select_count = 0
        self._submit_count = 0
        self._last_fire = 0.0
        try:
            self._hold_spread = float(os.environ.get("TUIO_BOTTLE_SIGNUP_HOLD_MAX_SPREAD", "0.065"))
        except ValueError:
            self._hold_spread = 0.065

    def reset(self) -> None:
        self._xs.clear()
        self._ys.clear()
        self._select_count = 0
        self._submit_count = 0

    def _motion_spread(self) -> float:
        spread = 0.0
        if len(self._xs) >= 2:
            spread = max(spread, float(max(self._xs) - min(self._xs)))
        if len(self._ys) >= 2:
            spread = max(spread, float(max(self._ys) - min(self._ys)))
        return spread

    def _try_swipe(self, now: float) -> Optional[str]:
        if len(self._xs) < self._swipe_window:
            return None
        dx = self._xs[-1] - self._xs[0]
        if dx >= self._swipe_min_dx:
            self._xs.clear()
            self._select_count = 0
            self._submit_count = 0
            self._last_fire = now
            return "rotateRight"
        if dx <= -self._swipe_min_dx:
            self._xs.clear()
            self._select_count = 0
            self._submit_count = 0
            self._last_fire = now
            return "rotateLeft"
        return None

    def push(
        self,
        cx: float,
        cy: float,
        active: bool,
        hand_xy: Optional[Tuple[float, float]] = None,
    ) -> Optional[str]:
        if not active:
            self.reset()
            return None

        now = time.monotonic()
        if now - self._last_fire < self._action_cooldown_s:
            return None

        cx = max(0.0, min(1.0, float(cx)))
        cy = max(0.0, min(1.0, float(cy)))
        self._xs.append(cx)
        self._ys.append(cy)

        swipe = self._try_swipe(now)
        if swipe is not None:
            return swipe

        if self._motion_spread() > self._hold_spread:
            self._select_count = 0
            self._submit_count = 0
            return None

        try:
            sub_below = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SUBMIT_BELOW_HAND", "0.08"))
            sel_near = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_NEAR_HAND", "0.20"))
            sub_dx = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SUBMIT_MAX_DX", "0.26"))
            sel_x0 = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_X0", "0.38"))
            sel_x1 = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_X1", "0.62"))
            sel_y0 = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_Y0", "0.30"))
            sel_y1 = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_Y1", "0.62"))
            sub_y0 = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SUBMIT_Y0", "0.68"))
        except ValueError:
            sub_below, sel_near, sub_dx = 0.08, 0.20, 0.26
            sel_x0, sel_x1, sel_y0, sel_y1, sub_y0 = 0.38, 0.62, 0.30, 0.62, 0.68

        if hand_xy is not None:
            hx = max(0.0, min(1.0, float(hand_xy[0])))
            hy = max(0.0, min(1.0, float(hand_xy[1])))
            in_submit = cy >= hy + sub_below and abs(cx - hx) <= sub_dx
            in_select = (
                abs(cx - hx) <= sel_near
                and abs(cy - hy) <= sel_near
                and not in_submit
            )
        else:
            in_select = sel_x0 <= cx <= sel_x1 and sel_y0 <= cy <= sel_y1
            in_submit = cy >= sub_y0 and sel_x0 <= cx <= sel_x1

        if in_submit:
            self._submit_count += 1
            self._select_count = 0
            if self._submit_count >= self._submit_need:
                self._submit_count = 0
                self._last_fire = now
                self._xs.clear()
                return "submit"
            return None

        if in_select:
            self._select_count += 1
            self._submit_count = 0
            if self._select_count >= self._select_need:
                self._select_count = 0
                self._last_fire = now
                self._xs.clear()
                return "select"
            return None

        self._select_count = 0
        self._submit_count = 0
        return None


def make_bottle_signup_controller_from_env() -> BottleSignupController:
    try:
        w = int(os.environ.get("TUIO_BOTTLE_SIGNUP_SWIPE_WINDOW", "6"))
    except ValueError:
        w = 6
    try:
        mdx = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SWIPE_MIN_DX", "0.08"))
    except ValueError:
        mdx = 0.08
    try:
        scd = float(os.environ.get("TUIO_BOTTLE_SIGNUP_SWIPE_COOLDOWN_S", "0.35"))
    except ValueError:
        scd = 0.35
    try:
        sel_n = int(os.environ.get("TUIO_BOTTLE_SIGNUP_SELECT_STABLE_FRAMES", "5"))
    except ValueError:
        sel_n = 5
    try:
        sub_n = int(os.environ.get("TUIO_BOTTLE_SIGNUP_SUBMIT_STABLE_FRAMES", "5"))
    except ValueError:
        sub_n = 5
    try:
        acd = float(os.environ.get("TUIO_BOTTLE_SIGNUP_ACTION_COOLDOWN_S", "0.30"))
    except ValueError:
        acd = 0.30
    return BottleSignupController(w, mdx, scd, sel_n, sub_n, acd)
