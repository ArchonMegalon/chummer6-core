using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Typed specialization mechanics remain solely in CharacterCareerSkillSpecializationRules.
// This adapter binds their exact selection/plan to original-operation identity and the
// selected saved XML serializer; it is not persistence or a caller-supplied price path.
public static class CharacterCareerSpecializationAuthority
{
    public static bool IsRequest(CharacterCareerSpecializationRequest? request)
        => request is { SkillIdentity: not null, Selection: not null }
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.SpecializationId != Guid.Empty
            && request.ExpenseId != Guid.Empty && request.OperationId != request.SpecializationId
            && request.OperationId != request.ExpenseId && request.SpecializationId != request.ExpenseId
            && request.OperationId != request.SkillIdentity.SkillId
            && request.OperationId != request.SkillIdentity.SourceSkillId
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal.Ticks % TimeSpan.TicksPerSecond == 0;

    public static bool IsQuote(CharacterCareerSpecializationRequest request,
        CharacterCareerSkillSpecializationQuote? quote)
        => IsRequest(request) && CharacterCareerSkillSpecializationRules.IsCoherent(quote)
            && quote!.Identity == request.SkillIdentity && quote.Selection == request.Selection
            && CharacterCareerSkillSpecializationRules.TryPlanAdd(quote,
                quote.CharacterRevision, quote.SourceRevision, quote.RuleDigest, quote.LogicalRevision,
                true, request.SpecializationId, request.ExpenseId, request.ExpenseDateLocal, out _);

    public static string Materialize(string xml, CharacterCareerSpecializationCommand command)
    {
        if (!CharacterCareerSpecializationTransaction.IsConfirmedCommand(command))
            throw new InvalidOperationException("specialization_exact_confirmed_plan_required");
        var quote = command.Quote;
        // SourceRevision is intentionally distinct: a specialization changes the complete
        // character revision, including custom knowledge, without changing its source identity.
        var root = SpecializationXmlReader.RequireCharacterRoot(XDocument.Parse(xml, LoadOptions.PreserveWhitespace));
        if (CharacterCareerSpecializationTransaction.Hash(root.ToString(SaveOptions.DisableFormatting)) != quote.CharacterRevision)
            throw new InvalidOperationException("specialization_character_revision_changed");
        var request = command.Request;
        return CareerSkillSpecializationMutation.Apply(xml, new(request.WorkspaceId,
            command.Binding.ContentRevision, quote, quote.CharacterRevision, quote.SourceRevision,
            quote.RuleDigest, quote.LogicalRevision, true, request.SpecializationId, request.ExpenseId,
            request.ExpenseDateLocal));
    }
}
