namespace Chummer.Contracts.Characters;

public sealed record CharacterAttributeSummary(
    string Name,
    int BaseValue,
    int TotalValue)
{
    public int KarmaValue { get; init; }

    public int MetatypeMin { get; init; }

    public int MetatypeMax { get; init; }

    public int MetatypeAugMax { get; init; }

    public int PriorityMaximum { get; init; }

    public int KarmaMaximum { get; init; }

    public bool BaseUnlocked { get; init; } = true;

    public bool Created { get; init; }

    public int AvailableKarma { get; init; }

    public int UpgradeKarmaCost { get; init; } = -1;

    public bool CanCareerUpgrade { get; init; }
}

public sealed record CharacterAttributesSection(
    int Count,
    IReadOnlyList<CharacterAttributeSummary> Attributes);

public sealed record CharacterAttributeDetailSummary(
    string Name,
    int MetatypeMin,
    int MetatypeMax,
    int MetatypeAugMax,
    int BaseValue,
    int KarmaValue,
    int TotalValue,
    string MetatypeCategory)
{
    public int PriorityMaximum { get; init; }

    public int KarmaMaximum { get; init; }

    public bool BaseUnlocked { get; init; } = true;

    public bool Created { get; init; }

    public int AvailableKarma { get; init; }

    public int UpgradeKarmaCost { get; init; } = -1;

    public bool CanCareerUpgrade { get; init; }
}

public sealed record CharacterAttributeDetailsSection(
    int Count,
    IReadOnlyList<CharacterAttributeDetailSummary> Attributes);

public sealed record CharacterInventorySection(
    int GearCount,
    int WeaponCount,
    int ArmorCount,
    int CyberwareCount,
    int VehicleCount,
    IReadOnlyList<string> GearNames,
    IReadOnlyList<string> WeaponNames,
    IReadOnlyList<string> ArmorNames,
    IReadOnlyList<string> CyberwareNames,
    IReadOnlyList<string> VehicleNames);

public sealed record CharacterProfileSection(
    string Name,
    string Alias,
    string PlayerName,
    string Metatype,
    string Metavariant,
    string Sex,
    string Age,
    string Height,
    string Weight,
    string Hair,
    string Eyes,
    string Skin,
    string Concept,
    string Description,
    string Background,
    string CreatedVersion,
    string AppVersion,
    string BuildMethod,
    string GameplayOption,
    bool Created,
    bool Adept,
    bool Magician,
    bool Technomancer,
    bool AI,
    int MainMugshotIndex,
    int MugshotCount)
{
    public string CharacterNotes { get; init; } = string.Empty;

    public string GameNotes { get; init; } = string.Empty;

    public string GroupNotes { get; init; } = string.Empty;

    public string PrimaryArm { get; init; } = "Right";

    public bool Ambidextrous { get; init; }
}

public sealed record CharacterProgressSection(
    decimal Karma,
    decimal Nuyen,
    decimal StartingNuyen,
    int StreetCred,
    int Notoriety,
    int PublicAwareness,
    int BurntStreetCred,
    int BuildKarma,
    int TotalAttributes,
    int TotalSpecial,
    int PhysicalCmFilled,
    int StunCmFilled,
    decimal TotalEssence,
    int InitiateGrade,
    int SubmersionGrade,
    bool MagEnabled,
    bool ResEnabled,
    bool DepEnabled)
{
    public int AstralReputation { get; init; }

    public int WildReputation { get; init; }

    public int CurrentLiftCarryHits { get; init; }
}

public sealed record CharacterConditionMonitorSection(
    int PhysicalTrack,
    int PhysicalFilled,
    int PhysicalOverflow,
    int PhysicalThresholdOffset,
    string PhysicalNaturalRecovery,
    int StunTrack,
    int StunFilled,
    int StunThresholdOffset,
    string StunNaturalRecovery,
    bool PhysicalActsAsCore,
    bool StunActsAsMatrix,
    bool Created = false);

