# Plant_Lab sync-out (written by Claude Code)
last_sync_out: 2026-10-25 | transferred to claude.ai log: 2026-10-09 (last paste)

## Current state (max ~10 lines, overwritten at each sync out)
- Unity 6000.5.10f1, URP, WebGL tablet target. One terrarium with one procedural plant; 3D console from Blender
  (screen shows Camera A, 768x512 RenderTexture), 0.5 s tick, camera arrows in 20 degree steps.
- Plants: L-system development (rules as text in PlantSettings, grown tip by tip) on the existing body (bending,
  thickness, glass collisions, phases, withering); classic spawn logic still switchable (to be removed).
- Self-collision: leaves/flowers avoid own stems, leaves yield to flowers; 20 Hz, sleeping organs, sim time budget.
- Console still has the old per-tick 6 buttons (11 actions); superseded by paradigm v3 (trial loop + shutter).
- Not built yet: plant responses to conditions (hidden state), trial loop, labs/multiple specimens, logging, agent API.
- Next: settle open v3 questions (see top entry), then trial loop -> lab definition -> effects layer -> 2nd species.

## New entries since the last transfer (newest first, 2 to 4 lines each)
- 2026-10-25: Panel UI decisions (Laura): shutter starts open, tap closes/reopens it (selection can be changed until
  Start). "?" only between trials (lit when available); instructions in retro VCR style on the black screen.
  Selection history as an LED grid (rows = trials, columns = buttons) instead of counts per combination; timer as a
  backward-running progress bar beside the screen. Between trials: animated static/lines, no lab view.
  Open: agent should then get the same history matrix instead of the count vector (equivalence).
- 2026-10-25: Build decisions (Laura): combination = 4 buttons each on/off (16 incl. all off; maybe later max 2 on),
  "on" shown by a faint glow. First effects: water, heat, light, fertilizer (ventilation later). Plant responses
  first: light attraction/repulsion, final size/growth speed, wilting, flowering + fruit. Each trial = seedling to
  full cycle within T (adjustable). One lab first. Camera keeps orbiting the growing area. Next plant: ball cactus.
- 2026-10-25: Paradigm v3 adopted. Open questions for the build: what a "combination" is (on/off per button = 16?),
  effect pool per lab, which hidden plant variables respond and how they show, initial plant state per trial,
  camera with several specimens (orbit centre vs per plant), trial length T. Proposed order: trial loop, lab
  definition (multi-specimen, performance check), effects layer, second species + roots, logging/agent interface.
- 2026-10-24: Self-collision (organs vs own stems; leaves yield to flowers, flowers to earlier flowers; stems never
  yield). Grid broad phase + whole-organ box + coarse leaf shapes, sleeping organs, 20 Hz. Pitfall: sim fell behind ->
  200 steps/frame (5-10 s frames); fix: per-frame sim time budget. Files: PlantSelfCollision.cs, GrowthStop.cs.
- 2026-10-24: Stem "rubber band" rings (permanent radius caps after brief contact) fixed: caps released when free,
  smoothed radius profile around caps; the release then pulsated -> wait before regrowing, doubling per re-contact.
  Falling organs: collider now from the current shape-key state (was the largest state, e.g. ripe fruit).
- 2026-10-20: L-system usage: elongation-free translation of classic rules (tip parameter n = remaining steps,
  segment length = len(n-1)); bracketed plant reproduced exactly. Leaves oriented by the turtle frame. Unclosed
  brackets now an error; warning when branch/leaf/flower limits cut a plant (preview has no limits).
- 2026-10-18: L-system development in Unity: tips work through rule successors over time (F grows, turns, [ ] =
  side branch, organ symbols, no rule = finished); phases can end automatically or on trigger; settings unused by the
  current mode hidden. C# L-system core verified identical to the Python test bed (lsystem_lab/lsys.py).
- 2026-10-16: 3D console (Blender FBX, parts found by name at runtime, re-export safe); console has its own light via
  URP rendering layers (terrarium lighting changes can't leak into the console). 6 placeholder buttons (now obsolete).
- 2026-10-11/13: 2D control panel prototype: tick clock, Camera A -> RenderTexture, seeded per-participant
  button mapping, static screen overlay; lab room with tiles; recording/compression notes.
