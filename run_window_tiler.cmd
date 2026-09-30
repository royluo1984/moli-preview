@echo off
setlocal
set "EXE=%~dp0WindowTiler\bin\Release\MoliWindowTiler.exe"
if not exist "%EXE%" call "%~dp0build_window_tiler.cmd"
if exist "%EXE%" start "" "%EXE%"
endlocal
