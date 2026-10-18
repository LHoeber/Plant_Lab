using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The plant's decision layer: where branches, leaves and flowers appear and when tips stop.
/// Everything else (the body: growing tips, bending, thickness, collisions, organ sizes, phases, withering)
/// is the same for every development. Chosen by PlantSettings.development.
/// </summary>
public interface IPlantDevelopment
{
    /// <summary>Called once when the simulation starts, after the main stem exists.</summary>
    void Begin(PlantSimulation sim);
    /// <summary>Grows the branches by up to dL (only called in the Growing phase).</summary>
    void Grow(PlantSimulation sim, float dL);
    /// <summary>Called every step after positions and contacts are up to date.</summary>
    void AfterStep(PlantSimulation sim);
    /// <summary>Length a branch grows to (used for its taper toward the end).</summary>
    float MaxLengthOf(PlantSimulation sim, Branch b);
}

/// <summary>
/// The original spawn logic: every branch grows to a length set by the settings; along the way, spawn checks place
/// leaves and side branches (PlantSimulation.SpawnCheck); finished tips may get a flower after flowerDelay.
/// </summary>
public class ClassicDevelopment : IPlantDevelopment
{
    public void Begin(PlantSimulation sim)
    {
        sim.Root.nextSpawnCheck = sim.Settings.spawnStartFraction * MaxLengthOf(sim, sim.Root);
    }

    public void Grow(PlantSimulation sim, float dL)
    {
        //only branches that existed at the start of the step grow in it;
        //branches spawned during this step start growing in the next one
        int n = sim.Branches.Count;
        for (int i = 0; i < n; i++)
            sim.Branches[i].Grow(dL, MaxLengthOf(sim, sim.Branches[i]), sim);
    }

    public void AfterStep(PlantSimulation sim)
    {
        //finished tips whose bud delay has passed get their (one) flower check;
        //no new buds after Flowering (they'd have no chance to open and set fruit)
        if (sim.Phase > PlantPhase.Flowering) return;
        foreach (Branch b in sim.Branches)
            if (b.finishTime >= 0f && !b.flowerDecided && sim.SimTime - b.finishTime >= sim.Settings.flowerDelay)
                sim.FlowerCheck(b);
    }

    public float MaxLengthOf(PlantSimulation sim, Branch b) =>
        b.depth == 0 ? sim.Settings.maxLength : sim.Settings.SideBranchLength(sim.BaseDistance(b));
}

/// <summary>
/// What one tip still has to do: a sequence of L-system modules, worked through from `pos` on.
/// </summary>
public class TipProgram
{
    public readonly List<LModule> queue;
    public int pos;
    /// <summary>Length still to grow for the current F (meters).</summary>
    public float toGrow;

    public TipProgram(List<LModule> modules) { queue = modules; }
}

/// <summary>
/// L-system development: the rules decide the plant's structure, read as growth programs carried by the tips.
/// Every tip works through its modules in order, over time:
///   F(len)              the tip grows len units (len x lsUnit meters) at growthSpeed, with light bias, noise,
///                       bending and collisions like any branch
///   + - &amp; ^ \ / |   turn the tip's growth direction (rest shape, same conventions as the preview/notebook)
///   [ ... ]             a side branch from the current point; its content is its own program. Leaves/flowers at its
///                       start sit on the current branch; the branch itself only appears with its first F or module
///   leaf/flower symbol  a leaf / flower bud at the current point (parameter = size factor). A leaf's blade points along
///                       the turtle's heading H, its upper surface faces the turtle's up U: & tilts it away from the
///                       stem with the upper side toward the stem's tip, \ and / turn its surface around the blade
///   module with rules   replaced by a rule's successor right there (chosen with the branch's own random stream)
///   module, no rule     the tip is finished (it rests there)
///   other symbols, !, f no effect (thickness comes from the thickness settings)
/// Growth only happens at tips: a module is expanded when the tip reaches it, and grown segments never get longer.
/// </summary>
public class LSystemDevelopment : IPlantDevelopment
{
    /// <summary>The parsed rules (null if they couldn't be read: then nothing grows).</summary>
    public readonly LSystem System;
    /// <summary>Why the rules couldn't be read (null if fine).</summary>
    public readonly string Error;

    readonly HashSet<char> ruleSymbols = new HashSet<char>();
    readonly string leafSymbols, flowerSymbols;
    readonly float unit, angle;

