# Plant_Lab inbox (mirror from the claude.ai sync hub)
Last updated: 2026-11-10 by the "PhD topic context" session. Read-only for Claude Code.
Full design text: Notion "Experimental Prototype 2" -> section "Paradigm Structure".
Plan until the first TAC meeting (mid-December): project doc claude/15-plan-until-tac.md (claude.ai project).

## New (2026-11-08): first Stage 1 results -> the tables will still change
- The Stage 1 notebook (tabular world with water / heat / light / vent, observation table, Dirichlet learner,
  4 drivers, pure-driver agents) runs. First result with full observation: the current world is learned within
  5 to 8 trials, so the drivers separate only weakly. The rules will be redesigned (slower or rarer effects,
  effects that need the right view, transient events). Keep holding back concrete plant responses per button.
- The vent + bugs mechanic (04.11) stays. Next on the concept side: view-restricted observer (camera-dependent).
- Lab-meeting feedback (10.11) for the game: check whether people prefer certain buttons by position (e.g. the lower
  ones) -> keep the mapping randomized per participant and log button positions; organisms must be alien enough.

## (2026-11-04): air vent + bugs replace fertilizer + weeds
- Laura's decision (Stage 1 tables, 04.11): the 4th button is now an AIR VENT (not fertilizer). Noisy TV = BUGS:
  while the vent is on, the bug level (none / few / many) moves one step up or down at random every tick
  (random walk, p_up = p_down = 0.3 for now); vent off = bugs stay at their current level.
- Why: weeds that only grow saturate at "many" (the randomness disappears); bugs that come and go keep the
  outcome unpredictable all trial long, and flying bugs are visible from many views (noisy TV also for viewing).
- Bugs have no effect on the plants for now (pure noise). A second cause for bugs (e.g. fertilizer) is a later scenario.
- Laura wants to build the bugs with particles / shaders / flocking. Keep their behaviour a function of the
  bug level + seed (density per level), so the visual matches the table state.
- Buttons now: water (moisture up), heat (moisture down; dry soil stalls the seedling), light (bending toward or
  away, shorter growing phase), vent (bugs). Water and heat cancel when both are on.

## (2026-11-02): what to build next, and what to hold back
- Priority now on the concept side: Stage 1 tables (hidden variables, effects, observation table, designed
  noise) for one lab. The tables define the rules of the game; they are written in claude.ai, then handed here.
- Build now (does not depend on the tables):
  1. Effects layer that reads its rules from a table/config (effect -> hidden variable, direction, form,
     delay, probability), so later rule changes are data, not code.
  2. Per-tick logging: hidden state, visibility per variable and view, action, combination, remaining time, seeds.
  3. Lab template with several specimens (2 species x 2 to 3 specimens, random placement).
  4. Background rooms for the labs (free design). Exception: anything inside the terrarium that blocks
     sight lines (pots, pipes, lamps) changes the observation table, so document it per lab.
  5. Later: headless / batch data generation (random camera policy, combination as a fixed per-trial input).
- Hold back: concrete plant responses to specific buttons (wait for the tables), large dataset generation
  (first a small test set and a readability check of the cues at panel resolution).
- Determinism: visible changes must be a function of hidden state + seed. Randomness that is not in the
  tables (e.g. falling leaves with physics) is uncontrolled noise for the model: remove it or make it seeded.
- Lighting stays constant (controlled factor), except where a button changes it by design.

## Current paradigm (v3, decided 2026-10-25)
- Trial loop on the control panel: choose a combination of 4 unlabelled buttons (max 2 on, FIFO,
  11 combinations incl. all off; LED grid history) -> close shutter (locks buttons, uncovers Start) ->
  Start: camera feed on the panel screen, world resets to the same seeded state and start view ->
  runs for T, only 4 camera arrows (20 degree steps) + idle -> "camera offline", shutter opens.
- "?" only between trials, does not use T. No button presses during a trial (possible later variant).
- Trial budget per lab about 12 to 15 (to be set by Stage 1). Forced first trials: open (ask Fabian).
- Not everything can be seen in one trial, by design (simultaneous events in different views +
  stochastic outcomes), not by requiring fast camera movement.
- Panel screen as large as possible; important cues must be visible at that size.

## Labs and effects
- 2 to 3 labs per session, fixed set, counterbalanced order. Per lab: new species (no reused parts),
  reshuffled button mapping (same effect set), own substrate (transparent liquid vs soil).
- First effects: water, heat, light, air vent (since 04.11; fertilizer + weeds dropped for now).
  Vent = noisy TV: bugs come and go at random while the vent is on.
- Controls familiar, organisms alien; sign, form, delay and interactions are per-species parameters,
  no veridical plant rules. Salience independent of informativeness.
- Heat and freeze never in the same lab.

## Agent equivalence
- The agent sees only Camera A's render (terrarium), never the panel; it also gets the selection
  history (same matrix as the LED grid) and the remaining time. Combination = one discrete input per trial.

## Open items from other sessions
- 2026-10-09 (LeWM): controllable novelty axis (ID/OOD split) + logged ground-truth state.
  With own data the ID/OOD split becomes unseen views or combinations; designed random effects with
  known probability give the aleatoric ground truth directly.
- 2026-10-11: Camera A RenderTexture split + logging schema (claude/13-control-panel-spec.md). Event logging not yet built.
- 2026-10-22: not an upscaled table: partial/probabilistic observation, information spread over views
  and time, several hidden causes for the same symptom.
- Camera with several specimens: orbit centre vs. per plant still open (decides which views exist).

## Current builds (from your sync-out)
- Cactus (2nd plant): grows in the editor; stutter fixed (05.11), multi-material tufts (06.11). Open: segments stuck
  in the 2.5 mm clearance gap (fix proposed); then cactus beside the tree. Panel arrow spacing configurable (09.11).
