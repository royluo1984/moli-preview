@echo off
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
if not exist "%ROOT%WindowTiler\bin\Release" mkdir "%ROOT%WindowTiler\bin\Release"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"%ROOT%WindowTiler\app.manifest" /out:"%ROOT%WindowTiler\bin\Release\MoliWindowTiler.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll "%ROOT%WindowTiler\AdaptiveUi.cs" "%ROOT%WindowTiler\Layout.cs" "%ROOT%WindowTiler\Native.cs" "%ROOT%WindowTiler\PositionStore.cs" "%ROOT%WindowTiler\SettingsStore.cs" "%ROOT%WindowTiler\HotkeySettings.cs" "%ROOT%WindowTiler\SwitcherOverlay.cs" "%ROOT%WindowTiler\TargetProfile.cs" "%ROOT%WindowTiler\TargetProfileDialog.cs" "%ROOT%WindowTiler\MainForm.cs" "%ROOT%WindowTiler\Program.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built: %ROOT%WindowTiler\bin\Release\MoliWindowTiler.exe
endlocal
