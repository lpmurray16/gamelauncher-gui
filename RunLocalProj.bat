@echo off
setlocal
set "appDir=%~dp0src\GameLauncher\bin\Debug\net10.0-windows"
set "appExe=%appDir%\GameLauncher.exe"

if not exist "%appExe%" (
    echo No local Debug build found. Run RunBuild.bat first.
    pause
    exit /b 1
)

start "" /D "%appDir%" "%appExe%"
if errorlevel 1 (
    echo Could not start Game Launcher.
    pause
    exit /b 1
)
exit /b 0
