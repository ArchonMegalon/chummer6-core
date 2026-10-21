using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Shared by full compilation and selected reconstruction. Source capture is
// admitted separately; merely holding the draft is never execution evidence.
internal static class CharacterCreationHeritageGraph832
{
    internal static void ApplyMovement(XElement root, CharacterCreationPriorityHeritageSelection heritage)
    {
        if (heritage.Movement.IsSpecial) return;
        SetMovementField(root, "movement", string.Empty);
        foreach (var (field, rate) in Rates(heritage.Movement))
            SetMovementField(root, field, Rate(rate));
    }

    internal static CharacterCreationMetatypeMovementProjection? MatchingMovement(
        XElement root, CharacterCreationPriorityHeritageSelection heritage)
    {
        if (heritage.Movement.IsSpecial || root.Elements("movement").ToArray() is not [var movement]
            || movement.HasAttributes || movement.HasElements || movement.Value.Length != 0) return null;
        foreach (var (field, rate) in Rates(heritage.Movement))
            if (root.Elements(field).ToArray() is not [var node]
                || node.HasAttributes || node.HasElements || node.Value != Rate(rate)) return null;
        return heritage.Movement;
    }

    private static IEnumerable<(string Field, CharacterCreationMetatypeMovementRate Rate)> Rates(
        CharacterCreationMetatypeMovementProjection movement)
    {
        yield return ("walk", movement.Walk);
        yield return ("run", movement.Run);
        yield return ("sprint", movement.Sprint);
    }

    private static void SetMovementField(XElement root, string field, string value)
    {
        var nodes = root.Elements(field).Take(2).ToArray();
        if (nodes.Length > 1) throw new InvalidDataException("Duplicate movement field.");
        if (nodes.Length == 1) nodes[0].Value = value;
        else root.Add(new XElement(field, value));
    }

    private static string Rate(CharacterCreationMetatypeMovementRate rate) => string.Join('/',
        rate.Ground.ToString(CultureInfo.InvariantCulture), rate.Swim.ToString(CultureInfo.InvariantCulture),
        rate.Fly.ToString(CultureInfo.InvariantCulture));
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Read-only across the Infrastructure boundary. Only the actual compiler
    // caller can attach a result after comparing the final graph's fields.
    [System.Text.Json.Serialization.JsonIgnore]
    public CharacterCreationMetatypeMovementProjection? CompiledMovement832 { get; internal init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SourceExecution832 { get; internal init; }
}
