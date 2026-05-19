"""
Per-service processing hooks (gaze, face, YOLO object plane, mediapipe stub, bluetooth control).
"""

from __future__ import annotations

import os
import threading
from typing import Any, Dict, List, Optional, Set, Tuple

import numpy as np

try:
    import cv2
except ImportError:
    cv2 = None  # type: ignore

_yolo_model = None
_yolo_lock = threading.Lock()
_ble_scan_requested = False


def _decode_jpeg(jpeg: Optional[bytes]) -> Optional[np.ndarray]:
    if not jpeg or cv2 is None:
        return None
    arr = np.frombuffer(jpeg, dtype=np.uint8)
    return cv2.imdecode(arr, cv2.IMREAD_COLOR)


def _parse_target_classes() -> Set[str]:
    raw = os.environ.get("OBJECT_TARGET_CLASSES", "airplane").strip().lower()
    return {x.strip().lower() for x in raw.split(",") if x.strip()}


def _get_yolo():
    global _yolo_model
    if _yolo_model is not None:
        return _yolo_model
    with _yolo_lock:
        if _yolo_model is not None:
            return _yolo_model
        from ultralytics import YOLO

        weight_path = os.environ.get("YOLO_MODEL_PATH", "yolo11n.pt").strip()
        _yolo_model = YOLO(weight_path)
        return _yolo_model


def handle_gaze(header: Dict[str, Any], jpeg: Optional[bytes]) -> Dict[str, Any]:
    """Gaze estimation stub — returns center-normalized gaze."""
    frame = _decode_jpeg(jpeg)
    h, w = (480, 640) if frame is None else frame.shape[:2]
    return {"gaze_x": 0.5, "gaze_y": 0.5, "frame_size": [int(w), int(h)]}


def handle_face(header: Dict[str, Any], jpeg: Optional[bytes]) -> Dict[str, Any]:
    """Face detection / recognition stub."""
    frame = _decode_jpeg(jpeg)
    return {"faces": 0 if frame is None else 1, "stub": True}


def _yolo_predict(frame: np.ndarray, conf_th: float) -> Any:
    model = _get_yolo()
    try:
        imgsz = int(os.environ.get("YOLO_IMGSZ", "640"))
    except ValueError:
        imgsz = 640
    imgsz = max(320, min(1280, imgsz))
    return model.predict(frame, verbose=False, conf=conf_th, imgsz=imgsz)


def _parse_all_yolo_boxes(results: Any, frame: np.ndarray) -> List[Dict[str, Any]]:
    names = getattr(results[0], "names", {}) if results else {}
    boxes = results[0].boxes if results else None
    out: List[Dict[str, Any]] = []
    if boxes is None or not len(boxes):
        return out
    xyxy = boxes.xyxy.cpu().numpy()
    cls_ids = boxes.cls.cpu().numpy().astype(int)
    confs = boxes.conf.cpu().numpy()
    h, w = frame.shape[:2]
    for i in range(len(xyxy)):
        cid = int(cls_ids[i])
        label = str(names.get(cid, str(cid))).lower()
        conf = float(confs[i])
        x1, y1, x2, y2 = [float(v) for v in xyxy[i]]
        cx = ((x1 + x2) / 2.0) / max(w, 1)
        cy = ((y1 + y2) / 2.0) / max(h, 1)
        bw = (x2 - x1) / max(w, 1)
        bh = (y2 - y1) / max(h, 1)
        out.append(
            {
                "label": label,
                "confidence": conf,
                "bbox_norm": [cx, cy, bw, bh],
            }
        )
    return out


def _bbox_iou(a: List[float], b: List[float]) -> float:
    ax1 = a[0] - a[2] / 2.0
    ay1 = a[1] - a[3] / 2.0
    ax2 = a[0] + a[2] / 2.0
    ay2 = a[1] + a[3] / 2.0
    bx1 = b[0] - b[2] / 2.0
    by1 = b[1] - b[3] / 2.0
    bx2 = b[0] + b[2] / 2.0
    by2 = b[1] + b[3] / 2.0
    ix1 = max(ax1, bx1)
    iy1 = max(ay1, by1)
    ix2 = min(ax2, bx2)
    iy2 = min(ay2, by2)
    iw = max(0.0, ix2 - ix1)
    ih = max(0.0, iy2 - iy1)
    inter = iw * ih
    if inter <= 0.0:
        return 0.0
    area_a = max(1e-9, (ax2 - ax1) * (ay2 - ay1))
    area_b = max(1e-9, (bx2 - bx1) * (by2 - by1))
    return float(inter / (area_a + area_b - inter))


