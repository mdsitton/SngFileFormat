cd Server
dotnet publish /p:DebugType=None -c Release --self-contained -r win-x64 --output .\bin\build\win-x64
dotnet publish /p:DebugType=None -c Release --self-contained -r linux-x64 --output .\bin\build\linux-x64
@REM dotnet publish /p:DebugType=None -c Release --self-contained -r osx-x64 --output .\bin\build\osx-x64
pause