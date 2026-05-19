"""Tests for MediapipeTuioBridge OSC bundle output (UDP mocked)."""

from __future__ import annotations

from unittest.mock import MagicMock

import pytest

from mediapipe_tuio_bridge import MediapipeTuioBridge


def test_bundle_starts_with_osc_bundle() -> None:
    b = MediapipeTuioBridge(host="127.0.0.1", port=3333)
    b._sock = MagicMock()
    b.set_mode("point8_only")
    b.set_plane_position(0.25, 0.75, True)
    b.tick()
    assert b._sock.sendto.called
    pkt = b._sock.sendto.call_args[0][0]
    assert pkt.startswith(b"#bundle")
    b.close()


def test_point8_only_inactive_smaller_bundle_than_active() -> None:
    b_inactive = MediapipeTuioBridge(host="127.0.0.1", port=3333)
    b_inactive._sock = MagicMock()
    b_inactive.set_mode("point8_only")
    b_inactive.set_plane_position(0.5, 0.5, False)
    b_inactive.tick()
    inactive_len = len(b_inactive._sock.sendto.call_args[0][0])

    b_active = MediapipeTuioBridge(host="127.0.0.1", port=3333)
    b_active._sock = MagicMock()
    b_active.set_mode("point8_only")
    b_active.set_plane_position(0.5, 0.5, True)
    b_active.tick()
    active_len = len(b_active._sock.sendto.call_args[0][0])
    assert active_len > inactive_len
    b_inactive.close()
    b_active.close()


def test_on_gesture_rotate_updates_default_mode() -> None:
    b = MediapipeTuioBridge(host="127.0.0.1", port=3333)
    b._sock = MagicMock()
    b.set_mode("default")
    ang0 = b._scroll_angle
    b.on_gesture_result("Rotate Right")
    assert b._scroll_angle > ang0
    b.close()