def _hand_dist(cx: float, cy: float, hand_xy: Optional[Tuple[float, float]]) -> float:
    if hand_xy is None:
        return 0.0
    return float(((cx - hand_xy[0]) ** 2 + (cy - hand_xy[1]) ** 2) ** 0.5)


def _bottle_hold_geometry_ok(
    cx: float,
    cy: float,
    hand_xy: Optional[Tuple[float, float]],
    *,
    for_cap: bool = False,
) -> bool:
    """Bottle near palm; cap blobs may sit above the wrist."""
    if hand_xy is None:
        return True
    if os.environ.get("TUIO_BOTTLE_HOLD_GEOMETRY", "1").strip().lower() in ("0", "false", "no"):
        return True
    hx, hy = hand_xy
    try:
        max_dx = float(os.environ.get("TUIO_BOTTLE_HOLD_MAX_DX", "0.30"))
        above = float(os.environ.get("TUIO_BOTTLE_HOLD_MAX_ABOVE", "0.30"))
        below = float(os.environ.get("TUIO_BOTTLE_HOLD_MAX_BELOW", "0.50"))
    except ValueError:
        max_dx, above, below = 0.30, 0.30, 0.50
    if abs(cx - hx) > max_dx * (1.15 if for_cap else 1.0):
        return False
    if for_cap:
        return cy <= hy + below
    if cy < hy - above:
        return False
    if cy > hy + below:
        return False
    return True


def _bottle_shape_ok(bw: float, bh: float) -> bool:
    area = bw * bh
    try:
        min_frac = float(os.environ.get("TUIO_BOTTLE_MIN_AREA_FRAC", "0.005"))
        max_frac = float(os.environ.get("TUIO_BOTTLE_MAX_AREA_FRAC", "0.35"))
    except ValueError:
        min_frac, max_frac = 0.005, 0.35
    if area < min_frac or area > max_frac:
        return False
    aspect = bh / max(bw, 1e-6)
    try:
        min_ar = float(os.environ.get("TUIO_BOTTLE_MIN_ASPECT", "0.35"))
        max_ar = float(os.environ.get("TUIO_BOTTLE_MAX_ASPECT", "5.5"))
    except ValueError:
        min_ar, max_ar = 0.35, 5.5
    return min_ar <= aspect <= max_ar


def bottle_control_norm_xy(cx: float, cy: float, bw: float, bh: float) -> Tuple[float, float]:
    """TUIO plane point: bottle cap / neck (top of box), not bbox center."""
    mode = os.environ.get("TUIO_BOTTLE_PLANE_AT", "cap").strip().lower()
    if mode in ("center", "middle", "bbox"):
        return max(0.0, min(1.0, cx)), max(0.0, min(1.0, cy))
    return max(0.0, min(1.0, cx)), max(0.02, min(0.98, cy - bh * 0.32))


def _plane_dict_from_bbox(
    cx: float,
    cy: float,
    conf: float,
    label: str,
    bbox: Optional[List[float]],
) -> Dict[str, Any]:
    if bbox is not None and len(bbox) >= 4:
        px, py = bottle_control_norm_xy(float(bbox[0]), float(bbox[1]), float(bbox[2]), float(bbox[3]))
    else:
        px, py = bottle_control_norm_xy(cx, cy, 0.08, 0.20)
    return {
        "cx": px,
        "cy": py,
        "confidence": conf,
        "label": label,
        "bbox_norm": bbox,
    }


def _is_hand_skeleton_false_positive(
    cx: float,
    cy: float,
    bw: float,
    bh: float,
    conf: float,
    hand_bbox_norm: Optional[List[float]],
    hand_xy: Optional[Tuple[float, float]],
) -> bool:
    """
    Reject YOLO boxes that are probably the bare hand/finger (MediaPipe mode, no bottle).
    A real bottle may overlap the hand bbox — do not reject those.
    """
    if hand_bbox_norm is None:
        return False
    bbox = [cx, cy, bw, bh]
    iou = _bbox_iou(bbox, hand_bbox_norm)
    area = bw * bh
    aspect = bh / max(bw, 1e-6)

    # Confident bottle-shaped box overlapping the hand → user is holding a bottle.
    if conf >= 0.32:
        return False
    if conf >= 0.24 and area >= 0.010 and aspect >= 0.50:
        return False

    try:
        fp_iou = float(os.environ.get("TUIO_BOTTLE_HAND_FP_IOU", "0.40"))
    except ValueError:
        fp_iou = 0.40
    if iou < fp_iou:
        return False

    # Tiny high-overlap blob on the hand skeleton = knuckle/finger mis-detect.
    if area < 0.011 and conf < 0.30:
        return True
    if hand_xy is not None:
        if _hand_dist(cx, cy, hand_xy) < 0.055 and conf < 0.26 and area < 0.016:
            return True
    return iou > 0.55 and conf < 0.24


