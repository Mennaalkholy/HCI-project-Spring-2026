"""
Frame orchestrator: captures from the **main** camera (e.g. Camo / phone as index 2)
and sends each compressed frame to every registered pipeline worker over **separate TCP sockets**.

Optional: relays YOLO plane poses to NotificationHub for TUIO fusion (HUB_PLANE_RELAY).

Start workers first, then run:
  python main.py

Env:
  MAIN_CAPTURE_INDEX   — OpenCV camera index for this pipeline (default: 4, DSHOW on Windows)
  CAMERA_FALLBACK_INDICES — comma list tried after preferred (optional)
  MAIN_WAIT_FOR_CAMERA — 1 = retry until a camera appears
  MAIN_ALLOW_NO_CAMERA — 1 (default) = stream blank frames if no camera (keeps workers alive)
  MAIN_SKIP_CAMERA — 1 = never open a capture (always synthetic). Use with one physical webcam so
    face_auth_service.py can monopolize the device (Windows often allows only one consumer per camera).
  PIPELINE_JPEG_QUALITY — 1-100 (default: 75)
  GAZE_PORT, FACE_PORT, OBJECT_PORT, MEDIAPIPE_PORT, BLUETOOTH_PORT — set to 0 to skip a service
  HUB_PLANE_RELAY — 1 to forward object worker plane results to hub (default: 1)
  HUB_HOST, HUB_PORT — hub for plane relay (defaults 127.0.0.1:5000)
  CAMERA_MIRROR_HORIZONTAL — 1 (default): mirror each frame before workers / preview (matches inverted webcam).
"""

from __future__ import annotations

import json
import os
import queue
import socket
import sys
import threading
import time
from typing import Any, Dict, List, Optional, Tuple

_script_dir = os.path.dirname(os.path.abspath(__file__))
if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

import cv2

from camera_utils import (
    default_capture_index,
    mirror_frame_horizontal_if_enabled,
    open_capture_with_fallback,
    synthetic_frame_generator,
)
from pipeline.protocol import send_framed_jpeg


def _env_port(key: str, default: int) -> int:
    try:
        return int(os.environ.get(key, str(default)))
    except ValueError:
        return default


def _default_ports() -> List[Tuple[str, int]]:
    return [
        ("gaze", _env_port("GAZE_PORT", 5111)),
        ("face", _env_port("FACE_PORT", 5112)),
        ("object", _env_port("OBJECT_PORT", 5113)),
        ("mediapipe", _env_port("MEDIAPIPE_PORT", 5114)),
        ("bluetooth", _env_port("BLUETOOTH_PORT", 5115)),
    ]


def _connect_workers(host: str, ports: List[Tuple[str, int]]) -> Dict[str, socket.socket]:
    socks: Dict[str, socket.socket] = {}
    for name, port in ports:
        if port <= 0:
            print(f"[main] Skip service '{name}' (port disabled)")
            continue
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(30.0)
        try:
            s.connect((host, port))
            s.setblocking(True)
            socks[name] = s
            print(f"[main] Connected to {name} at {host}:{port}")
        except OSError as ex:
            print(f"[main] Could not connect {name} @ {port}: {ex}")
            try:
                s.close()
            except OSError:
                pass
    return socks


def _recv_results_loop(name: str, sock: socket.socket, q: "queue.Queue[Dict[str, Any]]") -> None:
    buf = bytearray()
    try:
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            buf.extend(chunk)
            while b"\n" in buf:
                idx = buf.index(b"\n")
                line = bytes(buf[:idx])
                del buf[: idx + 1]
                line = line.strip()
                if not line:
                    continue
                try:
                    obj = json.loads(line.decode("utf-8"))
                    if isinstance(obj, dict):
                        q.put({"from": name, "msg": obj})
                except json.JSONDecodeError:
                    continue
    except OSError:
        pass
    finally:
        q.put({"from": name, "closed": True})


def _hub_plane_relay_enabled() -> bool:
    return os.environ.get("HUB_PLANE_RELAY", "1").strip().lower() not in ("0", "false", "no")


def _make_hub_client() -> Optional[Any]:
    if not _hub_plane_relay_enabled():
        return None
    host = os.environ.get("HUB_HOST", os.environ.get("PIPELINE_SERVICE_HOST", "127.0.0.1"))
    try:
        port = int(os.environ.get("HUB_PORT", "5000"))
    except ValueError:
        port = 5000
    try:
        from hub_client import HubServiceClient

        client = HubServiceClient("pipeline", host=host, port=port, on_command=lambda _m: None)
        client.connect()
        print(f"[main] Hub plane relay active → {host}:{port} as service 'pipeline'")
        return client
    except Exception as ex:
        print("[main] Hub plane relay disabled (hub not reachable):", ex)
        return None


_last_plane_hub_sig: Optional[Tuple[Any, ...]] = None


