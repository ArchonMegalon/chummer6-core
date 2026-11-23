namespace Chummer.Application.Characters;

/// <summary>
/// An explicit complete-source producer binds the selected standalone file in
/// its configured source namespace. Ordinary character import never opts in.
/// The returned source still uses the existing capture and admission boundaries.
/// </summary>
public interface ICharacterRetainedSettingsExportSource
{
    ICharacterSourceDataResolver ForCompleteSourceExport();
}