public sealed record CharacterRulesSection(
    string GameEdition,
    string Settings,
    string GameplayOption,
    int GameplayOptionQualityLimit,
    int MaxNuyen,
    int MaxKarma,
    int ContactMultiplier,
    IReadOnlyList<string> BannedWareGrades);

public sealed record CharacterBuildSection(
    string BuildMethod,
    string PriorityMetatype,
    string PriorityAttributes,
    string PrioritySpecial,
    string PrioritySkills,
    string PriorityResources,
    string PriorityTalent,
    int SumToTen,
    int Special,
    int TotalSpecial,
    int TotalAttributes,
    int ContactPoints,
    int ContactPointsUsed);

public sealed record CharacterMovementSection(
    string Walk,
    string Run,
    string Sprint,
    string WalkAlt,
    string RunAlt,
    string SprintAlt,
    int PhysicalCmFilled,
    int StunCmFilled);

public sealed record CharacterAwakeningSection(
    bool MagEnabled,
    bool ResEnabled,
    bool DepEnabled,
    bool Adept,
    bool Magician,
    bool Technomancer,
    bool AI,
    int InitiateGrade,
    int SubmersionGrade,
    string Tradition,
    string TraditionName,
    string TraditionDrain,
    string SpiritCombat,
    string SpiritDetection,
    string SpiritHealth,
    string SpiritIllusion,
    string SpiritManipulation,
    string Stream,
    string StreamDrain,
    int CurrentCounterspellingDice,
    int SpellLimit,
    int CfpLimit,
    int AiNormalProgramLimit,
    int AiAdvancedProgramLimit);

public sealed record CharacterGearSummary(
    string Guid,
    string Name,
    string Category,
    string Rating,
    string Quantity,
    string Cost,
    bool Equipped,
    string Location,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    bool WirelessEnabled = false,
    bool HomeNode = false,
    string ParentGuid = "",
    string ParentName = "",
    string HierarchyPath = "",
    int Depth = 0,
    int ChildCount = 0,
    int MatrixDamage = 0,
    int MatrixConditionMaximum = 0,
    bool MatrixConditionMaximumExact = false,
    bool CareerEditable = false);

public sealed record CharacterGearSection(
    int Count,
    IReadOnlyList<CharacterGearSummary> Gear);

public sealed record CharacterWeaponSummary(
    string Guid,
    string Name,
    string Category,
    string Type,
    string Damage,
    string AP,
    string Accuracy,
    string Mode,
    string Ammo,
    string Cost,
    bool Equipped,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    bool WirelessEnabled = false,
    int MatrixDamage = 0,
    int MatrixConditionMaximum = 0,
    bool MatrixConditionMaximumExact = false,
    bool CareerEditable = false);

public sealed record CharacterWeaponsSection(
    int Count,
    IReadOnlyList<CharacterWeaponSummary> Weapons);

public sealed record CharacterArmorSummary(
    string Guid,
    string Name,
    string Category,
    string ArmorValue,
    string Rating,
    string Cost,
    bool Equipped,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    bool WirelessEnabled = false,
    int MatrixDamage = 0,
    int MatrixConditionMaximum = 0,
    bool MatrixConditionMaximumExact = false,
    bool HomeNode = false,
    bool CareerEditable = false);

public sealed record CharacterArmorsSection(
    int Count,
    IReadOnlyList<CharacterArmorSummary> Armors);

public sealed record CharacterCyberwareSummary(
    string Guid,
    string Name,
    string Category,
    string Essence,
    string Capacity,
    string Rating,
    string Cost,
    string Grade,
    string Location,
    string ParentGuid = "",
    string ParentName = "",
    string MountSlot = "",
    string HierarchyPath = "",
    int Depth = 0,
    int ChildCount = 0,
    bool IsModular = false,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    bool Equipped = false,
    bool WirelessEnabled = false,
    bool HomeNode = false,
    int MatrixDamage = 0,
    int MatrixConditionMaximum = 0,
    bool MatrixConditionMaximumExact = false,
    bool CareerEditable = false);

