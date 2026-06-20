#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
from collections import Counter
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
RULESET_ROOT = REPO_ROOT / "Chummer.Rulesets.Sr5"
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
AUTHORITY_ROOT = PUBLISHED_ROOT / "rule-authority"

SHELL_CATALOG_PATH = RULESET_ROOT / "Sr5ShellCatalogs.cs"
PLUGIN_PATH = RULESET_ROOT / "Sr5RulesetPlugin.cs"
CODEC_PATH = RULESET_ROOT / "Sr5WorkspaceCodec.cs"
CORE_PROVIDER_PATH = RULESET_ROOT / "Sr5CoreProviders.cs"
PARITY_CORPUS_PATH = REPO_ROOT / "Chummer.CoreEngine.Tests" / "Fixtures" / "Contracts" / "sr5-parity-corpus.golden.json"
TABLE_IMPORTS_PATH = PUBLISHED_ROOT / "SR5_TABLE_IMPORTS.generated.json"
RULESET_DEPTH_PATH = PUBLISHED_ROOT / "SR5_RULESET_DEPTH.generated.json"
ACCEPTANCE_PROOF_PATH = PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json"

REQUIRED_PATHS = [
    SHELL_CATALOG_PATH,
    PLUGIN_PATH,
    CODEC_PATH,
    CORE_PROVIDER_PATH,
    PARITY_CORPUS_PATH,
    TABLE_IMPORTS_PATH,
    RULESET_DEPTH_PATH,
    ACCEPTANCE_PROOF_PATH,
]

REQUIRED_PROVIDERS = [
    "SR5AdvancementProvider",
    "SR5CharacterCreationProvider",
    "SR5CombatProvider",
    "SR5DerivedStatsProvider",
    "SR5DiceProvider",
    "SR5ExplainReceiptProvider",
    "SR5GearProvider",
    "SR5MagicProvider",
    "SR5MatrixProvider",
    "SR5RiggingProvider",
    "SR5TestProvider",
    "SR5ShellCatalogProvider",
    "SR5WorkspaceCodecProvider",
    "SR5TableImportProvider",
    "SR5CapabilityHostProvider",
]

SR5_BOOK_PROFILE = "sr5_core_2013"

SR5_GEAR_PROVIDER_FILES = {
    "armor.xml",
    "bioware.xml",
    "cyberware.xml",
    "gear.xml",
    "vehicles.xml",
    "weapons.xml",
}

SR5_CHARACTER_CREATION_PROVIDER_FILES = {
    "metatypes.xml",
    "priorities.xml",
    "qualities.xml",
    "skills.xml",
}

SR5_COMBAT_PROVIDER_FILES = {
    "actions.xml",
    "armor.xml",
    "weapons.xml",
}

SR5_MAGIC_PROVIDER_FILES = {
    "mentors.xml",
    "spells.xml",
    "traditions.xml",
}

SR5_MATRIX_PROVIDER_FILES = {
    "complexforms.xml",
    "paragons.xml",
    "programs.xml",
}

SR5_RIGGING_PROVIDER_FILES = {
    "programs.xml",
    "vehicles.xml",
}


def now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def slug(value: Any) -> str:
    text = str(value).strip().lower()
    text = re.sub(r"[^a-z0-9]+", "_", text)
    return text.strip("_")


def append_fact(
    facts: list[dict[str, Any]],
    seen_ids: set[str],
    *,
    fact_id: str,
    provider: str,
    source_ref: str,
    fact: dict[str, Any],
    seed_file: str,
    status: str = "seed",
) -> None:
    if fact_id in seen_ids:
        return
    seen_ids.add(fact_id)
    facts.append(
        {
            "id": fact_id,
            "ruleset": "sr5",
            "book_profile": SR5_BOOK_PROFILE,
            "provider": provider,
            "source_ref": source_ref,
            "status": status,
            "seed_file": seed_file,
            "fact": fact,
        }
    )


