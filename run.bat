@echo off
setlocal
pushd "%~dp0"
if not exist "Kalkulace.Api.dll" (
    echo Run this script from artifacts/kalkulace-release or an extracted kalkulace-release.zip. 1>&2
    popd
    exit /b 1
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo Install the ASP.NET Core 9 or 10 runtime to run this app. 1>&2
    popd
    exit /b 1
)
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://localhost:5080"
dotnet Kalkulace.Api.dll %*
set "result=%ERRORLEVEL%"
popd
exit /b %result%
