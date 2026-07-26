# Claude Usage Meter — icon layers

Artwork: 1024x1024 SVG, transparent, no corner mask baked in (the icon tool applies the shape).
Disc outer edge is 788px across, so it sits inside the safe area for rounded/circular masks.

## Layer stack (bottom to top)
1. Background — flat fill, set in the icon tool: dark #1F1E1D / light #F0EEE6
2. 01-tracks.svg — unused encoder segments (neutral)
3. 02-usage.svg — consumed segments, orange #E8874F
4. 03-head.svg — read head + hub, marks the current level

## Sets
- dark/  — for the dark appearance (cream tracks + head)
- light/ — for the light appearance (ink tracks + head)
- mono/  — pure black artwork for mono/tinted appearances; set opacity per layer in the tool
- flat/  — single-file icons with background + squircle, for GitHub, README, favicon, web

## Notes
- All three tracks stop at the same 56% level; the stepped edge is the mark's signature. To render a live level, keep the first dash lengths proportional per ring (outer C=2211.7, middle C=1583.4, inner C=955.0 at these radii).
- Keep the head above the usage layer so specular/glass effects catch it.
