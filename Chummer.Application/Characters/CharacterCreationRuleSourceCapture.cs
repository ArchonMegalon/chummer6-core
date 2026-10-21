using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Engine-local retention only. The exported bytes contain private paths, manifests,
/// raw rule XML and character inputs. Never send them to prompts, logs or public receipts.
/// Integrity is not authorization, source coverage, or a substitute for live re-admission.
/// </summary>
public abstract class CharacterCreationRuleSourceCapture
{
    public abstract string IntegrityDigest { get; }
    public abstract string EffectivePolicyDigest { get; }
    public virtual Chummer.Contracts.Characters.CharacterCreationSourcebookSelectionAuthority? SourcebookSelection => null;

    /// <summary>Returns a detached private archive; mutating it cannot change this capture.</summary>
    public abstract byte[] ExportPrivateInputs();

    /// <summary>Creates a fresh resolver with no ambient filesystem fallback.</summary>
    public abstract ICharacterSourceDataResolver CreateReplayResolver();

    // Opt-in capabilities. Historical capture implementations cannot claim execution.
    public virtual bool MatchesOriginalCharacter(string characterXml) => false;
    public virtual CharacterCreationRuleSourceResolution? CreateExecutionResolution() => null;
    public virtual CharacterCreationRuleSourceResolution? CreateExecutionResolution(string? schema)
        => schema is null or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 ? CreateExecutionResolution() : null;
}
