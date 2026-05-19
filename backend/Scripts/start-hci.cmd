@echo off
setlocal
set BACKEND_ROOT=%~dp0..
set PY=%BACKEND_ROOT%\Python
set PYTHONPATH=%PY%
rem Optional: copy Python\.env.example and set variables here.
if not defined MAIN_CAPTURE_INDEX set MAIN_CAPTURE_INDEX=4
if not defined TUIO_CAPTURE_INDEX set TUIO_CAPTURE_INDEX=2
if not defined FACE_CAPTURE_INDEX set FACE_CAPTURE_INDEX=4
if not defined CAMERA_CAPTURE_BACKEND set CAMERA_CAPTURE_BACKEND=dshow

powershell -ExecutionPolicy Bypass -File "%~dp0start-hci.ps1"
