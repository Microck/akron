# Set Inventory Jumps persistence contract

Resolves issue #266 through its documented-exception option. Set Inventory changes Air Jumps settings; it does not install a chapter-scoped temporary jump inventory. Positive Jumps enables finite extra jumps, zero disables Air Jumps. Chapter restart does not reset these settings. Optional Restore on Death keeps its pre-click snapshot behavior.

## Native baseline QA108
Native Celeste1.4.0.0/Everest1.6580.0, package6787c562660c8393fee3995df8407d55880873dd79c9bc8b00ec52df65dcc204. Initial Air Jumps Off/Infinite/1 survives a control native Restart Chapter. Configure Set Inventory Dashes1/Jumps3/RestoreOff without Set: Air Jumps unchanged. One Set changes it to On/finite/3. A second real native Restart Chapter retains On/finite/3 in first and later popup observations. The old footer falsely promised map defaults. No airborne extra-jump execution or dash-budget restoration is claimed. Original settings were restored and native Exit0 recorded.

## Corrected copy QA111
Package d0ce0a647bceaea0c5ef66ca100ce5739791046226973b06951816e46f54f02a,2407 native-reference Release and Debug tests passed. In the running game, the RestoreOff footer displays the complete two-line warning that chapter restart does not reset Air Jumps settings. Numeric Jumps help and row help fully wrap and explain persistence. RestoreOn preserves the snapshot-on-apply footer and death-restore tooltip. Restore was toggled back Off; no Set action was performed in this layout-only run. Native Save and Quit/Exit0, before/after settings retained.

Only text, help, command status explanation and documentation change. Existing status keys and inventory/death lifecycle behavior are preserved. This intentionally documents the exception rather than claiming a new chapter-scoped runtime restoration feature. The earlier native baseline establishes the lifetime behavior; the corrected native run establishes visible copy/layout, not a new gameplay effect.
