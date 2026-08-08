@echo off
setlocal

cd /d "%~dp0"

taskkill /f /im ResSwitcher9000.exe >nul 2>nul

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: .NET SDK was not found.
    echo Install the .NET 8 SDK, then run this file again.
    pause
    exit /b 1
)

echo Building ResSwitcher9000 v0.2.1...

dotnet publish "ResSwitcher9000.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "publish\win-x64"

if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Build complete:
echo %CD%\publish\win-x64\ResSwitcher9000.exe
echo.
pause

endlocal