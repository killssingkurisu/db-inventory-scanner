@echo off
setlocal
rem Removes the DPS overlay from the Dungeon Blitz: R launcher. Close the game first.
set "APPDIR=%LOCALAPPDATA%\Programs\Dungeon Blitz R"
if not "%~1"=="" set "APPDIR=%~1"
if not exist "%APPDIR%\Dungeon Blitz R.exe" (
  echo Couldn't find the launcher in "%APPDIR%".
  pause
  exit /b 1
)
set ELECTRON_RUN_AS_NODE=1
"%APPDIR%\Dungeon Blitz R.exe" "%~dp0install\install.js" --app-dir "%APPDIR%" --uninstall
set ERR=%ERRORLEVEL%
echo.
pause
exit /b %ERR%
