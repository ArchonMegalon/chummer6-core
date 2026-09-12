namespace Chummer.Application.Characters;

/// <summary>
/// Detached internal source observation, not an avatar response or an authorization
/// capability. Settings identity is not a runtime-profile alias. Input digests and
/// the digest of the exact returned UTF-8 XML representation remain distinct.
/// </summary>
public sealed class CharacterRuleSourceCapture
{
    public CharacterRuleSourceCapture(
        string rawCharacterXmlDigest,
        string settingsProfileId,
        IReadOnlyList<string> enabledSourcebooks,
        string rawProfileInputsDigest,
        string effectiveReferencesInputsDigest,
        string selectedReferencesCustomDataInputsDigest,
        string effectiveReferencesXml,
        string effectiveReferencesXmlDigest)
    {
        RawCharacterXmlDigest = rawCharacterXmlDigest;
        SettingsProfileId = settingsProfileId;
        EnabledSourcebooks = Array.AsReadOnly(enabledSourcebooks.ToArray());
        RawProfileInputsDigest = rawProfileInputsDigest;
        EffectiveReferencesInputsDigest = effectiveReferencesInputsDigest;
        SelectedReferencesCustomDataInputsDigest = selectedReferencesCustomDataInputsDigest;
        EffectiveReferencesXml = effectiveReferencesXml;
        EffectiveReferencesXmlDigest = effectiveReferencesXmlDigest;
    }

    public string RawCharacterXmlDigest { get; }
    public string SettingsProfileId { get; }
    public IReadOnlyList<string> EnabledSourcebooks { get; }
    public string RawProfileInputsDigest { get; }
    public string EffectiveReferencesInputsDigest { get; }
    public string SelectedReferencesCustomDataInputsDigest { get; }
    public string EffectiveReferencesXml { get; }
    public string EffectiveReferencesXmlDigest { get; }

    public static CharacterRuleSourceCapture Unavailable { get; } = new(
        string.Empty, string.Empty, [], string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}
