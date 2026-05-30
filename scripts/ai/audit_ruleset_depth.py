#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_ROOT = REPO_ROOT / ".codex-studio" / "published"

RULESETS = (
    {
        "ruleset_id": "sr4",
        "display_name": "Shadowrun 4",
        "plugin_path": REPO_ROOT / "Chummer.Rulesets.Sr4" / "Sr4RulesetPlugin.cs",
        "codec_path": REPO_ROOT / "Chummer.Rulesets.Sr4" / "Sr4WorkspaceCodec.cs",
        "claim_ceiling": "partial",
        "serious_implementation_claim": "not_allowed",
        "claim_summary": "SR4 has a deterministic baseline host plus a broad XML projection layer, but it is not a serious rules-complete SR4 implementation.",
    },
    {
        "ruleset_id": "sr5",
        "display_name": "Shadowrun 5",
        "plugin_path": REPO_ROOT / "Chummer.Rulesets.Sr5" / "Sr5RulesetPlugin.cs",
        "codec_path": REPO_ROOT / "Chummer.Rulesets.Sr5" / "Sr5WorkspaceCodec.cs",
        "claim_ceiling": "partial",
        "serious_implementation_claim": "bounded_partial",
        "claim_summary": "SR5 is the strongest ruleset lane in this repo set, but the current host and receipt set still support only a bounded partial seriousness claim.",
    },
    {
        "ruleset_id": "sr6",
        "display_name": "Shadowrun 6",
        "plugin_path": REPO_ROOT / "Chummer.Rulesets.Sr6" / "Sr6RulesetPlugin.cs",
        "codec_path": REPO_ROOT / "Chummer.Rulesets.Sr6" / "Sr6WorkspaceCodec.cs",
        "claim_ceiling": "no",
        "serious_implementation_claim": "not_allowed",
        "claim_summary": "SR6 currently exposes shell/import scaffolding and a deterministic baseline host, but the section and rules depth remain skeletal.",
    },
)

AUTHORITY_ROOT = OUTPUT_ROOT / "rule-authority"


def load_json(path: Path) -> dict[str, object]:
    if not path.is_file():
        return {}
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    return payload if isinstance(payload, dict) else {}


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def rel(path: Path) -> str:
    return str(path.relative_to(REPO_ROOT))


def count_regex(pattern: str, text: str) -> int:
    return len(re.findall(pattern, text, flags=re.MULTILINE))


def build_receipt(config: dict[str, object]) -> dict[str, object]:
    plugin_path = Path(config["plugin_path"])
    codec_path = Path(config["codec_path"])
    plugin_text = plugin_path.read_text(encoding="utf-8")
    codec_text = codec_path.read_text(encoding="utf-8")

    workflow_count = count_regex(r"WorkflowDefinitionIds\.", plugin_text)
    surface_count = count_regex(r'new\("sr[456]\.', plugin_text)
    capability_ids = []
    if "RulePackCapabilityIds.DeriveStat" in plugin_text:
        capability_ids.append("derive.stat")
    if "RulePackCapabilityIds.DeriveInitiative" in plugin_text:
        capability_ids.append("derive.initiative")
    if "RulePackCapabilityIds.SessionQuickActions" in plugin_text:
        capability_ids.append("session.quick-actions")

    section_ids = sorted(set(re.findall(r'"([^"]+)"\s*=>', codec_text)))
    summary = {
        "workflow_count": workflow_count,
        "surface_count": surface_count,
        "capability_ids": capability_ids,
        "section_ids": section_ids,
        "supports_wrap_import": "WrapImport(" in codec_text,
        "supports_parse_summary": "ParseSummary(" in codec_text,
        "supports_parse_section": "ParseSection(" in codec_text,
        "supports_validate": "Validate(" in codec_text,
        "supports_update_metadata": "UpdateMetadata(" in codec_text,
        "supports_build_download": "BuildDownload(" in codec_text,
        "supports_export_bundle": "BuildExportBundle(" in codec_text,
        "delegates_shared_section_queries": "_characterSectionQueries.ParseSection" in codec_text,
        "contains_empty_stub_sections": "Array.Empty<" in codec_text and "string.Empty" in codec_text,
    }
    ruleset_id = str(config["ruleset_id"])
    edition = ruleset_id.upper()
    registry = load_json(AUTHORITY_ROOT / f"{edition}_RULEFACT_REGISTRY.generated.json")
    coverage = load_json(AUTHORITY_ROOT / f"{edition}_PROVIDER_COVERAGE.generated.json")
    fixtures = load_json(AUTHORITY_ROOT / f"{edition}_GOLDEN_FIXTURES.generated.json")
    authority_ready = (
        registry.get("status") == "pass"
        and coverage.get("status") == "pass"
        and fixtures.get("status") == "pass"
        and coverage.get("summary_only") is False
        and int(coverage.get("mapped_rulefacts") or 0) >= 10
        and int(coverage.get("fixture_count") or 0) >= 10
    )
    claim_ceiling = "full_rule_authority" if authority_ready else config["claim_ceiling"]
    serious_claim = "allowed" if authority_ready else config["serious_implementation_claim"]
    claim_summary = (
        f"{edition} has detailed RuleFact, provider-coverage, and golden-fixture authority receipts in chummer6-core; full product rule authority claim is allowed."
        if authority_ready
        else str(config["claim_summary"])
    )

    return {
        "contract_name": f"chummer-core.{config['ruleset_id']}_ruleset_depth",
        "status": "pass",
        "generated_at": now_iso(),
        "ruleset_id": config["ruleset_id"],
        "display_name": config["display_name"],
        "claim_ceiling": claim_ceiling,
        "serious_implementation_claim": serious_claim,
        "claim_summary": claim_summary,
        "code_summary": summary,
        "rule_authority": {
            "status": "pass" if authority_ready else "fail",
            "registry": rel(AUTHORITY_ROOT / f"{edition}_RULEFACT_REGISTRY.generated.json") if registry else "",
            "provider_coverage": rel(AUTHORITY_ROOT / f"{edition}_PROVIDER_COVERAGE.generated.json") if coverage else "",
            "golden_fixtures": rel(AUTHORITY_ROOT / f"{edition}_GOLDEN_FIXTURES.generated.json") if fixtures else "",
            "mapped_rulefacts": coverage.get("mapped_rulefacts", 0),
            "fixture_count": coverage.get("fixture_count", 0),
            "summary_only": coverage.get("summary_only", True),
        },
        "evidence_sources": [
            rel(plugin_path),
            rel(codec_path),
        ],
    }


def main() -> int:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    for config in RULESETS:
        payload = build_receipt(config)
        output_path = OUTPUT_ROOT / f"{str(config['ruleset_id']).upper()}_RULESET_DEPTH.generated.json"
        output_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