def _forward_plane_to_hub(hub: Any, msg: Dict[str, Any]) -> None:
    global _last_plane_hub_sig
    if msg.get("type") != "result" or msg.get("service") != "object":
        return
    payload = msg.get("payload")
    if not isinstance(payload, dict):
        return
    plane = payload.get("plane")
    active = bool(payload.get("active"))
    if not active or not isinstance(plane, dict):
        sig: Tuple[Any, ...] = ("inactive",)
        if sig == _last_plane_hub_sig:
            return
        _last_plane_hub_sig = sig
        hub.send_event(
            {
                "fanout": "python_peers",
                "forward_csharp": True,
                "event": "plane_pose",
                "active": False,
                "seq": int(msg.get("seq", 0)),
            }
        )
        return
    cx = round(float(plane.get("cx", 0.5)), 4)
    cy = round(float(plane.get("cy", 0.5)), 4)
    conf = round(float(plane.get("confidence", 0.0)), 4)
    lbl = str(plane.get("label", ""))
    sig = ("active", cx, cy, conf, lbl)
    if sig == _last_plane_hub_sig:
        return
    _last_plane_hub_sig = sig
    hub.send_event(
        {
            "fanout": "python_peers",
            "forward_csharp": True,
            "event": "plane_pose",
            "active": True,
            "cx": cx,
            "cy": cy,
            "confidence": conf,
            "label": lbl,
            "seq": int(msg.get("seq", 0)),
        }
    )


def main() -> None:
    host = os.environ.get("PIPELINE_SERVICE_HOST", "127.0.0.1")
    cam_index = default_capture_index()
    wait_cam = os.environ.get("MAIN_WAIT_FOR_CAMERA", "0").strip().lower() in ("1", "true", "yes")
    allow_no_cam = os.environ.get("MAIN_ALLOW_NO_CAMERA", "1").strip().lower() not in ("0", "false", "no")
    synth_fps = float(os.environ.get("MAIN_SYNTHETIC_FPS", "15"))
    jpeg_q = int(os.environ.get("PIPELINE_JPEG_QUALITY", "75"))
    preview = os.environ.get("MAIN_PREVIEW", "1").strip().lower() not in ("0", "false", "no")
    ports = _default_ports()
    workers = _connect_workers(host, ports)
    if not workers:
        print("[main] No workers connected — start pipeline workers first.")
        sys.exit(1)

    hub_client = _make_hub_client()

    result_q: queue.Queue[Dict[str, Any]] = queue.Queue()
    threads: List[threading.Thread] = []
    for name, sock in workers.items():
        t = threading.Thread(target=_recv_results_loop, args=(name, sock, result_q), daemon=True)
        t.start()
        threads.append(t)

    skip_cam = os.environ.get("MAIN_SKIP_CAMERA", "0").strip().lower() in ("1", "true", "yes")
    cap = None
    opened_idx = -1
    if not skip_cam:
        cap, opened_idx = open_capture_with_fallback(
            preferred_index=cam_index,
            label="main",
            auto_fallback=True,
            wait_forever=wait_cam,
        )
    synth_gen = None
    used_idx = opened_idx
    if skip_cam:
        synth_gen = synthetic_frame_generator(640, 480)
        used_idx = -1
        print(
            "[main] MAIN_SKIP_CAMERA=1 — not opening any device; streaming synthetic frames "
            f"at {synth_fps} FPS (leave the real camera for face_auth / another app)."
        )
    elif cap is None and allow_no_cam:
        synth_gen = synthetic_frame_generator(640, 480)
        used_idx = -1
        print(
            "[main] No camera — streaming synthetic blank frames at "
            f"{synth_fps} FPS (MAIN_ALLOW_NO_CAMERA=1). Connect Camo or set CAMERA_FALLBACK_INDICES."
        )
    elif cap is None:
        print(f"[main] Cannot open camera (preferred index {cam_index}). Set MAIN_ALLOW_NO_CAMERA=1 to continue.")
        if hub_client is not None:
            hub_client.close()
        sys.exit(1)

    print(f"[main] Camera index {used_idx}; streaming to {list(workers.keys())}")

    seq = 0
    frame_interval = 1.0 / max(synth_fps, 1.0)
    try:
        while True:
            if cap is not None:
                ok, frame = cap.read()
                if not ok or frame is None:
                    print("[main] Frame grab failed — retrying")
                    time.sleep(0.05)
                    continue
            else:
                frame = next(synth_gen)  # type: ignore
                ok = True
                time.sleep(frame_interval)

            frame = mirror_frame_horizontal_if_enabled(frame)

            ok_enc, enc = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), jpeg_q])
            if not ok_enc:
                continue
            blob = enc.tobytes()
            seq += 1
            meta = {"ts": time.time(), "cam": int(used_idx)}

            for name, sock in workers.items():
                try:
                    send_framed_jpeg(sock, seq=seq, jpeg=blob, extra=meta)
                except OSError as ex:
                    print(f"[main] send to {name} failed:", ex)

            while True:
                try:
                    item = result_q.get_nowait()
                except queue.Empty:
                    break
                if item.get("closed"):
                    print(f"[main] Worker stream closed: {item.get('from')}")
                    continue
                m = item.get("msg")
                if isinstance(m, dict) and m.get("type") == "result":
                    if hub_client is not None:
                        _forward_plane_to_hub(hub_client, m)
                    if os.environ.get("MAIN_LOG_RESULTS", "0") == "1":
                        print(json.dumps(m, ensure_ascii=False))

            if preview:
                cv2.imshow("main.py pipeline (Camo / MAIN_CAPTURE_INDEX)", frame)
                if cv2.waitKey(1) == ord("q"):
                    break
    finally:
        if cap is not None:
            cap.release()
        if preview:
            cv2.destroyAllWindows()
        for sock in workers.values():
            try:
                sock.close()
            except OSError:
                pass
        if hub_client is not None:
            hub_client.close()
        print("[main] Exit.")


if __name__ == "__main__":
    main()
