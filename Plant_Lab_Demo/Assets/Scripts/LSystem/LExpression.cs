using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// A small expression language for L-system rules: a subset of Python expressions, parsed once into a tree and then
/// evaluated many times (no runtime code generation, so it also works in WebGL builds).
///
///   numbers: 3, 0.5, .5, 1e-3        names: rule parameters and constants (resolved when parsing)
///   arithmetic: + - * / // % **      comparison: &lt; &lt;= &gt; &gt;= == != (chainable: 0 &lt; x &lt; 1)
///   logic: and or not, True False    conditional: a if cond else b
///   functions: sin cos tan asin acos atan atan2 sqrt exp log log10 pow floor ceil abs min max round
///              radians degrees hypot clamp      constants: pi e tau inf
///
/// All values are doubles; for logic, 0 is false and anything else true (as in Python).
/// Angles in sin/cos/... are radians (as in Python); turtle angles in rules are degrees.
/// </summary>
public sealed class LExpression
{
    public readonly string Text;
    readonly Node root;

    LExpression(string text, Node root) { Text = text; this.root = root; }

    /// <summary>Evaluates with the given variable values (slots, as numbered by the resolver used when parsing).</summary>
    public double Evaluate(double[] slots) => root.Eval(slots);

    /// <summary>
    /// Parses an expression. resolve(name) returns the slot index of a variable, or -1 if the name is unknown
    /// (then parsing fails with an error naming it).
    /// </summary>
    public static LExpression Parse(string text, Func<string, int> resolve)
    {
        var p = new Parser(text, resolve);
        Node n = p.ParseExpression();
        p.ExpectEnd();
        return new LExpression(text.Trim(), n);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tree
    // ------------------------------------------------------------------------------------------------------------

    abstract class Node { public abstract double Eval(double[] s); }

    sealed class Const : Node
    {
        readonly double v;
        public Const(double v) { this.v = v; }
        public override double Eval(double[] s) => v;
    }

    sealed class Var : Node
    {
        readonly int slot;
        public Var(int slot) { this.slot = slot; }
        public override double Eval(double[] s) => s[slot];
    }

    sealed class Unary : Node
    {
        readonly char op; readonly Node a;
        public Unary(char op, Node a) { this.op = op; this.a = a; }
        public override double Eval(double[] s)
        {
            double x = a.Eval(s);
            return op == '-' ? -x : op == '!' ? (x == 0 ? 1 : 0) : x;
        }
    }

    sealed class Binary : Node
    {
        readonly string op; readonly Node a, b;
        public Binary(string op, Node a, Node b) { this.op = op; this.a = a; this.b = b; }
        public override double Eval(double[] s)
        {
            switch (op)
            {
                //and/or: short-circuit and return an operand, like Python
                case "and": { double x = a.Eval(s); return x == 0 ? x : b.Eval(s); }
                case "or": { double x = a.Eval(s); return x != 0 ? x : b.Eval(s); }
            }
            double l = a.Eval(s), r = b.Eval(s);
            switch (op)
            {
                case "+": return l + r;
                case "-": return l - r;
                case "*": return l * r;
                case "/": return l / r;
                case "//": return Math.Floor(l / r);
                case "%": return l - r * Math.Floor(l / r);//Python modulo: the result has the divisor's sign
                case "**": return Math.Pow(l, r);
                default: throw new InvalidOperationException(op);
            }
        }
    }

    /// <summary>a &lt; b &lt;= c ... : every pair must hold (Python's chained comparison).</summary>
    sealed class Compare : Node
    {
        readonly Node[] items; readonly string[] ops;
        public Compare(Node[] items, string[] ops) { this.items = items; this.ops = ops; }
        public override double Eval(double[] s)
        {
            double l = items[0].Eval(s);
            for (int i = 0; i < ops.Length; i++)
            {
                double r = items[i + 1].Eval(s);
                bool ok = ops[i] switch
                {
                    "<" => l < r, "<=" => l <= r, ">" => l > r, ">=" => l >= r, "==" => l == r, _ => l != r,
                };
                if (!ok) return 0;
                l = r;
            }
            return 1;
        }
    }

    sealed class IfElse : Node
    {
        readonly Node cond, yes, no;
        public IfElse(Node yes, Node cond, Node no) { this.yes = yes; this.cond = cond; this.no = no; }
        public override double Eval(double[] s) => cond.Eval(s) != 0 ? yes.Eval(s) : no.Eval(s);
    }

    sealed class Call : Node
    {
        readonly string name; readonly Node[] args;
        public Call(string name, Node[] args) { this.name = name; this.args = args; }
        public override double Eval(double[] s)
        {
            double A(int i) => args[i].Eval(s);
            switch (name)
            {
                case "sin": return Math.Sin(A(0));
                case "cos": return Math.Cos(A(0));
                case "tan": return Math.Tan(A(0));
                case "asin": return Math.Asin(A(0));
                case "acos": return Math.Acos(A(0));
                case "atan": return Math.Atan(A(0));
                case "atan2": return Math.Atan2(A(0), A(1));
                case "sqrt": return Math.Sqrt(A(0));
                case "exp": return Math.Exp(A(0));
                case "log": return args.Length == 1 ? Math.Log(A(0)) : Math.Log(A(0)) / Math.Log(A(1));
                case "log10": return Math.Log10(A(0));
                case "pow": return Math.Pow(A(0), A(1));
                case "floor": return Math.Floor(A(0));
                case "ceil": return Math.Ceiling(A(0));
                case "abs": return Math.Abs(A(0));
                case "radians": return A(0) * Math.PI / 180.0;
                case "degrees": return A(0) * 180.0 / Math.PI;
                case "hypot": return Math.Sqrt(A(0) * A(0) + A(1) * A(1));
                case "clamp": return Math.Max(A(1), Math.Min(A(2), A(0)));
                case "round": return args.Length == 1 ? Math.Round(A(0)) : Math.Round(A(0), (int)A(1));//half to even, like Python
                case "min": { double m = A(0); for (int i = 1; i < args.Length; i++) m = Math.Min(m, A(i)); return m; }
                case "max": { double m = A(0); for (int i = 1; i < args.Length; i++) m = Math.Max(m, A(i)); return m; }
                default: throw new InvalidOperationException(name);
            }
        }
    }

    static readonly Dictionary<string, (int min, int max)> Functions = new Dictionary<string, (int, int)>
    {
        ["sin"] = (1, 1), ["cos"] = (1, 1), ["tan"] = (1, 1), ["asin"] = (1, 1), ["acos"] = (1, 1), ["atan"] = (1, 1),
        ["atan2"] = (2, 2), ["sqrt"] = (1, 1), ["exp"] = (1, 1), ["log"] = (1, 2), ["log10"] = (1, 1), ["pow"] = (2, 2),
        ["floor"] = (1, 1), ["ceil"] = (1, 1), ["abs"] = (1, 1), ["radians"] = (1, 1), ["degrees"] = (1, 1),
        ["hypot"] = (2, 2), ["clamp"] = (3, 3), ["round"] = (1, 2), ["min"] = (2, 64), ["max"] = (2, 64),
    };

    static readonly Dictionary<string, double> NamedConstants = new Dictionary<string, double>
    {
        ["pi"] = Math.PI, ["e"] = Math.E, ["tau"] = 2 * Math.PI, ["inf"] = double.PositiveInfinity,
        ["True"] = 1, ["False"] = 0,
    };

    // ------------------------------------------------------------------------------------------------------------
    // Parser (recursive descent, Python precedence)
    // ------------------------------------------------------------------------------------------------------------

    sealed class Parser
    {
        readonly string text;
        readonly Func<string, int> resolve;
        readonly List<string> tokens = new List<string>();
        int pos;

        public Parser(string text, Func<string, int> resolve)
        {
            this.text = text;
            this.resolve = resolve;
            Tokenize();
        }

        Exception Error(string msg) => new FormatException($"{msg} in expression '{text.Trim()}'");

        void Tokenize()
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i;
                if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                    {
                        i++;
                        if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
                        while (i < text.Length && char.IsDigit(text[i])) i++;
                    }
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                }
                else
                {
                    string two = i + 1 < text.Length ? text.Substring(i, 2) : "";
                    i += two == "**" || two == "//" || two == "<=" || two == ">=" || two == "==" || two == "!=" ? 2 : 1;
                    if ("+-*/%<>=!(),".IndexOf(c) < 0) throw Error($"unexpected character '{c}'");
                }
                tokens.Add(text.Substring(start, i - start));
            }
        }

        string Peek => pos < tokens.Count ? tokens[pos] : null;
        bool Accept(string t) { if (Peek == t) { pos++; return true; } return false; }
        void Expect(string t) { if (!Accept(t)) throw Error($"expected '{t}'" + (Peek != null ? $" before '{Peek}'" : " at the end")); }
        public void ExpectEnd() { if (Peek != null) throw Error($"unexpected '{Peek}'"); }

        // expression := or ['if' or 'else' expression]
        public Node ParseExpression()
        {
            Node n = Or();
            if (Accept("if"))
            {
                Node cond = Or();
                Expect("else");
                return new IfElse(n, cond, ParseExpression());
            }
            return n;
        }

        Node Or() { Node n = And(); while (Accept("or")) n = new Binary("or", n, And()); return n; }
        Node And() { Node n = Not(); while (Accept("and")) n = new Binary("and", n, Not()); return n; }
        Node Not() => Accept("not") ? new Unary('!', Not()) : Comparison();

        Node Comparison()
        {
            Node first = Sum();
            var items = new List<Node> { first };
            var ops = new List<string>();
            while (Peek == "<" || Peek == "<=" || Peek == ">" || Peek == ">=" || Peek == "==" || Peek == "!=")
            {
                ops.Add(tokens[pos++]);
                items.Add(Sum());
            }
            return ops.Count == 0 ? first : new Compare(items.ToArray(), ops.ToArray());
        }

        Node Sum()
        {
            Node n = Term();
            while (Peek == "+" || Peek == "-") { string op = tokens[pos++]; n = new Binary(op, n, Term()); }
            return n;
        }

        Node Term()
        {
            Node n = UnaryOp();
            while (Peek == "*" || Peek == "/" || Peek == "//" || Peek == "%") { string op = tokens[pos++]; n = new Binary(op, n, UnaryOp()); }
            return n;
        }

        Node UnaryOp()
        {
            if (Accept("-")) return new Unary('-', UnaryOp());
            if (Accept("+")) return UnaryOp();
            return Power();
        }

        // power := atom ['**' unary]   (right-associative, binds tighter than a unary minus on its left: -2**2 = -4)
        Node Power()
        {
            Node n = Atom();
            return Accept("**") ? new Binary("**", n, UnaryOp()) : n;
        }

        Node Atom()
        {
            string t = Peek;
            if (t == null) throw Error("unexpected end");
            pos++;
            if (t == "(") { Node n = ParseExpression(); Expect(")"); return n; }
            if (char.IsDigit(t[0]) || t[0] == '.')
            {
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) throw Error($"bad number '{t}'");
                return new Const(v);
            }
            if (char.IsLetter(t[0]) || t[0] == '_')
            {
                if (Accept("("))
                {
                    if (!Functions.TryGetValue(t, out var arity)) throw Error($"unknown function '{t}'");
                    var args = new List<Node>();
                    if (!Accept(")"))
                    {
                        do args.Add(ParseExpression()); while (Accept(","));
                        Expect(")");
                    }
                    if (args.Count < arity.min || args.Count > arity.max) throw Error($"wrong number of arguments for '{t}'");
                    return new Call(t, args.ToArray());
                }
                int slot = resolve != null ? resolve(t) : -1;//parameters and constants shadow the built-in names
                if (slot >= 0) return new Var(slot);
                if (NamedConstants.TryGetValue(t, out double c)) return new Const(c);
                throw Error($"unknown name '{t}'");
            }
            throw Error($"unexpected '{t}'");
        }
    }
}