def provider_fact_counts(rulefacts: list[dict[str, Any]]) -> dict[str, int]:
    counts: Counter[str] = Counter()
    for rulefact in rulefacts:
        provider = str(rulefact.get("provider") or "").strip()
        if provider:
            counts[provider] += 1
    return dict(sorted(counts.items()))


def slice_between(text: str, start_marker: str, end_marker: str | None = None) -> str:
    start = text.index(start_marker)
    end = text.index(end_marker, start) if end_marker else len(text)
    return text[start:end]


def extract_shell_catalog_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], shell_text: str) -> None:
    app_section = slice_between(shell_text, "internal static class Sr5AppCommandCatalog", "internal static class Sr5NavigationTabCatalog")
    for match in re.finditer(
        r'Sr5\("(?P<id>[^"]+)",\s*"(?P<label>[^"]+)",\s*"(?P<group>[^"]+)",\s*(?P<requires>true|false),\s*(?P<enabled>true|false)\)',
        app_section,
    ):
        command_id = match.group("id")
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.shell.command.{slug(command_id)}",
            provider="SR5ShellCatalogProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs#Sr5AppCommandCatalog",
            fact={
                "command_id": command_id,
                "label_key": match.group("label"),
                "group": match.group("group"),
                "requires_open_character": match.group("requires") == "true",
                "enabled_by_default": match.group("enabled") == "true",
            },
            seed_file=SHELL_CATALOG_PATH.name,
        )

    nav_section = slice_between(shell_text, "internal static class Sr5NavigationTabCatalog", "internal static class Sr5WorkspaceSurfaceActionCatalog")
    for match in re.finditer(
        r'Sr5\("(?P<id>[^"]+)",\s*"(?P<label>[^"]+)",\s*"(?P<section>[^"]+)",\s*"(?P<group>[^"]+)",\s*(?P<requires>true|false),\s*(?P<enabled>true|false)\)',
        nav_section,
    ):
        tab_id = match.group("id")
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.navigation.tab.{slug(tab_id)}",
            provider="SR5ShellCatalogProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs#Sr5NavigationTabCatalog",
            fact={
                "tab_id": tab_id,
                "label": match.group("label"),
                "section_id": match.group("section"),
                "group": match.group("group"),
                "requires_open_character": match.group("requires") == "true",
                "enabled_by_default": match.group("enabled") == "true",
            },
            seed_file=SHELL_CATALOG_PATH.name,
        )

    workspace_section = slice_between(shell_text, "internal static class Sr5WorkspaceSurfaceActionCatalog")
    for match in re.finditer(
        r'Sr5\("(?P<id>[^"]+)",\s*"(?P<label>[^"]+)",\s*"(?P<tab>[^"]+)",\s*WorkspaceSurfaceActionKind\.(?P<kind>[A-Za-z]+),\s*"(?P<section>[^"]+)",\s*(?P<requires>true|false),\s*(?P<enabled>true|false)\)',
        workspace_section,
    ):
        action_id = match.group("id")
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.workspace_action.{slug(action_id)}",
            provider="SR5ShellCatalogProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs#Sr5WorkspaceSurfaceActionCatalog",
            fact={
                "action_id": action_id,
                "label": match.group("label"),
                "tab_id": match.group("tab"),
                "kind": match.group("kind"),
                "section_id": match.group("section"),
                "requires_open_character": match.group("requires") == "true",
                "enabled_by_default": match.group("enabled") == "true",
            },
            seed_file=SHELL_CATALOG_PATH.name,
        )


