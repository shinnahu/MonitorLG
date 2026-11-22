@echo off
rem Compila con el csc.exe de .NET Framework 4.x que viene con Windows.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:dist\MonitorLG.exe src\MonitorLG.cs || exit /b 1
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /resource:dist\MonitorLG.exe,MonitorLG.exe /out:dist\MonitorLGSetup.exe src\Setup.cs || exit /b 1
echo Listo: dist\MonitorLGSetup.exe
