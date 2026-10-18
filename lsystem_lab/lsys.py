"""
Algorithms adapted from:
Prusinkiewicz, P., & Lindenmayer, A. (1990). The Algorithmic Beauty of Plants. Springer-Verlag.

Minimal parametric, stochastic L-system
Rule syntax (one rule per line, '#' starts a comment):

    predecessor(parameters) : condition -> successor @ probability

Examples:
    At the tip A, go l forward, start tip with 70% of len, increase order,
    return to start of subbranch, call tip again with 90% length and same order
    A(l, o) : o < maxOrder -> F(l) [ +(30) A(l*0.7, o+1) ] A(l*0.9, o)
    if maximum order is reached, create organ
    A(l, o) : o >= maxOrder -> F(l) O

    With 50% chance, at tip B, go forward, start subtip to the right, reset, continue main branch to the left
    Or with 50% chance, go forward, start new tip
    B -> F [ -B ] +B @ 0.5
    B -> F B @ 0.5

- symbols are single characters, optionally with parameters in parentheses: A, F(1.5), +(30)
- condition and probability are optional; expressions are Python and can use the parameters, the constants
  passed to derive() and math functions (sin, sqrt, pi, ...)
- several rules can match the same module: their probabilities are weights for a random choice
- modules without a matching rule stay as they are

Turtle symbols (H = heading, L = left, U = up(out of screen); at the start: H points up, +Z):
    F(len)  move forward and draw          f(len)  move forward without drawing
    +(a) -(a)  turn left / right around U   &(a) ^(a)  pitch down / up around L
    \\(a) /(a)  roll left / right around H  |  turn around (180 deg)
    [  ]    save / restore the turtle state (does everything inside the brackets and then goes back to the state before those brackets)
    !(w)    set the segment width          any other symbol is simply registered at the current position (e.g. A = tip, O = organ)
                                           if you assigned a rule to that letter, it will be noted there (executed when calling "derive" on everything)
    @ p relative probability weight to all other rules that are valid at the moment (doesn't need to be between [0,1])
Defaults when a parameter is missing: step length 1, angle 25 deg, width 1, probability weight 1.
"""
from __future__ import annotations

import math
import random
import re
from dataclasses import dataclass, field

# ---------------------------------------------------------------------------------------------------------------------
# Modules and rules
# ---------------------------------------------------------------------------------------------------------------------

@dataclass
class Module:
    """One symbol with its parameter values, e.g. A(1.5, 0)."""
    symbol: str
    params: tuple = ()

    def __repr__(self):
        if not self.params:
            return self.symbol
        #floats are transformed into "general" format, i.e. 6 significant digits, or use scientific notation
        return f"{self.symbol}({', '.join(f'{p:g}' if isinstance(p, float) else str(p) for p in self.params)})"

#create a set of global function names that can be used from the math module, for easier validity checking
SAFE = {name: getattr(math, name) for name in dir(math) if not name.startswith("_")}
#add some additional valid functions
SAFE.update(min=min, max=max, abs=abs, round=round)


def _compile(expr: str):
    #speed up processing of the same expression multiple times
    #compiles it once, so the string doesn't have to be parsed anew at each step
    #also does error check at start, not waiting until faulty rule is called
    return compile(expr.strip(), "<rule>", "eval")


def _split_args(text: str) -> list[str]:
    """Splits 'a, f(b, c), d' at top-level commas."""
    parts, depth, start = [], 0, 0
    for i, ch in enumerate(text):
        #function arguments are kept together
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        #only top level splits are considered
        elif ch == "," and depth == 0:
            parts.append(text[start:i])
            start = i + 1
    if text.strip():
        parts.append(text[start:])
    return [p.strip() for p in parts]


def _parse_modules(text: str) -> list[tuple[str, list]]:
    """'F(l) [ +(30) A(l*0.7) ]' -> [('F', [code]), ('[', []), ('+', [code]), ('A', [code]), (']', [])]"""
    out, i = [], 0
    while i < len(text):
        ch = text[i]
        if ch.isspace():#ignore space
            i += 1
            continue
        args = []
        i += 1
        if i < len(text) and text[i] == "(":
            depth = 1
            j = i + 1
            while depth:
                if j >= len(text):
                    raise ValueError(f"unclosed '(' in: {text}")
                #increase or decrease depth if brackets open/close
                #if neither is the case, keep the same depth
                if text[j] =="(":
                    depth +=1
                elif text[j] ==")":
                    depth -=1
                j += 1
            args = [_compile(a) for a in _split_args(text[i + 1:j - 1])]
            i = j
        #append function and its parameters separated
        out.append((ch, args))
    return out


