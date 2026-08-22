@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Phat hanh Verba

echo.
echo ========================================
echo           PHAT HANH VERBA
echo ========================================
echo.
echo [1] Phat hanh ban cap nhat
echo [2] Phat hanh lan dau
echo [3] Chi build va dong goi (khong upload)
echo [Q] Thoat
echo.

choice /C 123Q /N /M "Chon: "
set "VERBA_CHOICE=%ERRORLEVEL%"

if "%VERBA_CHOICE%"=="4" exit /b 0

set "VERBA_RELEASE_ARGS="
if "%VERBA_CHOICE%"=="2" set "VERBA_RELEASE_ARGS=-FirstRelease"
if "%VERBA_CHOICE%"=="3" set "VERBA_RELEASE_ARGS=-FirstRelease -AllowDirtyWorkingTree -WhatIf"

if not "%VERBA_CHOICE%"=="3" (
    if not defined VERBA_GITHUB_TOKEN if not exist "%~dp0.env" (
        echo.
        echo LOI: Khong tim thay VERBA_GITHUB_TOKEN hoac file .env.
        echo Hay copy .env.example thanh .env va dien token vao do.
        pause
        exit /b 1
    )
)

echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-release.ps1" %VERBA_RELEASE_ARGS%
set "VERBA_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%VERBA_EXIT_CODE%"=="0" (
    echo PHAT HANH THAT BAI. Hay kiem tra loi ben tren.
) else if "%VERBA_CHOICE%"=="3" (
    echo BUILD VA DONG GOI THANH CONG. Khong co gi duoc upload.
) else (
    echo PHAT HANH THANH CONG.
)
echo.
pause
exit /b %VERBA_EXIT_CODE%
