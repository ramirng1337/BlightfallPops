BLIGHTFALL POPS — WINDOWS OVERLAY

1. Double-click BlightfallPops.exe. No build script or administrator rights are needed.
2. Choose your World of Warcraft combat log when prompted. The file is normally
   under World of Warcraft\_retail_\Logs\ and starts with WoWCombatLog.
3. Enable /combatlog in game and leave the overlay running alongside WoW.
   Windowed (Fullscreen) lets the overlay stay visible above the game.

The folder button lets you change combat logs. The refresh button starts a new
session. The gear opens display options; window size and settings are saved
under %LOCALAPPDATA%\BlightfallPopsDesktop for each Windows user.
The small log-page icon in the toolbar pauses or resumes the overlay's log
reader. A green check means the selected file grew recently; a gray line means
no recent lines (which can simply mean you are between fights); red pause means
the overlay is paused. Pausing keeps your current cards and resuming reads the
missed lines. This button does not switch WoW's /combatlog command on or off:
use /combatlog in game for that. The overlay cannot reliably identify WoW's
logging state from an idle file. The reader setting is saved between launches.
"Overlay stays on top" is enabled by default for gameplay on the same
screen. Switch it off in the gear for OBS capture on a second monitor. Keep
the window open: minimizing may stop OBS window capture.
"Keep WoW active" lets you click the overlay without moving keyboard focus
away from WoW. Turn it on in the gear, then click WoW once. The overlay still
receives the mouse click; WoW keeps keyboard movement. Turn it off to type in
the overlay's numeric option fields. Your choice is saved.
Collapsing the toolbar also closes the gear panel; reopen it with the cogwheel.
Shrinking the window switches to two-line mini cards automatically. At its
smallest height it displays one event; scroll to see earlier events. The gear
temporarily expands a small window so its settings remain usable.
The gear also has "Check for updates". A quiet check on startup compares this
EXE with the latest GitHub release. You choose whether to download and install
an available patch; the overlay then restarts. No updater install is needed.

This is a Windows desktop overlay that reads a local combat log. It is not a
WoW addon and does not install into the game's AddOns folder. The EXE contains
the spell art and character icon; no other files are needed to run it.

Source code and more detailed usage notes are in the public project repository.
For each new release, update VersionInfo.cs to match the GitHub tag (for example,
1.0.0.4 for v1.0.0.4), run Build Once.cmd, and attach a BlightfallPops.zip that
contains the newly built BlightfallPops.exe. This keeps update checks accurate.
