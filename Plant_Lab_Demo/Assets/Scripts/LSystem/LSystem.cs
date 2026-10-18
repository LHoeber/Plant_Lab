using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>One symbol with its parameter values, e.g. A(1.5, 0).</summary>
public readonly struct LModule
{
    public readonly char Symbol;
    public readonly double[] Params;//never null (empty if there are none)

    public static readonly double[] NoParams = new double[0];

    public LModule(char symbol, double[] parameters) { Symbol = symbol; Params = parameters ?? NoParams; }

    public override string ToString()
    {
        if (Params.Length == 0) return Symbol.ToString();
        var sb = new StringBuilder().Append(Symbol).Append('(');
        for (int i = 0; i < Params.Length; i++)
            sb.Append(i > 0 ? ", " : "").Append(Params[i].ToString("G6", CultureInfo.InvariantCulture));
        return sb.Append(')').ToString();
    }
}

/// <summary>
/// One rule:  predecessor(params) : condition -> successor @ weight   (condition and weight optional).
/// </summary>
public sealed class LRule
{
    public char Symbol;
    public string[] ParamNames;
    public LExpression Condition;//null = always
    public LExpression Weight;   //null = 1
    public (char symbol, LExpression[] args)[] Successor;
    public string Text;          //the rule as written
}

/// <summary>
/// A parametric, stochastic L-system: rules as text, parallel rewriting. Same notation as the Python notebook
/// (lsystem_lab/lsys.py), expressions are a subset of Python (see LExpression).
///
///   A(l, o) : o &lt; maxOrder -> F(l) [ +(30) A(l*0.7, o+1) ] A(l*0.9, o)
///   B -> F [ -B ] +B @ 0.5
///
/// Symbols are single characters. Several matching rules: their weights decide the random choice.
/// Modules without a matching rule stay as they are. '#' starts a comment.
/// </summary>
public sealed class LSystem
{
    public readonly List<LRule> Rules = new List<LRule>();
    public readonly string[] ConstantNames;
    public readonly double[] ConstantValues;
    public readonly LModule[] Axiom;

    readonly Dictionary<char, List<LRule>> bySymbol = new Dictionary<char, List<LRule>>();

    static readonly Regex RulePattern = new Regex(
        @"^\s*(?<pred>\S)\s*(\((?<params>[^)]*)\))?\s*(:(?<cond>.*?))?->(?<succ>.*?)(@(?<prob>[^@]*))?$", RegexOptions.Compiled);

    /// <summary>
    /// Parses axiom and rules. Constants (name -> value) can be used in all expressions.
    /// Throws a FormatException naming the line and the problem if something can't be read.
    /// </summary>
    public LSystem(string axiom, string rules, IList<(string name, double value)> constants = null)
    {
        int n = constants?.Count ?? 0;
        ConstantNames = new string[n];
        ConstantValues = new double[n];
        for (int i = 0; i < n; i++) { ConstantNames[i] = constants[i].name.Trim(); ConstantValues[i] = constants[i].value; }

        //slot layout for evaluation: [rule parameters..., constants...]; the axiom has no parameters, only constants
        try
        {
            Axiom = ParseModules(axiom ?? "", ResolverFor(new string[0]), "axiom")
                .ConvertAll(m => new LModule(m.symbol, EvalArgs(m.args, ConstantValues))).ToArray();
            var axiomModules = new (char, LExpression[])[Axiom.Length];
            for (int i = 0; i < Axiom.Length; i++) axiomModules[i] = (Axiom[i].Symbol, null);
            CheckBrackets(axiomModules, "axiom");
        }
        catch (FormatException e) { throw new FormatException($"axiom: {e.Message}"); }

        int lineNo = 0;
        foreach (string raw in (rules ?? "").Split('\n'))
        {
            lineNo++;
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;
            try { AddRule(line); }
            catch (FormatException e) { throw new FormatException($"rule line {lineNo}: {e.Message}"); }
        }
    }

    void AddRule(string line)
    {
        Match m = RulePattern.Match(line);
        if (!m.Success) throw new FormatException($"can't read '{line}' (expected: predecessor : condition -> successor @ weight)");
        var names = new List<string>();
        foreach (string p in m.Groups["params"].Value.Split(','))
            if (p.Trim().Length > 0) names.Add(p.Trim());
        Func<string, int> resolve = ResolverFor(names.ToArray());
        string cond = m.Groups["cond"].Success ? m.Groups["cond"].Value : null;
        string prob = m.Groups["prob"].Success ? m.Groups["prob"].Value : null;
        var rule = new LRule
        {
            Symbol = m.Groups["pred"].Value[0],
            ParamNames = names.ToArray(),
            Condition = string.IsNullOrWhiteSpace(cond) ? null : LExpression.Parse(cond, resolve),
            Weight = string.IsNullOrWhiteSpace(prob) ? null : LExpression.Parse(prob, resolve),
            Successor = ParseModules(m.Groups["succ"].Value, resolve, "successor").ToArray(),
            Text = line,
        };
        CheckBrackets(rule.Successor, "successor");
        Rules.Add(rule);
        if (!bySymbol.TryGetValue(rule.Symbol, out var list)) bySymbol[rule.Symbol] = list = new List<LRule>();
        list.Add(rule);
    }

    /// <summary>
    /// Every '[' needs its ']' (and the other way round): an unclosed bracket would silently turn
    /// everything after it into a side branch.
    /// </summary>
    static void CheckBrackets((char symbol, LExpression[] args)[] modules, string what)
    {
        int depth = 0;
        foreach (var (symbol, _) in modules)
        {
            if (symbol == '[') depth++;
            else if (symbol == ']' && --depth < 0) throw new FormatException($"']' without '[' in the {what}");
        }
        if (depth > 0) throw new FormatException($"{depth} '[' not closed with ']' in the {what}");
    }

