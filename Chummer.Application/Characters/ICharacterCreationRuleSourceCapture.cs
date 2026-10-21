namespace Chummer.Application.Characters;

/// <summary>
/// Opt-in, synchronous capture of private rule inputs from one admitted source scope.
/// This is neither rules coverage nor authority to write a live character.
/// </summary>
public interface ICharacterCreationRuleSourceCapture
{
    bool TryCaptureRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture);

    // Separate recipe. Old implementors and old entry points do not opt in.
    bool TryCaptureSourcebookRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture)
    { capture = null; return false; }
}
