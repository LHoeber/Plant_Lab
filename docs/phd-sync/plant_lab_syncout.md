# Plant_Lab sync-out (written by Claude Code)
last_sync_out: 2026-10-26 | transferred to claude.ai log: 2026-10-26

## Current state (max ~10 lines, overwritten at each sync out)
- Unity 6000.5.10f1, URP, WebGL tablet target. One terrarium with one procedural plant (L-system development on the
  existing body: bending, thickness, glass collisions, self-collision, phases, withering).
- Control panel: generated at Play start from a PanelConfig (buttons, trials, max on, T) and a PanelLayout (positions),
  simple placeholder blocks for now (Blender parts can replace them). Trial workflow implemented (TrialController):
  select (max 2, FIFO) -> shutter -> Start -> camera feed + timer -> camera offline -> next LED row.
- Not built yet: plant responses to the buttons (effects layer, hidden state), lab template with several specimens,
  logging, agent interface. Classic plant development still switchable (to be removed).
- Cactus: segment shape done and tuned (CactusShape, CactusSegmentPreview). Next: L-system symbols + preview (now)
  -> segment body in the simulation (growth, development, collisions, withering) -> preset beside the tree.

## New entries, not yet transferred (newest first, 2 to 4 lines each)
- 2026-10-29: Cactus step 1 done: Laura tuned a base segment (saved in SampleScene, CactusSegmentPreview). Added
  "hills" (tubercles: a bump under every tuft, height + sharpness; strong + sharp ribs = spikes with a tuft on top).
  Wither (state, 0..1) vs Withered Core (how shriveled when fully withered) clarified in the Inspector.
  Next: step 2, L-system symbols for segments + preview (design questions to Laura).
- 2026-10-29: Cactus preview glowed white (Bloom) with black patches. Cause: surface normals from tiny finite
  differences; Unity's .normalized returns (0,0,0) below length 1e-5 -> NaN in the shader -> Bloom blowout.
  Fix in CactusShape.Normal (slopes divided by the step, own normalization). Pitfall for all generated meshes.
- 2026-10-28: Cactus (2nd plant) design agreed: segments = ribbed 3D ellipsoids (random width/height/depth, ribs, twist,
  tip dip) growing outward from their attachment point; tufts on the rib ridges (Laura's Blender model) pointing base
  to tip; flowers only at segment tips; withering = deflating toward a thin core; slight flexibility on contact.
  Step 1 built: CactusShape (shape math + mesh) and CactusSegmentPreview (tune one segment in the scene).
- (none yet)

## Already transferred to the claude.ai log (reference only, do not add here)
- 2026-10-26: Panel + trial workflow built (untested in Unity): PanelBuilder (panel from config + layout, any number of
  buttons/trials), TrialController (states Selecting/Ready/Running/Finished, LED history, world reset per trial, screen
  texts in a built-in pixel font, static between trials), menu "Botanic Lab > Build Trial Panel". Old per-tick console
  removed (ControlPanel, Interventions, 2D setup). Agent hooks: History, RemainingTime, TrialStarted/TrialEnded events.
- 2026-10-26: Panel v2 sketch: Start + "?" + timer bar on top, effect buttons + LED grid in the middle, arrows at the
  bottom; shutter open = covers the top half, closed = covers buttons + grid. Max 2 buttons on, a 3rd press switches
  off the oldest (FIFO). Row "done" LED: blinks while selecting, steady once the shutter is closed (logged in).
  Panel must be generated from a config (number of buttons, trials) out of modular parts, not modelled per condition.
- 2026-10-25 (later): Panel layout fixed from Laura's sketch: screen left; arrows, Start + "?", 2x2 effect buttons
  (numbered 1-4, red) with shutter sliding up over Start, LED history grid (columns 1-4 red, plus one white
  "trial done" LED per row; X = 13-15 trials). At most 2 buttons on per trial (11 combinations incl. all off).
  Agent equivalence agreed: agent gets the same history matrix (trials x buttons + done flags) and remaining time.
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
