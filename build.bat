@echo off
echo Building ResSwitcher as a single WinExe...
dotnet publish -c Release
echo.
echo Done! You can find the executable in:
echo bin\Release\net8.0-windows\win-x64\publish\ResSwitcher.exe
pause
