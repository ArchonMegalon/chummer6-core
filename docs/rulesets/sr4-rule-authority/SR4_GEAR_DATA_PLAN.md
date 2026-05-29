# SR4 Gear Data Import Plan

## Purpose

The Street Gear chapter is largely structured data. Do not hand-code it from memory.

## Source range

```yaml
chapter: Street Gear
pages: 310-349
```

## Import categories

```yaml
categories:
  - melee_weapons
  - projectile_throwing_weapons
  - firearms
  - firearm_accessories
  - ammunition
  - grenades_rockets_missiles
  - explosives
  - clothing_armor
  - electronics
  - software
  - id_credsticks
  - tools
  - visual_sensors_imaging
  - audio_sensors_enhancers
  - sensors
  - security_devices
  - breaking_entering
  - chemicals_drugs
  - survival_gear
  - biotech
  - disguises
  - cyberware
  - bioware
  - magical_equipment
  - vehicles_drones
```

## Data row schema

```yaml
GearItem:
  id:
  name:
  category:
  rating:
  availability:
  legality:
  cost_nuyen:
  damage_value:
  damage_type:
  armor_ballistic:
  armor_impact:
  armor_mods:
  armor_capacity:
  firearm_mode:
  recoil_compensation:
  ammo_capacity:
  range_category:
  matrix_attributes:
    firewall:
    response:
    signal:
    system:
  essence_cost:
  essence_category: cyberware | bioware | none
  capacity:
  vehicle_stats:
    handling:
    acceleration:
    speed:
    pilot:
    body:
    armor:
    sensor:
  source_ref:
  public_description: null
```

## Import rules

1. Extract numeric/stat rows into JSON/YAML.
2. Preserve item names and game stats only.
3. Do not copy descriptive prose.
4. Each row must include page reference.
5. Every ambiguous OCR/table row gets `needs_review: true`.
6. Human review required for each table family.
7. Copyright scan must confirm no long descriptive text.

## Required artifacts

```text
SR4_GEAR_TABLE_IMPORT.generated.json
SR4_GEAR_IMPORT_REVIEW.md
SR4_GEAR_PROVIDER_TESTS.generated.json
```

## Acceptance

Gear support is not production-ready until:
- all required table families are imported;
- stats compile;
- pricing/availability tests pass;
- rules that alter Essence, armor, weapons, vehicles, and Matrix attributes are tested;
- copyright scan confirms no copied prose.
