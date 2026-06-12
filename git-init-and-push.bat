@echo off
cd /d "H:\AAIAGitHub\aaia-module-manager"
git add .
git commit -m "feat: macOS builds in publish workflow (arm64 + x64)"
git push origin main
echo === Done! ===
pause