@dataclass
class Rule:
    symbol: str
    param_names: list[str]
    condition: object = None
    successor: list = field(default_factory=list)
    probability: object = None  # weight expression (may use constants and parameters); None = 1
    text: str = ""

    def matches(self, m: Module, env: dict) -> bool:
        #check if line calls this rule and if the number of parameters are correct
        if m.symbol != self.symbol or len(m.params) != len(self.param_names):
            return False
        #check if no condition is given, or given condition is true
        return self.condition is None or bool(eval(self.condition, SAFE, env))

    def weight(self, env: dict) -> float:
        #check the probatility weight (doesn't need to be between 0 and 1)
        return 1.0 if self.probability is None else float(eval(self.probability, SAFE, env))

    def apply(self, env: dict) -> list[Module]:
        #env holds the currently chosen values for each parameter passed to the rule + global constants like maxOrder
        #all filled out rules are returned as list of modules
        return [Module(sym, tuple(eval(a, SAFE, env) for a in args)) for sym, args in self.successor]

#regular expression for parsing defined rules
RULE_PATTERN = re.compile(r"^\s*(?P<pred>\S)\s*(\((?P<params>[^)]*)\))?\s*(:(?P<cond>.*?))?->(?P<succ>.*?)(@(?P<prob>[^@]*))?$")


def parse_rules(text: str) -> list[Rule]:
    rules = []
    #the full string of one or multiple rules is checked line by line
    #return list of all the matching rules separated
    for line in text.splitlines():
        line = line.split("#", 1)[0].strip()
        if not line:
            continue
        m = RULE_PATTERN.match(line)#check if rule inside one line follows the expected pattern
        if m is None:
            raise ValueError(f"can't read rule: {line}")
        params = [p.strip() for p in (m.group("params") or "").split(",") if p.strip()]
        cond = m.group("cond")
        rules.append(Rule(
            symbol=m.group("pred"),
            param_names=params,
            condition=_compile(cond) if cond and cond.strip() else None,
            successor=_parse_modules(m.group("succ")),
            probability=_compile(m.group("prob")) if m.group("prob") else None,
            text=line,
        ))
    return rules


def parse_axiom(text: str, constants: dict | None = None) -> list[Module]:
    #get the axiom with its parameters and all the global parameters, that are currently set
    env = dict(constants or {})
    return [Module(sym, tuple(eval(a, SAFE, env) for a in args)) for sym, args in _parse_modules(text)]


# ---------------------------------------------------------------------------------------------------------------------
# Rewriting
# ---------------------------------------------------------------------------------------------------------------------

def rewrite(modules: list[Module], rules: list[Rule], constants: dict | None = None, rng: random.Random | None = None) -> list[Module]:
    """One growth step: every module is replaced (in parallel) by the successor of a matching rule."""
    rng = rng or random.Random(0)
    by_symbol: dict[str, list[Rule]] = {}
    for r in rules:
        by_symbol.setdefault(r.symbol, []).append(r)
    out = []
    for m in modules:
        candidates = []
        for r in by_symbol.get(m.symbol, ()):
            env = dict(constants or {})
            env.update(zip(r.param_names, m.params))
            if r.matches(m, env):
                candidates.append((r, env))
        if not candidates:
            out.append(m)
            continue
        if len(candidates) == 1:
            rule, env = candidates[0]
        else:
            rule, env = rng.choices(candidates, weights=[r.weight(e) for r, e in candidates])[0]
        out.extend(rule.apply(env))
    return out


def derive(axiom: str, rules: str | list[Rule], steps: int, constants: dict | None = None, seed: int = 0,
           max_modules: int = 200_000) -> list[list[Module]]:
    """All generations from the axiom (index 0) to `steps`. Same seed -> same plant."""
    rules = parse_rules(rules) if isinstance(rules, str) else rules
    rng = random.Random(seed)
    gens = [parse_axiom(axiom, constants)]
    for _ in range(steps):
        nxt = rewrite(gens[-1], rules, constants, rng)
        if len(nxt) > max_modules:
            print(f"stopped: generation {len(gens)} would have {len(nxt)} modules (max_modules = {max_modules})")
            break
        gens.append(nxt)
    return gens


# ---------------------------------------------------------------------------------------------------------------------
# Turtle interpretation
# ---------------------------------------------------------------------------------------------------------------------

def _add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def _mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _rotate(v, axis, deg):
    """Rotates v around a unit axis (right-handed)."""
    t = math.radians(deg)
    c, s = math.cos(t), math.sin(t)
    d = v[0] * axis[0] + v[1] * axis[1] + v[2] * axis[2]
    cr = _cross(axis, v)
    return (v[0] * c + cr[0] * s + axis[0] * d * (1 - c),
            v[1] * c + cr[1] * s + axis[1] * d * (1 - c),
            v[2] * c + cr[2] * s + axis[2] * d * (1 - c))


