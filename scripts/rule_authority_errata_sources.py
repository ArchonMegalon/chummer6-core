from __future__ import annotations

from typing import Any


ERRATA_SOURCES_BY_RULESET: dict[str, list[dict[str, Any]]] = {
    "sr4": [],
    "sr6": [
        {
            "id": "sr6_aug_2019",
            "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2019/08/SR6-Core-Rulebook-Errata-Aug-2019.pdf",
            "observed_page_count": 10,
            "observed_sha256": "84a488965df544eb5661def7188baeef2a8d38d1fb006f00b5537e1850b6b5db",
        },
        {
            "id": "sr6_feb_2020",
            "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2020/03/SR6-Core-Rulebook-Errata-Feb-2020.pdf",
            "observed_page_count": 6,
            "observed_sha256": None,
        },
        {
            "id": "sr6_city_edition_notice",
            "url": "https://shadowrunsixthworld.com/2021/09/15/hit-the-streets-with-shadowrun-sixth-world-city-edition-and-improved-dice-roller-app/",
            "observed_fact": "official notice says City Edition: Seattle includes latest errata and updates",
        },
    ],
}


def errata_sources_for_ruleset(ruleset: str) -> list[dict[str, Any]]:
    return list(ERRATA_SOURCES_BY_RULESET.get(ruleset, []))


def errata_sources_by_id() -> dict[str, dict[str, Any]]:
    return {
        str(source["id"]): {key: value for key, value in source.items() if key != "id"}
        for sources in ERRATA_SOURCES_BY_RULESET.values()
        for source in sources
        if source.get("id")
    }
