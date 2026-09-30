BLIGHTFALL POPS — NATIVE WINDOWS APP TEST BUILD

1. Extract the whole folder to a location you control, such as Documents.
2. Double-click "Build Once.cmd" once to create BlightfallPops.exe using the
   Windows .NET Framework compiler. If Windows says the compiler is missing,
   install the .NET Framework 4.8 Developer Pack and try again.
   Keep BlightfallPops.ico in this folder while building: the build embeds the
   character's skull portrait as the executable and window icon.
   Keep the Assets folder beside Build Once.cmd while building. The eight
   spell icons are embedded in the EXE; after building, you can distribute
   the EXE by itself. Recipients do not need this script, the assets, or admin.
3. Double-click BlightfallPops.exe from then on. It does not start PowerShell
   and does not request administrator rights. Pick your existing
   World of Warcraft\_retail_\Logs\WoWCombatLog.txt on first launch.
4. Keep /combatlog enabled in WoW. Windowed (Fullscreen) lets this overlay
   remain above the game. No WoW addon install is needed.
   Blightfall and its DP/VP Erupt hits are recognized by spell ID, so German
   and English spell names in the combat log both work.

CONTROLS
The folder button changes logs; the circular arrow starts a fresh session.
The top icon row reads Mini, Options, Log reader, Refresh, Folder, Lock,
Collapse, Minimize, Close from left to right. Mini switches to two-line cards:
event icon, name, hit count and total on the first line; DP/VP or CB/BiL icon
and damage plus the Scythe or Blightfall status icon on the second. Click a
mini damage icon or its number to expand individual hits; click it again to
close them. Mini DP/CB icons line up with the event title; VP/BiL icons line
up with the hit count above. The Blood Beast icon toggles Blood Beast cards and shows a red
prohibition mark when they are hidden. The padlock locks movement and resizing.
The small corner triangle resizes. The chevron hides the toolbar while keeping
the dark window cover at its current size; the events move up to fill the
available space. Collapsing also closes the gear panel. Click the 10-pixel
strip on top to show controls again, and
the events move back below the toolbar. The narrow dark scrollbar and mouse
wheel also work while locked. Click DP, VP, CB, or BiL to reveal individual hits and
targets. Expanded hits show a yellow "Crit" label for critical hits and fit
long enemy names on a second line when needed. DP and VP (or CB and BiL) stay side by side even in a narrow window;
the small status icon uses its own line only when needed. Blightfall cards show
their Erupt hit count just left of the total damage, which aligns to the same
right edge as Blood Beast damage. The larger Blightfall and
Blood Beast icons are centered in their event headers. The gear changes
Blightfall matching time, text size, spell icon size,
number format, and whether Blood Beast shows the small Blightfall icon.
"Overlay stays on top" is enabled by default. Turn it off when the
overlay is on a second monitor for OBS window capture so it does not cover
other windows. Keep the app open; minimizing may stop OBS window capture.
The choice is saved between launches.
Clicks and scrolling keep WoW's keyboard focus by default. Click WoW once
before using the overlay. Use the numeric arrow buttons or mouse wheel to
adjust options. There is no focus checkbox. File selection dialogs still work.
The log-page icon pauses/resumes the overlay's reader. A green check shows the
file grew recently, gray means idle or missing (not proof that /combatlog is
off), and red pause means the overlay reader is off. Resuming reads missed
lines. This control does not toggle WoW's /combatlog command. Its setting is
saved across launches.
The Soul Reaper checkbox shows the Soul Reaper icon to the left of Festering
Scythe on Blightfall cards, including mini cards. It tracks your Soul Reaper
enemy debuff (spell ID 1241521) for eight seconds after application or refresh;
the badge gets a red X when no enemy has that debuff at Blightfall cast. Hover
for the number of affected targets. This setting is saved. The ability's spell
ID 343294 is different from the debuff ID. The Soul Reaper artwork you provided
is embedded in the executable when you run Build Once.cmd.
The cogwheel also has "Beasts first, then Blightfall": when unchecked, cards
follow their original combat-log order; when checked, each type forms its own
chronological group. Mini mode and grouping are saved between launches.
Scythe and Blood Beast's small Blightfall icon show a red X if absent; hover
over them for details. Window placement and choices save
under your own %LOCALAPPDATA%\BlightfallPopsDesktop directory.

This is a genuine WinForms executable compiled from C# source, independent
from the older PowerShell version. The first build is a one-time command
window; opening the resulting executable has no console window.

FOR PUBLIC DOWNLOADS
Upload the repository with its .github/workflows/build-windows.yml file to
GitHub. In the Actions tab, "Build portable Windows overlay" can build a
ready-to-run ZIP manually. For a patch, change the version in VersionInfo.cs,
commit and push the changes, then push a matching version tag such as v1.0.0.5.
The workflow rejects a tag that differs from the compiled EXE and publishes
BlightfallPops.zip as a public GitHub Release asset. The running overlay checks
the latest release on startup and from the cogwheel's "Check for updates"
button. It asks before downloading and restarts after installing. Keep the
asset name BlightfallPops.zip for later patches. People download that ZIP and run the EXE;
they do not build the program. README-Portable.txt contains their instructions.

The Erupt match still uses a time window and source GUID. Other abilities
that trigger Erupt in the same interval may be included; do not interpret the
totals as guaranteed to be caused by Blightfall. Blood is Life damage is
attributed to the most recently summoned own Blood Beast, as in the overlay.
This version uses a plain dark window; the portrait art is not yet ported.
Please test on Windows and send any build errors or a screenshot of the UI.

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
