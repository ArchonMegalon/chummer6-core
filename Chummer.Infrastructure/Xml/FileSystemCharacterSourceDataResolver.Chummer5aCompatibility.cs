using Chummer.Application.Characters;
using Chummer.Contracts.Content;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    // These APIs are synchronous. Collect diagnostics even when existing Try* source
    // readers deliberately translate an InvalidOperationException into unavailable.
    // No ambient collector survives the call, including a nested/failed capture.
    [ThreadStatic] private static List<RulePackResolutionDiagnostic>? _compatibilityDiagnostics;

    internal static void ReportCompatibilityDiagnostics(IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
        => _compatibilityDiagnostics?.AddRange(diagnostics);

    public bool TryCaptureChummer5aRuleSources(string characterXml,
        out CharacterCreationRuleSourceCapture? capture, out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
    {
        return TryCaptureChummer5aRuleSources(characterXml,
            _retainedInputs?.CompatibilityProfile ?? Chummer5aLegacyImportCompatibility.ProfileV1,
            out capture, out diagnostics);
    }

    /// <summary>Explicit version selection; restore and recapture retain the archived selection.</summary>
    public bool TryCaptureChummer5aRuleSources(string characterXml, string compatibilityProfile,
        out CharacterCreationRuleSourceCapture? capture, out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
    {
        using var reads = CreateValidatedReadScope();
        return CaptureChummer5aRuleSources(reads, characterXml, out capture, out diagnostics, compatibilityProfile);
    }

    private static bool CaptureChummer5aRuleSources(ValidatedReadScope reads, string characterXml,
        out CharacterCreationRuleSourceCapture? capture, out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics,
        string compatibilityProfile = Chummer5aLegacyImportCompatibility.ProfileV1)
    {
        var previous = _compatibilityDiagnostics;
        var observed = new List<RulePackResolutionDiagnostic>();
        _compatibilityDiagnostics = observed;
        try
        {
            if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(compatibilityProfile))
            {
                capture = null;
                observed.Add(RetainedCharacterRuleSourceInputs.CompatibilityDiagnostic("unsupported-profile",
                    "capture", null, null, "The requested retained import profile is not supported."));
                diagnostics = CaptureDiagnostics(capture, observed);
                return false;
            }
            bool result = CaptureRuleSources(reads, characterXml, out capture, bindLegacyImportProfile: compatibilityProfile);
            diagnostics = CaptureDiagnostics(capture, observed);
            return result;
        }
        finally { _compatibilityDiagnostics = previous; }
    }

    /// <summary>Private import with loss/failure diagnostics; does not activate or authorize mutation.</summary>
    public static bool TryRestoreRuleSourceCapture(ReadOnlySpan<byte> privateInputs, string integrityDigest,
        out CharacterCreationRuleSourceCapture? capture, out IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
    {
        var previous = _compatibilityDiagnostics;
        var observed = new List<RulePackResolutionDiagnostic>();
        _compatibilityDiagnostics = observed;
        try
        {
            bool result = TryRestoreRuleSourceCapture(privateInputs, integrityDigest, out capture);
            diagnostics = CaptureDiagnostics(capture, observed);
            return result;
        }
        finally { _compatibilityDiagnostics = previous; }
    }

    private static IReadOnlyList<RulePackResolutionDiagnostic> CaptureDiagnostics(
        CharacterCreationRuleSourceCapture? capture, List<RulePackResolutionDiagnostic> observed)
    {
        if (capture is not null) return capture.CompatibilityDiagnostics;
        if (observed.Count == 0)
            observed.Add(RetainedCharacterRuleSourceInputs.CompatibilityDiagnostic("capture-unavailable",
                "capture", null, null, "Exact source capture, integrity or policy replay could not be admitted. No fallback was used."));
        return Array.AsReadOnly(observed.Distinct().ToArray());
    }

    private sealed partial class RuleSourceCapture
    {
        // Only immutable retained-input metadata is memoized; currentness remains live.
        private readonly object _sourceMetadataGate = new();
        private IReadOnlyList<ArtifactVersionReference>? _requiredRulePacks;
        private IReadOnlyList<RulePackAssetDescriptor>? _requiredAssets;
        private IReadOnlyList<RulePackAssetDescriptor>? _orderedCustomSources;

        public override string? CompatibilityProfile => _inputs.CompatibilityProfile;
        public override string CompatibilityStatus => CompatibilityProfile is null ? "legacy-unverified"
            : _inputs.CompatibilityDiagnostics.Count == 0 ? "bound-inputs-only" : "retained-effects-unverified";
        public override string? RequiredPackageSetDigest { get; }
        public override IReadOnlyList<ArtifactVersionReference> RequiredRulePacks
        {
            get
            {
                if (CompatibilityProfile is null) return [];
                lock (_sourceMetadataGate)
                    return Array.AsReadOnly((_requiredRulePacks ??= _inputs.RequiredRulePacks()).ToArray());
            }
        }

        public override IReadOnlyList<RulePackAssetDescriptor> RequiredAssets
        {
            get
            {
                if (CompatibilityProfile is null) return [];
                lock (_sourceMetadataGate)
                    return Array.AsReadOnly((_requiredAssets ??= _inputs.RequiredAssets()).ToArray());
            }
        }

        public override IReadOnlyList<RulePackAssetDescriptor> OrderedCustomSources
        {
            get
            {
                if (CompatibilityProfile is null) return [];
                lock (_sourceMetadataGate)
                    return Array.AsReadOnly((_orderedCustomSources ??= _inputs.OrderedCustomSources()).ToArray());
            }
        }
        public override IReadOnlyList<RulePackResolutionDiagnostic> CompatibilityDiagnostics
            => CompatibilityProfile is null
                ? [RetainedCharacterRuleSourceInputs.CompatibilityDiagnostic("legacy-unverified", "capture",
                    IntegrityDigest, null, "Historical capture has no pinned import profile or verified package-set identity.",
                    severity: RulePackResolutionSeverityLevels.Warning)]
                : _inputs.CompatibilityDiagnostics;

        // Existing historical receipts retain their old execution behavior. The explicit
        // new profile cannot turn uncertain imported effects into execution authority.
        private bool CanCreateCompatibilityExecution => !Chummer.Contracts.Characters.CharacterRetainedSettingsFile.IsFileSelector(_inputs.ProfileId)
            && (CompatibilityProfile is null || _inputs.CompatibilityDiagnostics.Count == 0);

        public override CharacterCreationRuleSourceCapture? RecaptureCurrentEnvironment(string characterXml,
            string semanticsProfile, string semanticsFingerprint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(CompatibilityProfile)
                || !CurrentCharacterRuleSourceSemantics.IsCurrentExecutionProfile(semanticsProfile, semanticsFingerprint)
                || _inputs.CompatibilityDiagnostics.Any(row => row.Severity == RulePackResolutionSeverityLevels.Error)
                || RetainedCharacterRuleSourceInputs.Digest(_inputs.Export()) != IntegrityDigest) return null;
            try
            {
                // Retained resolver IO is closed over the original captured bytes/inventories.
                // The seed changes only the character input; the ordinary capture recipe must
                // actually observe the proposed character and produce its own new result.
                var seed = _inputs.SeedCurrentCharacterObservation(characterXml);
                var retained = CreateRetainedResolver(seed);
                if (!retained.TryCaptureChummer5aRuleSources(characterXml, CompatibilityProfile!, out var captured, out _)
                    || captured is not RuleSourceCapture observed
                    || !observed.MatchesOriginalCharacter(characterXml)
                    || !_inputs.HasSameCurrentSourceClosure(observed._inputs)
                    || observed.CreateCurrentEnvironmentResolution(semanticsProfile, semanticsFingerprint) is null
                    || RetainedCharacterRuleSourceInputs.Digest(_inputs.Export()) != IntegrityDigest) return null;
                cancellationToken.ThrowIfCancellationRequested();
                return observed;
            }
            catch (Exception error) when (IsCaptureRejection(error)) { return null; }
        }

        public override CharacterCreationRuleSourceResolution? CreateCurrentEnvironmentResolution(
            string semanticsProfile, string semanticsFingerprint)
        {
            if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(CompatibilityProfile)
                || !CurrentCharacterRuleSourceSemantics.IsCurrentExecutionProfile(semanticsProfile, semanticsFingerprint)
                || _inputs.CompatibilityDiagnostics.Any(diagnostic => diagnostic.Severity
                    == RulePackResolutionSeverityLevels.Error))
                return null;
            if (Chummer.Contracts.Characters.CharacterRetainedSettingsFile.IsFileSelector(_inputs.ProfileId)
                && !CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(semanticsProfile, semanticsFingerprint))
                return null;
            var resolution = new RuleSourceAccounting(_inputs, IntegrityDigest);
            return resolution.EnableCurrentEnvironmentSourceSemantics(semanticsProfile, semanticsFingerprint)
                ? resolution : null;
        }

    }
}