public sealed record CharacterCyberwaresSection(
    int Count,
    IReadOnlyList<CharacterCyberwareSummary> Cyberwares);

public sealed record CharacterVehicleSummary(
    string Guid,
    string Name,
    string Category,
    string Handling,
    string Speed,
    string Body,
    string Armor,
    string Sensor,
    string Seats,
    string Cost,
    int ModCount,
    int WeaponCount,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    int PhysicalDamage = 0,
    int PhysicalConditionMaximum = 0,
    bool PhysicalConditionMaximumExact = false,
    bool CareerEditable = false,
    int MatrixDamage = 0,
    int MatrixConditionMaximum = 0,
    bool MatrixConditionMaximumExact = false,
    bool HomeNode = false,
    int LocationCount = 0,
    IReadOnlyList<CharacterLocationSummary>? Locations = null);

public sealed record CharacterVehiclesSection(
    int Count,
    IReadOnlyList<CharacterVehicleSummary> Vehicles);

public sealed record CharacterWeaponAccessorySummary(
    string WeaponGuid,
    string WeaponName,
    string AccessoryGuid,
    string Name,
    string Mount,
    string ExtraMount,
    string Rating,
    string Cost,
    bool Equipped,
    string Category = "",
    string Source = "",
    string Notes = "",
    string CustomName = "",
    string Location = "",
    bool WirelessEnabled = false);

public sealed record CharacterWeaponAccessoriesSection(
    int Count,
    IReadOnlyList<CharacterWeaponAccessorySummary> Accessories);

public sealed record CharacterArmorModSummary(
    string ArmorGuid,
    string ArmorName,
    string ModGuid,
    string Name,
    string Category,
    string Rating,
    string Cost,
    bool Equipped,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    string Location = "",
    bool WirelessEnabled = false);

public sealed record CharacterArmorModsSection(
    int Count,
    IReadOnlyList<CharacterArmorModSummary> ArmorMods);

public sealed record CharacterVehicleModSummary(
    string VehicleGuid,
    string VehicleName,
    string ModGuid,
    string Name,
    string Category,
    string Slots,
    string Rating,
    string Cost,
    bool Equipped,
    string Source = "",
    string Notes = "",
    string CustomName = "",
    string Location = "",
    bool WirelessEnabled = false);

public sealed record CharacterVehicleModsSection(
    int Count,
    IReadOnlyList<CharacterVehicleModSummary> VehicleMods);

