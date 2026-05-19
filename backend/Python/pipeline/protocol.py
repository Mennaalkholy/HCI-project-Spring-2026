"""
TCP framing between main.py (producer) and processing services (consumers).

Each message:
  1) One UTF-8 line of JSON (header), ending with \\n
  2) 4-byte big-endian uint32 (JPEG byte length)
  3) Raw JPEG bytes (may be empty when length is 0)

Reply from worker → main (same socket):
  {"type":"result","service":"<name>","seq":<int>,"payload":{...}}\n
"""

from __future__ import annotations

import json
import socket
import struct
from typing import Any, Dict, Optional, Tuple


def send_framed_jpeg(
    sock: socket.socket,
    *,
    seq: int,
    jpeg: bytes,
    extra: Optional[Dict[str, Any]] = None,
) -> None:
    meta: Dict[str, Any] = {"type": "frame", "seq": int(seq)}
    if extra:
        meta.update(extra)
    header = json.dumps(meta, separators=(",", ":")) + "\n"
    sock.sendall(header.encode("utf-8"))
    sock.sendall(struct.pack(">I", len(jpeg)))
    sock.sendall(jpeg)


def send_control(sock: socket.socket, *, seq: int, command: str, extra: Optional[Dict[str, Any]] = None) -> None:
    meta: Dict[str, Any] = {"type": "control", "seq": int(seq), "command": command}
    if extra:
        meta.update(extra)
    header = json.dumps(meta, separators=(",", ":")) + "\n"
    sock.sendall(header.encode("utf-8"))
    sock.sendall(struct.pack(">I", 0))


class FrameStreamReader:
    """Stateful reader so TCP carry-over bytes after one frame are kept for the next."""

    def __init__(self, sock: socket.socket) -> None:
        self.sock = sock
        self._buf = bytearray()

    def read_one_message(self) -> Tuple[Dict[str, Any], Optional[bytes]]:
        buf = self._buf
        while b"\n" not in buf:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("peer closed")
            buf.extend(chunk)
        idx = buf.index(b"\n")
        line = bytes(buf[:idx])
        del buf[: idx + 1]
        header = json.loads(line.decode("utf-8"))
        while len(buf) < 4:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("peer closed")
            buf.extend(chunk)
        ln = struct.unpack(">I", bytes(buf[:4]))[0]
        del buf[:4]
        if ln == 0:
            return header, None
        while len(buf) < ln:
            chunk = self.sock.recv(min(65536, ln - len(buf)))
            if not chunk:
                raise ConnectionError("peer closed")
            buf.extend(chunk)
        jpeg = bytes(buf[:ln])
        del buf[:ln]
        return header, jpeg


def read_one_message(sock: socket.socket) -> Tuple[Dict[str, Any], Optional[bytes]]:
    """One-shot read (no carry-over). Prefer FrameStreamReader on persistent connections."""
    return FrameStreamReader(sock).read_one_message()


def send_result(sock: socket.socket, *, service: str, seq: int, payload: Dict[str, Any]) -> None:
    line = json.dumps(
        {"type": "result", "service": service, "seq": seq, "payload": payload},
        separators=(",", ":"),
    ) + "\n"
    sock.sendall(line.encode("utf-8"))
