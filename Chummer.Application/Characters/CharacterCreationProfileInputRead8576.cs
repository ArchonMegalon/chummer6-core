using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Pure current-input comparison, owned only by a synchronous source-consumption
// call. This retains no inputs, resolver, assessment, or execution authority.
internal static class CharacterCreationProfileInputRead8576
{
    [ThreadStatic]
    private static ReadScope? current;

    internal static IDisposable Begin() => new ReadScope();

    internal static bool Matches(CharacterCreationCompletionReviewInputs inputs)
    {
        if (current is not { } scope)
            return inputs.SnapshotDigest == CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs);
        // Matches serializes the entire current graph on EVERY call. Only exact
        // bytes already validated against the canonical digest can be reused.
        bool matches = scope.Validation.Matches(inputs);
        CharacterCreationCompletionCost3340.Log.Checkpoint(8576);
        return matches;
    }

    private sealed class ReadScope : IDisposable
    {
        private readonly ReadScope? previous = current;
        private readonly int thread = Environment.CurrentManagedThreadId;
        internal readonly CharacterCreationCompleteRuleSourceExecution.ProfileSettingInputValidation1203 Validation = new();
        private bool disposed;

        internal ReadScope() => current = this;

        public void Dispose()
        {
            if (disposed) return;
            if (Environment.CurrentManagedThreadId != thread || !ReferenceEquals(current, this))
                throw new InvalidOperationException("Profile input comparison scope mismatch.");
            current = previous;
            Validation.Dispose();
            disposed = true;
        }
    }
}
