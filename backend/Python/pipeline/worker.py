"""
Run one processing service: TCP server that accepts a single client (main.py)
and processes framed JPEG messages.

Usage:
  python -m pipeline.worker --name gaze --port 5111
"""

from __future__ import annotations

import argparse
import os
import socket
import traceback

from pipeline.handlers import HANDLERS
from pipeline.protocol import FrameStreamReader, send_result


def run_worker(service_name: str, host: str, port: int) -> None:
    handler = HANDLERS.get(service_name)
    if handler is None:
        raise SystemExit(f"Unknown service name: {service_name}. Choose from {list(HANDLERS)}")

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(1)
    print(f"[{service_name}] Listening on {host}:{port} — start main.py to connect.")

    conn, addr = srv.accept()
    print(f"[{service_name}] main.py connected from {addr[0]}:{addr[1]}")
    conn.setblocking(True)
    reader = FrameStreamReader(conn)

    try:
        while True:
            try:
                header, jpeg = reader.read_one_message()
            except ConnectionError:
                break
            except Exception as ex:
                print(f"[{service_name}] frame read error:", repr(ex))
                continue

            seq = int(header.get("seq", 0))
            try:
                payload = handler(header, jpeg)
            except Exception:
                payload = {"error": traceback.format_exc()}
            try:
                send_result(conn, service=service_name, seq=seq, payload=payload)
            except OSError:
                break
    finally:
        try:
            conn.close()
        except OSError:
            pass
        srv.close()
        print(f"[{service_name}] Stopped.")


def main() -> None:
    p = argparse.ArgumentParser(description="Pipeline worker service")
    p.add_argument("--name", required=True, choices=sorted(HANDLERS.keys()))
    p.add_argument("--host", default=os.environ.get("PIPELINE_SERVICE_HOST", "127.0.0.1"))
    p.add_argument("--port", type=int, required=True)
    args = p.parse_args()
    run_worker(args.name, args.host, args.port)


if __name__ == "__main__":
    main()
