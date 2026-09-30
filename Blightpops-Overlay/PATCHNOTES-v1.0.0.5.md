## Blightfall Pops v1.0.0.5

- Fixed final Erupt hits being missed when logged just after an encounter-end marker, within the configured matching window.
- Improved detection of advanced damage rows with shorter optional tails.
- Added an optional **Show overkill** toggle in the gear, off by default and saved between launches. Overkill appears only in expanded hit dropdowns; the main normal/mini cards keep their compact layout.
- Full logged damage and hit counts include lethal hits. Overkill is shown separately and is not added twice.

Build with **Build Once.cmd** or the existing GitHub Actions workflow, and publish under **v1.0.0.5**.

The source package includes synthetic tracker regression tests for lethal hits, late final hits, short advanced rows, normal rows, crits, player filtering, reset and window expiry. The GitHub workflow runs them before packaging. Windows compilation and UI rendering have not been run in this editing environment. If a hit is still missing, supply the combat log around that cast to verify its spell ID and timing.