def hand_bbox_norm_from_landmarks(lm: Any, *, pad: float = 0.03) -> Optional[List[float]]:
    """MediaPipe hand landmark list → normalized [cx, cy, w, h] for overlap checks."""
    if lm is None:
        return None
    try:
        xs = [float(p.x) for p in lm.landmark]
        ys = [float(p.y) for p in lm.landmark]
    except (AttributeError, TypeError):
        return None
    if not xs or not ys:
        return None
    p = max(0.0, min(0.12, float(pad)))
    x1 = max(0.0, min(xs) - p)
    x2 = min(1.0, max(xs) + p)
    y1 = max(0.0, min(ys) - p)
    y2 = min(1.0, max(ys) + p)
    bw = max(1e-4, x2 - x1)
    bh = max(1e-4, y2 - y1)
    return [(x1 + x2) / 2.0, (y1 + y2) / 2.0, bw, bh]


def _pick_best_bottle_candidate(
    detections: List[Dict[str, Any]],
    targets: Set[str],
    hand_xy: Optional[Tuple[float, float]],
    hand_bbox_norm: Optional[List[float]] = None,
) -> Tuple[Optional[Tuple[float, float, float, str]], Optional[List[float]]]:
    require_hand = os.environ.get("TUIO_BOTTLE_REQUIRE_NEAR_HAND", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )
    try:
        max_hand_dist = float(os.environ.get("TUIO_BOTTLE_HAND_MAX_DIST", "0.28"))
    except ValueError:
        max_hand_dist = 0.28
    try:
        min_conf = float(os.environ.get("TUIO_BOTTLE_MIN_CONF", "0.20"))
    except ValueError:
        min_conf = 0.20
    try:
        hand_min_conf = float(os.environ.get("TUIO_BOTTLE_HAND_MIN_CONF", "0.15"))
    except ValueError:
        hand_min_conf = 0.15
    try:
        no_hand_min_conf = float(os.environ.get("TUIO_BOTTLE_NO_HAND_MIN_CONF", "0.30"))
    except ValueError:
        no_hand_min_conf = 0.30
    try:
        face_band = float(os.environ.get("TUIO_BOTTLE_REJECT_TOP_FRAC", "0.22"))
    except ValueError:
        face_band = 0.22
    try:
        person_iou_reject = float(os.environ.get("TUIO_BOTTLE_PERSON_IOU_REJECT", "0.10"))
    except ValueError:
        person_iou_reject = 0.10
    person_boxes = [d["bbox_norm"] for d in detections if d.get("label") == "person"]
    best_score = -1.0
    best: Optional[Tuple[float, float, float, str]] = None
    best_bbox: Optional[List[float]] = None

    for d in detections:
        label = str(d.get("label", "")).lower()
        if label not in targets:
            continue
        conf = float(d.get("confidence", 0.0))
        bbox = d.get("bbox_norm")
        if not isinstance(bbox, (list, tuple)) or len(bbox) < 4:
            continue
        cx, cy, bw, bh = [float(v) for v in bbox[:4]]
        if not _bottle_shape_ok(bw, bh):
            continue
        if _is_hand_skeleton_false_positive(cx, cy, bw, bh, conf, hand_bbox_norm, hand_xy):
            continue
        dist = _hand_dist(cx, cy, hand_xy)
        near_hand = hand_xy is not None and dist <= max_hand_dist
        conf_floor = hand_min_conf if near_hand else min_conf
        if conf < conf_floor:
            continue
        person_overlap = False
        for pb in person_boxes:
            if _bbox_iou(bbox, pb) > person_iou_reject:
                person_overlap = True
                break
        # Reject body/shirt bottles unless very close to palm with decent confidence.
        if person_overlap:
            if not near_hand or conf < max(hand_min_conf + 0.08, 0.22):
                continue
        if hand_xy is not None:
            if require_hand and not near_hand:
                continue
            if not _bottle_hold_geometry_ok(cx, cy, hand_xy):
                continue
            conf_score = conf * max(0.05, 1.0 - dist / max(max_hand_dist, 1e-6))
        else:
            if require_hand:
                continue
            if conf < no_hand_min_conf:
                continue
            conf_score = conf
        if cy < face_band and (hand_xy is None or dist > max_hand_dist * 0.75):
            continue
        # Prefer the bottle nearest the palm (not the largest box in the room).
        if hand_xy is not None:
            score = conf_score / (1.0 + dist * 5.0)
        else:
            score = conf_score * ((bw * bh) ** 0.25)
        if score > best_score:
            best_score = score
            best = (cx, cy, conf, label)
            best_bbox = [cx, cy, bw, bh]

    return best, best_bbox


