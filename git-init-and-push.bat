@echo off
cd /d "H:\AAIAGitHub\aaia-module-manager"
git add .
git commit -m "fix: force Node.js 24 for GitHub Actions"
git push origin main
echo === Done! ===
pause
