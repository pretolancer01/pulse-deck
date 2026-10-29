@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
if "%AMDRMMONITORSDKPATH%"=="" set "AMDRMMONITORSDKPATH=C:\Program Files\AMD\RyzenMasterMonitoringSDK"
cl /nologo /std:c++17 /EHsc /LD /MT /I"%AMDRMMONITORSDKPATH%\include" "%~dp0..\src\PulseDeck\Native\PulseDeckRyzenBridge.cpp" /link /OUT:"%~dp0..\src\PulseDeck\Native\PulseDeckRyzenBridge.dll"
exit /b %errorlevel%
