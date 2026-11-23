using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
namespace Chummer.Application.Characters;
public sealed partial class CharacterCurrentRuleEnvironmentExecution
{
    public CharacterCareerSkillGroupAdvanceInput? ReadCareerSkillGroupInput6494(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.ReadCurrentSkillGroupInput6494(
            repeated.context, catalog, source, groupId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

}
