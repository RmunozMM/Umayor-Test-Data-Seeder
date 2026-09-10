@echo off
REM Thin wrapper so double-clicking installs without a manual PowerShell ExecutionPolicy change.
REM Same pattern as DataverseMasterDataMigrator's own installer.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Umayor-Test-Data-Seeder.ps1" -SourceFolder "%~dp0bin" %*
pause
