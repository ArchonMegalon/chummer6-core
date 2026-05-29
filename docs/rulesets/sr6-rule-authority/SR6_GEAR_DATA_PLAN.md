# SR6 Gear Data Import Plan

## Purpose

The Gear chapter is mostly structured data. Do not hand-code it from memory.

## Source range

```yaml
chapter: gear
pages: 244-303
```

## Import categories

```yaml
categories:
  - melee_weapons
  - firearms
  - ammunition
  - explosives
  - clothing_armor
  - armor_mods
  - electronics
  - communication_countermeasures
  - software
  - id_credit
  - tools
  - optics_imaging
  - audio
  - sensors
  - security_devices
  - breaking_entering
  - biotech
  - augmentations
  - magical_equipment
  - vehicles
  - drones
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
  attack_rating:
    close:
    near:
    medium:
    far:
    extreme:
  damage_value:
  damage_type:
  modes:
  capacity:
  device_rating:
  matrix_attributes:
    attack:
    sleaze:
    data_processing:
    firewall:
  essence:
  armor:
  defense_boost:
  vehicle_stats:
    handling_on:
    handling_off:
    acceleration:
    speed_interval:
    top_speed:
    body:
    armor:
    pilot:
    sensor:
    seats:
  source_ref:
  public_description: null
```

## Import rules

1. Extract table rows into JSON/YAML.
2. Preserve numeric stats and short item names.
3. Do not copy descriptive prose.
4. Each row must include page reference.
5. Human review required for every table family.
6. If a row is ambiguous, mark `needs_review`.

## Required artifacts

```text
SR6_GEAR_TABLE_IMPORT.generated.json
SR6_GEAR_IMPORT_REVIEW.md
SR6_GEAR_PROVIDER_TESTS.generated.json
```

## Acceptance

Gear support is not production-ready until:
- all required table families are imported;
- item stats compile;
- validation tests exist;
- copyright scan confirms no copied prose.
