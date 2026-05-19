"""Tests for pipeline.protocol framing over TCP."""

from __future__ import annotations

import json
import socket
import struct

import pytest

from pipeline.protocol import (
    FrameStreamReader,
    send_control,
    send_framed_jpeg,
    send_result,
)


def test_send_framed_jpeg_roundtrip() -> None:
    a, b = socket.socketpair()
    try:
        jpeg = b"\xff\xd8\xff\xd9"
        send_framed_jpeg(b, seq=7, jpeg=jpeg, extra={"cam": 2})
        header_line = b""
        while b"\n" not in header_line:
            header_line += a.recv(4096)
        idx = header_line.index(b"\n")
        meta = json.loads(header_line[:idx].decode("utf-8"))
        rest = header_line[idx + 1 :]
        assert meta["type"] == "frame"
        assert meta["seq"] == 7
        assert meta["cam"] == 2
        while len(rest) < 4:
            rest += a.recv(4096)
        ln = struct.unpack(">I", rest[:4])[0]
        body = rest[4:]
        while len(body) < ln:
            body += a.recv(4096)
        assert ln == len(jpeg)
        assert body[:ln] == jpeg
    finally:
        a.close()
        b.close()


def test_send_control_zero_jpeg_length() -> None:
    a, b = socket.socketpair()
    try:
        send_control(b, seq=1, command="scan_start")
        buf = b""
        while b"\n" not in buf:
            buf += a.recv(4096)
        line_end = buf.index(b"\n")
        meta = json.loads(buf[:line_end].decode("utf-8"))
        tail = buf[line_end + 1 :]
        assert meta["type"] == "control"
        assert meta["command"] == "scan_start"
        while len(tail) < 4:
            tail += a.recv(4096)
        ln = struct.unpack(">I", tail[:4])[0]
        assert ln == 0
    finally:
        a.close()
        b.close()


def test_frame_stream_reader_two_messages_carry_over() -> None:
    a, b = socket.socketpair()
    try:
        send_framed_jpeg(b, seq=1, jpeg=b"aa")
        send_framed_jpeg(b, seq=2, jpeg=b"bbbb")
        r = FrameStreamReader(a)
        h1, j1 = r.read_one_message()
        h2, j2 = r.read_one_message()
        assert h1["seq"] == 1 and j1 == b"aa"
        assert h2["seq"] == 2 and j2 == b"bbbb"
    finally:
        a.close()
        b.close()


def test_send_result_line() -> None:
    a, b = socket.socketpair()
    try:
        send_result(b, service="gaze", seq=3, payload={"x": 1})
        line = b""
        while b"\n" not in line:
            line += a.recv(4096)
        obj = json.loads(line.strip().decode("utf-8"))
        assert obj["type"] == "result"
        assert obj["service"] == "gaze"
        assert obj["seq"] == 3
        assert obj["payload"]["x"] == 1
    finally:
        a.close()
        b.close()
