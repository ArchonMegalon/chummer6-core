using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultRetrievalService : IRetrievalService
{
    private readonly IBuildIdeaCardCatalogService _buildIdeaCardCatalogService;
    private readonly IAiDigestService? _aiDigestService;

    public DefaultRetrievalService(
        IBuildIdeaCardCatalogService? buildIdeaCardCatalogService = null,
        IAiDigestService? aiDigestService = null)
    {
        _buildIdeaCardCatalogService = buildIdeaCardCatalogService ?? new DefaultBuildIdeaCardCatalogService();
        _aiDigestService = aiDigestService;
    }

    public AiGroundingBundle BuildGroundingBundle(OwnerScope owner, string routeType, AiConversationTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(routeType);
        ArgumentNullException.ThrowIfNull(request);

        AiRoutePolicyDescriptor policy = AiGatewayDefaults.ResolveRoutePolicy(routeType);
        AiRuntimeSummaryProjection? runtimeSummary = ResolveRuntimeSummary(owner, request);
        AiCharacterDigestProjection? characterDigest = ResolveCharacterDigest(owner, request);
        AiSessionDigestProjection? sessionDigest = ResolveSessionDigest(owner, request);
        var runtimeFacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["routeType"] = routeType,
            ["ownerScope"] = owner.NormalizedValue,
            ["retrievalOrder"] = string.Join(",", policy.RetrievalCorpusIds)
        };
        if (runtimeSummary is not null)
        {
            runtimeFacts["runtimeFingerprint"] = runtimeSummary.RuntimeFingerprint;
            runtimeFacts["rulesetId"] = runtimeSummary.RulesetId;
            runtimeFacts["runtimeTitle"] = runtimeSummary.Title;
            runtimeFacts["catalogKind"] = runtimeSummary.CatalogKind;
            runtimeFacts["engineApiVersion"] = runtimeSummary.EngineApiVersion;
            runtimeFacts["contentBundles"] = string.Join(", ", runtimeSummary.ContentBundles);
            runtimeFacts["rulePacks"] = string.Join(", ", runtimeSummary.RulePacks);
            runtimeFacts["providerBindings"] = string.Join(
                ", ",
                runtimeSummary.ProviderBindings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}={pair.Value}"));
            if (!string.IsNullOrWhiteSpace(runtimeSummary.Visibility))
            {
                runtimeFacts["visibility"] = runtimeSummary.Visibility;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RuntimeFingerprint))
        {
            runtimeFacts["runtimeFingerprint"] = request.RuntimeFingerprint;
        }

        var characterFacts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (characterDigest is not null)
        {
            characterFacts["characterId"] = characterDigest.CharacterId;
            characterFacts["displayName"] = characterDigest.DisplayName;
            characterFacts["rulesetId"] = characterDigest.RulesetId;
            characterFacts["runtimeFingerprint"] = characterDigest.RuntimeFingerprint;
            characterFacts["name"] = characterDigest.Summary.Name;
            characterFacts["alias"] = characterDigest.Summary.Alias;
            characterFacts["metatype"] = characterDigest.Summary.Metatype;
            characterFacts["buildMethod"] = characterDigest.Summary.BuildMethod;
            characterFacts["karma"] = characterDigest.Summary.Karma.ToString(CultureInfo.InvariantCulture);
            characterFacts["nuyen"] = characterDigest.Summary.Nuyen.ToString(CultureInfo.InvariantCulture);
            characterFacts["hasSavedWorkspace"] = characterDigest.HasSavedWorkspace ? "true" : "false";
            characterFacts["lastUpdatedUtc"] = characterDigest.LastUpdatedUtc.ToString("O", CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrWhiteSpace(request.CharacterId))
        {
            characterFacts["characterId"] = request.CharacterId;
        }

        if (!string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            characterFacts["workspaceId"] = request.WorkspaceId;
        }

        if (sessionDigest is not null)
        {
            characterFacts["sessionSelectionState"] = sessionDigest.SelectionState;
            characterFacts["sessionReady"] = sessionDigest.SessionReady ? "true" : "false";
            characterFacts["bundleFreshness"] = sessionDigest.BundleFreshness;
            characterFacts["requiresBundleRefresh"] = sessionDigest.RequiresBundleRefresh ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(sessionDigest.ProfileTitle))
            {
                characterFacts["sessionProfile"] = sessionDigest.ProfileTitle;
            }
            else if (!string.IsNullOrWhiteSpace(sessionDigest.ProfileId))
            {
                characterFacts["sessionProfile"] = sessionDigest.ProfileId;
            }
        }

        IReadOnlyList<AiRetrievedItem> retrievedItems = policy.RetrievalCorpusIds
            .SelectMany(corpusId => ResolveRetrievedItems(owner, routeType, request, corpusId, runtimeSummary, characterDigest, sessionDigest))
            .ToList();

        IReadOnlyList<string> constraints =
        [
            "Prefer structured Chummer data before prose documents.",
            "Do not mutate character, workspace, or session state from AI preview flows.",
            "Treat tool-driven apply or simulation steps as explicit follow-up actions."
        ];
        AiGroundingCoverage coverage = CreateCoverage(runtimeFacts, characterFacts, constraints, retrievedItems);

        return new AiGroundingBundle(
            RouteType: routeType,
            RuntimeFingerprint: request.RuntimeFingerprint,
            CharacterId: request.CharacterId,
            ConversationId: request.ConversationId,
            RuntimeFacts: runtimeFacts,
            CharacterFacts: characterFacts,
            Constraints: constraints,
            RetrievedItems: retrievedItems,
            AllowedTools: policy.AllowedTools,
            Coverage: coverage,
            WorkspaceId: request.WorkspaceId);
    }

    private static string ToCorpusTitle(string corpusId)
        => corpusId switch
        {
            AiRetrievalCorpusIds.Runtime => "Runtime",
            AiRetrievalCorpusIds.Private => "Private",
            AiRetrievalCorpusIds.Community => "Community",
            _ => corpusId
        };

    private static AiGroundingCoverage CreateCoverage(
        IReadOnlyDictionary<string, string> runtimeFacts,
        IReadOnlyDictionary<string, string> characterFacts,
        IReadOnlyList<string> constraints,
        IReadOnlyList<AiRetrievedItem> retrievedItems)
    {
        List<string> presentSignals = [];
        List<string> missingSignals = [];

        if (HasMeaningfulRuntimeFacts(runtimeFacts))
        {
            presentSignals.Add("runtime");
        }
        else
        {
            missingSignals.Add("runtime");
        }

        if (HasMeaningfulCharacterFacts(characterFacts))
        {
            presentSignals.Add("character");
        }
        else
        {
            missingSignals.Add("character");
        }

        if (constraints.Count > 0)
        {
            presentSignals.Add("constraints");
        }
        else
        {
            missingSignals.Add("constraints");
        }

        if (retrievedItems.Count > 0)
        {
            presentSignals.Add("retrieved");
        }
        else
        {
            missingSignals.Add("retrieved");
        }

        int scorePercent = (int)Math.Round((double)presentSignals.Count * 100 / 4, MidpointRounding.AwayFromZero);
        IReadOnlyList<string> retrievedCorpusIds = retrievedItems
            .Select(static item => item.CorpusId)
            .Where(static corpusId => !string.IsNullOrWhiteSpace(corpusId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static corpusId => corpusId, StringComparer.Ordinal)
            .ToArray();
        string summary = missingSignals.Count == 0
            ? $"coverage {scorePercent}%: runtime, character, constraints, and retrieved evidence present."
            : $"coverage {scorePercent}%: present {string.Join(", ", presentSignals)}; missing {string.Join(", ", missingSignals)}.";

        return new AiGroundingCoverage(
            ScorePercent: scorePercent,
            Summary: summary,
            PresentSignals: presentSignals,
            MissingSignals: missingSignals,
            RetrievedCorpusIds: retrievedCorpusIds);
    }

    private static bool HasMeaningfulRuntimeFacts(IReadOnlyDictionary<string, string> runtimeFacts)
        => runtimeFacts.Keys.Any(static key =>
            !string.Equals(key, "routeType", StringComparison.Ordinal)
            && !string.Equals(key, "ownerScope", StringComparison.Ordinal)
            && !string.Equals(key, "retrievalOrder", StringComparison.Ordinal));

    private static bool HasMeaningfulCharacterFacts(IReadOnlyDictionary<string, string> characterFacts)
        => characterFacts.Count > 0;

    private IReadOnlyList<AiRetrievedItem> ResolveRetrievedItems(
        OwnerScope owner,
        string routeType,
        AiConversationTurnRequest request,
        string corpusId,
        AiRuntimeSummaryProjection? runtimeSummary,
        AiCharacterDigestProjection? characterDigest,
        AiSessionDigestProjection? sessionDigest)
        => corpusId switch
        {
            AiRetrievalCorpusIds.Runtime => ResolveRuntimeRetrievedItems(routeType, runtimeSummary),
            AiRetrievalCorpusIds.Private => ResolvePrivateRetrievedItems(routeType, characterDigest, sessionDigest),
            AiRetrievalCorpusIds.Community => ResolveCommunityRetrievedItems(owner, routeType, request),
            _ =>
            [
                new AiRetrievedItem(
                    CorpusId: corpusId,
                    ItemId: $"{routeType}:{corpusId}:preview",
                    Title: $"{ToCorpusTitle(corpusId)} scaffold",
                    Summary: $"Preview scaffold for {corpusId} retrieval on the {routeType} route.")
            ]
        };

    private static IReadOnlyList<AiRetrievedItem> ResolveRuntimeRetrievedItems(string routeType, AiRuntimeSummaryProjection? runtimeSummary)
    {
        if (runtimeSummary is null)
        {
            return
            [
                new AiRetrievedItem(
                    CorpusId: AiRetrievalCorpusIds.Runtime,
                    ItemId: $"{routeType}:{AiRetrievalCorpusIds.Runtime}:preview",
                    Title: "Runtime scaffold",
                    Summary: $"Preview scaffold for {AiRetrievalCorpusIds.Runtime} retrieval on the {routeType} route.")
            ];
        }

        return
        [
            new AiRetrievedItem(
                CorpusId: AiRetrievalCorpusIds.Runtime,
                ItemId: runtimeSummary.RuntimeFingerprint,
                Title: runtimeSummary.Title,
                Summary: $"Ruleset {runtimeSummary.RulesetId}; engine {runtimeSummary.EngineApiVersion}; bundles {runtimeSummary.ContentBundles.Count}; packs {runtimeSummary.RulePacks.Count}.",
                Provenance: runtimeSummary.CatalogKind,
                RulesetId: runtimeSummary.RulesetId)
        ];
    }

    private static IReadOnlyList<AiRetrievedItem> ResolvePrivateRetrievedItems(
        string routeType,
        AiCharacterDigestProjection? characterDigest,
        AiSessionDigestProjection? sessionDigest)
    {
        List<AiRetrievedItem> items = [];
        if (characterDigest is not null)
        {
            items.Add(new AiRetrievedItem(
                CorpusId: AiRetrievalCorpusIds.Private,
                ItemId: characterDigest.CharacterId,
                Title: characterDigest.DisplayName,
                Summary: $"Metatype {characterDigest.Summary.Metatype}; build {characterDigest.Summary.BuildMethod}; karma {characterDigest.Summary.Karma.ToString(CultureInfo.InvariantCulture)}; nuyen {characterDigest.Summary.Nuyen.ToString(CultureInfo.InvariantCulture)}.",
                Provenance: "character-digest",
                RulesetId: characterDigest.RulesetId));
        }

        if (sessionDigest is not null)
        {
            items.Add(new AiRetrievedItem(
                CorpusId: AiRetrievalCorpusIds.Private,
                ItemId: $"{sessionDigest.CharacterId}:session",
                Title: $"Session: {sessionDigest.DisplayName}",
                Summary: $"Selection {sessionDigest.SelectionState}; bundle {sessionDigest.BundleFreshness}; ready={sessionDigest.SessionReady.ToString().ToLowerInvariant()}.",
                Provenance: "session-digest",
                RulesetId: sessionDigest.RulesetId));
        }

        if (items.Count > 0)
        {
            return items;
        }

        return
        [
            new AiRetrievedItem(
                CorpusId: AiRetrievalCorpusIds.Private,
                ItemId: $"{routeType}:{AiRetrievalCorpusIds.Private}:preview",
                Title: "Private scaffold",
                Summary: $"Preview scaffold for {AiRetrievalCorpusIds.Private} retrieval on the {routeType} route.")
        ];
    }

    private IReadOnlyList<AiRetrievedItem> ResolveCommunityRetrievedItems(
        OwnerScope owner,
        string routeType,
        AiConversationTurnRequest request)
        => _buildIdeaCardCatalogService
            .SearchBuildIdeas(owner, routeType, request.Message, maxCount: 3)
            .Select(card => new AiRetrievedItem(
                CorpusId: AiRetrievalCorpusIds.Community,
                ItemId: card.IdeaId,
                Title: card.Title,
                Summary: card.Summary,
                Provenance: card.Provenance,
                RulesetId: card.RulesetId))
            .ToArray();

    private AiRuntimeSummaryProjection? ResolveRuntimeSummary(OwnerScope owner, AiConversationTurnRequest request)
    {
        if (_aiDigestService is null || string.IsNullOrWhiteSpace(request.RuntimeFingerprint))
        {
            return null;
        }

        return _aiDigestService.GetRuntimeSummary(owner, request.RuntimeFingerprint);
    }

    private AiCharacterDigestProjection? ResolveCharacterDigest(OwnerScope owner, AiConversationTurnRequest request)
    {
        if (_aiDigestService is null || string.IsNullOrWhiteSpace(request.CharacterId))
        {
            return null;
        }

        return _aiDigestService.GetCharacterDigest(owner, request.CharacterId);
    }

    private AiSessionDigestProjection? ResolveSessionDigest(OwnerScope owner, AiConversationTurnRequest request)
    {
        if (_aiDigestService is null || string.IsNullOrWhiteSpace(request.CharacterId))
        {
            return null;
        }

        return _aiDigestService.GetSessionDigest(owner, request.CharacterId);
    }
}