@dataclass
class Geometry:
    segments: list = field(default_factory=list)  # (start, end, width, depth)
    marks: list = field(default_factory=list)     # (symbol, position, heading, params, depth)


def interpret(modules: list[Module], step: float = 1.0, angle: float = 25.0, width: float = 1.0) -> Geometry:
    """Walks once through the modules and records segments and marked symbols."""
    pos, H, L, U = (0.0, 0.0, 0.0), (0.0, 0.0, 1.0), (-1.0, 0.0, 0.0), (0.0, -1.0, 0.0)
    w, depth, stack = width, 0, []
    geo = Geometry()
    for m in modules:
        s, p = m.symbol, m.params
        a = p[0] if p else angle
        if s in "Ff":
            new = _add(pos, _mul(H, p[0] if p else step))
            if s == "F":
                geo.segments.append((pos, new, w, depth))
            pos = new
        elif s == "+": H, L = _rotate(H, U, a), _rotate(L, U, a)
        elif s == "-": H, L = _rotate(H, U, -a), _rotate(L, U, -a)
        elif s == "&": H, U = _rotate(H, L, a), _rotate(U, L, a)
        elif s == "^": H, U = _rotate(H, L, -a), _rotate(U, L, -a)
        elif s == "\\": L, U = _rotate(L, H, a), _rotate(U, H, a)
        elif s == "/": L, U = _rotate(L, H, -a), _rotate(U, H, -a)
        elif s == "|": H, L = _mul(H, -1), _mul(L, -1)
        elif s == "!": w = p[0] if p else width
        elif s == "[":
            stack.append((pos, H, L, U, w, depth))
            depth += 1
        elif s == "]":
            pos, H, L, U, w, depth = stack.pop()
        else:
            geo.marks.append((s, pos, H, p, depth))
    return geo


# ---------------------------------------------------------------------------------------------------------------------
# Plotting
# ---------------------------------------------------------------------------------------------------------------------

def plot(geo: Geometry, title: str = "", mark_styles: dict | None = None, width_scale: float = 3.0, height: int = 600):
    """
    Interactive 3D view (plotly). Segments are grouped by width; marked symbols are drawn as points.
    mark_styles: {'O': dict(color='green', size=8), ...}; symbols not listed are drawn small and grey.
    """
    import plotly.graph_objects as go

    fig = go.Figure()
    by_width: dict[float, list] = {}
    for a, b, w, _ in geo.segments:
        by_width.setdefault(round(w, 3), []).append((a, b))
    for w, segs in sorted(by_width.items()):
        xs, ys, zs = [], [], []
        for a, b in segs:
            xs += [a[0], b[0], None]; ys += [a[1], b[1], None]; zs += [a[2], b[2], None]
        fig.add_trace(go.Scatter3d(x=xs, y=ys, z=zs, mode="lines", line=dict(color="saddlebrown", width=max(1.0, w * width_scale)),
                                   name=f"width {w:g}", showlegend=False, hoverinfo="skip"))
    styles = mark_styles or {}
    by_symbol: dict[str, list] = {}
    for s, p, *_ in geo.marks:
        by_symbol.setdefault(s, []).append(p)
    for s, pts in by_symbol.items():
        st = styles.get(s, dict(color="grey", size=2))
        fig.add_trace(go.Scatter3d(x=[p[0] for p in pts], y=[p[1] for p in pts], z=[p[2] for p in pts], mode="markers",
                                   marker=st, name=s))
    #equal scale on all axes, in a cube around the plant: always fits the view, whatever the plant's proportions
    pts = [p for a, b, *_ in geo.segments for p in (a, b)] + [m[1] for m in geo.marks] or [(0.0, 0.0, 0.0)]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    half = 0.5 * max(max(h - l for l, h in zip(lo, hi)), 1e-6) * 1.05
    axis = lambda i: dict(range=[0.5 * (lo[i] + hi[i]) - half, 0.5 * (lo[i] + hi[i]) + half], visible=False)
    fig.update_layout(title=title, height=height, margin=dict(l=0, r=0, t=40, b=0),
                      scene=dict(aspectmode="cube", xaxis=axis(0), yaxis=axis(1), zaxis=axis(2),
                                 camera=dict(eye=dict(x=0.35, y=-1.2, z=0.15), up=dict(x=0, y=0, z=1))))
    return fig


def summary(modules: list[Module]) -> str:
    """Module counts per symbol, e.g. 'F: 120, A: 32, [: 31, ]: 31'."""
    counts: dict[str, int] = {}
    for m in modules:
        counts[m.symbol] = counts.get(m.symbol, 0) + 1
    return ", ".join(f"{k}: {v}" for k, v in sorted(counts.items(), key=lambda kv: -kv[1]))
