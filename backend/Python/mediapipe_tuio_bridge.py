
from __future__ import annotations

import os
import socket
import struct
from typing import List, Optional, Tuple


SESS_BAR = 910000       # Symbol 0 — toggle bar
SESS_SCROLL = 910001   # Symbol 1 — scroll dial
SESS_CONFIRM = 910002  # Symbol 2 — confirm
SESS_DIAL = 910004     # Symbol 4 — sign-up character dial (rotate)
SESS_PLANE = 910010    # Symbol 10 — plane/tayara pointer
SESS_GAZE = 910011     # Symbol 11 — soft gaze dot (game overlay; separate from finger plane)


def _pad(b: bytes) -> bytes:
    b = b + b"\x00"
    while len(b) % 4:
        b += b"\x00"
    return b


def _pack_string(s: str) -> bytes:
    return _pad(s.encode("latin-1", errors="replace"))


def _pack_int(i: int) -> bytes:
    return struct.pack(">i", int(i))


def _pack_float(f: float) -> bytes:
    return struct.pack(">f", float(f))


def _build_osc_message(address: str, parts: List[Tuple[str, object]]) -> bytes:
    """parts: list of ('s'|'i'|'f', value)"""
    data = bytearray()
    data += _pack_string(address)
    tags = [","]
    payload = bytearray()
    for t, v in parts:
        tags.append(t)
        if t == "s":
            payload += _pack_string(str(v))
        elif t == "i":
            payload += _pack_int(int(v))
        elif t == "f":
            payload += _pack_float(float(v))
        else:
            raise ValueError(t)
    data += _pad("".join(tags).encode("ascii"))
    data += payload
    return bytes(data)


def _tuio_set_2dobj(session_id: int, symbol_id: int, x: float, y: float, angle: float) -> bytes:
    return _build_osc_message(
        "/tuio/2Dobj",
        [
            ("s", "set"),
            ("i", session_id),
            ("i", symbol_id),
            ("f", x),
            ("f", y),
            ("f", angle),
            ("f", 0.0),
            ("f", 0.0),
            ("f", 0.0),
            ("f", 0.0),
            ("f", 0.0),
        ],
    )


def _tuio_alive(session_ids: List[int]) -> bytes:
    parts: List[Tuple[str, object]] = [("s", "alive")]
    for sid in session_ids:
        parts.append(("i", sid))
    return _build_osc_message("/tuio/2Dobj", parts)


def _tuio_fseq(seq: int) -> bytes:
    return _build_osc_message("/tuio/2Dobj", [("s", "fseq"), ("i", int(seq))])


def _build_bundle(inner_messages: List[bytes]) -> bytes:
    out = bytearray()
    out += _pack_string("#bundle")
    out += struct.pack(">Q", 1)  # OSC "immediately" timetag (OSC.NET compatible)
    for msg in inner_messages:
        out += _pack_int(len(msg))
        out += msg
    return bytes(out)