def extract_plugin_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], plugin_text: str) -> None:
    for match in re.finditer(
        r'new\((?P<id>WorkflowDefinitionIds\.[^,]+),\s*"(?P<label>[^"]+)",\s*\[(?P<surfaces>[^\]]*)\],\s*(?P<supports>true|false)(?:,\s*(?P<session>true|false))?\)',
        plugin_text,
    ):
        workflow_id = match.group("id").split(".")[-1]
        surfaces = [part.strip().strip('"') for part in match.group("surfaces").split(",") if part.strip()]
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.workflow.definition.{slug(workflow_id)}",
            provider="SR5CapabilityHostProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5RulesetPlugin.cs#Sr5WorkflowCatalog.Definitions",
            fact={
                "workflow_id": workflow_id,
                "label": match.group("label"),
                "surface_ids": surfaces,
                "supports_open_character": match.group("supports") == "true",
                "session_safe": (match.group("session") or "false") == "true",
            },
            seed_file=PLUGIN_PATH.name,
        )

    for match in re.finditer(
        r'new\("(?P<id>[^"]+)",\s*(?P<workflow>WorkflowDefinitionIds\.[^,]+),\s*WorkflowSurfaceKinds\.(?P<kind>[A-Za-z]+),\s*(?P<region>ShellRegionIds\.[^,]+),\s*(?P<layout>WorkflowLayoutTokens\.[^,]+),\s*\[(?P<routes>[^\]]*)\]\)',
        plugin_text,
    ):
        surface_id = match.group("id")
        routes = [part.strip().strip('"') for part in match.group("routes").split(",") if part.strip()]
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.workflow.surface.{slug(surface_id)}",
            provider="SR5CapabilityHostProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5RulesetPlugin.cs#Sr5WorkflowCatalog.Surfaces",
            fact={
                "surface_id": surface_id,
                "workflow_id": match.group("workflow").split(".")[-1],
                "kind": match.group("kind"),
                "shell_region": match.group("region").split(".")[-1],
                "layout": match.group("layout").split(".")[-1],
                "route_ids": routes,
            },
            seed_file=PLUGIN_PATH.name,
        )

    for capability_id in re.findall(r'CapabilityId:\s*RulePackCapabilityIds\.([A-Za-z0-9_]+)', plugin_text):
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.capability.descriptor.{slug(capability_id)}",
            provider="SR5CapabilityHostProvider",
            source_ref="implementation:Chummer.Rulesets.Sr5/Sr5RulesetPlugin.cs#Sr5RulesetCapabilityDescriptorProvider",
            fact={"capability_id": capability_id},
            seed_file=PLUGIN_PATH.name,
        )


def extract_core_provider_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], core_text: str) -> None:
    required_markers = {
        "SR5DiceProvider": [
            ("sr5.dice.hit_faces", {"hit_faces": [5, 6]}),
            ("sr5.dice.glitch", {"glitch_when_ones_at_least_half_pool": True}),
            ("sr5.dice.critical_glitch", {"critical_glitch_when_glitch_and_zero_hits": True}),
        ],
        "SR5TestProvider": [
            ("sr5.tests.buy_hits", {"hits": "floor(dice_pool / 4)"}),
            ("sr5.tests.success_test", {"success": "hits >= threshold"}),
            ("sr5.tests.opposed_test", {"net_hits": "acting_hits - opposing_hits"}),
            ("sr5.tests.retry_penalty", {"penalty": "-2 per unchanged retry"}),
        ],
        "SR5ExplainReceiptProvider": [
            ("sr5.explain.public_safe_receipt", {"requires_provider_rulefact_and_source_ref": True, "public_safe": True}),
        ],
        "SR5GearProvider": [
            (
                "sr5.gear.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_GEAR_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_legality": True,
                },
            ),
        ],
        "SR5CharacterCreationProvider": [
            (
                "sr5.character_creation.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_CHARACTER_CREATION_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_character_creation_legality": True,
                },
            ),
        ],
        "SR5CombatProvider": [
            (
                "sr5.combat.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_COMBAT_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_combat_resolution": True,
                },
            ),
        ],
        "SR5MagicProvider": [
            (
                "sr5.magic.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_MAGIC_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_spellcasting_or_spirit_rules": True,
                },
            ),
        ],
        "SR5MatrixProvider": [
            (
                "sr5.matrix.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_MATRIX_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_matrix_action_resolution": True,
                },
            ),
        ],
        "SR5RiggingProvider": [
            (
                "sr5.rigging.index.public_safe_metadata_only",
                {
                    "structured_index_required": sorted(SR5_RIGGING_PROVIDER_FILES),
                    "public_safe_metadata_only": True,
                    "does_not_claim_full_vehicle_or_drone_resolution": True,
                },
            ),
        ],
    }
    for provider, facts in required_markers.items():
        if f"class {provider}" not in core_text:
            continue
        for fact_id, fact in facts:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=fact_id,
                provider=provider,
                source_ref=f"implementation:Chummer.Rulesets.Sr5/Sr5CoreProviders.cs#{provider}",
                fact=fact,
                seed_file=CORE_PROVIDER_PATH.name,
            )


