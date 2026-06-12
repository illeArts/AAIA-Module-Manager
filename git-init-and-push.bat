@echo off
chcp 65001 >nul
cd /d "H:\AAIAGitHub\aaia-module-manager"
git add .
git commit -m "fix: add .gitattributes"
git push origin main
echo === Done! ===
pause
