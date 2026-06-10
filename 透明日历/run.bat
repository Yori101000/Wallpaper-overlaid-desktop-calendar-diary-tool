@echo off
cd /d "%~dp0"
set "APP_EXE=%~dp0bin\Debug\net9.0-windows\TransparentCalendar.exe"

if not exist "%APP_EXE%" (
  dotnet build "%~dp0透明日历.csproj"
)

start "" "%APP_EXE%"
