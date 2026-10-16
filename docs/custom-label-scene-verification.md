# Literal custom labels outside levels (#211)

An anchored custom label is gated outside a Level only when its template reads a level-only variable. Literal words such as Room, map, speed or stamina do not themselves read game state.

The classifier follows the formatter's brace boundaries and supported variable lookup operands. Conditional literal branches, extra function arguments, configuration keys, unknown expressions and unsupported nested function syntax are not scanned as word lists. For nested braces that can construct another expression during formatter output rescanning, a conservative identifier guard is retained. This change does not redefine the template language or promise complete symbolic evaluation of nested-brace forms. Absolute-position/anchor exceptions remain unchanged.

Native test: enable outside scenes via the actual `akron_hud_labels outside on` console command; turn Labels Visible on; create visible TopLeft, nonabsolute, Always labels containing Practice and Room practice. With the overlay closed, both must render inside a level and after Save and Quit to the main menu. QA177 baseline renders both inside Prologue but hides Room practice outside while retaining Practice. The saved scored profile confirms all three rendering gates are enabled. Existing room/player variable labels should remain gated outside.

Delete only the temporary labels, restore Labels Visible off, custom labels off, outside scenes off, selected label and Debug off. Verify final settings against the original profile. Automated dependency cases supplement this live rendering test.
