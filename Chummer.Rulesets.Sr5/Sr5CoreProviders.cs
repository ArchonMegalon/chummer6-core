namespace Chummer.Rulesets.Sr5;

public sealed class SR5DiceProvider
{
    public SR5DiceRollResult Evaluate(IReadOnlyList<int> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        int hits = 0;
        int ones = 0;
        foreach (int die in dice)
        {
            if (die is < 1 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(dice), "SR5 dice values must be between 1 and 6.");
            }

            if (die >= 5)
            {
                hits++;
            }

            if (die == 1)
            {
                ones++;
            }
        }

        bool glitch = ones >= Math.Ceiling(dice.Count / 2.0);
        return new SR5DiceRollResult(
            DicePool: dice.Count,
            Hits: hits,
            Ones: ones,
            Glitch: glitch,
            CriticalGlitch: glitch && hits == 0);
    }
}

public sealed class SR5TestProvider
{
    public int BuyHits(int dicePool)
    {
        if (dicePool < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dicePool), "SR5 dice pool cannot be negative.");
        }

        return dicePool / 4;
    }

    public bool SuccessTestSucceeds(int hits, int threshold = 1)
    {
        if (hits < 0 || threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hits), "SR5 hit counts and thresholds cannot be negative.");
        }

        return hits >= threshold;
    }

    public SR5OpposedTestResult EvaluateOpposed(int actingHits, int opposingHits)
    {
        if (actingHits < 0 || opposingHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actingHits), "SR5 opposed hits cannot be negative.");
        }

        int netHits = actingHits - opposingHits;
        return new SR5OpposedTestResult(
            ActingHits: actingHits,
            OpposingHits: opposingHits,
            NetHits: netHits,
            ActingSideWins: netHits > 0,
            IsTie: netHits == 0);
    }

    public int RetryPenalty(int unchangedRetryCount)
    {
        if (unchangedRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unchangedRetryCount), "SR5 retry count cannot be negative.");
        }

        return unchangedRetryCount * -2;
    }
}

public sealed class SR5ExplainReceiptProvider
{
    public SR5ExplainReceipt Create(string provider, string rulefactId, string sourceRef)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("SR5 explain receipt provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(rulefactId))
        {
            throw new ArgumentException("SR5 explain receipt RuleFact id is required.", nameof(rulefactId));
        }

        if (string.IsNullOrWhiteSpace(sourceRef))
        {
            throw new ArgumentException("SR5 explain receipt source reference is required.", nameof(sourceRef));
        }

        return new SR5ExplainReceipt(provider.Trim(), rulefactId.Trim(), sourceRef.Trim(), PublicSafe: true);
    }
}

public sealed class SR5GearProvider
{
    private static readonly string[] RequiredEquipmentFiles =
    [
        "armor.xml",
        "bioware.xml",
        "cyberware.xml",
        "gear.xml",
        "vehicles.xml",
        "weapons.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredEquipmentFiles, files);
}

public sealed class SR5CharacterCreationProvider
{
    private static readonly string[] RequiredCharacterCreationFiles =
    [
        "metatypes.xml",
        "priorities.xml",
        "qualities.xml",
        "skills.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredCharacterCreationFiles, files);
}

public sealed class SR5CombatProvider
{
    private static readonly string[] RequiredCombatFiles =
    [
        "actions.xml",
        "armor.xml",
        "weapons.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredCombatFiles, files);
}

public sealed class SR5MagicProvider
{
    private static readonly string[] RequiredMagicFiles =
    [
        "mentors.xml",
        "spells.xml",
        "traditions.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredMagicFiles, files);
}

public sealed class SR5MatrixProvider
{
    private static readonly string[] RequiredMatrixFiles =
    [
        "complexforms.xml",
        "paragons.xml",
        "programs.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredMatrixFiles, files);
}

public sealed class SR5RiggingProvider
{
    private static readonly string[] RequiredRiggingFiles =
    [
        "programs.xml",
        "vehicles.xml"
    ];

    public SR5StructuredProviderIndexReceipt CreateStructuredIndexReceipt(IEnumerable<SR5StructuredSourceFile> files) =>
        SR5StructuredIndexReceiptFactory.Create(RequiredRiggingFiles, files);
}

internal static class SR5StructuredIndexReceiptFactory
{
    public static SR5StructuredProviderIndexReceipt Create(
        IReadOnlyList<string> requiredFiles,
        IEnumerable<SR5StructuredSourceFile> files)
    {
        ArgumentNullException.ThrowIfNull(requiredFiles);
        ArgumentNullException.ThrowIfNull(files);

        List<SR5StructuredSourceFile> indexedFiles = files.ToList();
        HashSet<string> seenFiles = indexedFiles
            .Select(file => file.FileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> missingFiles = requiredFiles
            .Where(required => !seenFiles.Contains(required))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool allFilesHaveMetadata = indexedFiles.All(file =>
            !string.IsNullOrWhiteSpace(file.FileName)
            && !string.IsNullOrWhiteSpace(file.Sha256)
            && file.RowCount > 0
            && file.ContainerCounts.Count > 0
            && file.ContainerCounts.Values.All(count => count > 0));
        int recordCount = indexedFiles.Sum(file => file.ContainerCounts.Values.Sum());

        return new SR5StructuredProviderIndexReceipt(
            Files: indexedFiles,
            MissingRequiredFiles: missingFiles,
            RowCount: indexedFiles.Sum(file => file.RowCount),
            RecordCount: recordCount,
            PublicSafeMetadataOnly: true,
            Valid: missingFiles.Count == 0 && allFilesHaveMetadata && recordCount > 0);
    }
}

public sealed record SR5DiceRollResult(int DicePool, int Hits, int Ones, bool Glitch, bool CriticalGlitch);

public sealed record SR5OpposedTestResult(int ActingHits, int OpposingHits, int NetHits, bool ActingSideWins, bool IsTie);

public sealed record SR5ExplainReceipt(string Provider, string RuleFactId, string SourceRef, bool PublicSafe);

public sealed record SR5StructuredSourceFile(
    string FileName,
    string Sha256,
    int RowCount,
    IReadOnlyDictionary<string, int> ContainerCounts);

public sealed record SR5StructuredProviderIndexReceipt(
    IReadOnlyList<SR5StructuredSourceFile> Files,
    IReadOnlyList<string> MissingRequiredFiles,
    int RowCount,
    int RecordCount,
    bool PublicSafeMetadataOnly,
    bool Valid);