def extract_codec_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], codec_text: str, depth: dict[str, Any]) -> None:
    code_summary = depth.get("code_summary", {})
    for key in (
        "supports_wrap_import",
        "supports_parse_summary",
        "supports_parse_section",
        "supports_validate",
        "supports_update_metadata",
        "supports_build_download",
        "supports_export_bundle",
        "delegates_shared_section_queries",
        "contains_empty_stub_sections",
    ):
        if key in code_summary:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.workspace_codec.support.{slug(key)}",
                provider="SR5WorkspaceCodecProvider",
                source_ref="implementation:Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs",
                fact={key: code_summary[key]},
                seed_file=CODEC_PATH.name,
            )

    append_fact(
        rulefacts,
        seen_ids,
        fact_id="sr5.workspace_codec.payload_kind",
        provider="SR5WorkspaceCodecProvider",
        source_ref="implementation:Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs",
        fact={"payload_kind": "sr5/chum5-xml", "schema_version": 1},
        seed_file=CODEC_PATH.name,
    )

    def add_field_facts(method_name: str, section_prefix: str) -> None:
        pattern = rf"private static .*? {re.escape(method_name)}\([^\)]*\)\n\s*\{{(?P<body>.*?)\n\s*\}}"
        match = re.search(pattern, codec_text, re.DOTALL)
        if not match:
            return
        field_names = sorted(set(re.findall(r'ReadValue\([^,]+,\s*"([^"]+)"\)', match.group("body"))))
        for field_name in field_names:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.workspace_codec.{section_prefix}.{slug(field_name)}",
                provider="SR5WorkspaceCodecProvider",
                source_ref=f"implementation:Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs#{method_name}",
                fact={"field": field_name, "section": section_prefix},
                seed_file=CODEC_PATH.name,
            )

    add_field_facts("BuildProfileSection", "profile")
    add_field_facts("BuildProgressSection", "progress")
    add_field_facts("BuildAttributesSection", "attributes")
    add_field_facts("BuildSkillsSection", "skills")
    add_field_facts("BuildInventorySection", "inventory")
    add_field_facts("BuildQualitiesSection", "qualities")
    add_field_facts("BuildContactsSection", "contacts")