    //a rule chain without any growth (e.g. A -> /(10) A) would loop forever: at most this many replacements per tip and step
    const int MaxRewritesPerStep = 64;
    //bounds the growth pieces per tip and step (several short F's in one step)
    const int MaxPiecesPerStep = 32;

    enum State { Growing, Finished, Waiting }

    public LSystemDevelopment(PlantSettings s)
    {
        unit = Mathf.Max(1e-5f, s.lsUnit);
        angle = s.lsAngle;
        leafSymbols = s.leafSymbols ?? "";
        flowerSymbols = s.flowerSymbols ?? "";
        try
        {
            var constants = new List<(string, double)>();
            if (s.lsConstants != null)
                foreach (LConstant c in s.lsConstants)
                    if (!string.IsNullOrWhiteSpace(c.name)) constants.Add((c.name, c.value));
            System = new LSystem(s.lsAxiom, s.lsRules, constants);
            foreach (LRule r in System.Rules) ruleSymbols.Add(r.Symbol);
        }
        catch (global::System.FormatException e)
        {
            Error = e.Message;
            Debug.LogError($"Plant L-system ({s.name}): {e.Message}", s);
        }
    }

    public void Begin(PlantSimulation sim)
    {
        Branch root = sim.Root;
        root.nextSpawnCheck = float.PositiveInfinity;//no classic spawn checks
        //turtle frame at the start, as in the preview: H up, U = Unity -Z (notebook -Y), L = H x U = -X
        root.tipNormal = Vector3.back;
        if (System == null) { root.finishTime = 0f; return; }
        root.program = new TipProgram(new List<LModule>(System.Axiom));
    }

    public float MaxLengthOf(PlantSimulation sim, Branch b) => b.TotalLength;//final length unknown: taper toward the current tip

    public void AfterStep(PlantSimulation sim) { }

    public void Grow(PlantSimulation sim, float dL)
    {
        //only branches that existed at the start of the step grow in it (new ones start in the next step)
        int n = sim.Branches.Count;
        for (int i = 0; i < n; i++) GrowTip(sim, sim.Branches[i], dL);
    }

    void GrowTip(PlantSimulation sim, Branch b, float dL)
    {
        TipProgram p = b.program;
        if (p == null || b.stopped || b.blocked) return;
        int rewrites = 0;
        float left = dL;
        for (int piece = 0; piece < MaxPiecesPerStep; piece++)
        {
            if (p.toGrow <= 1e-6f)
            {
                State state = Advance(sim, b, p, ref rewrites);
                if (state == State.Finished) { if (b.finishTime < 0f) b.finishTime = sim.SimTime; return; }
                if (state == State.Waiting) break;
                continue;//a new F: grow it
            }
            if (left <= 1e-7f) break;
            float before = b.TotalLength;
            //growing up to the end of the current F: Branch.Grow ends a segment exactly there, so turns happen at that point
            b.Grow(Mathf.Min(left, p.toGrow), before + p.toGrow, sim);
            if (b.blocked) return;//ran into obstacles with no way out: finished
            float grown = b.TotalLength - before;
            p.toGrow -= grown;
            left -= grown;
            if (grown <= 1e-9f) break;
        }
        b.finishTime = -1f;//still has work to do (Branch.Grow marks it finished at the end of every F)
        //drop the part of the program that's done, so it doesn't pile up
        if (p.pos > 256) { p.queue.RemoveRange(0, p.pos); p.pos = 0; }
    }

    /// <summary>Works through the tip's program up to the next F (Growing), its end (Finished), or the rewrite limit (Waiting).</summary>
    State Advance(PlantSimulation sim, Branch b, TipProgram p, ref int rewrites)
    {
        List<LModule> q = p.queue;
        while (p.pos < q.Count)
        {
            LModule m = q[p.pos];
            char c = m.Symbol;
            if (c == 'F')
            {
                p.pos++;
                p.toGrow = (m.Params.Length > 0 ? (float)m.Params[0] : 1f) * unit;
                if (p.toGrow > 1e-6f) return State.Growing;
                continue;
            }
            if (IsTurn(c))
            {
                Vector3 H = b.tipDir, U = b.tipNormal;
                Turn(ref H, ref U, c, AngleOf(m));
                b.tipDir = H;
                b.tipNormal = U;
                p.pos++;
                continue;
            }
            if (c == '[')
            {
                int end = MatchingBracket(q, p.pos);
                Lateral(sim, b, q, p.pos + 1, end, b.tipDir, b.tipNormal);
                p.pos = Mathf.Min(end + 1, q.Count);
                continue;
            }
            if (IsOrgan(c)) { PlaceOrgan(sim, b, m, b.tipDir, b.tipNormal); p.pos++; continue; }
            if (ruleSymbols.Contains(c))
            {
                if (rewrites >= MaxRewritesPerStep) return State.Waiting;
                LRule rule = System.Choose(m, b.spawnRng, out double[] slots);
                if (rule == null) return State.Finished;//no rule applies: the tip rests here
                rewrites++;
                var successor = new List<LModule>(rule.Successor.Length);
                LSystem.Apply(rule, slots, successor);
                q.RemoveAt(p.pos);
                q.InsertRange(p.pos, successor);
                continue;
            }
            p.pos++;//']', '!', 'f', symbols without rules: no effect here
        }
        return State.Finished;
    }

