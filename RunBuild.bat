@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 (
    echo Could not open the project folder.
    pause
    exit /b 1
)

dotnet build GameLauncher.slnx --configuration Debug
set "buildResult=%errorlevel%"
popd

echo.
if "%buildResult%"=="0" (
    echo Build complete. Use RunLocalProj.bat to launch the app.
) else (
    echo Build failed. See the errors above.
)
pause
exit /b %buildResult%
