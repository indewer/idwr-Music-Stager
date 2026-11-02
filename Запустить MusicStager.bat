@echo off
set "EXE_PATH=%~dp0publish\idwr-Music-Stager-v0.1-b9.exe"
if not exist "%EXE_PATH%" (
    echo [ERROR] idwr-Music-Stager-v0.1-b9.exe not found
    pause
    exit /b 1
)
start "" "%EXE_PATH%"
exit