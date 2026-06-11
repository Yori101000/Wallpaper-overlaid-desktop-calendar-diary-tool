@echo off
cd /d "%~dp0"
set "APP_EXE=%~dp0bin\Debug\net9.0-windows\TransparentCalendar.exe"
set "PROJECT_FILE="

for %%F in ("%~dp0*.csproj") do (
  if defined PROJECT_FILE (
    echo Expected exactly one .csproj file in "%~dp0".
    exit /b 1
  )
  set "PROJECT_FILE=%%~fF"
)

if not defined PROJECT_FILE (
  echo Expected exactly one .csproj file in "%~dp0".
  exit /b 1
)

if not exist "%APP_EXE%" (
  dotnet build "%PROJECT_FILE%"
  if errorlevel 1 exit /b %errorlevel%
)

start "" "%APP_EXE%"
