"""Laser spot detection (synthetic frames, no camera)."""

from __future__ import annotations

import numpy as np
import cv2

from tuio_only_service import detect_laser_spot_norm_xy


def test_detect_laser_bright_small_blob_center() -> None:
    frame = np.zeros((120, 160, 3), dtype=np.uint8)
    # Large enough to survive default Gaussian blur + morphology
    cv2.circle(frame, (80, 60), 10, (255, 255, 255), -1)
    out = detect_laser_spot_norm_xy(
        frame, mode="bright", blur_ksize=5, vmin=200, smax=255, min_area=8.0, max_area=2000.0
    )
    assert out is not None
    assert abs(out[0] - 80 / 160.0) < 0.05
    assert abs(out[1] - 60 / 120.0) < 0.05


def test_detect_laser_green_blob() -> None:
    frame = np.zeros((100, 100, 3), dtype=np.uint8)
    cv2.circle(frame, (50, 50), 10, (0, 255, 0), -1)
    out = detect_laser_spot_norm_xy(frame, mode="green", blur_ksize=5, min_area=8.0, max_area=2000.0)
    assert out is not None


def test_detect_laser_none_on_empty() -> None:
    frame = np.zeros((80, 80, 3), dtype=np.uint8)
    assert detect_laser_spot_norm_xy(frame, mode="bright", vmin=250) is None