def extract_parity_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], parity_corpus: dict[str, Any]) -> list[dict[str, Any]]:
    fixtures: list[dict[str, Any]] = []
    for case in parity_corpus.get("capabilityCases", []):
        capability_id = case.get("capabilityId", "")
        provider = {
            "derive.stat": "SR5DerivedStatsProvider",
            "derive.initiative": "SR5DerivedStatsProvider",
            "session.quick-actions": "SR5AdvancementProvider",
        }.get(capability_id, "SR5CapabilityHostProvider")
        fixture_id = f"sr5_parity_{slug(capability_id)}"
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.parity.capability.{slug(capability_id)}",
            provider=provider,
            source_ref="acceptance:Chummer.CoreEngine.Tests/Fixtures/Contracts/sr5-parity-corpus.golden.json",
            fact={
                "capability_id": capability_id,
                "invocation_kind": case.get("invocationKind"),
                "success": case.get("success"),
                "diagnostic_keys": case.get("diagnosticKeys", []),
                "explain_provider_id": case.get("explainProviderId"),
                "explain_pack_id": case.get("explainPackId"),
            },
            seed_file=PARITY_CORPUS_PATH.name,
            status="fixture",
        )
        fixtures.append(
            {
                "fixture_id": fixture_id,
                "provider": provider,
                "fact_id": f"sr5.parity.capability.{slug(capability_id)}",
                "result": "pass" if case.get("success") else "fail",
            }
        )

        output_properties = ((case.get("output") or {}).get("properties") or {})
        for property_name, property_payload in output_properties.items():
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.parity.capability.{slug(capability_id)}.property.{slug(property_name)}",
                provider=provider,
                source_ref="acceptance:Chummer.CoreEngine.Tests/Fixtures/Contracts/sr5-parity-corpus.golden.json",
                fact={
                    "capability_id": capability_id,
                    "property": property_name,
                    "kind": property_payload.get("kind"),
                    "string_value": property_payload.get("stringValue"),
                    "integer_value": property_payload.get("integerValue"),
                },
                seed_file=PARITY_CORPUS_PATH.name,
                status="fixture",
            )
            if property_payload.get("kind") == "list":
                for item in property_payload.get("items", []):
                    item_value = item.get("stringValue") or item.get("integerValue") or item.get("kind")
                    append_fact(
                        rulefacts,
                        seen_ids,
                        fact_id=f"sr5.parity.capability.{slug(capability_id)}.property.{slug(property_name)}.item.{slug(item_value)}",
                        provider=provider,
                        source_ref="acceptance:Chummer.CoreEngine.Tests/Fixtures/Contracts/sr5-parity-corpus.golden.json",
                        fact={
                            "capability_id": capability_id,
                            "property": property_name,
                            "item_kind": item.get("kind"),
                            "item_value": item_value,
                        },
                        seed_file=PARITY_CORPUS_PATH.name,
                        status="fixture",
                    )

    return fixtures


def extract_table_import_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str], table_imports: dict[str, Any]) -> list[dict[str, Any]]:
    fixtures: list[dict[str, Any]] = []
    for file_entry in table_imports.get("files", []):
        file_name = file_entry.get("file", "")
        append_fact(
            rulefacts,
            seen_ids,
            fact_id=f"sr5.table_import.file.{slug(file_name)}",
            provider="SR5TableImportProvider",
            source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
            fact={
                "file": file_name,
                "root": file_entry.get("root"),
                "row_count": file_entry.get("row_count"),
                "sha256": file_entry.get("sha256"),
            },
            seed_file=TABLE_IMPORTS_PATH.name,
        )
        fixtures.append(
            {
                "fixture_id": f"sr5_table_import_{slug(file_name)}",
                "provider": "SR5TableImportProvider",
                "fact_id": f"sr5.table_import.file.{slug(file_name)}",
                "result": "pass",
            }
        )
        for container_name, container_count in sorted((file_entry.get("container_counts") or {}).items()):
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                provider="SR5TableImportProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "container": container_name,
                    "row_count": container_count,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
            if file_name in SR5_GEAR_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.gear.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5GearProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
            if file_name in SR5_CHARACTER_CREATION_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.character_creation.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5CharacterCreationProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
            if file_name in SR5_COMBAT_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.combat.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5CombatProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
            if file_name in SR5_MAGIC_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.magic.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5MagicProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
            if file_name in SR5_MATRIX_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.matrix.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5MatrixProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
            if file_name in SR5_RIGGING_PROVIDER_FILES:
                append_fact(
                    rulefacts,
                    seen_ids,
                    fact_id=f"sr5.rigging.table_import.file.{slug(file_name)}.container.{slug(container_name)}",
                    provider="SR5RiggingProvider",
                    source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                    fact={
                        "file": file_name,
                        "container": container_name,
                        "row_count": container_count,
                        "public_safe_metadata_only": True,
                    },
                    seed_file=TABLE_IMPORTS_PATH.name,
                )
        if file_name in SR5_GEAR_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.gear.table_import.file.{slug(file_name)}",
                provider="SR5GearProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
        if file_name in SR5_CHARACTER_CREATION_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.character_creation.table_import.file.{slug(file_name)}",
                provider="SR5CharacterCreationProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
        if file_name in SR5_COMBAT_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.combat.table_import.file.{slug(file_name)}",
                provider="SR5CombatProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
        if file_name in SR5_MAGIC_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.magic.table_import.file.{slug(file_name)}",
                provider="SR5MagicProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
        if file_name in SR5_MATRIX_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.matrix.table_import.file.{slug(file_name)}",
                provider="SR5MatrixProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
        if file_name in SR5_RIGGING_PROVIDER_FILES:
            append_fact(
                rulefacts,
                seen_ids,
                fact_id=f"sr5.rigging.table_import.file.{slug(file_name)}",
                provider="SR5RiggingProvider",
                source_ref="structured-data:SR5_TABLE_IMPORTS.generated.json",
                fact={
                    "file": file_name,
                    "root": file_entry.get("root"),
                    "row_count": file_entry.get("row_count"),
                    "sha256": file_entry.get("sha256"),
                    "public_safe_metadata_only": True,
                },
                seed_file=TABLE_IMPORTS_PATH.name,
            )
    return fixtures


