# Dash direction preset axes (#229)

The installed Extended Variants provider reads its matrix as `[Sign(Y)+1][Sign(X)+1]`. Akron's preset builder previously wrote its conceptual `(x,y)` points as `[x][y]`, transposing non-symmetric presets. The correction writes `[y][x]` in the local Allow helper only.

Horizontal, Vertical, No Up and No Down now use the provider's row/column convention. All, Cardinal, Diagonal and None are transpose-symmetric and unchanged. Custom `matrix:` parsing and serialization remain row-major and untouched; existing custom values do not silently change meaning. The provider's default center value and Akron All's center value remain unchanged.

## Native baseline

QA150 uses real Celeste/Everest gameplay in City room1 from grounded `(19,144)`, with a fresh dash, and the same input-only sequence in each trial: 60 neutral, 1 Up+Dash, 14 Up, 45 neutral. Holding Up during startup is essential. Default allows actual upward travel with StDash and logical SpeedY−240; Horizontal incorrectly allows the same upward dash; Vertical incorrectly blocks it. None blocks the dash, including direct capture of the press frame. Reset restores upward travel. The complete observed baseline is allow/allow/block/block/allow. Actual Madeline/trail and HUD progression are inspected, not the golden follower or final grounded position alone. Recorded acquisition gaps are retained as limits.

Horizontal was configured while Off, then its row explicitly enabled. A setup-only master toggle was corrected before the Horizontal trial; screenshot07 confirms master On before recording. Vertical and None were applied while On. No gameplay is attributed to the temporary master-Off setup state.

## Automated and source checks

Release and Debug each pass 2,521 tests. Eight preset matrices are checked against the provider's Y/X indexing, plus three asymmetric custom-matrix round trips. Read-only source review found no blocking regression.

## Corrected-package native acceptance

Pending. The matching live matrix and controls must pass before reporting the correction verified. Upward native trials alone do not establish every direction or both No Up/No Down gameplay cases.