def _pick_largest_by_area(
    detections: List[Dict[str, Any]], targets: Set[str]
) -> Tuple[Optional[Tuple[float, float, float, str]], Optional[List[float]]]:
    best = None
    best_area = -1.0
    best_bbox: Optional[List[float]] = None
    for d in detections:
        label = str(d.get("label", "")).lower()
        if label not in targets:
            continue
        bbox = d.get("bbox_norm")
        if not isinstance(bbox, (list, tuple)) or len(bbox) < 4:
            continue
        cx, cy, bw, bh = [float(v) for v in bbox[:4]]
        area = bw * bh
        conf = float(d.get("confidence", 0.0))
        if area > best_area:
            best_area = area
            best = (cx, cy, conf, label)
            best_bbox = [cx, cy, bw, bh]
    return best, best_bbox


def _parse_results_to_detections(
    results: Any, frame: np.ndarray, targets: Set[str]
) -> Tuple[List[Dict[str, Any]], Optional[Tuple[float, float, float, str]], Optional[List[float]]]:
    out_detections = _parse_all_yolo_boxes(results, frame)
    best, best_bbox = _pick_largest_by_area(out_detections, targets)
    return out_detections, best, best_bbox


def detect_plane_in_bgr(
    frame: np.ndarray,
    conf_override: Optional[float] = None,
    targets_override: Optional[Set[str]] = None,
) -> Dict[str, Any]:
    """
    YOLO on a BGR frame; picks best box matching OBJECT_TARGET_CLASSES (default: airplane).
    Use ``bottle`` with ``TUIO_PLANE_SOURCE=bottle_hand``. Returns plane cx/cy in [0,1].
    """
    if frame is None or frame.size == 0:
        return {"detections": [], "plane": None, "active": False}

    targets = targets_override if targets_override is not None else _parse_target_classes()
    if conf_override is not None:
        conf_th = float(conf_override)
    else:
        conf_th = float(os.environ.get("OBJECT_CONF_THRESHOLD", "0.35"))
    try:
        results = _yolo_predict(frame, conf_th)
    except Exception as ex:
        return {"detections": [], "plane": None, "active": False, "error": str(ex)}

    out_detections, best, best_bbox = _parse_results_to_detections(results, frame, targets)
    if best is None:
        return {"detections": out_detections, "plane": None, "active": False}

    cx, cy, conf, label = best
    return {
        "detections": out_detections,
        "plane": {
            "cx": cx,
            "cy": cy,
            "confidence": conf,
            "label": label,
            "bbox_norm": best_bbox,
        },
        "active": True,
    }


