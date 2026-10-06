@echo off
setlocal
for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%I"
if not defined VS (
  echo MSVC x64 Build Tools required. 1>&2
  exit /b 1
)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
pushd "%~dp0"
cl /nologo /LD /O2 /MT /W4 /WX EditorBridge.cpp /link user32.lib psapi.lib /INCREMENTAL:NO /OUT:AomEditorBridge.dll
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
