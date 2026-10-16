# Whole import and pending Set Inventory restore

Issue #262. The fix discards a stale Set Inventory restore snapshot only after a successful Whole import commits with Restore on Death Off. The supplied session is used directly. Failed imports retain the snapshot and original settings; unrelated scopes and Whole Restore On preserve prior behavior. The change is deliberately outside generic settings application, which is also used for scoped imports and rollback.

## Native baseline QA112
On native Linux Celeste1.4.0.0/Everest1.6580.0, baseline package d0ce0a647bceaea0c5ef66ca100ce5739791046226973b06951816e46f54f02a. A genuine native Whole export contains Air Jumps Off/finite/Extra7, Set Inventory Dashes1/Jumps0/RestoreOff and no StartPos entries. Before arming, Extra3 is configured; one native Set with Jumps1/RestoreOn captures it. Real Zenity accepts the exact exported archive. Imported RestoreOff/Jumps0 and Extra7 are visibly installed while the Set Inventory tooltip still says On. Native pause-menu Retry and full respawn then leave Extra3. A matched rearm/import followed by explicit checkbox On→Off without Set leaves Extra7 after Retry.

Archive SHA256 acd20d316809a480411ce4dd091804a5eee3eb27519fcc5c60ea2a952db8e513; CRC valid, manifest.json/setup.json only. Success toasts were not captured; native chooser acceptance and changed imported values establish the observed path. No extra-jump execution, dash budget or internal getter is claimed. An initial accidental Dash verticals toggle was corrected before export. A transient UI transport disconnect before the paired disarm applied no input; fresh state was inspected before resuming. Original preferences restored, native Exit0.

## Automated regression scope
2414 native-reference Release and Debug tests pass. Seven new cases cover committed Whole Restore Off/On, Keybinds/Audio/HUD/StartPos preservation, and metadata-persistence failure preserving snapshot identity and settings. These tests supplement genuine gameplay checks rather than replace them.


## Native candidate QA113
Exact package95e53afa8ccf93cc7bc5170dfb453a13ab28a2a839b1fd15219271f72ed30d33, identical genuine baseline archive copied unchanged. From finite Extra3, native Set Jumps1/RestoreOn arms restoration. Whole import visibly applies RestoreOff/Jumps0 and Extra7. Without touching Restore afterward, native Retry/respawn retains Extra7.

Scoped guard: reconfigure Extra3 and Set Jumps1/RestoreOn, then import the same archive with Keybinds scope. Restore remains On and current extra jumps remain1. Because the initial unchanged-bind import lacked a captured success toast, the guard was strengthened while still armed: change Cursor Zoom hold from LeftAlt to Q through its Bind UI, repeat the Keybinds import, capture the explicit Imported Keybinds toast and restored LeftAlt binding, then native Retry. Extra jumps returns to pre-click3, showing unrelated import preserved functional death restoration. Missed popup clicks after the repeated chooser were a desktop-focus/window-position issue; AltTab restored the focused game before inspecting binding and performing Retry. They are not scored as mod failures.

This is visible native settings/lifecycle acceptance, not airborne-jump execution. Original inventory/jump settings and Whole scope were restored before native Save and Quit.