def detect_water_bottle_cap_norm_xy(
    frame_bgr: np.ndarray,
    hand_xy: Optional[Tuple[float, float]] = None,
    hand_bbox_norm: Optional[List[float]] = None,
) -> Optional[Tuple[float, float, List[float]]]:
    """
    Fallback for clear plastic water bottles: blue cap near the hand (avoids white shirts).
    """
    if frame_bgr is None or cv2 is None or frame_bgr.size < 64:
        return None
    if hand_xy is None:
        return None
    h, w = frame_bgr.shape[:2]
    try:
        cap_max_hand_dist = float(os.environ.get("TUIO_BOTTLE_CAP_HAND_MAX_DIST", "0.22"))
    except ValueError:
        cap_max_hand_dist = 0.22
    blurred = cv2.GaussianBlur(frame_bgr, (9, 9), 0)
    hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, (100, 85, 65), (128, 255, 255))
    if os.environ.get("TUIO_BOTTLE_CAP_WHITE", "0").strip().lower() in ("1", "true", "yes"):
        white = cv2.inRange(hsv, (0, 0, 190), (180, 45, 255))
        mask = cv2.bitwise_or(mask, white)
    mask = cv2.erode(mask, None, iterations=1)
    mask = cv2.dilate(mask, None, iterations=2)
    cnts, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not cnts:
        return None

    best: Optional[Tuple[float, float, List[float], float]] = None
    min_a = float(os.environ.get("TUIO_BOTTLE_CAP_MIN_AREA", "80"))
    max_a = float(os.environ.get("TUIO_BOTTLE_CAP_MAX_AREA", "4500"))
    for c in cnts:
        area = float(cv2.contourArea(c))
        if area < min_a or area > max_a:
            continue
        x, y, bw, bh = cv2.boundingRect(c)
        cx = (x + bw / 2.0) / max(w, 1)
        cy = (y + bh / 2.0) / max(h, 1)
        bw_n = bw / max(w, 1)
        bh_n = bh / max(h, 1)
        if not _bottle_shape_ok(bw_n, bh_n):
            continue
        dist = _hand_dist(cx, cy, hand_xy)
        if dist > cap_max_hand_dist:
            continue
        if not _bottle_hold_geometry_ok(cx, cy, hand_xy, for_cap=True):
            continue
        cap_bbox = [cx, cy, bw_n, bh_n]
        if _is_hand_skeleton_false_positive(cx, cy, bw_n, bh_n, 0.55, hand_bbox_norm, hand_xy):
            continue
        score = area / (1.0 + dist)
        if best is None or score > best[3]:
            best = (cx, cy, cap_bbox, score)
    if best is None:
        return None
    cx, cy, bbox, _ = best
    return (max(0.0, min(1.0, cx)), max(0.0, min(1.0, cy)), bbox)


def detect_water_bottle_in_bgr(
    frame: np.ndarray,
    hand_xy: Optional[Tuple[float, float]] = None,
    hand_bbox_norm: Optional[List[float]] = None,
) -> Dict[str, Any]:
    """
    Water-bottle YOLO with false-positive filters (near hand, not on person/face/shirt).
  Optional cup fallback and blue-cap detection require ``hand_xy`` when enabled.
    """
    if frame is None or frame.size == 0:
        return {"detections": [], "plane": None, "active": False}

    try:
        conf = float(os.environ.get("OBJECT_CONF_THRESHOLD", "0.14"))
    except ValueError:
        conf = 0.14

    try:
        results = _yolo_predict(frame, conf)
    except Exception as ex:
        return {"detections": [], "plane": None, "active": False, "error": str(ex)}

    all_dets = _parse_all_yolo_boxes(results, frame)
    best, best_bbox = _pick_best_bottle_candidate(
        all_dets,
        {"bottle"},
        hand_xy,
        hand_bbox_norm=hand_bbox_norm,
    )
    if best is not None:
        cx, cy, cconf, label = best
        return {
            "detections": all_dets,
            "plane": _plane_dict_from_bbox(cx, cy, cconf, label, best_bbox),
            "active": True,
        }

    det: Dict[str, Any] = {"detections": all_dets, "plane": None, "active": False}

    allow_fb = os.environ.get("TUIO_BOTTLE_CLEAR_FALLBACK", "0").strip().lower() in (
        "1",
        "true",
        "yes",
    )
    if allow_fb and hand_xy is not None:
        fb_conf = max(0.12, conf - 0.04)
        try:
            results_fb = _yolo_predict(frame, fb_conf)
            fb_dets = _parse_all_yolo_boxes(results_fb, frame)
            best_fb, bbox_fb = _pick_best_bottle_candidate(
                fb_dets,
                {"cup", "wine glass"},
                hand_xy,
                hand_bbox_norm=hand_bbox_norm,
            )
            if best_fb is not None:
                cx, cy, cconf, _ = best_fb
                return {
                    "detections": all_dets + fb_dets,
                    "plane": _plane_dict_from_bbox(cx, cy, cconf, "bottle", bbox_fb),
                    "active": True,
                }
            det["detections"] = all_dets + fb_dets
        except Exception:
            pass

    cap_fb = os.environ.get("TUIO_BOTTLE_CAP_FALLBACK", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )
    cap_requires_yolo = os.environ.get("TUIO_BOTTLE_CAP_REQUIRES_YOLO", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )
    if cap_fb and hand_xy is not None:
        if cap_requires_yolo:
            try:
                yolo_hint = float(os.environ.get("TUIO_BOTTLE_CAP_YOLO_HINT", "0.12"))
            except ValueError:
                yolo_hint = 0.12
            has_bottle_hint = any(
                str(d.get("label", "")).lower() == "bottle"
                and float(d.get("confidence", 0.0)) >= yolo_hint
                for d in all_dets
            )
            if not has_bottle_hint:
                cap_fb = False
        if cap_fb:
            cap_hit = detect_water_bottle_cap_norm_xy(frame, hand_xy, hand_bbox_norm=hand_bbox_norm)
            if cap_hit is not None:
                cx, cy, bbox = cap_hit
                px, py = bottle_control_norm_xy(cx, cy, float(bbox[2]), float(bbox[3]))
                return {
                    "detections": det.get("detections", []),
                    "plane": {
                        "cx": px,
                        "cy": py,
                        "confidence": 0.55,
                        "label": "bottle",
                        "bbox_norm": bbox,
                        "source": "cap",
                    },
                    "active": True,
                }

    return det