public sealed record CharacterSkillSummary(
    string Guid,
    string Suid,
    string Category,
    bool IsKnowledge,
    int BaseValue,
    int KarmaValue,
    IReadOnlyList<string> Specializations,
    string Name = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterSkillsSection(
    int Count,
    int KnowledgeCount,
    IReadOnlyList<CharacterSkillSummary> Skills);

public sealed record CharacterQualitySummary(
    string Name,
    string Source,
    int BP,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterQualitiesSection(
    int Count,
    IReadOnlyList<CharacterQualitySummary> Qualities);

public sealed record CharacterContactSummary(
    string Name,
    string Role,
    string Location,
    int Connection,
    int Loyalty,
    string Guid = "",
    string Notes = "",
    string CustomName = "",
    string Metatype = "",
    string Gender = "",
    string Age = "",
    string ContactType = "",
    string PreferredPayment = "",
    string HobbiesVice = "",
    string PersonalLife = "",
    string GroupName = "",
    bool IsGroup = false,
    bool Free = false,
    bool Family = false,
    bool Blackmail = false,
    int ConnectionMaximum = 6,
    bool IdentityEditable = true,
    bool ConnectionEditable = true,
    bool LoyaltyEditable = true,
    bool GroupEditable = true,
    bool FreeEditable = true,
    bool FamilyEditable = true,
    bool BlackmailEditable = true,
    bool CanDelete = true,
    bool EditSemanticsExact = true,
    CharacterLinkedAssociationSummary? LinkedCharacter = null);

public sealed record CharacterContactsSection(
    int Count,
    IReadOnlyList<CharacterContactSummary> Contacts);

public sealed record CharacterSpellDefenseMetricSummary(
    string Id,
    string Label,
    int BaseValue,
    int CounterspellingDice,
    int TotalValue,
    string Formula);

public sealed record CharacterSpellDefenseSection(
    int Count,
    int CurrentCounterspellingDice,
    IReadOnlyList<CharacterSpellDefenseMetricSummary> Metrics);

public sealed record CharacterSpellSummary(
    string Name,
    string Category,
    string Type,
    string Range,
    string Duration,
    string DrainValue,
    string Source,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterSpellsSection(
    int Count,
    IReadOnlyList<CharacterSpellSummary> Spells);

public sealed record CharacterPowerSummary(
    string Name,
    int Rating,
    string Source,
    decimal PointsPerLevel,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterPowersSection(
    int Count,
    IReadOnlyList<CharacterPowerSummary> Powers);

public sealed record CharacterComplexFormSummary(
    string Name,
    string Target,
    string Duration,
    string FadingValue,
    string Source,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterComplexFormsSection(
    int Count,
    IReadOnlyList<CharacterComplexFormSummary> ComplexForms);

public sealed record CharacterSpiritSummary(
    string Name,
    int Force,
    int Services,
    bool Bound,
    string Guid = "",
    string Notes = "",
    string CustomName = "")
{
    /// <summary>
    /// Chummer5's persisted SpiritType value (Spirit or Sprite). An empty value means the
    /// persisted value was not recognized and rules that depend on the type must stay read-only.
    /// </summary>
    public string EntityType { get; init; } = "";

    public string CritterName { get; init; } = "";

    /// <summary>
    /// True only when saved data proves no linked-character path is configured. A configured
    /// path may resolve to an existing linked runner at runtime, so it must remain fail-closed.
    /// </summary>
    public bool CritterNameEditableExact { get; init; }

    /// <summary>
    /// The governed linked-runner association persisted for a Spirit or Sprite, when present.
    /// The original Spirit record remains authoritative; this supplies only the safe link state
    /// required to replace or remove the associated app-private runner file.
    /// </summary>
    public CharacterLinkedAssociationSummary? LinkedCharacter { get; init; }

    /// <summary>
    /// The legacy SpiritControl ceiling for Force/Rating when it can be derived entirely from
    /// the saved runner. The corresponding exactness flag is deliberately separate because a
    /// Spirit's ceiling can depend on a character-settings profile that is not embedded in XML.
    /// </summary>
    public int ForceMaximum { get; init; }

    public bool ForceMaximumExact { get; init; }

    public bool ForceEditable { get; init; }
}

public sealed record CharacterSpiritsSection(
    int Count,
    IReadOnlyList<CharacterSpiritSummary> Spirits)
{
    public bool Created { get; init; }
}

public sealed record CharacterFocusSummary(
    string Guid,
    string GearId);

public sealed record CharacterFociSection(
    int Count,
    IReadOnlyList<CharacterFocusSummary> Foci);

public sealed record CharacterAiProgramSummary(
    string Name,
    string Rating,
    string Source,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterAiProgramsSection(
    int Count,
    IReadOnlyList<CharacterAiProgramSummary> AiPrograms);

public sealed record CharacterMartialArtSummary(
    string Name,
    string Source,
    int Rating,
    IReadOnlyList<string> Techniques);

public sealed record CharacterMartialArtsSection(
    int Count,
    IReadOnlyList<CharacterMartialArtSummary> MartialArts);

public sealed record CharacterLimitModifierSummary(
    string Name,
    string Limit,
    string Condition,
    int Bonus);

public sealed record CharacterLimitModifiersSection(
    int Count,
    IReadOnlyList<CharacterLimitModifierSummary> LimitModifiers);

public sealed record CharacterLifestyleSummary(
    string Name,
    string BaseLifestyle,
    string Source,
    decimal Cost,
    int Months);

public sealed record CharacterLifestylesSection(
    int Count,
    IReadOnlyList<CharacterLifestyleSummary> Lifestyles);

public sealed record CharacterMetamagicSummary(
    string Name,
    string Source,
    int Grade,
    bool PaidWithKarma);

public sealed record CharacterMetamagicsSection(
    int Count,
    IReadOnlyList<CharacterMetamagicSummary> Metamagics);

public sealed record CharacterArtSummary(
    string Name,
    string Source,
    int Grade);

public sealed record CharacterArtsSection(
    int Count,
    IReadOnlyList<CharacterArtSummary> Arts);

public sealed record CharacterInitiationGradeSummary(
    int Grade,
    bool Res,
    bool Group,
    bool Ordeal,
    bool Schooling,
    string Guid = "",
    string Reward = "",
    string Notes = "");

public sealed record CharacterInitiationGradesSection(
    int Count,
    IReadOnlyList<CharacterInitiationGradeSummary> InitiationGrades);

public sealed record CharacterCritterPowerSummary(
    string Name,
    string Category,
    string Type,
    string Action,
    string Range,
    string Duration,
    string Source,
    int Rating,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterCritterPowersSection(
    int Count,
    IReadOnlyList<CharacterCritterPowerSummary> CritterPowers);

public sealed record CharacterMentorSpiritSummary(
    string Name,
    string MentorType,
    string Source,
    string Advantage,
    string Disadvantage);

public sealed record CharacterMentorSpiritsSection(
    int Count,
    IReadOnlyList<CharacterMentorSpiritSummary> MentorSpirits);

public sealed record CharacterExpenseSummary(
    string Date,
    decimal Amount,
    string Reason,
    string Type,
    bool Refund);

public sealed record CharacterExpensesSection(
    int Count,
    decimal TotalKarma,
    decimal TotalNuyen,
    IReadOnlyList<CharacterExpenseSummary> Expenses);

public sealed record CharacterSourcesSection(
    int Count,
    IReadOnlyList<string> Sources,
    int ReferencedSourceCount = 0,
    IReadOnlyList<CharacterSourcebookSummary>? Sourcebooks = null);

public sealed record CharacterSourcebookSummary(
    string Code,
    int ItemReferenceCount,
    bool SelectedForCharacter,
    bool MissingFromSelectedList,
    bool SelectionOnly);

public sealed record CharacterLocationSummary(
    string Guid,
    string Name,
    string Notes);

public sealed record CharacterLocationsSection(
    int Count,
    IReadOnlyList<CharacterLocationSummary> Locations);

public sealed record CharacterCalendarEntrySummary(
    string Date,
    string Name,
    string Notes);

public sealed record CharacterCalendarSection(
    int Count,
    IReadOnlyList<CharacterCalendarEntrySummary> Entries);

public sealed record CharacterImprovementSummary(
    string ImprovedName,
    string ImprovementType,
    string ImprovementSource,
    int Rating,
    bool Enabled);

public sealed record CharacterImprovementsSection(
    int Count,
    int EnabledCount,
    IReadOnlyList<CharacterImprovementSummary> Improvements);

public sealed record CharacterCustomDataDirectoryNamesSection(
    int Count,
    IReadOnlyList<string> DirectoryNames);

public sealed record CharacterDrugSummary(
    string Name,
    string Category,
    string Source,
    int Rating,
    decimal Quantity,
    string Guid = "",
    string Notes = "",
    string CustomName = "");

public sealed record CharacterDrugsSection(
    int Count,
    IReadOnlyList<CharacterDrugSummary> Drugs);
