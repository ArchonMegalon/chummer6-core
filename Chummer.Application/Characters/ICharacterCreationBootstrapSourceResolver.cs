namespace Chummer.Application.Characters;

/// <summary>
/// Optional source-owned preparation for a NEW native SR5 bootstrap document.
/// Resolves its exact ordered profile selection, seeds only the saved directory
/// names, and fully admits the resulting XML and context from the same live
/// source snapshot. No caller-supplied directory list or authority is accepted.
/// Saved/retained documents must use the ordinary strict context path instead.
/// Missing capability or false means native creation must fail closed.
/// </summary>
public interface ICharacterCreationBootstrapSourceResolver
{
    bool TryPrepareNativeSr5Creation(
        string initialCharacterXml,
        out string characterXml,
        out ICharacterSourceDataContext? sourceContext);
}
