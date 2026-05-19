"""Tests for pipeline.handlers (mocked YOLO; no weights download in CI)."""

from __future__ import annotations

import os
from unittest.mock import MagicMock

import cv2
import numpy as np
import pytest

import pipeline.handlers as handlers


def _small_jpeg() -> bytes:
    img = np.zeros((64, 64, 3), dtype=np.uint8)
    ok, enc = cv2.imencode(".jpg", img)
    assert ok
    return enc.tobytes()


def test_handle_gaze_stub() -> None:
    out = handlers.handle_gaze({}, _small_jpeg())
    assert "gaze_x" in out and "gaze_y" in out
    assert out["frame_size"][0] > 0


def test_handle_face_stub() -> None:
    out = handlers.handle_face({}, _small_jpeg())
    assert out["faces"] >= 0


class _TorchLike:
    """Minimal Tensor-like object matching Ultralytics ``.cpu().numpy()`` usage."""

    def __init__(self, arr: np.ndarray) -> None:
        self._arr = arr

    def cpu(self) -> "_TorchLike":
        return self

    def numpy(self) -> np.ndarray:
        return self._arr


class _FakeBoxes:
    def __init__(self) -> None:
        # Center (32,32) on a 64×64 frame → normalized (0.5, 0.5)
        self.xyxy = _TorchLike(np.array([[10.0, 10.0, 54.0, 54.0]], dtype=np.float32))
        self.cls = _TorchLike(np.array([0], dtype=np.int64))
        self.conf = _TorchLike(np.array([0.92], dtype=np.float32))

    def __len__(self) -> int:
        return 1


def test_handle_object_detection_mock_yolo(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OBJECT_TARGET_CLASSES", "airplane")

    res0 = MagicMock()
    res0.names = {0: "airplane"}
    res0.boxes = _FakeBoxes()

    model = MagicMock()
    model.predict.return_value = [res0]

    monkeypatch.setattr(handlers, "_get_yolo", lambda: model)

    out = handlers.handle_object_detection({}, _small_jpeg())
    assert out["active"] is True
    assert out["plane"] is not None
    assert out["plane"]["label"] == "airplane"
    assert abs(out["plane"]["cx"] - 0.5) < 0.01
    assert len(out["detections"]) >= 1


def test_handle_object_no_match(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OBJECT_TARGET_CLASSES", "zebra")

    res0 = MagicMock()
    res0.names = {0: "airplane"}
    res0.boxes = _FakeBoxes()

    model = MagicMock()
    model.predict.return_value = [res0]

    monkeypatch.setattr(handlers, "_get_yolo", lambda: model)

    out = handlers.handle_object_detection({}, _small_jpeg())
    assert out["active"] is False
    assert out["plane"] is None


def test_handle_mediapipe_with_frame() -> None:
    out = handlers.handle_mediapipe({}, _small_jpeg())
    assert "stub" in out


def test_handle_bluetooth_control_scan_status(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(handlers, "_ble_scan_requested", False)
    r1 = handlers.handle_bluetooth({"type": "control", "command": "scan_start"}, None)
    assert r1["command"] == "scan_start"
    assert r1["devices"] == []
    r2 = handlers.handle_bluetooth({"type": "control", "command": "status"}, None)
    assert r2["scan_requested"] is True


def test_handlers_registry() -> None:
    assert set(handlers.HANDLERS.keys()) == {"gaze", "face", "object", "mediapipe", "bluetooth"}
