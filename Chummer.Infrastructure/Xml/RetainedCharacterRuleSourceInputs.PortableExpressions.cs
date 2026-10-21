using System.Xml.XPath;

namespace Chummer.Infrastructure.Xml;

internal sealed partial class RetainedCharacterRuleSourceInputs
{
    // Only retained source XML at a schema-defined bonus path uses this classifier.
    // Document selectors are syntax, while quoted literals still undergo the
    // ordinary privacy check. The original value/bytes are never rewritten.
    private static void ValidatePortableRuleExpression(string value, bool allowConditionProse = false)
    {
        string decoded = System.Net.WebUtility.HtmlDecode(value);
        if (decoded != value) ValidatePortableRuleExpressionTokens(decoded, allowConditionProse);
        ValidatePortableRuleExpressionTokens(value, allowConditionProse);
    }

    private static void ValidatePortableRuleExpressionTokens(string value, bool allowConditionProse)
    {
        // Conditions also carry human descriptions. Recognize only a bounded word
        // grammar with a standalone "and/or" conjunction, never arbitrary failed
        // XPath. Other slashes or expression punctuation cannot enter this branch.
        // Selector attributes never opt into this source-condition grammar.
        if (allowConditionProse && IsPortableConditionProse(value))
        {
            ValidatePortableTransferText(value);
            return;
        }
        char quote = '\0';
        int quoteStart = 0;
        int depth = 0, maximumDepth = 0;
        bool operand = true, admittedRoot = false;
        char[]? classified = null;
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    if (value.AsSpan(quoteStart, i - quoteStart).TrimStart()
                        .StartsWith("/", StringComparison.Ordinal)) throw Rejected();
                    quote = '\0';
                    operand = false;
                }
                continue;
            }
            if (char.IsWhiteSpace(current)) continue;
            if (current is '\'' or '"') { quote = current; quoteStart = i + 1; continue; }
            if (current is '(' or '[')
            {
                maximumDepth = Math.Max(maximumDepth, ++depth);
                operand = true;
                continue;
            }
            if (current is ')' or ']') { depth--; operand = false; continue; }
            if (char.IsLetter(current) || current == '_')
            {
                int start = i;
                while (i + 1 < value.Length && (char.IsLetterOrDigit(value[i + 1])
                    || value[i + 1] is '_' or '-' or '.' or ':')) i++;
                string token = value[start..(i + 1)];
                // Both an axis and a QName prefix expect a node test. In ns:*
                // the wildcard completes that test; it is not multiplication.
                operand = token.EndsWith(":", StringComparison.Ordinal)
                    || !operand && token is "and" or "or" or "div" or "mod";
                continue;
            }
            if (char.IsDigit(current) || current == '.')
            {
                while (i + 1 < value.Length && (char.IsDigit(value[i + 1]) || value[i + 1] == '.')) i++;
                operand = false;
                continue;
            }
            if (current == '*') { operand = !operand; continue; }
            if (current != '/') { operand = true; continue; }
            // XPath word operators (e.g. "div/home") also start an operand;
            // whitespace and punctuation alone are not a sufficient lexer.
            if (operand)
            {
                ReadOnlySpan<char> tail = value.AsSpan(i);
                if (!tail.StartsWith("/chummer/", StringComparison.Ordinal)
                    && !tail.StartsWith("/character/", StringComparison.Ordinal)
                    && !tail.StartsWith("/spell/", StringComparison.Ordinal)) throw Rejected();
                admittedRoot = true;
            }
            // Leave drive/URI separators visible to the ordinary private-text gate.
            if (i > 0 && value[i - 1] == ':')
            {
                if (i + 1 < value.Length && value[i + 1] == '/') i++;
                operand = true;
                continue;
            }
            classified ??= value.ToCharArray();
            classified[i] = '#'; // Scratch only, including relative path separators.
            if (i + 1 < value.Length && value[i + 1] == '/') classified[++i] = '#';
            operand = true;
        }
        ValidatePortableTransferText(classified is null ? value : new string(classified));
        if (!admittedRoot) return;
        // Bound the syntax parser independently of the archive size. It never
        // evaluates a selector, binds a context, opens a file or grants execution.
        if (value.Length > 4096 || maximumDepth > 32 || quote != '\0' || depth != 0) throw Rejected();
        try { _ = XPathExpression.Compile(value); }
        catch (XPathException) { throw Rejected(); }
    }

    // Shipped weapon capacity alternatives use numeric capacity(feed-code)/capacity(feed-code).
    // Only the unqualified weapon/ammo scalar opts in. This is a complete bounded grammar,
    // not an exception for arbitrary slash-containing text or failed XPath compilation.
    // Neither the retained source bytes nor their identity are changed.
    private static void ValidatePortableRuleAmmunitionCapacity(string value)
    {
        static bool CapacityAlternatives(string value)
        {
            if (value.Length > 256) return false;
            string[] alternatives = value.Trim().Split('/');
            if (alternatives.Length is < 2 or > 8) return false;
            foreach (string alternative in alternatives)
            {
                ReadOnlySpan<char> atom = alternative.AsSpan().Trim();
                int open = atom.IndexOf('(');
                if (open is < 1 or > 8 || atom.Length < open + 3 || atom[^1] != ')'
                    || atom[..open].IndexOfAnyExcept("0123456789") >= 0) return false;
                ReadOnlySpan<char> feed = atom[(open + 1)..^1];
                if (feed.Length is < 1 or > 16) return false;
                foreach (char character in feed)
                    if (!char.IsAsciiLetter(character)) return false;
            }
            return true;
        }
        static void Check(string text)
        {
            if (!CapacityAlternatives(text)) ValidatePortableTransferText(text);
        }
        Check(value);
        string decoded = System.Net.WebUtility.HtmlDecode(value);
        if (decoded != value) Check(decoded);
    }

    private static bool IsPortableConditionProse(string value)
    {
        if (value.Length > 4096) return false;
        string[] words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool conjunction = false;
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word == "and/or")
            {
                if (i == 0 || i == words.Length - 1) return false;
                conjunction = true;
                continue;
            }
            if (!word.Any(char.IsLetter) || word.Any(c => !char.IsLetterOrDigit(c)
                && c is not (',' or '.' or '\'' or '-'))) return false;
        }
        return conjunction;
    }

    // These are structural source-schema paths, not filenames, record identities,
    // or user-provided schemaLocation declarations. Namespaced ancestors never match.
    private static bool IsPortableRuleBonusPath(string path) => path is
        "chummer/armors/armor/bonus" or "chummer/armors/armor/wirelessbonus"
        or "chummer/biowares/bioware/bonus" or "chummer/complexforms/complexform/bonus"
        or "chummer/cyberwares/cyberware/bonus" or "chummer/echoes/echo/bonus"
        or "chummer/enhancements/enhancement/bonus" or "chummer/gears/gear/bonus"
        or "chummer/grades/grade/bonus" or "chummer/martialarts/martialart/bonus"
        or "chummer/mentors/mentor/bonus" or "chummer/mentors/mentor/choices/choice/bonus"
        or "chummer/metamagics/metamagic/bonus" or "chummer/metatypes/metatype/bonus"
        or "chummer/metatypes/metatype/metavariants/metavariant/bonus"
        or "chummer/mods/mod/bonus" or "chummer/options/option/bonus"
        or "chummer/powers/power/bonus" or "chummer/programs/program/bonus"
        or "chummer/qualities/quality/bonus" or "chummer/qualities/quality/firstlevelbonus"
        or "chummer/spells/spell/bonus" or "chummer/techniques/technique/bonus"
        or "chummer/traditions/tradition/bonus";

    private static bool IsPortableRuleCondition(string parent) => parent is
        "activeskillkarmacost" or "activeskillkarmacostmultiplier"
        or "activeskillpointcost" or "activeskillpointcostmultiplier"
        or "attributekarmacost" or "attributekarmacostmultiplier"
        or "attributepointcost" or "attributepointcostmultiplier"
        or "knowledgeskillkarmacost" or "knowledgeskillkarmacostmin"
        or "knowledgeskillkarmacostmultiplier" or "knowledgeskillpointcost"
        or "knowledgeskillpointcostmultiplier" or "limitmodifier"
        or "skillattribute" or "skillcategory" or "skillcategorykarmacost"
        or "skillcategorykarmacostmultiplier" or "skillcategorypointcost"
        or "skillcategorypointcostmultiplier" or "skillcategoryspecializationkarmacost"
        or "skillcategoryspecializationkarmacostmultiplier" or "skillgroup"
        or "skillgroupcategorykarmacost" or "skillgroupcategorykarmacostmultiplier"
        or "skillgroupcategorypointcost" or "skillgroupcategorypointcostmultiplier"
        or "skillgroupkarmacost" or "skillgroupkarmacostmultiplier"
        or "skillgrouppointcost" or "skillgrouppointcostmultiplier"
        or "skilllinkedattribute" or "specificskill" or "spellcategory";
}