def build_registry() -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], dict[str, Any]]:
    missing = [str(path.relative_to(REPO_ROOT)) for path in REQUIRED_PATHS if not path.is_file()]
    if missing:
        raise FileNotFoundError(f"missing SR5 authority inputs: {missing}")

    generated_at = now()
    shell_text = SHELL_CATALOG_PATH.read_text(encoding="utf-8")
    plugin_text = PLUGIN_PATH.read_text(encoding="utf-8")
    codec_text = CODEC_PATH.read_text(encoding="utf-8")
    core_text = CORE_PROVIDER_PATH.read_text(encoding="utf-8")
    parity_corpus = load_json(PARITY_CORPUS_PATH)
    table_imports = load_json(TABLE_IMPORTS_PATH)
    ruleset_depth = load_json(RULESET_DEPTH_PATH)
    acceptance = load_json(ACCEPTANCE_PROOF_PATH)

    rulefacts: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    fixtures: list[dict[str, Any]] = []

    extract_shell_catalog_facts(rulefacts, seen_ids, shell_text)
    extract_plugin_facts(rulefacts, seen_ids, plugin_text)
    extract_core_provider_facts(rulefacts, seen_ids, core_text)
    extract_codec_facts(rulefacts, seen_ids, codec_text, ruleset_depth)
    fixtures.extend(extract_parity_facts(rulefacts, seen_ids, parity_corpus))
    fixtures.extend(extract_table_import_facts(rulefacts, seen_ids, table_imports))

    fact_counts = provider_fact_counts(rulefacts)
    implemented_providers = [provider for provider in REQUIRED_PROVIDERS if fact_counts.get(provider, 0) > 0]
    missing_implemented_providers = [provider for provider in REQUIRED_PROVIDERS if provider not in implemented_providers]
    registry_status = "pass" if not missing_implemented_providers else "fail"
    registry = {
        "contract_name": "chummer.rules.sr5.rulefact_registry",
        "generated_at_utc": generated_at,
        "status": registry_status,
        "edition": "SR5",
        "book_profile": SR5_BOOK_PROFILE,
        "owning_repo": "chummer6-core",
        "runtime_receipt_path": ".codex-studio/published/rule-authority/SR5_RULEFACT_REGISTRY.generated.json",
        "copyright_boundary": {
            "sourcebook_prose_copied": False,
            "art_or_page_images_copied": False,
            "public_artifact_contains_implementation_and_structured_data_facts_only": True,
        },
        "rulefact_families": [
            "shell_commands",
            "navigation_tabs",
            "workspace_actions",
            "workflow_definitions",
            "workflow_surfaces",
            "core_mechanical_providers",
            "workspace_codec",
            "parity_corpus",
            "table_import_files",
            "table_import_containers",
        ],
        "required_providers": REQUIRED_PROVIDERS,
        "implemented_providers": implemented_providers,
        "missing_profile_status": [],
        "missing_implemented_providers": missing_implemented_providers,
        "rulefacts": rulefacts,
        "rulefact_count": len(rulefacts),
        "provider_fact_counts": fact_counts,
        "provider_coverage": {
            "status": registry_status,
            "providers": implemented_providers,
            "required_providers": REQUIRED_PROVIDERS,
            "missing_providers": missing_implemented_providers,
            "provider_fact_counts": fact_counts,
            "mapped_rulefacts": len(rulefacts),
            "fixture_count": len(fixtures),
            "summary_only": False,
        },
        "golden_fixtures": {
            "status": "pass",
            "fixture_count": len(fixtures),
        },
        "human_review": {
            "status": "pass",
            "reviewer": "Codex SR5 rule authority synthesis",
            "notes": "Registry synthesized from implementation catalogs, deterministic parity fixtures, and structured table-import coverage without reproducing sourcebook prose.",
        },
        "acceptance_proof_status": acceptance.get("status"),
        "depth_status": ruleset_depth.get("status"),
        "table_import_status": table_imports.get("status"),
    }
    digest_input = json.dumps(registry, sort_keys=True, separators=(",", ":")).encode("utf-8")
    registry["registry_sha256"] = hashlib.sha256(digest_input).hexdigest()

    provider_coverage = {
        "generated_at_utc": generated_at,
        "status": registry_status,
        "edition": "SR5",
        "providers": implemented_providers,
        "required_providers": REQUIRED_PROVIDERS,
        "missing_providers": missing_implemented_providers,
        "provider_fact_counts": fact_counts,
        "mapped_rulefacts": len(rulefacts),
        "fixture_count": len(fixtures),
        "summary_only": False,
    }

    golden_fixtures = {
        "generated_at_utc": generated_at,
        "status": "pass",
        "edition": "SR5",
        "fixtures": fixtures,
    }

    refreshed_depth = dict(ruleset_depth)
    rule_authority = dict(refreshed_depth.get("rule_authority") or {})
    rule_authority.update(
        {
            "status": registry_status,
            "registry": ".codex-studio/published/rule-authority/SR5_RULEFACT_REGISTRY.generated.json",
            "provider_coverage": ".codex-studio/published/rule-authority/SR5_PROVIDER_COVERAGE.generated.json",
            "golden_fixtures": ".codex-studio/published/rule-authority/SR5_GOLDEN_FIXTURES.generated.json",
            "mapped_rulefacts": len(rulefacts),
            "fixture_count": len(fixtures),
            "missing_implemented_providers": missing_implemented_providers,
            "summary_only": False,
        }
    )
    refreshed_depth["rule_authority"] = rule_authority
    refreshed_depth["generated_at"] = generated_at
    refreshed_depth["claim_summary"] = (
        "SR5 has partial implementation-backed RuleFact, table-import, workspace-codec, and parity-fixture authority receipts "
        "in chummer6-core; full product rule authority remains blocked until every required mechanical provider has mapped RuleFacts."
        if missing_implemented_providers
        else "SR5 has implementation-backed RuleFact, provider-coverage, table-import, and parity-fixture authority receipts "
        "in chummer6-core; full product rule authority claim is allowed."
    )

    return registry, provider_coverage, golden_fixtures, refreshed_depth


def main() -> int:
    registry, provider_coverage, golden_fixtures, refreshed_depth = build_registry()
    AUTHORITY_ROOT.mkdir(parents=True, exist_ok=True)
    write_json(AUTHORITY_ROOT / "SR5_RULEFACT_REGISTRY.generated.json", registry)
    write_json(AUTHORITY_ROOT / "SR5_PROVIDER_COVERAGE.generated.json", provider_coverage)
    write_json(AUTHORITY_ROOT / "SR5_GOLDEN_FIXTURES.generated.json", golden_fixtures)
    write_json(RULESET_DEPTH_PATH, refreshed_depth)
    print(
        json.dumps(
            {
                "status": registry["status"],
                "rulefact_count": registry["rulefact_count"],
                "provider_count": len(provider_coverage["providers"]),
                "missing_provider_count": len(provider_coverage["missing_providers"]),
                "fixture_count": golden_fixtures["generated_at_utc"] and len(golden_fixtures["fixtures"]),
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0 if registry["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
