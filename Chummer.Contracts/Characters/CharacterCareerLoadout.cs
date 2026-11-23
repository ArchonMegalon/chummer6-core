namespace Chummer.Contracts.Characters;

public enum CharacterCareerLoadoutAction { Save, Rename, Delete, Apply }

// Configuration only. Original commands, pending custody and receipts use the
// existing Gear transaction ledger and native journal, never this collection.
public sealed record CharacterCareerLoadoutItem(string Identity, string Fingerprint, bool? Equipped);
public sealed record CharacterCareerLoadoutPreset(Guid Id, string Name, IReadOnlyList<CharacterCareerLoadoutItem> Items);
public sealed record CharacterCareerLoadoutNode(CharacterCareerLoadoutItem Item, string DisplayPath,
    bool CanChange, string? Limitation, CharacterGearEquipmentState? Gear = null, Guid? ArmorId = null);
public sealed record CharacterCareerLoadoutCatalog(IReadOnlyList<CharacterCareerLoadoutPreset> Presets,
    IReadOnlyList<CharacterCareerLoadoutNode> Inventory);
public sealed record CharacterCareerLoadoutSelection(CharacterCareerLoadoutAction Action, Guid PresetId, string? Name = null);
public sealed record CharacterCareerLoadoutChange(CharacterCareerLoadoutNode Before, bool Equipped);
public sealed record CharacterCareerLoadoutPlan(CharacterCareerLoadoutSelection Selection,
    IReadOnlyList<CharacterCareerLoadoutPreset> BeforePresets,
    IReadOnlyList<CharacterCareerLoadoutPreset> AfterPresets,
    IReadOnlyList<CharacterCareerLoadoutChange> Changes);
public sealed record CharacterCareerLoadoutReview(CharacterCareerLoadoutPlan? Plan, bool NoOp,
    IReadOnlyList<string> Blockers);
