@echo off
setlocal
if "%KSP_MANAGED%"=="" set "KSP_MANAGED=C:\Games\Kerbal Space Program\KSP_x64_Data\Managed"
msbuild Source\RealisticDocking\RealisticDocking.csproj /t:Rebuild /p:Configuration=Release /p:KSPManaged="%KSP_MANAGED%"
endlocal
