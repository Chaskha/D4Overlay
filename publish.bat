@echo off
setlocal enableextensions
rem ------------------------------------------------------------------------------
rem  Fabrique les livrables dans dist\ :
rem
rem    publish.bat          -> les deux :
rem                            dist\D4Overlay-portable-<version>.zip   (zip portable, necessite le .NET 8 Desktop Runtime)
rem                            dist\D4Overlay-Setup-<version>.exe      (installeur, .NET embarque : rien a installer avant)
rem    publish.bat zip      -> seulement le zip portable
rem    publish.bat setup    -> seulement l'installeur (necessite Inno Setup : https://jrsoftware.org/isinfo.php)
rem
rem  Si Inno Setup est installe ailleurs que dans les dossiers habituels : set ISCC_PATH=C:\chemin\vers\ISCC.exe
rem ------------------------------------------------------------------------------

rem  A garder identique a Version dans Shared\AppInfo.cs (le script le verifie).
set VERSION=1.0.0

set MODE=%~1
if "%MODE%"=="" set MODE=all
set DO_ZIP=0
set DO_SETUP=0
if /i "%MODE%"=="all"   set DO_ZIP=1& set DO_SETUP=1
if /i "%MODE%"=="zip"   set DO_ZIP=1
if /i "%MODE%"=="setup" set DO_SETUP=1
if "%DO_ZIP%%DO_SETUP%"=="00" goto :usage

findstr /c:"Version = \"%VERSION%\"" Shared\AppInfo.cs >nul || echo ATTENTION : la version %VERSION% de ce script ne correspond pas a Shared\AppInfo.cs

if exist dist rmdir /s /q dist
mkdir dist

if "%DO_ZIP%"=="1" call :zip || goto :fail
if "%DO_SETUP%"=="1" call :setup || goto :fail

echo.
echo === Termine. Livrables dans le dossier dist : ===
dir /b dist\*.zip dist\*.exe 2>nul
exit /b 0

:usage
echo Usage : publish.bat [all ^| zip ^| setup]
exit /b 1

:fail
echo.
echo ECHEC.
exit /b 1

rem ---------- publication (%1 = dossier de sortie, %2 = true/false : .NET embarque ou non) ----------
:publish
dotnet publish Overlay\D4WorldBossOverlay.csproj -c Release -r win-x64 --self-contained %2 -o %1 || exit /b 1
dotnet publish Config\D4OverlayConfig.csproj -c Release -r win-x64 --self-contained %2 -o %1 || exit /b 1
rem Reglages prepares (alerts.json, layout.json, skins\, sounds\, fonts\) : copies chez l'utilisateur au premier lancement.
if exist Packaging\defaults xcopy /e /i /y Packaging\defaults %1\defaults >nul
copy /y Packaging\LISEZMOI.txt %1\ >nul
copy /y Packaging\README.txt %1\ >nul
copy /y Packaging\LICENSES.txt %1\ >nul
exit /b 0

rem ---------- zip portable ----------
:zip
call :publish dist\portable\D4Overlay false || exit /b 1
powershell -NoProfile -Command "Compress-Archive -Path 'dist\portable\D4Overlay\*' -DestinationPath 'dist\D4Overlay-portable-%VERSION%.zip' -Force" || exit /b 1
exit /b 0

rem ---------- installeur ----------
:setup
call :findiscc
if not defined ISCC (
    echo.
    echo Inno Setup est introuvable. Installe-le (gratuit : https://jrsoftware.org/isdl.php^) ou definis ISCC_PATH.
    exit /b 1
)
echo Inno Setup : %ISCC%
call :publish dist\setup-src\D4Overlay true || exit /b 1
"%ISCC%" /DAppVersion=%VERSION% Installer\D4Overlay.iss || exit /b 1
exit /b 0

:findiscc
set ISCC=
if defined ISCC_PATH if exist "%ISCC_PATH%" set ISCC=%ISCC_PATH%
if not defined ISCC for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" "%ProgramFiles%\Inno Setup 7\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe") do if not defined ISCC if exist %%P set ISCC=%%~P
if not defined ISCC for /f "delims=" %%I in ('where iscc 2^>nul') do if not defined ISCC set ISCC=%%I
exit /b 0
