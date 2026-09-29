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
The top icon row reads Mini, Blood Beast, Options, Log reader, Refresh, Folder, Lock,
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
"Keep WoW active" allows clicking and scrolling the overlay without taking
keyboard focus from WoW. Turn it on in the gear, then click WoW once so it is
the active game window. Keyboard movement should then continue while you use
the overlay mouse controls. The click belongs to the overlay, not the game;
mouse look is unavailable while the cursor is over the overlay. Turn this
setting off to type into the overlay's numeric option boxes. It is saved.
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
commit and push the changes, then push a matching version tag such as v1.0.0.4.
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
