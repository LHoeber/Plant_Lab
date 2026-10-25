# Plant_Lab inbox (mirror from the claude.ai sync hub)
Last updated: 2026-10-25 by the "PhD topic context" session. Read-only for Claude Code.
Full design text: Notion "Experimental Prototype 2" -> section "Paradigm Structure".

## Current paradigm (v3, decided 2026-10-25)
- Trial loop, all on the control panel (no full-screen view):
  1. Choose a combination of 4 unlabelled effect buttons (panel layout adaptable by parameter).
     The panel shows how often each combination was already tried. "?" reopens the instructions.
  2. Close a transparent sliding shutter over the selected buttons: locks them (they stay visible)
     and uncovers the Start button (the open shutter covers Start).
  3. Start: the camera feed on the panel screen switches on, camera arrows become active.
     World resets to the same seeded state and the same start view every trial.
  4. World runs for time T (speed adjustable, intermediate for now). Within a trial only the
     4 camera arrows (20 degree steps) + idle/no-op. Remaining time shown on the panel.
  5. After T: "camera offline", shutter opens for the next selection.
- Camera arrows outside a trial only show a hint ("camera not accessible, trial has not started").
- "?" does not use T. No effect-button presses during a trial (possible later variant).
- Trial budget per lab about 12 to 15 (set later). Forced first trials per lab: maybe (ask Fabian).
- Not everything can be seen in one trial, by design (simultaneous events in views that cannot be
  seen together + stochastic outcomes), not by requiring fast camera movement.
- Panel screen as large as possible: the terrarium appears smaller than full screen, important
  cues (bugs, leaf patterns) must be visible at that size.

## Labs
- 2 to 3 lab variants per session, fixed set, counterbalanced order, not chosen by participants.
- Per lab: new species (no reused plant parts), reshuffled button-to-effect mapping (same effect set),
  own substrate (transparent liquid vs soil; a lab property, not a button).
- 2 species per lab, 2 to 3 specimens each, random placement.
- Cosmetic variation (background, box shapes) free. Structural variation (pipe routes, pots,
  lamp positions) changes what is visible from where: document it per lab.
- Heat and freeze never in the same lab.

## Effects and plants
- Controls familiar (water, light, heat, air), organisms alien (procedural, no known species).
- Devices may be directly visible (light colour) or only via secondary signs (fog, frost);
  the hidden part is how the plants respond (sign, delay, interactions).
- Interactions between buttons: randomized pairs, never real-world physics (no "water freezes").
- Avoid veridical plant rules as defaults; sign, functional form, latency, interactions = per-species parameters.
- Design visual salience independently of informativeness (subtle diagnostic changes, loud uninformative ones).
- Vent jamming: random amount per press, visible.

## Agent equivalence
- The agent sees only Camera A's render (terrarium), never the panel.
- It also gets the count vector of tried combinations and the remaining time as inputs.
- The combination choice is one discrete action per trial.

## Logging (per tick)
- hidden state, visible subset per viewpoint (ideally a visibility probability per variable),
  action, chosen combination, remaining time, seeds.

## Open items from other sessions
- 2026-10-09 (LeWM): game needs a controllable novelty axis (ID/OOD split) and logged ground-truth state.
- 2026-10-11: logging schema + Camera A RenderTexture split (spec: claude/13-control-panel-spec.md in the
  claude.ai project). Event logging not yet implemented.
- 2026-10-22: the game must not be an upscaled table: partial/probabilistic observation from views,
  information spread over views and time, nuisance variation (lighting, foliage), several hidden causes
  for the same symptom (droop from roots / pests / light). Lighting = controlled factor.

## Next builds (Laura)
- Second alien plant (not tree-like).
- Roots + transparent containers (substrate per lab).
- First full lab with random plant placement.
- Panel with shutter, camera on/off, timer.
- Optional: pest infestation visuals.
