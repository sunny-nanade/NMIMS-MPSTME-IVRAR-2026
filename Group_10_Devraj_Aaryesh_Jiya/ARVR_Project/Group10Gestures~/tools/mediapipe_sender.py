"""
Low-Latency MediaPipe Hand-Tracking UDP Sender
Course: IVRAR 2026 - Group 10
Transmits 21 3D hand landmarks over binary/JSON UDP socket to Unity.
"""

import cv2
import json
import os
import socket
import sys
import time
import urllib.request
import argparse
import numpy as np

# UDP Default Target
DEFAULT_IP = "127.0.0.1"
DEFAULT_PORT = 5005

# Official Google MediaPipe Hand Landmarker Model URL
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hand_landmarker.task")

# Landmark connection definitions for drawing hand skeleton
HAND_CONNECTIONS = [
    (0, 1), (1, 2), (2, 3), (3, 4),        # Thumb
    (0, 5), (5, 6), (6, 7), (7, 8),        # Index
    (5, 9), (9, 10), (10, 11), (11, 12),   # Middle
    (9, 13), (13, 14), (14, 15), (15, 16), # Ring
    (13, 17), (17, 18), (18, 19), (19, 20),# Pinky
    (0, 17)                                # Palm Base
]

def ensure_model_exists():
    """Download Google MediaPipe hand_landmarker.task model if missing."""
    if not os.path.exists(MODEL_PATH):
        print(f"[MediaPipe Sender] Downloading Hand Landmarker model from Google...")
        try:
            urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
            print(f"[MediaPipe Sender] Model successfully downloaded to {MODEL_PATH}")
        except Exception as e:
            print(f"[MediaPipe Sender] Error downloading model: {e}")
            raise e

def create_detector():
    """Initializes the MediaPipe hand detector supporting modern (1.x) and legacy (0.10.x) APIs."""
    # Try modern tasks API first
    try:
        ensure_model_exists()
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        
        base_options = python.BaseOptions(model_asset_path=MODEL_PATH)
        options = vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.VIDEO,
            num_hands=2,
            min_hand_detection_confidence=0.6,
            min_hand_presence_confidence=0.5,
            min_tracking_confidence=0.5
        )
        detector = vision.HandLandmarker.create_from_options(options)
        return "tasks", detector
    except Exception as e:
        print(f"[MediaPipe Sender] Tasks API not used: {e}. Trying solutions fallback...")

    # Legacy solutions API fallback
    try:
        import mediapipe as mp
        if hasattr(mp, 'solutions') and hasattr(mp.solutions, 'hands'):
            hands = mp.solutions.hands.Hands(
                static_image_mode=False,
                max_num_hands=2,
                min_detection_confidence=0.6,
                min_tracking_confidence=0.5
            )
            return "solutions", hands
    except Exception as e:
        print(f"[MediaPipe Sender] Solutions API error: {e}")

    raise RuntimeError("Unsupported MediaPipe installation: neither tasks nor solutions could be initialized.")

def draw_skeleton(frame, landmarks, color):
    """Draws 21 joints and connecting bones on the preview frame."""
    h, w, _ = frame.shape
    coords = []
    for lm in landmarks:
        cx = int(lm[0] * w)
        cy = int(lm[1] * h)
        coords.append((cx, cy))
        cv2.circle(frame, (cx, cy), 4, color, -1)
        
    for p1, p2 in HAND_CONNECTIONS:
        if p1 < len(coords) and p2 < len(coords):
            cv2.line(frame, coords[p1], coords[p2], color, 2)

