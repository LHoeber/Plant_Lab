using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Minimal JSON writer for the logs: one object per call chain, compact, culture-independent numbers.
/// Usage: new Json().Add("tick", 3).Add("view", new Json().Add("az", 20f)).ToString()
/// (Unity's JsonUtility can't write dictionaries or nested variable content, hence this.)
/// </summary>
public class Json
{
    readonly StringBuilder sb = new StringBuilder("{");
    bool first = true;

    Json Key(string name)
    {
        if (!first) sb.Append(',');
        first = false;
        Str(sb, name);
        sb.Append(':');
        return this;
    }

    public Json Add(string name, int v) { Key(name).sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
    public Json Add(string name, long v) { Key(name).sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
    public Json Add(string name, bool v) { Key(name).sb.Append(v ? "true" : "false"); return this; }
    public Json Add(string name, float v) { Key(name); Num(sb, v); return this; }
    public Json Add(string name, double v) { Key(name); Num(sb, v); return this; }
    public Json Add(string name, string v) { Key(name); if (v == null) sb.Append("null"); else Str(sb, v); return this; }
    public Json Add(string name, Json v) { Key(name).sb.Append(v == null ? "null" : v.ToString()); return this; }

    public Json Add(string name, IEnumerable<int> v)
    {
        Key(name).sb.Append('[');
        bool f = true;
        foreach (int x in v) { if (!f) sb.Append(','); f = false; sb.Append(x.ToString(CultureInfo.InvariantCulture)); }
        sb.Append(']');
        return this;
    }

    public Json Add(string name, IEnumerable<bool> v)
    {
        Key(name).sb.Append('[');
        bool f = true;
        foreach (bool x in v) { if (!f) sb.Append(','); f = false; sb.Append(x ? '1' : '0'); }//compact: 0/1
        sb.Append(']');
        return this;
    }

    public Json Add(string name, IEnumerable<Json> v)
    {
        Key(name).sb.Append('[');
        bool f = true;
        foreach (Json x in v) { if (!f) sb.Append(','); f = false; sb.Append(x.ToString()); }
        sb.Append(']');
        return this;
    }

    public override string ToString() => sb.ToString() + "}";

    static void Num(StringBuilder sb, double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) { sb.Append("null"); return; }
        sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
    }

    static void Num(StringBuilder sb, float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) { sb.Append("null"); return; }
        sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
    }

    static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
