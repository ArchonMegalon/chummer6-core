namespace Chummer.Application.Characters;

// Private concrete-issuer seam. Null preserves independent checks for reports
// that are not deeply detached. False is a refusal, never a fallback permit.
// The caller supplies its private read-only report arrays, not caller collections.
internal interface IRetainedOperationReportIssuer8005
{
    bool? ValidateRetainedOperationReports8005(CharacterCurrentRuleEnvironmentContext current,
        IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs,
        IReadOnlyList<CharacterCurrentRuleDomainExecution> executions,
        CancellationToken cancellationToken);
}

public sealed partial class CharacterCurrentRuleEnvironmentExecution
{
    // Internal implementation seam, not a public purity claim. The Infrastructure
    // issuer owns the complete detached graph, concrete capture and sealed parser.
    // This only attests this execution's original issuer/frozen reports; it grants
    // no owner, source-copy, target or publication authority.
    internal bool ValidateOwnedOperationObservation3287(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.VerifyRetainedArchive() || context.RetainedEvaluation2764 is not null
            || outcomeDigest != OutcomeDigest(catalogs, executions)
            || catalogs.Count != 14 || executions.Count != 14) return false;
        if (!ValidateIssuedReports8139(context, resolution, catalogs, executions, cancellationToken)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        return context.VerifyRetainedArchive();
    }

    // Reuse only the concrete issuer's existing detached-report attestation.
    // Its two fresh complete mutable observations surround a callback-free
    // identity check. A refusal stays a refusal; unsupported/public reports
    // retain every independent check. The caller still owns all archive,
    // source, owner, record and lifetime fences around this helper.
    private static bool ValidateIssuedReports8139(CharacterCurrentRuleEnvironmentContext current,
        CharacterCreationRuleSourceResolution issuer,
        IReadOnlyList<CharacterCurrentRuleCatalogValidation> reports,
        IReadOnlyList<CharacterCurrentRuleDomainExecution> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool? retainedReports = (issuer as IRetainedOperationReportIssuer8005)
            ?.ValidateRetainedOperationReports8005(current, reports, results, cancellationToken);
        if (retainedReports is { } valid) return valid;
        for (int index = 0; index < reports.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!issuer.HasValidatedCurrentRuleCatalog(current, reports[index])
                || !issuer.HasExecutedCurrentRuleDomain(current, reports[index], results[index])) return false;
        }
        return true;
    }

    // Only operation-owned issuers can reach this overload. Public comparisons
    // continue to re-evaluate both inputs. The operation checks live inputs before
    // and after this call, including exceptions, cancellation and reentrancy.
    internal CharacterCurrentRuleEffectComparison? CompareOwnedOperationEffects3287(
        CharacterCurrentRuleEnvironmentExecution previous, CancellationToken cancellationToken)
    {
        if (context.Current.Id != previous.context.Current.Id
            || !previous.ValidateOwnedOperationObservation3287(cancellationToken)
            || !ValidateOwnedOperationObservation3287(cancellationToken)) return null;
        var before = previous.CalculateRuleEffects2912(cancellationToken);
        var after = CalculateRuleEffects2912(cancellationToken);
        var result = new CharacterCurrentRuleEffectComparison(previous, this, before, after, DescribeUpdateFrom(previous));
        // Both original issuers are also the exact projected executions here.
        if (!previous.ValidateOwnedOperationObservation3287(cancellationToken)
            || !ValidateOwnedOperationObservation3287(cancellationToken)) return null;
        return result;
    }
}