    /// <summary>
    /// The content of a bracket [from, end) on branch b: turns and organs act at the current point of b (organs sit on b);
    /// from the first F or module with rules on, the rest becomes the program of a new side branch.
    /// </summary>
    void Lateral(PlantSimulation sim, Branch b, List<LModule> q, int from, int end, Vector3 H, Vector3 U)
    {
        for (int i = from; i < end; i++)
        {
            LModule m = q[i];
            char c = m.Symbol;
            if (IsTurn(c)) { Turn(ref H, ref U, c, AngleOf(m)); continue; }
            if (c == '[')
            {
                int close = MatchingBracket(q, i);
                Lateral(sim, b, q, i + 1, Mathf.Min(close, end), H, U);
                i = close;
                continue;
            }
            if (IsOrgan(c)) { PlaceOrgan(sim, b, m, H, U); continue; }
            if (c == 'F' || ruleSymbols.Contains(c))
            {
                Branch child = sim.AddBranchAtTip(b, H, U);
                if (child != null) child.program = new TipProgram(q.GetRange(i, end - i));
                return;
            }
        }
    }

    void PlaceOrgan(PlantSimulation sim, Branch b, LModule m, Vector3 H, Vector3 U)
    {
        float size = m.Params.Length > 0 ? (float)m.Params[0] : 1f;
        if (leafSymbols.IndexOf(m.Symbol) >= 0) sim.AddLeafAtTip(b, H, U, size);
        else sim.AddFlowerAtTip(b, H, size);
    }

    bool IsOrgan(char c) => leafSymbols.IndexOf(c) >= 0 || flowerSymbols.IndexOf(c) >= 0;

    static bool IsTurn(char c) => c == '+' || c == '-' || c == '&' || c == '^' || c == '\\' || c == '/' || c == '|';

    float AngleOf(LModule m) => m.Params.Length > 0 ? (float)m.Params[0] : angle;

    /// <summary>Index of the ']' closing the '[' at `open` (the end of the list if it's never closed).</summary>
    static int MatchingBracket(List<LModule> q, int open)
    {
        int depth = 0;
        for (int i = open; i < q.Count; i++)
        {
            if (q[i].Symbol == '[') depth++;
            else if (q[i].Symbol == ']' && --depth == 0) return i;
        }
        return q.Count;
    }

    /// <summary>
    /// Turns the turtle frame (H = heading, U = up; L = H x U) like LTurtle does in notebook coordinates.
    /// Unity's coordinates are the notebook's with Y and Z swapped (a mirror image), so every angle changes sign.
    /// </summary>
    public static void Turn(ref Vector3 H, ref Vector3 U, char symbol, float deg)
    {
        Vector3 L = Vector3.Cross(H, U);
        switch (symbol)
        {
            case '+': H = Quaternion.AngleAxis(-deg, U) * H; break;//left around U
            case '-': H = Quaternion.AngleAxis(deg, U) * H; break;//right around U
            case '&': { Quaternion q = Quaternion.AngleAxis(-deg, L); H = q * H; U = q * U; break; }//pitch down around L
            case '^': { Quaternion q = Quaternion.AngleAxis(deg, L); H = q * H; U = q * U; break; }//pitch up around L
            case '\\': U = Quaternion.AngleAxis(-deg, H) * U; break;//roll left around H
            case '/': U = Quaternion.AngleAxis(deg, H) * U; break;//roll right around H
            case '|': H = -H; break;//turn around (L flips with it, U stays)
        }
        H.Normalize();
        U = Vector3.ProjectOnPlane(U, H).normalized;//keep the frame exactly perpendicular
    }
}
