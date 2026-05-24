@echo off
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%MyBrowserShell\bin\Debug\net8.0-windows"
set "EXE=%OUT%\MyBrowserShell.exe"

if not exist "%EXE%" (
  echo Building MyBrowserShell...
  dotnet build "%ROOT%MyBrowserShell\MyBrowserShell.csproj" -c Debug
  if errorlevel 1 exit /b 1
)

cd /d "%OUT%"
"%EXE%"do
