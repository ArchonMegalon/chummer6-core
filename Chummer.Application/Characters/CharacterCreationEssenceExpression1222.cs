using System.Globalization;

namespace Chummer.Application.Characters;

// Bounded decimal expression grammar. No XPath execution, I/O or fallback to 0.
public static class CharacterCreationEssenceExpression1222
{
    public static decimal Evaluate(string text, IReadOnlyDictionary<string, decimal> values)
    {
        if (text.Length is 0 or > 2048) throw new InvalidDataException("Invalid Essence expression.");
        if (text.StartsWith("FixedValues(", StringComparison.Ordinal) && text.EndsWith(')'))
        {
            string[] list = text[12..^1].Split(',');
            if (!values.TryGetValue("Rating", out var rating) || rating != decimal.Truncate(rating) || list.Length == 0)
                throw new InvalidDataException("Invalid fixed Essence rating.");
            text = list[Math.Clamp(checked((int)rating), 1, list.Length) - 1].Trim().Trim('[', ']');
        }
        var parser = new Parser(text, values);
        decimal value = parser.Sum(); parser.White();
        if (!parser.End) throw new InvalidDataException("Unsupported Essence expression.");
        return value;
    }
    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> values)
    {
        private int position, depth;
        internal bool End => position == text.Length;
        internal void White() { while (!End && char.IsWhiteSpace(text[position])) position++; }
        private bool Take(char c) { White(); if (End || text[position] != c) return false; position++; return true; }
        internal decimal Sum()
        {
            decimal n = Product();
            while (true) { if (Take('+')) n = checked(n + Product()); else if (Take('-')) n = checked(n - Product()); else return n; }
        }
        private decimal Product()
        {
            decimal n = Atom();
            while (true)
            {
                if (Take('*')) n = checked(n * Atom());
                else if (Take('/')) n /= Atom();
                else { White(); if (position + 3 <= text.Length && text.AsSpan(position, 3).SequenceEqual("div")) { position += 3; n /= Atom(); } else return n; }
            }
        }
        private decimal Atom()
        {
            if (++depth > 32) throw new InvalidDataException("Essence expression is too deep.");
            try
            {
                if (Take('+')) return Atom(); if (Take('-')) return -Atom();
                if (Take('(')) { decimal n = Sum(); if (!Take(')')) throw new InvalidDataException(); return n; }
                White(); int start = position;
                if (!End && (char.IsLetter(text[position]) || text[position] == '{'))
                {
                    bool brace = Take('{'); start = position;
                    while (!End && char.IsLetterOrDigit(text[position])) position++;
                    string key = text[start..position];
                    if (brace && !Take('}') || !values.TryGetValue(key, out var result)) throw new InvalidDataException("Unknown Essence variable.");
                    return result;
                }
                while (!End && (char.IsDigit(text[position]) || text[position] == '.')) position++;
                if (position == start) throw new InvalidDataException("Invalid Essence term.");
                return decimal.Parse(text[start..position], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }
            finally { depth--; }
        }
    }
}
