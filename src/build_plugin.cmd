@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe"
"%CSC%" /nologo /target:winexe /win32icon:"%~dp0LogSpiral.ico" /out:"%~dp0LogSpiralFitter.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%~dp0LogSpiralFitter.vb"
if errorlevel 1 exit /b 1
echo Built: %~dp0LogSpiralFitter.exe
