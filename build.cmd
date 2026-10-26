@echo off
rem Roda os testes e gera "dist\VALORANT CFG.exe" (um arquivo so). Precisa do .NET SDK so para compilar.
setlocal
cd /d "%~dp0"
set "EXE=VALORANT CFG.exe"
dotnet run --project tests -c Release || exit /b 1
dotnet build src -c Release -o build\bin || exit /b 1
if not exist dist mkdir dist
rem exe aberto nao pode ser sobrescrito, mas pode ser renomeado; sobras fechadas (e o nome antigo) sao apagadas
del /q "dist\*.old*.exe" "dist\ValCfgSync*.exe" 2>nul
if exist "dist\%EXE%" move /y "dist\%EXE%" "dist\VALORANT CFG.old%RANDOM%.exe" >nul
copy /y "build\bin\%EXE%" "dist\%EXE%" >nul || exit /b 1
echo.
echo Pronto: dist\%EXE%