class MediapipeTuioBridge:

    def __init__(self, host: Optional[str] = None, port: Optional[int] = None) -> None:
        self.host = host or os.environ.get("TUIO_HOST", "127.0.0.1")
        self.port = int(port or os.environ.get("TUIO_PORT", "3333"))
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._fseq = 0
        self._scroll_angle = 0.0
        self._dial_angle = 0.0
        self._pulse_bar = 0
        self._pulse_confirm = 0
        self._mode = "default"  # default | point8_only
        self._plane_active = False
        self._plane_x = 0.5
        self._plane_y = 0.5
        self._plane_was_active = False
        self._plane_smooth = 0.42
        try:
            self._plane_smooth = float(
                os.environ.get("TUIO_PLANE_TUIO_SMOOTH", os.environ.get("TUIO_LASER_SMOOTH", "0.42"))
            )
        except ValueError:
            self._plane_smooth = 0.42
        self._plane_smooth = max(0.05, min(1.0, self._plane_smooth))
        self._gaze_active = False
        self._gaze_x = 0.5
        self._gaze_y = 0.5
        self._gaze_was_active = False
        self._gaze_hold_frames = 0
        self._gaze_hold_max = 25
        self._gaze_smooth = 0.38
        try:
            self._gaze_smooth = float(os.environ.get("TUIO_GAZE_TUIO_SMOOTH", "0.38"))
        except ValueError:
            self._gaze_smooth = 0.38
        self._gaze_smooth = max(0.05, min(1.0, self._gaze_smooth))
        try:
            hold_s = float(os.environ.get("TUIO_GAZE_TUIO_HOLD_S", "0.85"))
            self._gaze_hold_max = max(1, min(90, int(hold_s * 30)))
        except ValueError:
            self._gaze_hold_max = 25
        try:
            self._angle_step = float(os.environ.get("TUIO_DIAL_STEP", "0.45"))
        except ValueError:
            self._angle_step = 0.45

    def set_mode(self, mode: str) -> None:
        raw = str(mode or "").strip().lower()
        if raw in ("point8_only", "point8", "finger_only"):
            self._mode = "point8_only"
        else:
            self._mode = "default"

    def set_plane_position(self, x: float, y: float, active: bool = True) -> None:
        """TUIO symbol 10 — EMA smoothing (laser / finger / bottle plane)."""
        if not active:
            self._plane_active = False
            self._plane_was_active = False
            return
        tx = max(0.0, min(1.0, float(x)))
        ty = max(0.0, min(1.0, float(y)))
        if not self._plane_was_active:
            self._plane_x, self._plane_y = tx, ty
            self._plane_was_active = True
        elif abs(tx - self._plane_x) > 0.62 or abs(ty - self._plane_y) > 0.62:
            self._plane_x, self._plane_y = tx, ty
        else:
            s = self._plane_smooth
            self._plane_x += (tx - self._plane_x) * s
            self._plane_y += (ty - self._plane_y) * s
        self._plane_active = True

    def set_gaze_position(self, x: float, y: float, active: bool = True) -> None:
        """TUIO symbol 11 — soft gaze marker on the game (point8_only bundles only)."""
        if active:
            self._gaze_hold_frames = self._gaze_hold_max
            tx = max(0.0, min(1.0, float(x)))
            ty = max(0.0, min(1.0, float(y)))
            if not self._gaze_was_active:
                self._gaze_x, self._gaze_y = tx, ty
                self._gaze_was_active = True
            elif abs(tx - self._gaze_x) > 0.62 or abs(ty - self._gaze_y) > 0.62:
                self._gaze_x, self._gaze_y = tx, ty
            else:
                s = self._gaze_smooth
                self._gaze_x += (tx - self._gaze_x) * s
                self._gaze_y += (ty - self._gaze_y) * s
            self._gaze_active = True
            return
        if self._gaze_hold_frames > 0 and self._gaze_was_active:
            self._gaze_hold_frames -= 1
            self._gaze_active = True
            return
        self._gaze_active = False
        self._gaze_was_active = False

    def close(self) -> None:
        try:
            self._sock.close()
        except OSError:
            pass

    def on_gesture_result(self, name: Optional[object]) -> None:
        """Map a gesture label (e.g. rotate left/right) to virtual TUIO dial angles; name is normalized internally."""
        if not name:
            return
        if isinstance(name, (list, tuple)) and len(name) > 0:
            raw = name[0]
        else:
            raw = name
        if raw is None or raw is False:
            return
        key = str(raw).strip()
        norm = key.lower().replace("_", "").replace(" ", "").replace("-", "")

        if norm in ("rotateleft", "rotateleft2", "swipeleft") or (
            "rotate" in norm and norm.endswith("left")
        ):
            self._scroll_angle -= self._angle_step
            self._dial_angle -= self._angle_step
        elif norm in ("rotateright", "rotateright2", "swiperight") or (
            "rotate" in norm and norm.endswith("right")
        ):
            self._scroll_angle += self._angle_step
            self._dial_angle += self._angle_step

    def tick(self) -> None:
        """Send current virtual TUIO state (call once per processed video frame)."""
        self._fseq += 1
        if self._fseq > 0x7FFFFFFF:
            self._fseq = 1

        inner: List[bytes] = []
        alive: List[int] = []

        if self._mode == "point8_only":
            if self._plane_active:
                alive.append(SESS_PLANE)
                inner.append(_tuio_set_2dobj(SESS_PLANE, 10, self._plane_x, self._plane_y, 0.0))
            if self._gaze_active:
                alive.append(SESS_GAZE)
                inner.append(_tuio_set_2dobj(SESS_GAZE, 11, self._gaze_x, self._gaze_y, 0.0))
        else:
            alive.extend([SESS_SCROLL, SESS_DIAL])
            inner.append(
                _tuio_set_2dobj(
                    SESS_SCROLL,
                    1,
                    0.5,
                    0.5,
                    self._scroll_angle,
                )
            )
            inner.append(
                _tuio_set_2dobj(
                    SESS_DIAL,
                    4,
                    0.5,
                    0.5,
                    self._dial_angle,
                )
            )

            if self._pulse_bar > 0:
                alive.append(SESS_BAR)
                inner.append(_tuio_set_2dobj(SESS_BAR, 0, 0.25, 0.5, 0.0))
                self._pulse_bar -= 1

            if self._pulse_confirm > 0:
                alive.append(SESS_CONFIRM)
                inner.append(_tuio_set_2dobj(SESS_CONFIRM, 2, 0.75, 0.5, 0.0))
                self._pulse_confirm -= 1

        inner.append(_tuio_alive(alive))
        inner.append(_tuio_fseq(self._fseq))

        packet = _build_bundle(inner)
        try:
            self._sock.sendto(packet, (self.host, self.port))
        except OSError as e:
            print("[MediapipeTuioBridge] send failed:", e)
