using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A tiny 5x7 pixel font for the panel: screen texts (retro look) and the numbers on buttons and LEDs.
/// Drawn into textures with point filtering, so no font assets are needed. Upper case only (lower case is converted).
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7;
    public const int CellWidth = 6, CellHeight = 9;//glyph plus spacing

    //each glyph: 7 rows from the top, 5 bits per row (0x10 = leftmost pixel)
    static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
    {
        ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
        ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
        ['D'] = new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
        ['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
        ['F'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
        ['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
        ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
        ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
        ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
        ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
        ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
        ['N'] = new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
        ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
        ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
        ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
        ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
        ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
        ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A },
        ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
        ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
        ['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
        ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
        ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
        ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
        ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
        ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
        ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
        ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
        ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
        ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
        ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
        [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
        ['.'] = new byte[] { 0, 0, 0, 0, 0, 0x0C, 0x0C },
        [','] = new byte[] { 0, 0, 0, 0, 0x0C, 0x04, 0x08 },
        [':'] = new byte[] { 0, 0x0C, 0x0C, 0, 0x0C, 0x0C, 0 },
        ['!'] = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0, 0x04 },
        ['?'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0, 0x04 },
        ['\''] = new byte[] { 0x04, 0x04, 0x08, 0, 0, 0, 0 },
        ['-'] = new byte[] { 0, 0, 0, 0x1F, 0, 0, 0 },
        ['/'] = new byte[] { 0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10 },
        ['('] = new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 },
        [')'] = new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 },
        ['%'] = new byte[] { 0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03 },
        ['>'] = new byte[] { 0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08 },
        ['<'] = new byte[] { 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02 },
        ['_'] = new byte[] { 0, 0, 0, 0, 0, 0, 0x1F },
    };

    /// <summary>
    /// Draws text into a pixel buffer (row 0 = bottom, as in Texture2D), starting at column x, line top yTop
    /// (counted from the top). '\n' starts a new line. Unknown characters are drawn as a space.
    /// </summary>
    public static void Draw(Color32[] px, int width, int height, string text, int x, int yTop, int scale, Color32 color)
    {
        int cx = x, cy = yTop;
        foreach (char raw in text)
        {
            if (raw == '\n') { cx = x; cy += CellHeight * scale; continue; }
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(raw), out byte[] g)) g = Glyphs[' '];
            for (int row = 0; row < GlyphHeight; row++)
                for (int col = 0; col < GlyphWidth; col++)
                {
                    if ((g[row] & (0x10 >> col)) == 0) continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px0 = cx + col * scale + sx;
                            int py0 = height - 1 - (cy + row * scale + sy);
                            if (px0 >= 0 && px0 < width && py0 >= 0 && py0 < height) px[py0 * width + px0] = color;
                        }
                }
            cx += CellWidth * scale;
        }
    }

    /// <summary>Breaks text into lines of at most `columns` characters, at spaces where possible (keeps '\n').</summary>
    public static string Wrap(string text, int columns)
    {
        var result = new System.Text.StringBuilder();
        foreach (string paragraph in text.Split('\n'))
        {
            int lineLength = 0;
            foreach (string word in paragraph.Split(' '))
            {
                if (lineLength > 0 && lineLength + 1 + word.Length > columns) { result.Append('\n'); lineLength = 0; }
                else if (lineLength > 0) { result.Append(' '); lineLength++; }
                result.Append(word);
                lineLength += word.Length;
            }
            result.Append('\n');
        }
        return result.ToString().TrimEnd('\n');
    }

    /// <summary>A texture with white text on transparent, at its natural pixel size (point filtered, no blurring).</summary>
    public static Texture2D Label(string text, int padding = 1)
    {
        string[] lines = text.Split('\n');
        int cols = 0;
        foreach (string l in lines) cols = Mathf.Max(cols, l.Length);
        int w = cols * CellWidth - 1 + 2 * padding, h = lines.Length * CellHeight - 2 + 2 * padding;
        var px = new Color32[w * h];
        Draw(px, w, h, text, padding, padding, 1, new Color32(255, 255, 255, 255));
        return MakeTexture(px, w, h, "Label " + text);
    }

    /// <summary>A filled triangle pointing up (for the arrow buttons), white on transparent.</summary>
    public static Texture2D Triangle(int size)
    {
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                bool inside = v > 0.12f && v < 0.88f && Mathf.Abs(u - 0.5f) < 0.42f * (0.88f - v) / 0.76f;
                if (inside) px[y * size + x] = new Color32(255, 255, 255, 255);
            }
        return MakeTexture(px, size, size, "Arrow");
    }

    /// <summary>A filled circle (LEDs), white on transparent.</summary>
    public static Texture2D Circle(int size)
    {
        var px = new Color32[size * size];
        float r = 0.5f * size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                if (dx * dx + dy * dy <= r * r) px[y * size + x] = new Color32(255, 255, 255, 255);
            }
        return MakeTexture(px, size, size, "LED");
    }

    public static Texture2D MakeTexture(Color32[] px, int w, int h, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
