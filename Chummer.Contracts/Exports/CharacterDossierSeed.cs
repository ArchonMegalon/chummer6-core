using System;
using System.Collections.Generic;

namespace Chummer.Contracts.Exports
{
    public record CharacterDossierSeed
    {
        public string Metatype { get; init; } = string.Empty;
        public IReadOnlyList<string> RoleTags { get; init; } = Array.Empty<string>();
        public string MoodTags { get; init; } = string.Empty;
    }
}
