#nullable enable annotations

using System;
using System.Collections.Generic;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RulesetWorkspaceCodecResolverTests
{
    [TestMethod]
    public void Resolve_throws_when_ruleset_is_missing()
    {
        IRulesetWorkspaceCodec first = new StubWorkspaceCodec("sr6");
        IRulesetWorkspaceCodec second = new StubWorkspaceCodec("sr5");
        RulesetWorkspaceCodecResolver resolver = new([first, second]);

        InvalidOperationException? ex = null;
        try
        {
            resolver.Resolve(null);
        }
        catch (InvalidOperationException captured)
        {
            ex = captured;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "Workspace ruleset id is required");
    }

    [TestMethod]
    public void Resolve_throws_for_unknown_rulesets()
    {
        IRulesetWorkspaceCodec first = new StubWorkspaceCodec("sr6");
        IRulesetWorkspaceCodec second = new StubWorkspaceCodec("sr5");
        RulesetWorkspaceCodecResolver resolver = new([first, second]);

        InvalidOperationException? ex = null;
        try
        {
            resolver.Resolve("shadowrun-x");
        }
        catch (InvalidOperationException captured)
        {
            ex = captured;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "shadowrun-x");
    }

    [TestMethod]
    public void Resolve_returns_matching_codec_for_normalized_ruleset()
    {
        IRulesetWorkspaceCodec first = new StubWorkspaceCodec("sr6");
        IRulesetWorkspaceCodec second = new StubWorkspaceCodec("sr5");
        RulesetWorkspaceCodecResolver resolver = new([second, first]);

        IRulesetWorkspaceCodec resolved = resolver.Resolve(" SR6 ");

        Assert.AreSame(first, resolved);
    }

    private sealed class StubWorkspaceCodec : IRulesetWorkspaceCodec
    {
        public StubWorkspaceCodec(string rulesetId)
        {
            RulesetId = rulesetId;
            PayloadKind = $"{rulesetId}/stub";
        }

        public string RulesetId { get; }

        public int SchemaVersion => 1;

        public string PayloadKind { get; }

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
        {
            return new WorkspacePayloadEnvelope(RulesetId, SchemaVersion, PayloadKind, document.Content);
        }

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
        {
            return new CharacterFileSummary(
                Name: RulesetId,
                Alias: string.Empty,
                Metatype: string.Empty,
                BuildMethod: string.Empty,
                CreatedVersion: string.Empty,
                AppVersion: string.Empty,
                Karma: 0m,
                Nuyen: 0m,
                Created: false);
        }

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
        {
            throw new NotSupportedException();
        }

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
        {
            return new CharacterValidationResult(true, Array.Empty<CharacterValidationIssue>());
        }

        public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
        {
            return envelope;
        }

        public WorkspaceDownloadReceipt BuildDownload(CharacterWorkspaceId id, WorkspacePayloadEnvelope envelope, WorkspaceDocumentFormat format)
        {
            return new WorkspaceDownloadReceipt(id, format, string.Empty, $"{id.Value}.txt", 0, RulesetId);
        }

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
        {
            return new DataExportBundle(
                Summary: ParseSummary(envelope),
                Profile: null,
                Progress: null,
                Attributes: null,
                Skills: null,
                Inventory: null,
                Qualities: null,
                Contacts: null);
        }
    }
}
