# Keyboard-only input-board labels

Game-action label presets now leave an element's label unchanged when its action bindings are null or empty. A keyboard-only key does not implicitly become the enum's first action, Left. Elements with real action bindings still use their first action for the selected preset.

## Native acceptance
Open Control Display options, Edit layout, Add key. Set Label F1, Keyboard keys F1 and Y120; leave game-action Binding Unbound. Apply Short, Names, Keyboard and Arrows independently, restoring the test label before each baseline attempt. The custom label must remain F1, while the ordinary Right action becomes R, Right, D and > respectively. Inspect both the native preview and selected element fields.

QA173 baseline changes F1 into L, Left, A and < while the physical F1 binding remains unchanged. This is editor/rendered-label acceptance, not a claim that F1 dispatch or every keyboard layout was exercised. Remove the added key and restore the Keyboard preset and all original display settings afterward.

Regression tests cover all four presets, empty/null action bindings, keyboard-only and entirely unbound custom labels, null elements/collections, unknown action values, and preservation of binding-list identity and coordinates. They supplement the native editor test.
