namespace Chummer.Contracts.Characters;

public sealed record CharacterAttributeSummary(
    string Name,
    int BaseValue,
    int TotalValue);

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
    string MetatypeCategory);

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
    int MugshotCount);

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
    bool DepEnabled);

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
    string Location);

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
    bool Equipped);

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
    bool Equipped);

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
    string Location);

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
    int WeaponCount);

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
    bool Equipped);

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
    bool Equipped);

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
    bool Equipped);

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
    IReadOnlyList<string> Specializations);

public sealed record CharacterSkillsSection(
    int Count,
    int KnowledgeCount,
    IReadOnlyList<CharacterSkillSummary> Skills);

public sealed record CharacterQualitySummary(
    string Name,
    string Source,
    int BP);

public sealed record CharacterQualitiesSection(
    int Count,
    IReadOnlyList<CharacterQualitySummary> Qualities);

public sealed record CharacterContactSummary(
    string Name,
    string Role,
    string Location,
    int Connection,
    int Loyalty);

public sealed record CharacterContactsSection(
    int Count,
    IReadOnlyList<CharacterContactSummary> Contacts);

public sealed record CharacterSpellSummary(
    string Name,
    string Category,
    string Type,
    string Range,
    string Duration,
    string DrainValue,
    string Source);

public sealed record CharacterSpellsSection(
    int Count,
    IReadOnlyList<CharacterSpellSummary> Spells);

public sealed record CharacterPowerSummary(
    string Name,
    int Rating,
    string Source,
    decimal PointsPerLevel);

public sealed record CharacterPowersSection(
    int Count,
    IReadOnlyList<CharacterPowerSummary> Powers);

public sealed record CharacterComplexFormSummary(
    string Name,
    string Target,
    string Duration,
    string FadingValue,
    string Source);

public sealed record CharacterComplexFormsSection(
    int Count,
    IReadOnlyList<CharacterComplexFormSummary> ComplexForms);

public sealed record CharacterSpiritSummary(
    string Name,
    int Force,
    int Services,
    bool Bound);

public sealed record CharacterSpiritsSection(
    int Count,
    IReadOnlyList<CharacterSpiritSummary> Spirits);

public sealed record CharacterFocusSummary(
    string Guid,
    string GearId);

public sealed record CharacterFociSection(
    int Count,
    IReadOnlyList<CharacterFocusSummary> Foci);

public sealed record CharacterAiProgramSummary(
    string Name,
    string Rating,
    string Source);

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
    bool Schooling);

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
    int Rating);

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
    IReadOnlyList<string> Sources);

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
    decimal Quantity);

public sealed record CharacterDrugsSection(
    int Count,
    IReadOnlyList<CharacterDrugSummary> Drugs);