def handle_object_detection(header: Dict[str, Any], jpeg: Optional[bytes]) -> Dict[str, Any]:
    """
    YOLO detection; picks best box whose class name matches OBJECT_TARGET_CLASSES (default: airplane).
    Use ``bottle`` for bottle-hand fusion (``TUIO_PLANE_SOURCE=bottle_hand`` on tuio_only_service).
    Returns normalized center cx, cy in [0,1] for TUIO plane fusion.
    """
    frame = _decode_jpeg(jpeg)
    if frame is None:
        return {"detections": [], "plane": None, "active": False}
    return detect_plane_in_bgr(frame)


def handle_mediapipe(header: Dict[str, Any], jpeg: Optional[bytes]) -> Dict[str, Any]:
    """Lightweight optional MediaPipe check on worker frame."""
    frame = _decode_jpeg(jpeg)
    if frame is None:
        return {"hands": False, "stub": True}
    try:
        import mediapipe as mp  # noqa: F401

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        return {"hands": False, "rgb_shape": list(rgb.shape), "stub": True}
    except Exception as ex:
        return {"error": str(ex), "stub": True}


def handle_bluetooth(header: Dict[str, Any], jpeg: Optional[bytes]) -> Dict[str, Any]:
    """
    Bluetooth control via pipeline control frames + stub scan/connect responses.

    Windows: prefer WinRT UWP Bluetooth APIs from Python via ``winrt`` (optional dependency).
    Cross-platform device discovery: ``bleak`` (optional).

    Commands (type=control): scan_start, scan_stop, status, connect:<address>, disconnect
    """
    global _ble_scan_requested

    if header.get("type") == "control":
        cmd = str(header.get("command", "")).strip().lower()
        if cmd == "scan_start":
            _ble_scan_requested = True
            devices = _bluetooth_scan_stub()
            return {"bluetooth": True, "command": cmd, "devices": devices, "stub": True}
        if cmd == "scan_stop":
            _ble_scan_requested = False
            return {"bluetooth": True, "command": cmd, "stub": True}
        if cmd == "status":
            return {
                "bluetooth": True,
                "command": cmd,
                "scan_requested": _ble_scan_requested,
                "note": "Use bleak/winrt for production BLE on Windows",
                "stub": True,
            }
        if cmd.startswith("connect:"):
            addr = cmd.split(":", 1)[1].strip()
            return {"bluetooth": True, "command": "connect", "address": addr, "stub": True, "connected": False}
        if cmd == "disconnect":
            return {"bluetooth": True, "command": cmd, "stub": True, "connected": False}

    return {"bluetooth": True, "note": "frame_processed_optional_stub", "stub": True}


def _bluetooth_scan_stub() -> List[Dict[str, str]]:
    """
    Stub scan result. For real BLE discovery on Windows install ``bleak`` and extend this
    to run an asyncio scan (see BleakScanner). Alternatively use WinRT UWP Bluetooth APIs
    via the ``winrt`` package from a dedicated async worker.
    """
    return []


HANDLERS = {
    "gaze": handle_gaze,
    "face": handle_face,
    "object": handle_object_detection,
    "mediapipe": handle_mediapipe,
    "bluetooth": handle_bluetooth,
}
