using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCareerQualityGraph
{
    /// <summary>
    /// Exact inverse of a locally retained transaction, not a fresh acquisition.
    /// Caller must admit owner, current revisions and local ledger provenance
    /// before persisting this projection. No generic replacement authorization.
    /// </summary>
    internal static bool TryApplyCorrection(string characterXml,
        CharacterCareerQualityTransactionEntry original, CharacterCareerQualityCorrectionPlan correction,
        out string? payload, out CharacterCareerQualityStateObservation? restored,
        out CharacterCareerQualityExpenseObservation? remainingExpense)
    {
        payload = null;
        restored = null;
        remainingExpense = null;
        try
        {
            if (!CharacterCareerQualityTransaction.IsCoherent(original)
                || !CharacterCareerQualityRules.IsCoherent(correction)
                || Hash(characterXml) != original.CharacterDigestAfter
                || !TryObserve(characterXml, original.Source, original.Plan.Identity, original.ResolvedArmCount,
                    original.Quote.Binding with { WorkspaceRevision = original.Plan.TargetWorkspaceRevision,
                        SavedRevision = original.Plan.TargetSavedRevision }, original.Quote.RuleDigest,
                    original.Plan.TransactionId, out var post, out var expense)
                || !CharacterCareerQualityRules.TryPlanCorrection(original.Receipt, post, expense,
                    correction.CorrectionId, correction.Reason, false, false, original.Receipt.ReceiptDigest,
                    out var expected) || !Same(correction, expected)) return false;

            var root = Parse(characterXml);
            var qualities = Container(root, "qualities", "quality");
            var improvements = Container(root, "improvements", "improvement");
            var expenses = Container(root, "expenses", "expense");
            if (original.Plan.Operation == CharacterCareerQualityOperation.AcquireLevel)
            {
                var acquired = qualities.Elements().Where(row =>
                    Guid.TryParse(Scalar(row, "guid"), out var id) && correction.RemoveInternalIds.Contains(id)).ToArray();
                if (acquired.Length != correction.RemoveInternalIds.Count) return false;
                foreach (var row in acquired) row.Remove();
                foreach (var row in improvements.Elements().Where(row =>
                    Guid.TryParse(Scalar(row, "sourcename"), out var id) && correction.RemoveInternalIds.Contains(id)).ToArray())
                    row.Remove();
            }
            else
            {
                var restoredIds = new HashSet<Guid>();
                foreach (var group in original.RemovedNodes.GroupBy(node => node.Container))
                {
                    XElement container = group.Key == "qualities" ? qualities : improvements;
                    string expectedName = group.Key == "qualities" ? "quality" : "improvement";
                    foreach (var fragment in group.OrderBy(node => node.Index))
                    {
                        XElement node = Parse(fragment.Xml);
                        if (node.Name != expectedName || node.HasAttributes
                            || !Guid.TryParse(Scalar(node, group.Key == "qualities" ? "guid" : "sourcename"), out var id)
                            || !original.Plan.AffectedInternalIds.Contains(id)) return false;
                        if (group.Key == "qualities" && (!restoredIds.Add(id)
                            || root.Descendants("guid").Any(current => Guid.TryParse(current.Value, out var used) && used == id)))
                            return false;
                        var current = container.Elements().ToArray();
                        if (fragment.Index > current.Length) return false;
                        if (fragment.Index == current.Length) container.Add(node);
                        else current[fragment.Index].AddBeforeSelf(node);
                    }
                }
                if (!restoredIds.SetEquals(correction.RestoreInstances.Select(row => row.Identity.InternalId))) return false;
            }
            if (correction.RemoveExpense)
            {
                var matches = expenses.Elements().Where(row => Guid.TryParse(Scalar(row, "guid"), out var id)
                    && id == correction.ExpenseIdToRemove).ToArray();
                if (matches.Length != 1) return false;
                matches[0].Remove();
            }
            Single(root, "karma").Value = correction.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture);
            string changed = root.ToString(SaveOptions.DisableFormatting);
            if (changed.Length > CharacterCareerReputationProjector.MaximumCharacterXmlLength
                || !TryObserve(changed, original.Source, original.Plan.Identity, original.ResolvedArmCount,
                    original.Quote.Binding with { WorkspaceRevision = correction.TargetWorkspaceRevision,
                        SavedRevision = correction.TargetSavedRevision }, original.Quote.RuleDigest,
                    original.Plan.TransactionId, out var state, out var remaining)
                || state!.AvailableKarma != original.Receipt.CharacterKarmaBefore
                || !Same(state.Instances, original.Receipt.InstancesBefore)
                || remaining!.MatchingEntryCount != 0) return false;
            payload = changed;
            restored = state;
            remainingExpense = remaining;
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException
            or ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return false; }
    }
}
