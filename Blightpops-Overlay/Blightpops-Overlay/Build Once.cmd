@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The Windows .NET Framework compiler was not found.
  echo Install .NET Framework 4.8 Developer Pack, then run this file again.
  if /i not "%~1"=="--ci" pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:BlightfallPops.ico /out:BlightfallPops.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /resource:Assets\inv_nullstone_shadow.jpg,BlightfallPops.Icons.inv_nullstone_shadow.jpg /resource:Assets\inv12_ability_deathknight_empowereddreadplague.jpg,BlightfallPops.Icons.inv12_ability_deathknight_empowereddreadplague.jpg /resource:Assets\ability_creature_disease_02.jpg,BlightfallPops.Icons.ability_creature_disease_02.jpg /resource:Assets\inv_polearm_2h_mawnecromancerboss_d_01_darkblue.jpg,BlightfallPops.Icons.inv_polearm_2h_mawnecromancerboss_d_01_darkblue.jpg /resource:Assets\ability_deathknight_soulreaper.jpg,BlightfallPops.Icons.ability_deathknight_soulreaper.jpg /resource:Assets\achievement_nazmir_boss_bloodofghuun.jpg,BlightfallPops.Icons.achievement_nazmir_boss_bloodofghuun.jpg /resource:Assets\inv_artifact_bloodoftheassassinated.jpg,BlightfallPops.Icons.inv_artifact_bloodoftheassassinated.jpg /resource:Assets\ability_ironmaidens_corruptedblood.jpg,BlightfallPops.Icons.ability_ironmaidens_corruptedblood.jpg BlightfallPops.cs Updater.cs VersionInfo.cs
if errorlevel 1 (
  echo Build failed. Send the error text for a fix.
  if /i not "%~1"=="--ci" pause
  exit /b 1
)
echo BlightfallPops.exe is ready. Double-click that executable from now on.
if /i not "%~1"=="--ci" pause
