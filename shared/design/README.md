# Shared design foundation

`tokens.json` (colours, spacing, radii, type roles, per-app profiles) and `status.json` (status and
freshness wording in English, Sinhala and Tamil) are the source. Figma values are kept as exported;
every accessibility change is listed under `adjustments` and applied through `overrides`.

    node shared/design/generate.mjs            # write the generated files
    node shared/design/generate.mjs --check    # fail on drift, missing wording or low contrast
    node shared/design/generate.mjs --report   # also print every contrast ratio
    node --test shared/design/freshness.test.ts

Outputs (committed): `generated/tokens.css`, `generated/status.ts`, and
`packages/smartrail_core/lib/src/generated/*.g.dart`. Fonts (Noto Sans, Abhaya Libre Regular for
Sinhala, and Noto Sans Tamil, SIL OFL licences alongside) live in `packages/smartrail_core/fonts/` and serve both Flutter and
the web apps. Sinhala and Tamil wording is **unreviewed** until native readers check it.

The user confirmed Abhaya Libre for Figma and apps on 19 October 2026. It replaces the earlier
Noto Sans Sinhala choice. The Sinhala reference face is Regular (400); preserve Latin emphasis
separately in mixed-script text instead of synthesising bold Sinhala. Source: Google Fonts'
`ofl/abhayalibre/AbhayaLibre-Regular.ttf`; its licence is `OFL-AbhayaLibre.txt` beside the font.
