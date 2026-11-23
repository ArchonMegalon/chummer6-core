using Chummer.Contracts.Content;

namespace Chummer.Application.Characters;

/// <summary>
/// Opt-in, synchronous capture of private rule inputs from one admitted source scope.
/// This is neither rules coverage nor authority to write a live character.
/// </summary>
public interface ICharacterCreationRuleSourceCapture
{
    bool TryCaptureRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture);

    // Explicit opt-in: original bytes and current order, never a desktop-equivalence claim.
    // Old implementors/scopes refuse instead of silently returning an unbound capture.
    bool TryCaptureChummer5aRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture,
        out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
    {
        capture = null;
        diagnostics = [new(RulePackResolutionDiagnosticKinds.CapabilityBlocked,
            RulePackResolutionSeverityLevels.Error, "capture", "This source scope does not support the compatibility profile.")];
        return false;
    }

    // Versioned new imports must traverse the operation scope, not cast past it.
    // Legacy implementors may satisfy only the historical version explicitly.
    bool TryCaptureChummer5aRuleSources(string characterXml, string compatibilityProfile,
        out CharacterCreationRuleSourceCapture? capture,
        out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
    {
        if (compatibilityProfile == Chummer5aLegacyImportCompatibility.ProfileV1)
            return TryCaptureChummer5aRuleSources(characterXml, out capture, out diagnostics);
        capture = null;
        diagnostics = [new(RulePackResolutionDiagnosticKinds.CapabilityBlocked,
            RulePackResolutionSeverityLevels.Error, "capture",
            "This source scope does not support the requested compatibility profile.",
            MessageKey: "chummer5a-import.unsupported-profile")];
        return false;
    }

    // Separate recipe. Old implementors and old entry points do not opt in.
    bool TryCaptureSourcebookRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture)
    { capture = null; return false; }
}
