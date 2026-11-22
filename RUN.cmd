@echo off
REM Nova Taskbar - executa a tarefa atual de scripts\next.ps1 e grava o log em docs\last-run.log
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\next.ps1" > "%~dp0docs\last-run.log" 2>&1
echo FIM >> "%~dp0docs\last-run.log"
