# Control Display command label-style verification (#273)

The console layout preset operation must apply the currently selected label style, just like the native layout preset button. It must not silently reset style metadata or leave raw builder labels behind.

Native comparison: enable the Everest console through Mod Options. In Control Display, choose Short labels then Presets → Compact keyboard. Inspect the eight labels X/U/C/L/D/R/G/J. Run `akron_control_display preset compact` through the actual console. Confirm its status reports Short and then inspect the board/editor: the same Short labels must remain. QA175 baseline reports Short but produces Dash/W/Demo/A/S/D/Grab/Jump. Restore Keyboard and Debug Off afterward. No recording or reflective game-state injection is required.

The repair reapplies the selected label preset only after a successful command layout selection. Invalid presets return first; explicit reset retains its existing Keyboard/default policy. Automated coverage checks five builders across four styles, structural preservation and source routing/reset policy. These diagnostics supplement native command acceptance rather than replacing it.
