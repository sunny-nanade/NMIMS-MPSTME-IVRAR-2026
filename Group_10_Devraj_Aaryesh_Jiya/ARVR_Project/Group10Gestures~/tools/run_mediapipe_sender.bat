@echo off
echo ===============================================================
echo  IVRAR 2026 - Group 10 MediaPipe Hand Tracking UDP Sender
echo ===============================================================
echo Starting webcam stream to Unity at 127.0.0.1:5005...
echo Press 'q' in the preview window to stop.
echo.

cd /d "%~dp0"
py mediapipe_sender.py
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Fallback: trying python command directly...
    python mediapipe_sender.py
)
pause
