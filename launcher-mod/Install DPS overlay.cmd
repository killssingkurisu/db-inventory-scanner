@echo off
setlocal
rem Installs the DPS overlay into the Dungeon Blitz: R launcher, using the launcher's own
rem executable to run the installer (no other software needed). Close the game first.
set "APPDIR=%LOCALAPPDATA%\Programs\Dungeon Blitz R"
if not "%~1"=="" set "APPDIR=%~1"
if not exist "%APPDIR%\Dungeon Blitz R.exe" (
  echo Couldn't find the launcher in "%APPDIR%".
  echo Drag the launcher's folder onto this file, or pass it as the first argument.
  pause
  exit /b 1
)
set ELECTRON_RUN_AS_NODE=1
"%APPDIR%\Dungeon Blitz R.exe" "%~dp0install\install.js" --app-dir "%APPDIR%"
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%