def main():
    parser = argparse.ArgumentParser(description="MediaPipe Hand Tracking UDP Sender for Unity")
    parser.add_argument("--host", default=DEFAULT_IP, help="Target UDP IP (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="Target UDP port (default: 5005)")
    parser.add_argument("--camera", type=int, default=0, help="Webcam device index (default: 0)")
    parser.add_argument("--width", type=int, default=640, help="Capture width (default: 640)")
    parser.add_argument("--height", type=int, default=480, help="Capture height (default: 480)")
    args = parser.parse_args()

    print("=======================================================")
    print(" IVRAR 2026 - Group 10 Hand Tracking UDP Sender")
    print(f" Target: {args.host}:{args.port}")
    print(f" Camera: Index {args.camera} ({args.width}x{args.height})")
    print("=======================================================")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    api_type, detector = create_detector()
    print(f"[MediaPipe Sender] Using detector backend: {api_type}")

    cap = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY)
    if not cap.isOpened():
        cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"[Error] Could not open camera {args.camera}. Please verify connection.")
        return

    # Latency tweaks (set the format BEFORE the resolution):
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))  # compressed stream: more fps / less delay on most webcams
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)                            # don't queue stale frames
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    cap.set(cv2.CAP_PROP_FPS, 60)

    fps = 0.0
    proc_ms_avg = 0.0
    frame_count = 0
    start_time = time.time()
    packet_count = 0

    import mediapipe as mp

    try:
        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                print("[Warning] Frame grab failed, retrying...")
                time.sleep(0.01)
                continue

            # Mirror for natural selfie interaction
            frame = cv2.flip(frame, 1)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            current_time_ms = int(time.time() * 1000)

            hands_data = []
            proc_start = time.perf_counter()

            if api_type == "tasks":
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
                detection_result = detector.detect_for_video(mp_image, current_time_ms)
                
                if detection_result.hand_landmarks:
                    for i, hand_lms in enumerate(detection_result.hand_landmarks):
                        # Determine handedness label
                        label = "Right"
                        if detection_result.handedness and i < len(detection_result.handedness):
                            cat = detection_result.handedness[i][0]
                            # When mirrored, MediaPipe's raw label is flipped
                            label = "Left" if cat.category_name == "Right" else "Right"

                        lm_list = [[round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)] for lm in hand_lms]
                        hands_data.append({"label": label, "lm": lm_list})

                        color = (0, 255, 128) if label == "Right" else (255, 180, 0)
                        draw_skeleton(frame, lm_list, color)
                        # Label text near wrist
                        wx = int(lm_list[0][0] * frame.shape[1])
                        wy = int(lm_list[0][1] * frame.shape[0])
                        cv2.putText(frame, label, (wx - 20, wy - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)

            else: # solutions
                results = detector.process(rgb_frame)
                if results.multi_hand_landmarks:
                    for i, hand_lms in enumerate(results.multi_hand_landmarks):
                        label = "Right"
                        if results.multi_handedness and i < len(results.multi_handedness):
                            raw_label = results.multi_handedness[i].classification[0].label
                            label = "Left" if raw_label == "Right" else "Right"

                        lm_list = [[round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)] for lm in hand_lms.landmark]
                        hands_data.append({"label": label, "lm": lm_list})

                        color = (0, 255, 128) if label == "Right" else (255, 180, 0)
                        draw_skeleton(frame, lm_list, color)
                        wx = int(lm_list[0][0] * frame.shape[1])
                        wy = int(lm_list[0][1] * frame.shape[0])
                        cv2.putText(frame, label, (wx - 20, wy - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)

            # How long detection + landmark conversion took (part of the latency Unity reports)
            proc_ms = (time.perf_counter() - proc_start) * 1000.0
            proc_ms_avg = proc_ms if proc_ms_avg == 0.0 else 0.9 * proc_ms_avg + 0.1 * proc_ms

            # Send payload via UDP
            payload = {
                "t": current_time_ms,
                "hands": hands_data
            }
            json_bytes = json.dumps(payload).encode('utf-8')
            try:
                sock.sendto(json_bytes, (args.host, args.port))
                packet_count += 1
            except Exception as e:
                print(f"[UDP Warning] Send failed: {e}")

            # Calculate FPS
            frame_count += 1
            elapsed = time.time() - start_time
            if elapsed >= 1.0:
                fps = frame_count / elapsed
                frame_count = 0
                start_time = time.time()

            # Render overlay information
            cv2.rectangle(frame, (10, 10), (420, 85), (20, 20, 20), -1)
            cv2.rectangle(frame, (10, 10), (420, 85), (80, 80, 80), 1)
            cv2.putText(frame, f"IVRAR 2026 - Group 10 Sender", (18, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1)
            cv2.putText(frame, f"FPS: {fps:.1f} | Hands: {len(hands_data)} | Detect: {proc_ms_avg:.0f} ms", (18, 52), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
            cv2.putText(frame, f"Packets: {packet_count} -> {args.host}:{args.port}", (18, 74), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (180, 180, 180), 1)

            cv2.imshow("Group 10 - MediaPipe Hand Tracking (Press 'q' to quit)", frame)
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q') or key == 27:
                print("[MediaPipe Sender] Quitting by user request.")
                break

    finally:
        cap.release()
        cv2.destroyAllWindows()
        sock.close()
        print("[MediaPipe Sender] Clean shutdown complete.")

if __name__ == "__main__":
    main()
