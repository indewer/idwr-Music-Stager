@echo off
chcp 65001 >nul
title (idwr) Music Stager Launcher

set "TARGET_DIR=%~dp0publish"
set "LATEST_EXE="

for /f "delims=" %%F in ('dir /b /o:n "%TARGET_DIR%\idwr-Music-Stager-*.exe" 2^>nul') do (
    set "LATEST_EXE=%TARGET_DIR%\%%F"
)

if not defined LATEST_EXE (
    for /f "delims=" %%F in ('dir /b /o:n "%TARGET_DIR%\*.exe" 2^>nul') do (
        set "LATEST_EXE=%TARGET_DIR%\%%F"
    )
)

if not defined LATEST_EXE (
    echo [ОШИБКА] Файл idwr-Music-Stager-*.exe не найден в папке publish!
    pause
    exit /b 1
)

start "" "%LATEST_EXE%"
exit
