using System.Text.Json;

namespace Chummer.Contracts.Characters;

public static partial class CharacterCreationLoadoutRules
{
    /// <summary>Admit one detached catalog for a bounded read-only planning operation.</summary>
    public static ProjectionSession? CreateProjectionSession(CharacterCreationLoadoutAuthority? authority)
        => ProjectionSession.Create(authority);

    public sealed class ProjectionSession
    {
        private readonly CharacterCreationLoadoutAuthority _authority;

        private ProjectionSession(CharacterCreationLoadoutAuthority authority) => _authority = authority;

        internal static ProjectionSession? Create(CharacterCreationLoadoutAuthority? authority)
        {
            if (authority is null) return null;
            var frozen = Snapshot(authority);
            return IsValidAuthority(frozen) ? new(frozen) : null;
        }

        public bool TryProjectBasket(CharacterCreationLoadoutSelections? selections,
            out CharacterCreationLoadoutLine[] lines, out decimal cost, out string[] blockers)
        {
            // No caller-owned arrays survive admission, including the arrays in
            // returned source references. Each basket still goes through the
            // normal kind, availability, quantity, pricing and overflow checks.
            var frozen = selections is null ? null : Snapshot(selections);
            bool accepted = Project(frozen, _authority.Options, _authority.MaximumBasketLines,
                _authority.MaximumQuantityPerLine, true, out lines, out cost, out blockers);
            lines = Snapshot(lines);
            return accepted;
        }

        private static T Snapshot<T>(T value)
            => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    }
}
