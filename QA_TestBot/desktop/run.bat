@echo off
REM Quick-launch QA TestBot Desktop (development build)
setlocal
set "ROOT=%~dp0..\.."
set "EXE=%ROOT%\QA_TestBot\desktop\bin\Release\net8.0-windows\QA.TestBot.Desktop.exe"

if not exist "%EXE%" (
    echo [BUILD] Chua co ban build - dang build...
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet-sdk"
    if exist "%DOTNET_ROOT%\dotnet.exe" (
        "%DOTNET_ROOT%\dotnet.exe" build "%ROOT%\QA_TestBot\desktop\QA.TestBot.Desktop.csproj" -c Release -v minimal
    ) else (
        echo [ERROR] Khong tim thay .NET SDK 8.0
        pause
        exit /b 1
    )
)

if not exist "%EXE%" (
    echo [ERROR] Build that bai.
    pause
    exit /b 1
)

start "" "%EXE%"
endlocal
