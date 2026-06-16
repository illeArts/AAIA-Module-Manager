@echo off
setlocal EnableDelayedExpansion
cd /d "H:\AAIAGitHub\aaia-module-manager"

set PROJECT=src\AAIA.ModuleManager\AAIA.ModuleManager.csproj
set VER=2.3.1

echo.
echo  ================================================
echo   AAIA Module Manager v%VER% - Multi-Platform Build
echo  ================================================
echo.

:: ── restore ──────────────────────────────────────────────────────────────────
echo [0/4]  dotnet restore ...
dotnet restore %PROJECT%
if errorlevel 1 ( echo FEHLER: restore & pause & exit /b 1 )

:: ── win-x64 ──────────────────────────────────────────────────────────────────
echo.
echo [1/4]  win-x64 ...
dotnet publish %PROJECT% ^
    -c Release -f net8.0-windows -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish\win-x64
if errorlevel 1 ( echo FEHLER: win-x64 & pause & exit /b 1 )
echo OK win-x64

:: ── linux-x64 ────────────────────────────────────────────────────────────────
echo.
echo [2/4]  linux-x64 ...
dotnet publish %PROJECT% ^
    -c Release -f net8.0 -r linux-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish\linux-x64
if errorlevel 1 ( echo FEHLER: linux-x64 & pause & exit /b 1 )
echo OK linux-x64

:: ── linux-arm64 ──────────────────────────────────────────────────────────────
echo.
echo [3/4]  linux-arm64 ...
dotnet publish %PROJECT% ^
    -c Release -f net8.0 -r linux-arm64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish\linux-arm64
if errorlevel 1 ( echo FEHLER: linux-arm64 & pause & exit /b 1 )
echo OK linux-arm64

:: ── osx-arm64 (wird wahrscheinlich nicht funktionieren) ──────────────────────
echo.
echo [4/4]  osx-arm64 (optional, kein Fehler-Abbruch) ...
dotnet publish %PROJECT% ^
    -c Release -f net8.0 -r osx-arm64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish\osx-arm64
if errorlevel 1 (
    echo WARNUNG: osx-arm64 fehlgeschlagen ^(erwartet^), weiter ...
) else (
    echo OK osx-arm64
)

:: ── git commit + push ─────────────────────────────────────────────────────────
echo.
echo  Committe und pushe ...
git add -A
git commit -m "release: v2.3.1 - Marketplace-Erweiterung (Account-Link, Download, Browse)"
git push

echo.
echo  ================================================
echo   v%VER% Build + Push fertig!
echo  ================================================
pause
