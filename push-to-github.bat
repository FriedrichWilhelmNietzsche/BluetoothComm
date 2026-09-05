@echo off
setlocal enabledelayedexpansion
echo ========================================
echo   BluetoothComm - Push to GitHub
echo ========================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Git not found. Install from: https://git-scm.com/download/win
    pause
    exit /b 1
)

cd /d "%~dp0"

if not exist .git (
    echo [1/5] Initializing Git repository...
    git init
    git branch -M main
) else (
    echo [1/5] Git repository already exists
)

echo.
set /p GIT_NAME=Enter GitHub username: 
set /p GIT_EMAIL=Enter GitHub email: 
git config user.name "!GIT_NAME!"
git config user.email "!GIT_EMAIL!"
echo [2/5] Git user configured.

echo.
echo [3/5] Adding files...
git add -A

echo [4/5] Committing...
git commit -m "Initial commit: BluetoothComm MAUI app" --allow-empty

git remote get-url origin >nul 2>nul
if errorlevel 1 (
    echo.
    echo Create empty repo first: https://github.com/new
    set /p REPO_URL=Enter GitHub repo URL: 
    git remote add origin "!REPO_URL!"
)

echo.
echo [5/5] Pushing to GitHub...
git push -u origin main

echo.
echo ========================================
echo   Done! Check Actions tab on GitHub.
echo ========================================
pause
