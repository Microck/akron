# Binding context feedback

Row context menus use the existing effective binding formatter for the Binding line, preserving custom menu keys, editable native defaults and the overlay toggle's special binding. Built-in descriptions remain separately labeled. Feature values still belong in feature settings/tooltips, not the Binding line.

## Native acceptance
- Search Screen Shake Intensity and configure2 while Off. Open its binding context with right-click or supported Shift-click, choose Bind input and press/release Q.
- Reopen context. Binding must display Q; Keybinds overview must agree.
- Close the overlay, press/release Q, reopen and search again. Verify On/Current2. Binding must still display Q.
- Repeat Q to Off/Current1. Clear binding and verify no staleQ remains. Reset the variant and restore the external variants master Off.
- Check a built-in/default-bound row still shows its actual binding and the separately labeled built-in information.

QA169 baseline shows a workingQ in Keybinds but Binding:Off|2 orOn|2 in context. This is feedback failure, not dispatch failure. Some desktop right-click attempts did not open the menu; supported Shift-click did. Native screenshots establish rendered behavior; the compiled wiring regression only ensures the renderer calls the binding formatter rather than the feature-value formatter.
