"""Tests for camera_utils (no real camera required)."""

from __future__ import annotations

import os

import numpy as np
import pytest

from camera_utils import (
    DEFAULT_CAPTURE_INDEX,
    DEFAULT_TUIO_CAPTURE_INDEX,
    _parse_fallback_indices,
    default_capture_index,
    default_face_capture_index,
    default_tuio_capture_index,
    mirror_frame_horizontal_if_enabled,
    synthetic_frame_generator,
)


def test_parse_fallback_indices_default_order() -> None:
    prev = os.environ.get("CAMERA_FALLBACK_INDICES")
    try:
        os.environ.pop("CAMERA_FALLBACK_INDICES", None)
        assert _parse_fallback_indices(4)[:4] == [4, 0, 1, 2]
    finally:
        if prev is not None:
            os.environ["CAMERA_FALLBACK_INDICES"] = prev


def test_parse_fallback_indices_env_custom() -> None:
    prev = os.environ.get("CAMERA_FALLBACK_INDICES")
    try:
        os.environ["CAMERA_FALLBACK_INDICES"] = "5,9,1"
        assert _parse_fallback_indices(2) == [2, 5, 9, 1]
    finally:
        if prev is not None:
            os.environ["CAMERA_FALLBACK_INDICES"] = prev
        else:
            os.environ.pop("CAMERA_FALLBACK_INDICES", None)


def test_default_capture_index() -> None:
    prev_main = os.environ.get("MAIN_CAPTURE_INDEX")
    prev_camo = os.environ.get("CAMO_CAMERA_INDEX")
    try:
        os.environ.pop("MAIN_CAPTURE_INDEX", None)
        os.environ.pop("CAMO_CAMERA_INDEX", None)
        assert default_capture_index() == DEFAULT_CAPTURE_INDEX
        os.environ["MAIN_CAPTURE_INDEX"] = "7"
        assert default_capture_index() == 7
    finally:
        if prev_main is not None:
            os.environ["MAIN_CAPTURE_INDEX"] = prev_main
        else:
            os.environ.pop("MAIN_CAPTURE_INDEX", None)
        if prev_camo is not None:
            os.environ["CAMO_CAMERA_INDEX"] = prev_camo
        else:
            os.environ.pop("CAMO_CAMERA_INDEX", None)


def test_default_tuio_and_face_capture_index() -> None:
    prev_tuio = os.environ.get("TUIO_CAPTURE_INDEX")
    prev_face = os.environ.get("FACE_CAPTURE_INDEX")
    prev_main = os.environ.get("MAIN_CAPTURE_INDEX")
    try:
        os.environ.pop("TUIO_CAPTURE_INDEX", None)
        os.environ.pop("FACE_CAPTURE_INDEX", None)
        os.environ.pop("MAIN_CAPTURE_INDEX", None)
        assert default_tuio_capture_index() == DEFAULT_TUIO_CAPTURE_INDEX
        assert default_face_capture_index() == DEFAULT_CAPTURE_INDEX
        os.environ["FACE_CAPTURE_INDEX"] = "4"
        assert default_face_capture_index() == 4
    finally:
        for key, prev in (
            ("TUIO_CAPTURE_INDEX", prev_tuio),
            ("FACE_CAPTURE_INDEX", prev_face),
            ("MAIN_CAPTURE_INDEX", prev_main),
        ):
            if prev is not None:
                os.environ[key] = prev
            else:
                os.environ.pop(key, None)


def test_mirror_frame_horizontal_respects_env() -> None:
    prev = os.environ.get("CAMERA_MIRROR_HORIZONTAL")
    try:
        f = np.zeros((2, 4, 3), dtype=np.uint8)
        f[:, 0, :] = (1, 2, 3)
        f[:, 3, :] = (9, 8, 7)
        os.environ["CAMERA_MIRROR_HORIZONTAL"] = "0"
        g = mirror_frame_horizontal_if_enabled(f.copy())
        assert np.array_equal(g, f)
        os.environ["CAMERA_MIRROR_HORIZONTAL"] = "1"
        h = mirror_frame_horizontal_if_enabled(f.copy())
        assert tuple(int(x) for x in h[0, 0]) == (9, 8, 7)
        assert tuple(int(x) for x in h[0, 3]) == (1, 2, 3)
    finally:
        if prev is not None:
            os.environ["CAMERA_MIRROR_HORIZONTAL"] = prev
        else:
            os.environ.pop("CAMERA_MIRROR_HORIZONTAL", None)


def test_synthetic_frame_generator_shape() -> None:
    gen = synthetic_frame_generator(640, 480)
    a = next(gen)
    b = next(gen)
    assert a.shape == (480, 640, 3)
    assert a.dtype == np.uint8
    assert np.all(a == 0)
    a[0, 0, 0] = 255
    assert b[0, 0, 0] == 0
