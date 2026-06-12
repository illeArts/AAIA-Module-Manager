@echo off
chcp 65001 >nul
cd /d "H:\AAIAGitHub\aaia-module-manager"

echo.
echo === AAIA Module Manager - Git Setup + Push ===
echo.

echo Removing broken .git folder...
rmdir /s /q .git 2>nul
echo Done.

echo.
echo Initializing git repo...
git init -b main
git config user.email "illedmx@gmail.com"
git config user.name "Andre Iljaschow"

echo.
echo Setting remote...
git remote add origin https://github.com/illeArts/AI-Module-Manager-.git

echo.
echo Staging all files...
git add .

echo.
echo Committing...
git commit -m "feat: macOS support + README updated" -m "- csproj: TargetFramework and OutputType OS-conditional (net8.0-windows / net8.0)" -m "- csproj: BuiltInComInteropSupport and ApplicationManifest Windows-only" -m "- AppConfig: platform-aware config path and default paths" -m "- IdeDetectionService: macOS paths for VS Code and Rider; removed Microsoft.Win32" -m "- SetupTabViewModel: brew install gh on macOS; gh auth logout for credentials reset" -m "- ProcessRunner: RunPsAsync uses /bin/sh on macOS; added RunShellAsync" -m "- HelpWindow: updated help texts for cross-platform" -m "- README: macOS installation, requirements and build instructions added"

echo.
echo Pushing to GitHub...
git push -u origin main

echo.
echo === Done! ===
pause