    /// <summary>Slot index of a name: rule parameters first, then constants (-1 = unknown).</summary>
    Func<string, int> ResolverFor(string[] paramNames) => name =>
    {
        if (paramNames != null)
            for (int i = 0; i < paramNames.Length; i++) if (paramNames[i] == name) return i;
        for (int i = 0; i < ConstantNames.Length; i++)
            if (ConstantNames[i] == name) return (paramNames?.Length ?? 0) + i;
        return -1;
    };

    /// <summary>'F(l) [ +(30) A(l*0.7) ]' -> (F, [l]), ([, []), (+, [30]), (A, [l*0.7]), (], [])</summary>
    static List<(char symbol, LExpression[] args)> ParseModules(string text, Func<string, int> resolve, string what)
    {
        var result = new List<(char, LExpression[])>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            i++;
            var args = new LExpression[0];
            if (i < text.Length && text[i] == '(')
            {
                int depth = 1, j = i + 1;
                while (depth > 0)
                {
                    if (j >= text.Length) throw new FormatException($"unclosed '(' after '{c}' in the {what}");
                    if (text[j] == '(') depth++;
                    else if (text[j] == ')') depth--;
                    j++;
                }
                var parts = SplitArgs(text.Substring(i + 1, j - i - 2));
                args = parts.ConvertAll(a => LExpression.Parse(a, resolve)).ToArray();
                i = j;
            }
            result.Add((c, args));
        }
        return result;
    }

    /// <summary>Splits 'a, f(b, c), d' at top-level commas.</summary>
    static List<string> SplitArgs(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0) { parts.Add(text.Substring(start, i - start)); start = i + 1; }
        }
        if (text.Trim().Length > 0) parts.Add(text.Substring(start));
        return parts;
    }

    static double[] EvalArgs(LExpression[] args, double[] slots)
    {
        if (args.Length == 0) return LModule.NoParams;
        var v = new double[args.Length];
        for (int i = 0; i < args.Length; i++) v[i] = args[i].Evaluate(slots);
        return v;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Rewriting
    // ------------------------------------------------------------------------------------------------------------

    readonly List<(LRule rule, double[] slots)> candidates = new List<(LRule, double[])>();

    /// <summary>
    /// The rule to apply to a module (and the variable values to apply it with), or null if none matches.
    /// Several matches: chosen at random by their weights.
    /// </summary>
    public LRule Choose(LModule m, Random rng, out double[] slots)
    {
        candidates.Clear();
        slots = null;
        if (!bySymbol.TryGetValue(m.Symbol, out var rules)) return null;
        foreach (LRule r in rules)
        {
            if (r.ParamNames.Length != m.Params.Length) continue;
            var s = new double[m.Params.Length + ConstantValues.Length];
            Array.Copy(m.Params, s, m.Params.Length);
            Array.Copy(ConstantValues, 0, s, m.Params.Length, ConstantValues.Length);
            if (r.Condition == null || r.Condition.Evaluate(s) != 0) candidates.Add((r, s));
        }
        if (candidates.Count == 0) return null;
        int pick = 0;
        if (candidates.Count > 1)
        {
            double total = 0;
            var w = new double[candidates.Count];
            for (int i = 0; i < w.Length; i++)
            {
                var (r, s) = candidates[i];
                w[i] = Math.Max(0, r.Weight == null ? 1 : r.Weight.Evaluate(s));
                total += w[i];
            }
            double u = rng.NextDouble() * total;
            pick = w.Length - 1;
            for (int i = 0; i < w.Length; i++) { if (u < w[i]) { pick = i; break; } u -= w[i]; }
        }
        slots = candidates[pick].slots;
        return candidates[pick].rule;
    }

    /// <summary>The successor of a rule, with the given variable values filled in.</summary>
    public static void Apply(LRule rule, double[] slots, List<LModule> output)
    {
        foreach (var (symbol, args) in rule.Successor) output.Add(new LModule(symbol, EvalArgs(args, slots)));
    }

    /// <summary>One growth step: every module is replaced (in parallel) by the successor of a matching rule.</summary>
    public List<LModule> Rewrite(List<LModule> modules, Random rng)
    {
        var output = new List<LModule>(modules.Count * 2);
        foreach (LModule m in modules)
        {
            LRule rule = Choose(m, rng, out double[] slots);
            if (rule == null) output.Add(m);
            else Apply(rule, slots, output);
        }
        return output;
    }

    /// <summary>
    /// All generations from the axiom (index 0) up to `steps`. Same seed -> same result.
    /// Stops early if a generation would exceed maxModules (stoppedEarly is then true).
    /// </summary>
    public List<List<LModule>> Derive(int steps, int seed, int maxModules, out bool stoppedEarly)
    {
        var rng = new Random(seed);
        var gens = new List<List<LModule>> { new List<LModule>(Axiom) };
        stoppedEarly = false;
        for (int k = 0; k < steps; k++)
        {
            List<LModule> next = Rewrite(gens[gens.Count - 1], rng);
            if (next.Count > maxModules) { stoppedEarly = true; break; }
            gens.Add(next);
        }
        return gens;
    }

    /// <summary>Module counts per symbol, most frequent first, e.g. "F: 120, A: 32, [: 31".</summary>
    public static string Summary(List<LModule> modules)
    {
        var counts = new Dictionary<char, int>();
        foreach (LModule m in modules) counts[m.Symbol] = counts.TryGetValue(m.Symbol, out int c) ? c + 1 : 1;
        var list = new List<KeyValuePair<char, int>>(counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        var sb = new StringBuilder();
        foreach (var kv in list) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(": ").Append(kv.Value);
        return sb.ToString();
    }
}
