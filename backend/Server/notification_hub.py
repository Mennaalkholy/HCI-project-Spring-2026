"""
Central notification hub: one TCP server that connects C# (TuioDemo) with Python services.

- C# connects and must send first line: {"role":"csharp"}
- Each Python service opens its own outbound connection and sends first line:
  {"role":"service","service":"<id>"}  e.g. camera, logging, face_auth

Routing from C#:
  - If message contains "target": "<service_id>" → only that service receives it.
  - If "target" is missing, null, or "all" → broadcast to every registered service (each client ignores unrelated events).

Routing from services:
  - Default: any JSON line from a service is forwarded unchanged to all C# connections.
  - If the message is JSON and "fanout" is "python_peers", the same line is sent to all
    other registered Python services (for pipeline → TUIO plane relay). It is still forwarded
    to C# unless "forward_csharp" is false.

Run:  python notification_hub.py
"""

from __future__ import annotations

import json
import os
import socket
import threading
from typing import Any, Dict, List, Optional


class NotificationHub:
    def __init__(self, host: str = "127.0.0.1", port: int = 5000) -> None:
        self.host = host
        self.port = int(port)
        self._server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._lock = threading.Lock()
        self._csharp: List[socket.socket] = []
        self._services: Dict[str, socket.socket] = {}
        self._running = False

    def start(self) -> None:
        self._server.bind((self.host, self.port))
        self._server.listen(32)
        self._running = True
        t = threading.Thread(target=self._accept_loop, daemon=True)
        t.start()
        print(f"[Hub] Listening on {self.host}:{self.port} — C# and Python services connect here.")

    def _accept_loop(self) -> None:
        while self._running:
            try:
                conn, addr = self._server.accept()
                conn.setblocking(True)
                t = threading.Thread(target=self._handshake, args=(conn, addr), daemon=True)
                t.start()
            except OSError:
                break

    def _handshake(self, conn: socket.socket, addr: tuple) -> None:
        buf = b""
        try:
            while self._running:
                chunk = conn.recv(8192)
                if not chunk:
                    conn.close()
                    return
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
                    role = str(msg.get("role", "")).strip().lower()
                    if role == "csharp":
                        self._register_csharp(conn, addr)
                        self._csharp_read_loop(conn, addr, buf)
                        return
                    if role == "service":
                        sid = str(msg.get("service", "")).strip()
                        if sid:
                            self._register_service(conn, addr, sid)
                            self._service_read_loop(conn, addr, sid, buf)
                            return
        except OSError:
            pass
        finally:
            try:
                conn.close()
            except OSError:
                pass

    def _register_csharp(self, conn: socket.socket, addr: tuple) -> None:
        with self._lock:
            self._csharp.append(conn)
        print(f"[Hub] C# registered from {addr[0]}:{addr[1]}")

    def _unregister_csharp(self, conn: socket.socket) -> None:
        with self._lock:
            if conn in self._csharp:
                self._csharp.remove(conn)

    def _register_service(self, conn: socket.socket, addr: tuple, service_id: str) -> None:
        with self._lock:
            old = self._services.get(service_id)
            if old is not None and old is not conn:
                try:
                    old.close()
                except OSError:
                    pass
            self._services[service_id] = conn
        print(f"[Hub] Service '{service_id}' registered from {addr[0]}:{addr[1]}")

    def _unregister_service(self, conn: socket.socket, service_id: Optional[str] = None) -> None:
        with self._lock:
            if service_id and self._services.get(service_id) is conn:
                del self._services[service_id]
            elif service_id is None:
                dead: List[str] = []
                for sid, c in self._services.items():
                    if c is conn:
                        dead.append(sid)
                for sid in dead:
                    del self._services[sid]

    def _forward_to_csharp(self, line_bytes: bytes) -> None:
        if not line_bytes.endswith(b"\n"):
            line_bytes = line_bytes + b"\n"
        dead: List[socket.socket] = []
        with self._lock:
            clients = list(self._csharp)
        for c in clients:
            try:
                c.sendall(line_bytes)
            except OSError:
                dead.append(c)
        for c in dead:
            self._unregister_csharp(c)
            try:
                c.close()
            except OSError:
                pass

    def _broadcast_to_service_peers(self, sender: socket.socket, line_bytes: bytes) -> None:
        if not line_bytes.endswith(b"\n"):
            line_bytes = line_bytes + b"\n"
        dead: List[tuple] = []
        with self._lock:
            items = list(self._services.items())
        for sid, sock in items:
            if sock is sender:
                continue
            try:
                sock.sendall(line_bytes)
            except OSError:
                dead.append((sid, sock))
        for sid, sock in dead:
            self._unregister_service(sock, sid)
            try:
                sock.close()
            except OSError:
                pass

    def _dispatch_from_csharp(self, msg: dict) -> None:
        raw = json.dumps(msg, separators=(",", ":")) + "\n"
        data = raw.encode("utf-8")
        tgt = msg.get("target", None)
        if tgt is None or str(tgt).strip().lower() in ("", "all"):
            self._broadcast_to_services(data)
            return
        key = str(tgt).strip()
        with self._lock:
            sock = self._services.get(key)
        if sock is not None:
            try:
                sock.sendall(data)
            except OSError:
                self._unregister_service(sock, key)
                try:
                    sock.close()
                except OSError:
                    pass
        else:
            ev = str(msg.get("event", "")).strip()
            print(f"[Hub] No service registered for target '{key}' — drop.")
            # TuioDemo may send shutdown before face_auth finishes registering; broadcast so any listener can react.
            if ev == "face_auth_shutdown":
                print("[Hub] face_auth_shutdown: broadcasting to all Python services (target was missing).")
                self._broadcast_to_services(data)

    def _broadcast_to_services(self, data: bytes) -> None:
        dead: List[tuple] = []
        with self._lock:
            items = list(self._services.items())
        for sid, sock in items:
            try:
                sock.sendall(data)
            except OSError:
                dead.append((sid, sock))
        for sid, sock in dead:
            self._unregister_service(sock, sid)
            try:
                sock.close()
            except OSError:
                pass

    def _csharp_read_loop(self, conn: socket.socket, addr: tuple, pending: bytes) -> None:
        buf = pending
        try:
            while self._running:
                while b"\n" in buf:
                    raw, buf = buf.split(b"\n", 1)
                    line = raw.decode("utf-8", errors="replace").strip()
                    if not line:
                        continue
                    try:
                        msg = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    if isinstance(msg, dict):
                        self._dispatch_from_csharp(msg)
                chunk = conn.recv(8192)
                if not chunk:
                    break
                buf += chunk
        except OSError:
            pass
        finally:
            self._unregister_csharp(conn)
            try:
                conn.close()
            except OSError:
                pass
            print(f"[Hub] C# disconnected from {addr[0]}:{addr[1]}")

    def _service_read_loop(self, conn: socket.socket, addr: tuple, service_id: str, pending: bytes) -> None:
        buf = pending
        try:
            while self._running:
                while b"\n" in buf:
                    raw, buf = buf.split(b"\n", 1)
                    line_b = raw + b"\n"
                    forward_cs = True
                    peer_fanout = False
                    try:
                        msg = json.loads(raw.decode("utf-8", errors="replace"))
                        if isinstance(msg, dict):
                            if str(msg.get("fanout", "")).strip().lower() == "python_peers":
                                peer_fanout = True
                            fc = msg.get("forward_csharp", True)
                            if fc is False:
                                forward_cs = False
                    except json.JSONDecodeError:
                        pass

                    if peer_fanout:
                        self._broadcast_to_service_peers(conn, line_b)
                    if forward_cs:
                        self._forward_to_csharp(raw + b"\n")
                chunk = conn.recv(8192)
                if not chunk:
                    break
                buf += chunk
        except OSError:
            pass
        finally:
            self._unregister_service(conn, service_id)
            try:
                conn.close()
            except OSError:
                pass
            print(f"[Hub] Service '{service_id}' disconnected from {addr[0]}:{addr[1]}")

    def close(self) -> None:
        self._running = False
        try:
            self._server.close()
        except OSError:
            pass
        with self._lock:
            for c in list(self._csharp):
                try:
                    c.close()
                except OSError:
                    pass
            self._csharp.clear()
            for s in list(self._services.values()):
                try:
                    s.close()
                except OSError:
                    pass
            self._services.clear()


def main() -> None:
    host = os.environ.get("HUB_HOST", os.environ.get("MEDIAPIPE_NOTIFY_HOST", "127.0.0.1"))
    raw_port = os.environ.get("HUB_PORT", os.environ.get("MEDIAPIPE_NOTIFY_PORT", "5000"))
    try:
        port = int(raw_port)
    except ValueError:
        port = 5000
    hub = NotificationHub(host=host, port=port)
    hub.start()
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        print("\n[Hub] Shutting down.")
        hub.close()


if __name__ == "__main__":
    main()
