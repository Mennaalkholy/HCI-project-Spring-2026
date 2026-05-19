"""
Outbound TCP client for Python services connecting to NotificationHub.

Flow:
  1. Connect and send {"role":"service","service":"<service_id>"}
  2. Incoming lines from the hub are notifications originating from C# (routed or broadcast).
  3. Outgoing lines are forwarded by the hub to all C# connections — unless fanout is python_peers (see hub).

Each service uses its own socket to the hub and filters messages locally (e.g. by "target"
or "event").
"""

from __future__ import annotations

import json
import socket
import threading
from typing import Any, Callable, Dict, Optional


class HubServiceClient:
    def __init__(
        self,
        service_id: str,
        host: str = "127.0.0.1",
        port: int = 5000,
        on_command: Optional[Callable[[dict], Any]] = None,
    ) -> None:
        self.service_id = service_id.strip()
        self.host = host
        self.port = int(port)
        self._on_command = on_command
        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()
        self._running = False
        self._reader: Optional[threading.Thread] = None

    def connect(self) -> None:
        if not self.service_id:
            raise ValueError("service_id is required")
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.connect((self.host, self.port))
        hello = json.dumps({"role": "service", "service": self.service_id}, separators=(",", ":")) + "\n"
        self._sock.sendall(hello.encode("utf-8"))
        self._running = True
        self._reader = threading.Thread(target=self._read_loop, daemon=True)
        self._reader.start()
        print(f"[HubClient:{self.service_id}] Connected to hub at {self.host}:{self.port}")

    def _read_loop(self) -> None:
        assert self._sock is not None
        buf = b""
        try:
            while self._running:
                chunk = self._sock.recv(8192)
                if not chunk:
                    break
                buf += chunk
                while b"\n" in buf:
                    raw, buf = buf.split(b"\n", 1)
                    line = raw.decode("utf-8", errors="replace").strip()
                    if not line:
                        continue
                    try:
                        msg = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    if not isinstance(msg, dict):
                        continue
                    if callable(self._on_command):
                        try:
                            self._on_command(msg)
                        except Exception as ex:
                            print(f"[HubClient:{self.service_id}] handler error:", repr(ex))
        except OSError as ex:
            if self._running:
                print(f"[HubClient:{self.service_id}] read error:", repr(ex))
        finally:
            self._running = False

    def send_event(self, obj: Dict[str, Any]) -> None:
        line = json.dumps(obj, separators=(",", ":")) + "\n"
        data = line.encode("utf-8")
        with self._lock:
            if self._sock is None:
                return
            try:
                self._sock.sendall(data)
            except OSError as ex:
                print(f"[HubClient:{self.service_id}] send failed:", repr(ex))

    def close(self) -> None:
        self._running = False
        with self._lock:
            if self._sock is not None:
                try:
                    self._sock.close()
                except OSError:
                    pass
                self._sock = None
