@echo off
setlocal EnableDelayedExpansion

:: ============================================================
::  AAIA Module Manager - Build + Installer
::  Aufruf: build-installer.bat
::  Erzeugt: installer\dist\AAIA_ModuleManager_v2.3.0_Setup.exe
:: ============================================================

set PROJECT=..\src\AAIA.ModuleManager\AAIA.ModuleManager.csproj
set PUBLISH_DIR=..\publish\win-x64
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo.
echo  ==============================================
echo       AAIA Module Manager - Build Pipeline
echo  ==============================================
echo.

:: Schritt 1: dotnet restore
echo [1/3]  dotnet restore ...
dotnet restore %PROJECT%
if errorlevel 1 (
    echo FEHLER: dotnet restore fehlgeschlagen.
    pause & exit /b 1
)

:: Schritt 2: dotnet publish (Self-Contained, Single-File)
echo.
echo [2/3]  dotnet publish  ^(Release / win-x64 / self-contained^) ...
dotnet publish %PROJECT% ^
    -c Release ^
    -f net8.0-windows ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o %PUBLISH_DIR%

if errorlevel 1 (
    echo FEHLER: dotnet publish fehlgeschlagen.
    pause & exit /b 1
)

:: Schritt 3: Inno Setup Compiler
echo.
echo [3/3]  Inno Setup Compiler ...

if not exist %ISCC% (
    echo FEHLER: Inno Setup nicht gefunden unter %ISCC%
    echo Bitte Inno Setup 6 installieren: https://jrsoftware.org/isinfo.php
    pause & exit /b 1
)

if not exist dist mkdir dist
%ISCC% setup.iss
if errorlevel 1 (
    echo FEHLER: Inno Setup Compiler fehlgeschlagen.
    pause & exit /b 1
)

:: Fertig
echo.
echo  ================================================
echo   Installer fertig:
echo   installer\dist\AAIA_ModuleManager_v2.3.0_Setup.exe
echo  ================================================
echo.
pause
