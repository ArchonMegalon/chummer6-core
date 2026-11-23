namespace Chummer.Application.Characters;

public abstract partial class CharacterCreationRuleSourceResolution
{
    // Default deny. Only the retained engine that performed this exact original
    // invocation can return a leaf execution; public source reports cannot.
    public virtual bool TryGetExecutedProfileMetadata1209(CharacterCreationRuleSourceScopeInvocation invocation,
        string field, out string? digest)
    {
        digest = null;
        return false;
    }
}
