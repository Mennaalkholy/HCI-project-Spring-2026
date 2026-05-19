"""Light integration: worker handler invoked like the real TCP loop (in-process)."""

from __future__ import annotations

import json
import socket
import struct
import threading
import time

import pytest

from pipeline.handlers import HANDLERS
from pipeline.protocol import FrameStreamReader, send_framed_jpeg, send_result


def _run_one_frame_service(service_name: str, header_extra: dict | None = None) -> dict:
    handler = HANDLERS[service_name]

    a, b = socket.socketpair()
    received: dict = {}

    def server_side() -> None:
        try:
            reader = FrameStreamReader(a)
            h, jpeg = reader.read_one_message()
            seq = int(h.get("seq", 0))
            payload = handler(h, jpeg)
            send_result(a, service=service_name, seq=seq, payload=payload)
        except Exception as ex:
            received["err"] = repr(ex)
        finally:
            try:
                a.close()
            except OSError:
                pass

    t = threading.Thread(target=server_side, daemon=True)
    t.start()

    extra = dict(header_extra or {})
    seq_val = int(extra.pop("seq", 1))
    send_framed_jpeg(b, seq=seq_val, jpeg=b"\xff\xd8\xff\xd9", extra=extra or None)
    buf = b""
    deadline = time.monotonic() + 3.0
    while time.monotonic() < deadline:
        chunk = b.recv(65536)
        if not chunk:
            break
        buf += chunk
        if b"\n" in buf:
            line = buf.split(b"\n", 1)[0]
            received["msg"] = json.loads(line.decode("utf-8"))
            break
    b.close()
    t.join(timeout=2.0)
    return received


def test_worker_gaze_roundtrip() -> None:
    out = _run_one_frame_service("gaze")
    assert "err" not in out
    assert out["msg"]["type"] == "result"
    assert out["msg"]["service"] == "gaze"
    assert "payload" in out["msg"]
