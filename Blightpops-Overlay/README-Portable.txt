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
Clicks and scrolling keep WoW's keyboard focus by default. Click WoW once
before using the overlay. Use the numeric arrow buttons or mouse wheel to
adjust options. There is no focus checkbox. File selection dialogs still work.
Collapsing the toolbar also closes the gear panel; reopen it with the cogwheel.
Shrinking the window switches to two-line mini cards automatically. At its
smallest height it displays one event; scroll to see earlier events. The gear
temporarily expands a small window so its settings remain usable.
The gear also has "Check for updates". A quiet check on startup compares this
EXE with the latest GitHub release. You choose whether to download and install
an available patch; the overlay then restarts. No updater install is needed.

OVERKILL DISPLAY (v1.0.0.5)

Lethal Erupt hits remain part of the hit count and full logged damage total.
Enable "Show overkill" in the gear to show overkill per target only in the
expanded hit dropdowns. No overkill line is added to the main event cards.
This option is off by default and your choice is saved between launches.
Turning it off hides the extra lines without changing damage or hit counts.
Overkill is already included in the damage numbers; do not add it again.
The extra line wraps on narrow windows and follows your number/text settings.
Events without overkill keep their existing layout. Blood Beast hits also
show this breakdown when the log records overkill.

The tracker also accepts final Erupt hits arriving after an encounter-end
marker, within the original Blightfall matching window. Damage parsing no
longer relies on a minimum row length to detect advanced logging.

RESIZE PREVIEW (v1.0.0.7)
Drag the lower-right triangle to preview a size using a transparent outline.
The window and events keep their current size until you release the mouse.
The preview shows normal/mini mode and dimensions of current event cards,
including open hit details. Release to apply; losing mouse capture cancels.
Escape also cancels when keyboard input is going to the overlay.
The scrollbar stops above the resize handle while unlocked. Locked windows
hide the handle and restore the scrollbar's full height. Event controls
outside the visible area update their layout when you scroll to them.

SPELL ICON TOGGLES (v1.0.0.9)
Open the cogwheel menu. Its right-hand column contains equal-size buttons:
Blood Beast, Soul Reaper, Festering Scythe. Click each to enable or disable
its events/marker. A red stop mark means disabled; a green line means enabled.
Blood Beast is now in this menu instead of the toolbar. Soul Reaper and
Festering Scythe marker settings are independent and saved between launches.