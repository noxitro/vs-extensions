@echo off
rem Build all extensions (Release) and collect .vsix files into artifacts\vsix\
where pwsh > nul 2>&1
if %errorlevel% == 0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
)
set result=%errorlevel%
echo.
if not %result% == 0 echo Build failed. exit code %result%
if "%~1" == "" pause
exit /b %result%
