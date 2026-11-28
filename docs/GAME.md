# How Path of Exile 2 works, for SSE

What the game does, written down so nobody has to ask the owner how Path of Exile 2 works. **Read this before asking the owner anything about the game.** If the answer is not here, look it up with the sources at the end, add it here with its source and status, and only then ask.

Researched 28 November 2026. The live game is **0.5.5h** (patch notes [18 Nov 2026][pn055h]); the full release **1.0** comes on **11 December 2026** and its patch notes were not out yet. Everything below is for 0.5.5 unless it says otherwise. **Recheck this file after 1.0**, starting with the [crafting](#3-crafting-currencies-and-what-each-does) and [copy format](#2-item-text-copy-formats) sections.

Status of each fact:

- **C** confirmed: official patch notes or game data, or several independent reliable sources.
- **L** likely: one good source, or consistent community knowledge.
- **U** unclear: sources disagree or say nothing; the competing claims are given, then the best answer.

Nothing here was checked in the game by SSE's own people. SSE's own claims about the game stay "unverified" in the app (rule 7 in [AGENTS.md](../AGENTS.md)) until someone checks them in game; this file only says what the best sources say.

## Answers to questions agents asked

- **Tier numbering:** Tier 1 is the best tier, the highest-level tier of that modifier type that can roll on that item class; bigger numbers are weaker. Since 0.2.1 (6 Jun 2025); before that Tier 1 was the weakest. **C** ([0.2.1 notes][pn021])
- **What "(Tier: N)" in an advanced copy means:** the same numbering, counted among the tiers the item's class can roll: "Stout" (+60–69 life) prints as Tier 6 on a helmet and Tier 4 on a ring. **C** ([§1.5](#15-tiers))
- **Does the game show the tier count?** No. No copy, tooltip or trade filter says "of N"; a count can only come from a data pack. **L**
- **Greater/Perfect orb minimum modifier levels:** Transmutation and Augmentation 44 / 70 (Greater was 55 before 0.5.0); Regal, Exalted and Chaos 35 / 50. Such an orb can't be used on an item whose item level is below its minimum, and a modifier type whose best tier is below the minimum still rolls. **C** ([§3.2](#32-greater-and-perfect-orbs-minimum-modifier-level))
- **What Chaos Orb does now:** on a Rare, removes one random modifier and adds one new random modifier. It has done so since early access began (0.1.0); it never rerolls the whole item. **C**. The new modifier goes to any open side, not necessarily the freed one. **L**
- **Vaal Orb outcomes:** on non-unique equipment one of: no change; up to three modifiers rerolled into new modifiers; a corruption enchantment added; an extra socket (weapons and armour, past the limit; nothing on jewellery). Uniques get their values multiplied by 0.78–1.22 instead of the reroll. Odds unpublished, about 25 % each. Outcomes **C**, odds **U** ([§3.4](#34-vaal-orb-and-corruption))
- **Orb of Alchemy:** Normal *or Magic* item to Rare with exactly 4 random modifiers; Magic modifiers are not kept (Magic allowed since 0.3.1). **C**
- **Essences:** Lesser, normal and Greater turn a Magic item Rare and add one fixed-tier modifier; Perfect and Corrupted essences remove a random modifier from a Rare and add theirs. Essence modifiers count as crafted: one per item since 0.5.0. **C**
- **Omens:** right-click to activate; the next use of the matching currency consumes it; several omens can apply to one use. **C**
- **Omen of Whittling's "lowest":** lowest *modifier level* (the item level the modifier needs), not the tier number. **L**
- **Fracturing Orb:** Rare with at least 4 modifiers; one random modifier becomes fractured; not on an item that already has one. **C**
- **Affix limits:** Magic 1 prefix + 1 suffix; Rare 3 + 3; rare jewels 2 + 2. **C** (jewels **L**)
- **Crafting bench:** none like Path of Exile 1's. "Crafted" modifiers are the ones essences, alloys and emotions add, one per item. **C**
- **Recombinators:** disabled in 0.5.0. **C**
- **Where real modifier weights come from:** not the game files (they hold only "can roll" or "can't"). poe2db and Craft of Exile publish community estimates. **C** ([§4](#4-modifier-data-where-real-numbers-come-from))
- **Leagues on poe.ninja now:** Forbidden Rites (the default), HC Forbidden Rites, Runes of Aldur, HC Runes of Aldur, Standard, Hardcore. **C**
- **Campaign:** 0.5.x has Acts 1–4 and three interludes; Cruel was removed in 0.3.0; 1.0 adds Acts 5 and 6. **C**
- **Ascendancy:** 8 points, 2 per trial completion; Trial of the Sekhemas (from Act 2) and Trial of Chaos (from Act 3) give the first four. **C**
- **Support gem "levels":** supports have no levels; they come in tiers named I, II, III… (Zenith I is tier 1), cut from Uncut Support Gems of level 1–5. **C**
- **"Expert" and "Advanced" bases:** renamed in 0.2.0 (Expert Bombard Crossbow is now Siege Crossbow). **C**
- **Requirements in a copy:** one line, `Requires: Level 79, 89 Str, 89 Dex`, since February 2025. **C**
- **poe.ninja and corrupted uniques:** one line per unique name and base; corrupted and uncorrupted copies are not priced apart. **L**
- **Money:** the Divine Orb is the unit of value, the Exalted Orb is small change; the trade site's default price unit is "Exalted Orb Equivalent". **C**

## Versions and dates

| Version | Date | What it brought (for SSE) | Status |
|---|---|---|---|
| 0.1.0 | 6 Dec 2024 | Early access | C |
| 0.2.0 "Dawn of the Hunt" | Apr 2025 ([notes][pn020]) | Expert/Advanced bases renamed; Fracturing Orb; Recombinators | C (date L) |
| 0.2.1 | 6 Jun 2025 ([notes][pn021]) | **Tier numbering reversed: 1 is best** | C |
| 0.3.0 "The Third Edict" | Aug 2025 ([notes][pn030]) | Abyss and Desecration; Greater/Perfect orbs; essences rework; Cruel removed, interludes added; support tiers; Lineage supports; Hinekora's Lock | C |
| 0.4.0 "The Last of the Druids" | notes 5 Dec 2025, launch 12 Dec 2025 ([notes][pn040]) | Fate of the Vaal: Architect's Orb, Vaal Cultivation Orb, Twice Corrupted; Abyss core; Rune Sockets renamed Augment Sockets | C (launch L) |
| 0.5.0 "Return of the Ancients" | notes 22 May, launch 29 May 2026 ([notes][pn050]) | League Runes of Aldur; Recombinator disabled; one crafted mod per item; Alloys, meta runes, Liquid Emotions on jewels; Greater Transmutation/Augmentation 55 → 44 | C |
| 0.5.5 | 4 Sep 2026 ([notes][pn055], [league][fr]) | Event league Forbidden Rites (Ritual); Runes of Aldur content made core | C |
| 0.5.5h | 18 Nov 2026 ([notes][pn055h]) | Latest patch | C |
| 1.0 | 11 Dec 2026 ([date][date10], [Steam][steam10]) | Free to play; Duelist and Shadow; Acts 5–6; league "Conquerors of Wraeclast"; Reforging | C (details L) |

## 1. Items and affixes

### 1.1 Rarities and how many modifiers

| Rarity | Explicit modifiers | Status |
|---|---|---|
| Normal | none (implicits only) | C |
| Magic | up to 1 prefix and 1 suffix: "Magic items can have up to two random modifiers" ([poe2db][db-cur]) | C |
| Rare | up to 3 prefixes and 3 suffixes: "Rare items can have up to six random modifiers" ([poe2db][db-cur]) | C |
| Rare jewel | up to 2 and 2 (Path of Building: `affixLimit = 4` for jewels) | L |
| Unique | a fixed set of "Unique Modifier" lines; corruption can add a "Vaal Unique Modifier" | C |

- Some implicits and runes change the limits: "-1 Suffix Modifier allowed", Serle's Triumph "+1 Suffix Modifier allowed", Contempt (Potent Liquid) "+1 Prefix Modifier allowed" ([poe2db][db-cur]). **C**
- The name: a Magic item's single line is prefix name + base + suffix name ("Crackling Temple Maul of the Brute"); a Rare has a random name line, then the base line. **C** ([Exiled Exchange 2][ee2])

### 1.2 Implicits, enchantments, augments and sockets

- **Implicits** come with the base type ("Grenade Skills Fire an additional Projectile" on Bombard, Cannonade and Siege Crossbows). A Divine Orb rerolls their values; Omen of the Blessed makes it reroll only them. **C** ([poe2db][db-xbow], [omens][db-omen])
- **Enchantments**: anointments ("Allocates …", from Liquid Emotions on amulets) and **corruption enchantments** (Vaal enchantments, from a Vaal Orb, drawn red). Corruption enchantments come from their own pool per item class (on helmets e.g. "+(50—100) to Accuracy Rating", "+(20—30) to Spirit"); runes do not replace them. **C** ([poe2wiki Corrupted][wiki-corr], [poe2db helmets][db-helm])
- **Augments** go into **Augment Sockets** (called Rune Sockets until 0.4.0): runes, soul cores, idols, and talismans' socketables. A rune does different things on a weapon and on armour: Greater Desert Rune is "Adds 13 to 16 Fire Damage" on a martial weapon and "+18% to Fire Resistance" on armour. **C** ([poe2db][db-xbow], [Greater Desert Rune][db-gdr])
- **Socket counts:** at most 2 on body armours and two-handed weapons (bows, crossbows, staves), 1 on everything else; since 0.3.0 a "very low chance" of 3 on two-socket bases from high-tier maps; a Vaal Orb can add one past the limit. Artificer's Orb: "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour". **C** ([0.3.0 notes][pn030], [poe2db][db-cur])
- Socket-bound augments (the meta runes, §3.8) can't go into corrupted or sanctified items since 0.5.4; other augments can still be placed in or swapped out of a corrupted item. **C** ([0.5.4 notes][pn054], [poe2wiki Corrupted][wiki-corr])

### 1.3 Quality

- On weapons and armour quality is its **own multiplier**, not added to "increased": physical damage = (base + added) × (1 + increased %) × (1 + quality %). A real Gemini Bow copy checks out exactly (169–326). **C** (Path of Building `Item.lua`, a real copy in [Exiled Exchange 2][ee2])
- 20 % is the normal maximum from currency; more through special implicits ("+20% to Maximum Quality", Essence of the Breach on jewellery) or Vaal Infusers (up to +10 % past the maximum, chance to corrupt, need 20 % quality first, 0.5.0). **C** ([0.5.0 notes][pn050], [poe2db][db-cur])
- **Jewellery quality** comes from Catalysts and boosts the modifiers with matching tags; the copy says `Quality (Attack Modifiers): +20% (augmented)` and the modifier header gains `— 20% Increased`. Since 0.5.0 Catalysts come only from Breach (Genesis Tree). **C** ([poe2db][db-cur], [0.5.0 notes][pn050])
- Gem quality is capped at 23 % (Path of Building clamps it there). **L**

### 1.4 Modifier families, hybrids, local and global

- **One modifier per family (mod group).** A hybrid family is a separate family: "Merciless" (% increased Physical Damage) and "Dictator's" (% increased Physical Damage + Accuracy) can sit on one weapon. **C** (poe2db `ModFamilyList`, a real Siege Crossbow copy in [Exiled Exchange 2][ee2])
- **A normal copy merges equal stats from different modifiers into one line**; an advanced copy prints each modifier on its own (a 0.1.1 copy: `124% increased Evasion Rating` in Ctrl+C, 91 % + 33 % in Ctrl+Alt+C). **C** ([EE2 #426][ee2-426])
- **Local or global** is decided by the item class: on a weapon, "#% increased Physical Damage" and "Adds # to # Physical Damage" are local; the global forms read "#% increased Global Physical Damage" and "Adds # to # Physical Damage to Attacks". "+# to Accuracy Rating" has the same text both ways and a local and a global trade id. **C** ([EE2 stats data][ee2])
- On weapons, critical hit chance is **flat**: "+(1.01—1.5)% to Critical Hit Chance" up to "+(4.41—5)%"; critical damage is "+#% to Critical Damage Bonus". Accuracy is a **prefix** on weapons. **C** ([poe2db crossbows][db-xbow])

### 1.5 Tiers

- **Now:** "Item modifier tier numbering has been reversed so that smaller numbers represent stronger modifiers, with 1 being the best available modifier of that type." ([0.2.1 notes][pn021], 6 Jun 2025). **C**
- **Before 0.2.1** Tier 1 was the weakest: a 0.1.1 copy has `"Cruel" (Tier: 6)` for 135–154 % physical, which now prints as Tier 3 ([EE2 #426][ee2-426], [forum thread][f3739189]). Old screenshots, guides and tools from before June 2025 count the other way. Exiled Exchange 2 still offers "PoE2 (higher is better)" numbering: that is the pre-0.2.1 order. **C**
- **Per item class.** The count runs over the tiers the class can roll. Ranged weapons can't roll attack speed above "of Acclaim" (17–19 %), so on a crossbow "of Acclaim" is Tier 1 ([EE2 copy][ee2], Path of Building weights). "Stout" life is Tier 6 on a helmet, Tier 4 on a ring ([EE2 #997][ee2-997]). **C**
- **Proof with real copies** of the crossbow family in [§4.2](#42-worked-example-physical-damage-on-a-crossbow): `"Merciless" (Tier: 1)` 173(170-179), `"Tyrannical" (Tier: 2)` 156(155-169) ([forum][f3891367]), `"Wicked" (Tier: 6)` 82(65-84) ([EE2 #776][ee2-776]), `"Heavy" (Tier: 8)` 46(40-49) ([forum][f3848640]). On helmets `"Hale" (Tier: 10)` +19(10-19) life. **C**
- **No count is shown.** Not in copies, tooltips or the trade site (which shows only "P3"/"S3" for prefix and suffix counts). **L**
- **Some modifiers have no tier**: Perfect essence modifiers (`{ Crafted Suffix Modifier "of the Essence" — Attack }`), "of Control". **C** ([EE2 #776][ee2-776], [#997][ee2-997])
- "Rank" never appears in a PoE2 copy; parsers accept `(Rank: N)` from Path of Exile 1 habits. **L**
- poe2db numbers tiers the game's way on its item-class pages (highest level = T1). Internal modifier ids count the other way (`LocalIncreasedPhysicalDamagePercent1` is "Heavy", `…8` is "Merciless"): never read a tier from an id. **C**

### 1.6 Item level and modifier level

- Each tier has a **modifier level**; it can roll only on an item whose item level is at least that ("Merciless" needs item level 82). **C** for the levels ([poe2db][db-xbow], Path of Building), **L** for the rule (no official sentence states it; every tool assumes it).
- Drops take their item level from the area level; Waystones Tier 1–16 are area levels 65–80. **C** ([poe2db Waystones][db-way])
- **Minimum Modifier Level** (Greater/Perfect orbs, Ancient bones): "Added random Modifiers are at least this level or higher, except if a specific Modifier type would be excluded entirely from being able to roll. Currency with a Minimum Modifier Level cannot be used on items with an item level below the Minimum Modifier Level." **C** ([poe2db][db-mml])
- An essence's modifier is fixed; whether the item level limits essences is unclear (poe2db lists only a required character level). Best answer: it does not. **U**

### 1.7 Special modifiers and item states

| Kind | What it is | Status |
|---|---|---|
| Fractured | Locked: can't be removed, rerolled by a Divine Orb (fixed in 0.2.0e), un-fractured by Runeforging (0.5.1) or transformed by Fluxes and the Aldur runes (0.5.1). The item shows `Fractured Item`. Crafted modifiers can be fractured too (`{ Fractured Crafted Suffix Modifier "of Archaeology" (Tier: 1) }`, [EE2 #956][ee2-956]) | C (crafted L) |
| Desecrated | From bones (§3.7). Unrevealed until revealed at the Well of Souls (choose 1 of 3). One per item since 0.5.0, except The Unborn Lich and Omen of Putrefaction (up to 6, corrupting the item). Can't be fractured, but counts towards the Fracturing Orb's 4 (poe2wiki) | C (fracturing L) |
| Crafted | Essence, Alloy and Liquid Emotion modifiers: "items can only have 1 crafted modifier at a time" (0.5.0); Astrid's Creativity rune allows one more | C |
| Corrupted | Vaal Orb and others (§3.4). Can't be changed by normal crafting; augments can still be swapped | C |
| Twice Corrupted | Architect's Orb (equipment, jewels) or Crystallised Corruption (gems): a second corruption outcome, or the item is destroyed | C (50 % L) |
| Sanctified | Omen of Sanctification + Divine Orb on a Rare: values multiplied by 0.78–1.22 (since 0.5.0, of the current value); can't be modified again | C |
| Mirrored | Mirror of Kalandra copy | L |

### 1.8 Base types

- 0.2.0: "Advanced and Expert item base types now have distinctive names." Expert Bombard Crossbow became **Siege Crossbow** (**C**, [0.2.0 notes][pn020]); the level-59 Bombard is now the Cannonade Crossbow (**L**). Names like "Expert Leather Boots" belong to 0.1 only.
- 0.5.0 Verisium Runeforging makes new base types "Runeforged …" and "Runemastered …" (armour gets Runic Ward; low-level uniques get a better base). poe.ninja prices those uniques as separate lines. **C** ([0.5.0 notes][pn050])
- Siege Crossbow (0.5.5): 29–115 physical, 5 % crit, 1.65 attacks per second, reload 0.75, Requires Level 79, 89 Str, 89 Dex (116/116 until 0.3.0), implicit "Grenade Skills Fire an additional Projectile". Gladiatorial Helm is an Armour/Evasion helmet (Armour 206, Evasion 187, Requires Level 80, 63 Str, 63 Dex). **C** ([poe2db][db-xbow], Path of Building bases)
- **Attack speed shortens the reload too**: reload time = base ÷ (1 + increased attack speed), rounded to two places, so a Siege Crossbow with 17 % shows `Reload Time: 0.64 (augmented)`. Real copies agree: a Siege Crossbow at +25 % shows 0.60, a Gemini Crossbow (1.10) at +18 % shows 0.93 ([EE2][ee2] test items). Weapon properties round the same way: physical = round((base + added) × (1 + increased) × (1 + quality)), attacks per second to two places; a local `+#% to Critical Hit Chance` adds to the base's 5 %. **C** (Path of Building `Item.lua` and the copies)

## 2. Item text copy formats

Ctrl+C copies the item's text; Ctrl+Alt+C copies the advanced description. The best collection of real copies is [Exiled Exchange 2][ee2]'s issues and test items; SSE has none of its own yet ([SOURCES.md](SOURCES.md#real-captures-versus-fixtures)).

### 2.1 Shared layout

- First lines `Item Class: Crossbows`, `Rarity: Normal|Magic|Rare|Unique|Gem|Currency`, then the name lines (Rare and Unique: name, then base; Magic: one line; Normal: the base, with "Superior " when it has quality; Unidentified: the base only). Sections are separated by `--------`. **C**
- Meta gems have no `Item Class:` line. Uncut gems: `Item Class: Uncut Skill Gems`, `Rarity: Currency`, `Uncut Skill Gem (Level 10)` ([EE2 #573][ee2-573]). **C**
- Property lines: `Quality: +20% (augmented)`; `Physical Damage: 414-1,043 (augmented)` (**thousands comma**); one line per element, tagged by element: `Fire Damage: 47-92 (fire)`, `Cold Damage: 78-115 (cold)`, `Lightning Damage: 11-78 (lightning)`; `Critical Hit Chance: 5.00%`; `Attacks per Second: 1.20 (augmented)`; `Reload Time: 0.60 (augmented)`; `Armour:`, `Evasion Rating:`, `Energy Shield:`, `Runic Ward:` (0.5), `Spirit: 152 (augmented)`, `Block chance: 25%`. `(augmented)` appears only when a modifier or quality changes the value. A combined `Elemental Damage:` line was seen only in a test item. **C** (`Elemental Damage` **U**)
- A rune's flat damage folds into the property line (rune Adds 2 to 120 + modifier Adds 4 to 192 → `Lightning Damage: 6-312 (lightning)`). **C** ([forum][f3891367])
- `Requires: Level 79, 89 (unmet) Str, 89 Dex`; `Requires: Level 75, 67 (augmented) Str, 67 (augmented) Int`. Unidentified items: `Requires: 78 (unmet) Intelligence` (full attribute name, no level). Gems add `Requires: Bow`; supports `Support Requirements: +5 Str`. Until February 2025 it was a block (`Requirements:` / `Level: 75` / `Dex: 67`). **C**
- `Sockets: S S ` (trailing space), then augment lines in their own section ending ` (rune)`: `36% increased Physical Damage (rune)` (two identical runes add up into one line). Augment lines have no `{ }` header even in an advanced copy. **C** ([forum][f3848640])
- `Item Level: 82` in its own section; `Grants Skill: Level 20 Chaos Bolt` in its own section. **C**
- Status lines, each in its own section near the end: `Unidentified` (since Feb 2026 also `Unidentified (Tier 4)`, an *item* tier for loot filters), `Corrupted`, `Twice Corrupted`, `Sanctified`, `Mirrored`, `Fractured Item` (last, before `Note:`). **C** (`Twice Corrupted` and `Mirrored` **L**: parsed by Exiled Exchange 2 and Path of Building from the game's strings, no raw copy seen; `Unidentified (Tier N)` **L**, [EE2 #617][ee2-617])
- `Note: ~b/o 10 divine` for a priced item; unique flavour text after the modifiers. "Split" does not exist in PoE2. **C** (Split **L**)
- Stackable currency: `Stack Size: 7/20`. Waystones: base line `Waystone (Tier 15)`, a property block (`Revives Available:`, `Pack Size:`, `Item Rarity:`, `Monster Effectiveness:`, `Waystone Drop Chance:`), then modifiers with tiers ([EE2 #936][ee2]). Flasks: `Recovers 155 (augmented) Mana over 3 Seconds`, `Consumes 10 of 125 (augmented) Charges on use`. Skill gems: `Level: 21 (augmented)`, `20 Levels from Gem (Max)`, `+1 Level from Corruption (augmented)`, `Sockets: G G G G G ` ([EE2 #954][ee2-954]). **C**

### 2.2 Normal copy (Ctrl+C)

- No `{ }` headers, no ranges. Modifier lines may end ` (implicit)`, ` (enchant)` (anointments and corruption enchantments alike), ` (rune)`, ` (fractured)`, ` (desecrated)`, ` (crafted)`. Equal stats from different modifiers are merged (§1.4). **C**
- An unrevealed desecrated modifier is the bare line `Desecrated Prefix` or `Desecrated Suffix`. **L** ([forum][f3849983], Exiled Exchange 2's client strings `VEILED_PREFIX: 'Desecrated Prefix'`)

### 2.3 Advanced copy (Ctrl+Alt+C)

Each modifier gets a header line `{ <Kind> "<name>" (Tier: N) — Tag, Tag }`, separated by an em dash (U+2014), then its stat lines. Shapes seen in real copies:

| Header | Meaning | Since | Status |
|---|---|---|---|
| `{ Prefix Modifier "Merciless" (Tier: 1) — Damage, Physical, Attack }` | Explicit | 0.1 | C |
| `{ Implicit Modifier }` / `{ Implicit Modifier — Attack }` | Implicit; tags may be missing (the Siege Crossbow's has none) | 0.1 | C |
| `{ Fractured Suffix Modifier "of the Ice" (Tier: 2) — Elemental, Cold, Resistance }` | Fractured | Feb 2026 | C |
| `{ Desecrated Suffix Modifier "of Amanamu" (Tier: 1) — … }` | Revealed desecrated | Feb 2026 | C |
| `{ Prefix Modifier "Veiled" }` then `Desecrated Prefix` | Unrevealed desecrated (2025 copies also had `"of the Veil"` and `""`) | 2025 | C |
| `{ Crafted Prefix Modifier "Virile" (Tier: 4) — Life }` | Crafted (essence) | 0.5.0 | C |
| `{ Fractured Crafted Suffix Modifier "of Archaeology" (Tier: 1) }` | Fractured crafted | 0.5 | C |
| `{ Crafted Suffix Modifier "of the Essence" — Attack }` | Perfect essence modifier, no tier | 0.5 | C |
| `{ Unique Modifier — Defences }` / `{ Unique Modifier }` | Unique | 0.1 | C |
| `{ Vaal Unique Modifier — Life, Defences }` | Corrupted unique (Vaal Cultivation Orb) | 0.4.0 | C |
| `{ Enhancement }` then `Allocates Hallowed — Unscalable Value` | Anointment | Jun 2026 | C (date L) |
| `{ Corruption Enhancement — Attack }` then `+86(50-100) to Accuracy Rating` | Corruption enchantment | Jun 2026 | C (date L) |
| `{ Prefix Modifier "…" (Tier: 2) — Attack — 60% Increased }` | Third part: catalyst quality boost on jewellery | seen 2026 | C |

- Values print as `173(170-179)`. Reduced and negative stats print positive numbers, range from the modifier's internal minimum to maximum: `48(50-46)% reduced Duration of Bleeding on You`, `13(15-10)% reduced Attack Speed`. Fixed values print no brackets (`+7 to Level of all Projectile Skills`). Some lines end ` — Unscalable Value`. **C**
- A hybrid modifier has one header and several lines: `"Dictator's" (Tier: 1)`, `78(75-79)% increased Physical Damage`, `+175(175-200) to Accuracy Rating`. **C**
- **History** (copies differ by patch): at 0.1.0 launch Ctrl+Alt+C gave the plain text (fixed by Feb 2025). 0.4.0 (Dec 2025) dropped the ` (implicit)`, ` (fractured)` and ` (desecrated)` endings from advanced copies (Ctrl+C kept them); GGG then added the `Fractured`/`Desecrated` words to the header (Feb 2026, [forum][f3891367]). Copies from Dec 2025 to Jan 2026 had blank lines between modifiers. Enchantment headers came in about June 2026 (0.5.2); before, an advanced copy showed `+74 to Accuracy Rating (enchant)`. 0.5.5 fixed Ctrl+C of an item linked in chat not copying the advanced description. **C** (dates around 0.5.2 **L**)

## 3. Crafting currencies and what each does

Text in quotation marks is the game's own item text ([poe2db][db-cur], 0.5.5). Version history from poe2db snapshots of [0.1.0][db-cur-010], [0.3.0][db-cur-030], [0.4.0][db-cur-040] and the patch notes.

### 3.1 The basic orbs

| Currency | Text and rule | Since / changes | Status |
|---|---|---|---|
| Orb of Transmutation | "Upgrades a Normal item to a Magic item with 1 modifier" | 0.1 | C |
| Orb of Augmentation | "Augments a Magic item with a new random modifier"; Magic holds 1 prefix + 1 suffix | 0.1 | C |
| Regal Orb | "Upgrades a Magic item to a Rare item, adding 1 modifier"; "Current modifiers are retained" | 0.1 | C |
| Orb of Alchemy | "Upgrades a Normal or Magic item to a Rare item with 4 random modifiers"; "Current modifiers are not retained". Always 4, sides random | Magic allowed since [0.3.1][pn031] | C |
| Exalted Orb | "Augments a Rare item with a new random modifier" (up to 6) | 0.1 | C |
| Chaos Orb | "Removes a random modifier and augments a Rare item with a new random modifier" | 0.1; Greater/Perfect 0.3.0 | C |
| Orb of Annulment | "Removes a random modifier from an item" (Magic or Rare); fractured modifiers stay | 0.1 | C |
| Divine Orb | "Randomises the numeric values of modifiers on an item": implicit and explicit values, not fractured ones, not on corrupted items | 0.1; fractured fix 0.2.0e | C |
| Fracturing Orb | "Fracture a random modifier on a rare item with at least 4 modifiers, locking it in place." "Cannot be used on Fractured items." Not on uniques | 0.2.0; drops need Atlas specialisation since 0.5.0 | C |
| Orb of Chance | "Unpredictably either upgrades a Normal item to Unique rarity or destroys it". No Greater/Perfect. Better odds on jewellery and charms (0.3.0); on Tablets (0.5.0) | 0.1 | C |
| Artificer's Orb | "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour" | wands/staves 0.2.0 | C |
| Lesser / Greater / Perfect Jeweller's Orb | "Sets a Skill Gem to have 3 / 4 / 5 Support Gem Sockets"; since 0.5.0 also items that grant skills | "sets" since 0.2.0 | C |
| Hinekora's Lock | "Allows an item to foresee the result of the next Currency item used on it"; "Modifying the item in any way removes the ability to foresee". Not on gems, corrupted, sanctified or mirrored items | 0.3.0 | C |
| Orb of Scouring, Regret, Alteration | **Do not exist** in PoE2 | — | C |

- **Chaos Orb's new modifier** can land on either side that has room; it is not tied to the side removed (on a full 3/3 Rare it can only go where the removal made room). No source says it outright; the text has the Exalted Orb's wording, and only removal-side omens exist. **L**

### 3.2 Greater and Perfect orbs: Minimum Modifier Level

The tooltip has a property line `Minimum Modifier Level: N`; the rest of the text is the basic orb's. **C** ([poe2db][db-mml], [0.5.0 notes][pn050])

| Orb | Greater | Perfect | Notes |
|---|---|---|---|
| Orb of Transmutation | 44 | 70 | Greater was 55 until 0.5.0 |
| Orb of Augmentation | 44 | 70 | Greater was 55 until 0.5.0 |
| Regal Orb | 35 | 50 | |
| Exalted Orb | 35 | 50 | both modifiers of Omen of Greater Exaltation obey it |
| Chaos Orb | 35 | 50 | |

- All ten came in 0.3.0. Can't be used on an item whose item level is below the minimum. A modifier type whose best tier is below the minimum is not shut out ("except if a specific Modifier type would be excluded entirely"). Best reading: it can still roll its tiers up to its best. **C** (the reading **L**)
- Ancient Jawbone, Rib and Collarbone have "Minimum Modifier Level: 40"; Gnawed bones "Maximum Item Level: 64". **C**

### 3.3 Other currencies

| Currency | What it does | Status |
|---|---|---|
| Architect's Orb (0.4.0, Atziri's Temple) | "Modifies a Corrupted Equipment or Jewel item unpredictably or destroys it": 50 % a second corruption enchantment and Twice Corrupted, else destroyed | C (50 % L) |
| Crystallised Corruption | "Modifies a Corrupted Skill Gem unpredictably or destroys it": 50 % a second gem outcome | C (50 % L) |
| Vaal Cultivation Orb (0.4.0) | "Replaces up to 2 modifiers on a Corrupted Vaal Unique / Replaces other Uniques with a Corrupted Unique of the same Item Class" ("Cultivated" uniques; trade filter `mutated`) | C |
| Vaal Infusers (0.5.0: Armourer's, Blacksmith's, Arcanist's, Catalysing) | "Improves the quality of a … exceeding maximum quality by up to 10% with a chance of Corrupting it"; need 20 % quality | C |
| Orbs of Sacrifice (0.5.4: Yaomac's, Kopec's, Kamasa's, Yugul's) | "Upgrades a Corruption Enchantment … and removes a random Modifier" on corrupted Rare weapons/quivers, armour, jewellery, jewels | C |
| Vaal Siphoner (Atziri) | On corrupted Rare jewellery: after enough kills, one modifier down a tier, the rest rerolled up to 110 % | L |
| Ancient Infuser, Core Destabiliser, Orb of Extraction | Tablets corrupted; Soul Cores modified or destroyed; destroy equipment to get non-socket-bound augments back | C |
| Recombinator | "The Recombinator has been disabled. The Omen of Recombination has been removed." (0.5.0) | C |

### 3.4 Vaal Orb and corruption

"Modifies an item unpredictably and Corrupts it"; "Corrupted items cannot be modified again". One outcome at random ([poe2wiki Corrupted][wiki-corr], [maxroll][maxroll-corr], [Switchblade][switchblade]):

| Item | Outcomes | Status |
|---|---|---|
| Non-unique equipment | No change · reroll up to three modifiers into new modifiers · add a corruption enchantment · add a socket to a weapon (sceptres too) or armour past the limit (jewellery: no change; never more sockets than the item's size) | C |
| Unique equipment | No change · values ×0.78–1.22 (since 0.5.0 of the current value) · enchantment · socket | C |
| Skill gem | No change · level ±1 · one support socket added or removed · quality changed (poe2wiki: up to ±10 %; maxroll: −7 % to +3 %), cap 23 % | C (quality amounts U) |
| Non-unique jewel | No change · reroll up to three modifiers · enchantment · add or remove one modifier past the limit | L |
| Waystone | No change · tier ±1 (the way to Tier 16) · prefixes and suffixes swapped · 0–4 modifiers added up to 8 | L |
| Flask, charm | Quality changed | L |

- **Odds:** never published. poe2wiki: "seem to be all equally likely"; Switchblade (0.5.4): 25 % each; maxroll: unknown. Best answer: about 25 % each, so on jewellery about 50 % nothing. **U**
- Omen of Corruption ("will always result in change") "can no longer be obtained" since 0.5.0 (Standard's exchange only). **C**
- A 1 can no longer roll to 0 (0.2.0). Paquate's Mechanism in Jiquani's Sanctum (Act 3) applies one Vaal Orb outcome once per character. Essence of Insanity (belts): "On Corruption, Item gains two Enchantments". **C**

### 3.5 Essences

Since 0.3.0: "Essences now add a specific modifier, depending on the Tier of Essence and type of item it is being applied to." ([0.3.0 notes][pn030], [poe2db][db-ess])

| Grade | Text | Works on | Status |
|---|---|---|---|
| Lesser, normal, Greater | "Upgrades a Magic item to a Rare item, adding a guaranteed modifier" | Magic only; the Magic modifiers stay (L) | C |
| Perfect | "Removes a random modifier and augments a Rare item with a new guaranteed modifier" | Rare only; essence-only modifier without a tier | C |
| Corrupted (Hysteria, Delirium, Horror, Insanity; also the Abyss, the Breach) | Same as Perfect | Rare; slot-specific modifiers | C |

- Lesser/normal/Greater add **one fixed tier of the normal family** with that tier's range. On a crossbow: Lesser Essence of Abrasion "Adds (5—8) to (10—15) Physical Damage" (mod level 8), Essence of Abrasion "(14—21) to (25—37)" (46, "Gleaming", T5 of 9), Greater "(23—35) to (39—59)" (60, "Razor-sharp", T3); Perfect "Gain (25—33)% of Damage as Extra Physical Damage". Essence of Haste on bows and crossbows (since 0.4.0): 8–10 / 11–13 / 14–16 %; Perfect Haste "(20—25)% chance to gain Onslaught on Killing Hits with this Weapon". **C** ([poe2db crossbows][db-xbow])
- Essence modifiers are **crafted** modifiers (`(crafted)` in Ctrl+C, `{ Crafted … }` in Ctrl+Alt+C), one per item since 0.5.0. A second essence on an item with one: guides say blocked. **C** (second essence **L**)
- Corrupted Essences come from using a Vaal Orb on an essence monster: on Greater essences per the 0.2.0 notes, on Perfect essences per poe2wiki. Best answer: the highest grade the monster holds. **U**. Essence of the Abyss: "Mark of the Abyssal Lord"; desecrating that item replaces the mark with a higher-tier unrevealed desecrated modifier. **C**

### 3.6 Omens

"Right click this item in your inventory to set it to be active. This item is consumed when triggered." Each reads "While this item is active in your inventory your next …". Stack to 10 since 0.3.0. Several can apply to one use (a Perfect Exalted Orb with Homogenising, Dextral and Greater Exaltation gave two suffixes, [forum][f3851632]); omens work on Greater and Perfect orbs too. **C** ([poe2db][db-omen])

| Omen | "… your next …" | Obtainable now? |
|---|---|---|
| Sinistral / Dextral Exaltation | "Exalted Orb will add only prefix / only suffix modifiers" | yes |
| Greater Exaltation | "Exalted Orb will add two random modifiers" (with a side omen both go to that side, L) | yes |
| Homogenising Exaltation | "Exalted Orb will add a Modifier of the same type as an existing Modifier on the Item" (same tag, L) | no: stopped dropping 0.4.0, Standard exchange only |
| Catalysing Exaltation | "Exalted Orb will consume all Catalyst Quality to increase the chance of the corresponding type of Modifier" | yes |
| Whittling | "Chaos Orb will remove the lowest level modifier" (modifier level, L; ties random, U; an unrevealed desecrated counts as level 1, L) | yes |
| Sinistral / Dextral Erasure | "Chaos Orb will remove only prefix / only suffix modifiers" | yes |
| Sinistral / Dextral Annulment | "Orb of Annulment will remove only prefix / only suffix modifiers" | yes |
| Light | "Orb of Annulment will remove only Desecrated modifiers" | yes |
| Greater Annulment | "Orb of Annulment will remove two modifiers" | no (0.3.0) |
| Sinistral / Dextral Coronation, Homogenising Coronation | Regal Orb only prefix / suffix / same type | no (0.3.0; Homogenising as Exaltation) |
| Sinistral / Dextral Alchemy | "Orb of Alchemy will result in the maximum number of prefix / suffix modifiers" | no (0.3.0) |
| Sinistral / Dextral Crystallisation | "Perfect or Corrupted Essence will remove only Prefix / only Suffix modifiers" | yes |
| Sinistral / Dextral Necromancy | "Desecration attempt will add only prefix / only suffix modifiers" | yes |
| Abyssal Echoes | "the next time you reveal Desecrated modifiers you can reroll the options once" | yes |
| Sovereign / Liege / Blackblooded | "Weapon or Jewellery Desecration attempt will guarantee a random Ulaman / Amanamu / Kurgal modifier" | yes |
| Putrefaction | "Desecration attempt will replace all modifiers on the item creating an item with up to 6 Unrevealed modifiers and Corrupting the item" | yes |
| Corruption | "Vaal Orb will always result in change" | no (0.5.0) |
| Chance / the Ancients | Orb of Chance "will not destroy the Item" / "upgrade the Item to a random Unique of the same Item Class" | yes |
| the Blessed / Sanctification | Divine Orb "will only reroll Implicit Modifiers" / "used on a Rare item will Sanctify it" | yes |
| Recombination | — | removed (0.5.0) |

- Side omens on *adding* currencies (Exaltation, Coronation, Necromancy) pick the side of the new modifier; on *removing* ones (Erasure, Annulment, Crystallisation) the side of the removed one. **C**
- Sinistral and Dextral of one family both active: no source. Best answer: refuse the pair. **U**
- A lord omen (Sovereign, Liege, Blackblooded) with Sinistral Necromancy reportedly uses both and the lord guarantee fails ([forum][f3849983]). **L**
- Obtainable list checked against poe.ninja's Forbidden Rites omens (type `Ritual`). Non-crafting omens: Bartering, Gambling, and the Waystone omens (Chaotic Rarity, Quantity, Monsters, Effectiveness: inverted to "do not grant" in 0.5.0). **C**

### 3.7 Desecration

Abyss came in 0.3.0 and is core since 0.4.0. "Desecrating an item adds an Unrevealed Desecrated modifier. If modifiers are full then a random modifier is also removed." "Items with Desecrated Modifiers cannot be Desecrated again." Reveal at the Well of Souls (Act 2): pick one of three. **C** ([poe2db][db-des], [0.3.0 notes][pn030])

| Bone | Targets | Limit |
|---|---|---|
| Gnawed / Preserved / Ancient Jawbone | Rare weapon or quiver | Gnawed: "Maximum Item Level: 64"; Ancient: "Minimum Modifier Level: 40" |
| Gnawed / Preserved / Ancient Rib | Rare armour | same |
| Gnawed / Preserved / Ancient Collarbone | Rare amulet, ring or belt | same |
| Preserved Cranium | Rare jewel (and "Unstable Desecration" uniques, which may be destroyed) | — |
| Preserved Vertebrae | Rare waystone | no longer drops (0.4.0) |
| Altered Collarbone | jewellery, "with a chance for otherworldly modifiers" | 0.5.x (L) |

- The lord modifiers ("Amanamu's", "Kurgal's", "Ulaman's", "of Amanamu", …) are level 65. Reveal options can include ordinary modifiers unless an omen guarantees a lord. Armour has no exclusive desecrated prefixes. **L**
- Trade: pseudo stats "# Unrevealed Prefix Modifiers", "# Desecrated Prefix Modifiers"; misc filters `veiled` ("Unrevealed") and `desecrated`. **C**

### 3.8 Crafted modifiers and other 0.5 crafting

- "All crafted modifiers are now guaranteed, but items can only have 1 crafted modifier at a time. Desecrated modifiers no longer count as crafted modifiers, but items are limited to 1 Desecrated modifier." ([0.5.0 notes][pn050]) **C**
- **Alloys** (13, 0.5.0): like Perfect essences, "Removes a random modifier and augments a Rare item with a new guaranteed modifier" (Celestial Alloy on a martial weapon: "+(327—427) to Accuracy Rating, (5—8)% increased Attack Speed"). **C**
- **Meta-crafting runes** (15, from Verisium Remnants, socket-bound): Masterwork Rune (upgrades a socketed rune), Passion / Breath / Ire / Betrayal of Aldur (weapon elemental modifiers become fire / cold / lightning / chaos), Serle's Triumph ("+1 Suffix Modifier allowed"), Astrid's Creativity ("Can have 1 additional Crafted Modifiers"), Cadigan's Epiphany (gloves: sockets → a jewel socket), Uhtred's Sidereus, Kolr's Hunt, Vorana's Carnage, Thrud's Might, Medved's Tending, Katla's Gloom ("Can roll Chronomancy / Marksman / Berserking / Destruction / Soul / Decay modifiers" on boots / gloves / helmet / weapon / body armour / gloves), Aldur's Legacy (destroys a Kalguuran or Ezomyte unique to make a rune). The list is poe2db's; the notes only say "15 Meta crafting runes". **L**
- **Fluxes**: "Blazing Flux: Transforms all Cold and Lightning Resistance modifiers on an item to equivalent Fire Resistance modifiers". **C**
- **Liquid Emotions** (Delirium): "Removes a random modifer and Augments a Rare Basic Jewel with a new guaranteed Crafted modifier" (the game's spelling); Ancient Emotions for Time-Lost jewels; still instil amulets at The Withered Willow; waystones no longer (0.4.0). **C**
- **Genesis Tree** (Breach, 0.5.0): Wombgifts and Hiveblood make rings, amulets, belts and currency, with bases found only there. **C**
- Verisium **Runeforging** changes the base (Runic Ward), not modifiers. Since 0.5.5 the Verisium Anvil unlocks through an Act 4 quest in every league. **C**

### 3.9 Announced for 1.0

- **Reforging**: "A new crafting system that allows players to replace unwanted item modifiers with permanently fractured modifiers, including powerful new effects obtained through blood-infused materials." ([press release][press10]) **C**. From the reveal: a currency marks chosen modifiers "Forgemarked", the Reforging Bench turns each into a new random fractured modifier ([maxroll][maxroll-10]) **L**; unreleased "Bloodvial of Eldra / Komaki / Rithva" items in poe2db's data **U**.
- No announced change to Chaos, Exalted, Vaal, essences, omens or Greater/Perfect orbs. Whether Alloys, meta runes and the one-crafted-modifier rule survive 1.0 is unknown until the notes. **U**

## 4. Modifier data: where real numbers come from

### 4.1 Sources and their terms

| Source | What it has | Weights | Terms |
|---|---|---|---|
| Game files, as exported by [Path of Building PoE2][pob] (`src/Data/ModItem.lua`; commit `49e9392`, "Export 0.5.5 data", 10 Sep 2026) | Every item modifier: id, prefix or suffix, affix name, text with ranges, **modifier level**, group, which item tags can roll it, trade stat hashes | **None**: every tier that can roll has `weightVal = 1`, others 0 | GGG: "We do not officially provide access to any in-game data outside of our supported APIs" ([data docs][dev-data]; SOURCES row 23). Path of Building is MIT; the data is GGG's |
| [poe2db.tw][db-xbow] | The same per item class, split into normal, corrupted (Vaal enchantments), corruption upgrades, desecrated, essence, perfect essence, socketable (runes, soul cores) | `DropChance`: "Weight information cannot be obtained from game file", compiled by Krakenbul (Prohibited Library) **using recombinators**; charms, jewels, tablets and waystones "obtained from parsing trade-site listings"; desecrated "queried from Trade" ([weightings][db-w]) | "CC BY-NC-SA 3.0, unless otherwise noted"; no API |
| [Craft of Exile][coe] (PoE2 mode) | Calculator and emulator | "extrapolated using a special methods": the same recombinator method plus trade listings; "I'm sure these are not perfect"; re-measured after major patches | No terms for the data found |
| poe2wiki.net | Modifier lists and mechanics pages | some | Blocks automated readers; its MediaWiki API answers (`/api.php?action=parse&prop=wikitext&page=<Page>&format=json`): for research by hand, never from the app. CC BY-NC-SA |

- Real weights are **not** in the game files (**C**). Every published weight is a community estimate (**C**), and the recombinator method stopped being repeatable when 0.5.0 disabled recombinators, so published weights may predate 0.5 (**L**).
- For research, poe2db's item-class pages embed their tables as JSON in `new ModsView({...})`; Path of Building's `ModItem.lua` has the levels and groups. The app must never fetch either (rules 5 and 6).

### 4.2 Worked example: physical damage on a crossbow

The local "#% increased Physical Damage" prefix (`LocalPhysicalDamagePercent`) on crossbows in 0.5.5, against SSE's practice pack (`fixtures/packs/practice-0.5.5.json`, family `crossbow.phys_pct`):

| Tier | Name | Mod level | Range | poe2db weight | SSE pack weight (made up) |
|---|---|---|---|---|---|
| 1 | Merciless | 82 | 170–179 | 25 | 250 |
| 2 | Tyrannical | 75 | 155–169 | 50 | 500 |
| 3 | Cruel | 60 | 135–154 | 100 | 1000 |
| 4 | Bloodthirsty | 46 | 110–134 | 200 | 1000 |
| 5 | Vicious | 33 | 85–109 | 400 | 1000 |
| 6 | Wicked | 16 | 65–84 | 1000 | 1000 |
| 7 | Serrated | 8 | 50–64 | 1000 | 1000 |
| 8 | Heavy | 1 | 40–49 | 1000 | 1000 |

Sources: [poe2db crossbows][db-xbow] (levels, ranges, weights), Path of Building `ModItem.lua` (same levels and ranges). **C** for names, levels and ranges; weights are poe2db's estimates.

Since 28 Nov 2026 the practice pack takes every family below from Path of Building's 0.5.5 export (names, levels, ranges, wording; [SOURCES.md](SOURCES.md) row 25); only its weights are made up. The other crossbow families on poe2db:

| Family (crossbow, 0.5.5) | Side | Tiers | Best tier |
|---|---|---|---|
| Adds # to # Physical Damage | prefix | 9 | Flaring, level 75, (37—55) to (63—94) |
| % Physical Damage + Accuracy (hybrid) | prefix | 8 | Dictator's, level 81, (75—79) % and +(175—200) |
| + Accuracy Rating | **prefix** | 10 | Valkyrie's, level 82, +(551—650) |
| % increased Attack Speed | suffix | 5 on ranged weapons | of Acclaim, level 37, (17—19) % |
| + % to Critical Hit Chance (flat) | suffix | 6 | of Unmaking, level 73, +(4.41—5) % |
| + % to Critical Damage Bonus | suffix | 6 | of Destruction, level 73, +(23—25) % |
| + Level of all Projectile Skills | **suffix** | 4 | of the Sniper, level 81, +5 |
| Leeches % of Physical Damage as Life | suffix | 4 | of the Vampire, level 65, (9—9.9) % |
| Gain # Life per enemy killed | suffix | 8 | of Legend, level 77, (69—84) |
| + Strength, + Dexterity | suffix | 8 each | of the Titan / of the Phantom, level 74, +(31—33) |
| Adds # to # Fire / Cold / Lightning Damage | prefix | 10 each | Carbonising / Crystalising / Vapourising, level 81 |
| Loads an additional bolt / Loads 2 additional bolts | suffix | 2 | of Bursting, level 82, "Loads 2 additional bolts" (of Shelling, level 55, "Loads an additional bolt") |
| % increased Reload Speed | — | not a crossbow explicit; only desecrated "of Kurgal" (17—25) % | — |

## 5. Uniques

- **Variants:** a balance change says either "Existing items can be updated using a Divine Orb" or "Existing items will not have this change"; the second leaves legacy versions in Standard and older leagues. Runeforged and Runemastered unique bases (0.5.0) are separate base types. **C** ([0.4.0][pn040], [0.5.0][pn050] notes)
- **Corrupted uniques:** a Vaal Orb's value multiplier (×0.78–1.22), enchantment or socket; Vaal Cultivation Orb's "Vaal Unique Modifier" lines and Cultivated uniques (trade `mutated`); Twice Corrupted through the Architect's Orb. "Foulborn" is a Path of Exile 1 word, not used in PoE2. **C** (Foulborn **L**)
- **poe.ninja** prices uniques with `GET /poe2/api/economy/stash/current/item/overview?league=<name>&type=<type>`, types UniqueWeapons, UniqueArmours, UniqueAccessories, UniqueFlasks, UniqueCharms, UniqueJewels, UniqueSanctumRelics, UniqueTablets, PrecursorTablets. One line per name and base (`detailsId` name-base), no links, no variant field (except tablets), a `corrupted` flag on a few lines but no corrupted/uncorrupted split, `listingCount` capped at 10,000. Lines with 1–3 listings can be absurd (one Winter's Bite at 43,652,000 divine): filter on listing count. **C** for the shape, **L** for "no split" ([poe.ninja][ninja-unique])

## 6. Gems

- **Uncut gems** (tradeable on the Currency Exchange since 0.3.0): Uncut Skill Gem level 1–20, Uncut Support Gem level 1–5, Uncut Spirit Gem level 4–20. Right-click opens the Gemcutting window: "Right Click to engrave a Skill Gem / Support Gem / Persistent Buff Skill Gem". Trade and poe.ninja ids `uncut-skill-gem-N`, `uncut-support-gem-N`, `uncut-spirit-gem-N`. **C** ([poe2db][db-uncut])
- **Skill gem tiers** are the lowest uncut level that can cut them: 1, 3, 5, 7, 9, 11, 13 (spirit gems 4, 8, 14). The cut gem comes out at the uncut gem's level, and a higher uncut gem raises an existing gem to its level. Gems don't level by experience. Natural maximum 20 (needs character level 90); 21+ through a Vaal Orb and "+level" gear. **C** (cutting levels **L**)
- **Support gems** have no levels. They come in tiers named with Roman numerals (Zenith I is tier 1, Zenith II tier 3), cut from Uncut Support Gems of the matching level 1–5 (0.3.0). Rules: "Cannot use multiple Support Gems of the same Category in one Skill" (Zenith I and II can't share a skill); "You cannot use multiple copies of the exact same Lineage Support across multiple Skills, or for Supports that grant global benefits to persistent reservation Skills while active." The old one-of-each-support-per-character rule went in 0.3.0. **C** ([poe2db][db-sup])
- **Support sockets:** a skill gem starts with 2; Lesser / Greater / Perfect Jeweller's Orb *set* it to 3 / 4 / 5. Item-granted skills get 3 at skill level 10, 4 at 15, 5 at 20. A 6th through corruption: unconfirmed. **C** (item skills **L**, 6th **U**)
- **Lineage supports:** much stronger supports, endgame only (40 in 0.3.0, about 76 now), from global drops or specific bosses; one copy of each across all skills. **C** ([poe2db][db-lin])
- **Spirit** is reserved by persistent buffs, meta gems, persistent minions and companions. Campaign Spirit: +30 (The King in the Mists, Freythorn, Act 1), +30 (Ignagduk, Act 3), +40 (Lythara, interlude) = 100; more from sceptres, body armour and amulet modifiers, the passive tree. **C**
- **Meta gems** hold other skills (Cast on Critical, Invocations, Blasphemy, Mirage Archer, Spell Totem…); trade category `gem.metagem`. **C**
- **Path of Building codes:** `<Gem nameSpec skillId gemId level quality …>` where `nameSpec` carries the support tier ("Zenith II"), `skillId` the granted effect (`SupportZenithPlayerTwo`) and `gemId` the game id (`Metadata/Items/Gem/SupportGemZenithTwo`). PoB does not store a gem's support-socket count or the uncut gem it was cut from. Guide exports usually hold several trees, item sets and skill sets plus notes. Details in [SOURCES.md](SOURCES.md) row 22 and [GUIDE.md](GUIDE.md). **C** (guide contents **L**)

## 7. Campaign and leagues

### 7.1 Campaign

- **0.5.x:** Acts 1–4 (towns Clearfell Encampment, Ardura Caravan, Ziggurat Encampment, Kingsmarch; Act 4 is a non-linear archipelago), then three interludes (The Curse of Holten, The Stolen Barya, Doryani's Contingency) from level 60 to 65. Cruel was removed in 0.3.0, when the interludes came in "temporarily". **C** ([0.3.0 notes][pn030], [poe2db][db-act])
- **1.0:** six acts; Acts 5 and 6 return to Oriath (24 new areas, 18 new unique bosses). Whether the interludes stay: GGG called them temporary; poe2db says their zones move into the endgame. **C** for the acts (reveal coverage, [rpgsite][rpgsite]), **U** for the interludes.
- **Act 1 in 0.5.5,** area level in brackets: The Riverbank (1, The Bloated Miller) → Clearfell Encampment (town) → Clearfell (2, Beira of the Rotten Pack, +10 % cold resistance) → Mud Burrow (3, The Devourer) → The Grelwood (4, The Brambleghast) → The Red Vale (5, The Rust King) → The Grim Tangle (6, The Rotten Druid) → Cemetery of the Eternals (7, Lachlann of Endless Lament) → Mausoleum of the Praetor (8, Draven) and Tomb of the Consort (8, Asinia) → Hunting Grounds (10, The Crowbell) → Freythorn (11, The King in the Mists, +30 Spirit) → Ogham Farmlands (12, Una's Lute) → Ogham Village (13, The Executioner) → The Manor Ramparts (14) → Ogham Manor (15, Candlemass +20 life, Count Geonor). Root Hollow and Lost Catacombs also exist; their place is unverified. **C** ([poe2db Act 1][db-act], [metabot][metabot]) (the extra zones **U**)
- **Permanent rewards:** 24 passive points from quests (2 each: The Crowbell, Una's Lute; Kabala, the Final Letter; Mighty Silverfist, the Sacrificial Heart; Omniphobia, Yama; Oswin; Akthi and Anundr; Yeti; Hooded One), 100 Spirit, +10 % to each elemental resistance (Beira, Sisters of Garukhan, Blackjaw), +20 life (Candlemass), +5 % life and mana, choices of a charm slot, thresholds or regeneration, attributes or resistances, flask recovery. Whether each "+2" is also weapon-set points: the item is "Book of Specialisation: two Weapon Set Passive Skill Points" while quest data lists both; best answer, both. **C** ([maxroll][maxroll-perm], poe2db) (weapon-set points **U**)
- **Ascendancy:** 8 points, 2 per trial completion; the first unlocks the ascendancy. Points 1–2: Trial of the Sekhemas with Balbala's Barya (from the Traitor's Passage, Act 2); 3–4: Trial of Chaos with the Chimeral Inscribed Ultimatum (Chimeral Wetlands, Act 3); 5–6: a level 60+ Djinn Barya (3 trials) or a level 65+ Inscribed Ultimatum (10 trials); 7–8: a level 75+ Barya (4 trials, Zarokh) or a level 65+ Ultimatum (10 trials, the Trialmaster). No new trial in 0.4 or 0.5. 0.5.5 reworked the Trial of Chaos (leave and resume, up to 30 rooms). **C** ([maxroll][maxroll-asc], [0.5.5 notes][pn055])

### 7.2 Leagues

| League (trade and poe.ninja name) | poe.ninja slug | Started | Status |
|---|---|---|---|
| Forbidden Rites (event league, Ritual; poe.ninja's default) | `forbiddenrites` | 4 Sep 2026, 0.5.5 | C |
| HC Forbidden Rites | `forbiddenriteshc` | same | C |
| Runes of Aldur | `runesofaldur` | 29 May 2026, 0.5.0 | C |
| HC Runes of Aldur | `runesofaldurhc` | same | C |
| Standard, Hardcore | `standard`, `hardcore` | permanent | C |

- Both temporary leagues end with 1.0. SSF versions exist on poe.ninja's builds pages only (`SSF Forbidden Rites`, `HC SSF …`); the trade site has none. poe.ninja's API takes the display name (`?league=Forbidden%20Rites`). **C** ([index-state][ninja-index], [trade leagues][trade-leagues])
- **1.0** (11 Dec 2026) launches the challenge league "Conquerors of Wraeclast" ([Steam][steam10]). What happens to Early Access characters in Standard is not announced; past leagues always moved into Standard and Hardcore. **C** (name), **U** (Standard)

## 8. Trade and exchange

- **Listing status** (`status.option`): `securable` "Instant Buyout", `available` "Instant Buyout and In Person", `onlineleague` "In Person (Online in League)", `online` "In Person (Online)", `any`. **C** ([filters][trade-filters]). Instant buyout runs through Merchant's Tabs (0.3.0; the buyer pays a gold fee). **L**
- **Filters** in [`/api/trade2/data/filters`][trade-filters]: type (`category`, `rarity` incl. `nonunique`, `ilvl`, `quality`); equipment (`damage`, `aps`, `crit`, `dps`, `pdps`, `edps`, `reload_time`, `ar`, `ev`, `es`, `ward`, `block`, `spirit`, `rune_sockets` "Augmentable Sockets"); requirements; map; misc (`gem_level`, `gem_sockets`, `area_level`, `stack_size`, `identified`, `fractured_item`, `corrupted`, `twice_corrupted`, `sanctified`, `mutated`, `veiled` "Unrevealed", `desecrated`, `crafted`, `mirrored`, `foreseeing`, `unidentified_tier`); trade (`indexed`, `sale_type`, `fee`, `price` with currency, default "Exalted Orb Equivalent"). **C**
- **Stats** ([`/api/trade2/data/stats`][trade-stats]): groups pseudo, explicit, implicit, fractured, crafted, enchant, rune ("Augment"), desecrated, sanctum, skill; stat groups `and`, `not`, `if`, `count`, `weight`. **No tier filter**: only value min and max. **C** (no tier filter **L**)
- **Static ids** ([`/api/trade2/data/static`][trade-static]) are what poe.ninja uses too: `exalted`, `greater-exalted-orb`, `perfect-orb-of-transmutation`, `omen-of-whittling`, `essence-of-abrasion`. **C**
- **Currency Exchange** in game (Ange since 0.3.0): order book with a market ratio and gold fees. GGG's public hourly digest: `GET https://web.poecdn.com/api/currency-exchange/poe2/<unix-hour>` (no login; per market `league`, `market_id`, `volume_traded`, lowest/highest stock and ratio; past hours only). **C** ([developer reference][dev-ref])
- Shift+Alt-click on an item fills the trade search in game (0.5.0). **C**

## 9. Other things a companion would otherwise ask

- **Values on 28 Nov 2026** (poe.ninja): Forbidden Rites 1 Divine ≈ 386 Exalted ≈ 12.6 Chaos, a Mirror ≈ 6,683 Divine; Runes of Aldur 1 Divine ≈ 529 Exalted ≈ 7.7 Chaos. poe.ninja's exchange lines are in Divine (`core.primary`), with `core.rates` for the others. **C**
- **Gold** drops in stacks; it pays vendors, respecs, Currency Exchange fees and Merchant's Tab fees. Omen of Bartering makes a vendor misjudge an item's gold value. **L**
- **Waystones** Tier 1–16 (area level 65–80); a Vaal Orb can push Tier 15 to 16. The Reliquary is a place (Reliquary Keys), not a currency. **C**
- **Exceptional items** and Fracturing Orbs drop only with Atlas specialisation since 0.5.0. **C**
- **Flasks and charms:** one life and one mana flask; charm slots from belts and the Act 2 Medallion choice. **C**
- **Instilling:** Liquid Emotions add notables to amulets; Potent emotions and "Raven-Touched" uniques since 0.5.0; no waystones since 0.4.0. **C**

## 10. Where SSE differs from the game

A snapshot of 28 Nov 2026, worst first. Fix the code or the data, then delete the line here. "Pack" is `fixtures/packs/practice-0.5.5.json` (until 28 Nov 2026 `synthetic-demo.json`).

### 10.1 Craft simulator (`src/StillSaneExile.Domain/Craft/CraftEngine.cs`)

Fixed on 28 Nov 2026: Vaal Orb outcomes, the Greater and Perfect minimum levels and their exception, Alchemy on Magic items, fracturing crafted stats, the corrupted wording, "every orb". Still left out of the bench: Orb of Chance, Artificer's Orb, Hinekora's Lock, Perfect and Corrupted essences, desecration, and which corruption enchantment a Vaal Orb adds.

### 10.2 Demo data pack

Fixed on 28 Nov 2026: the pack is now the game's 0.5.5 data from Path of Building's export (every crossbow family with its real side, names, tiers, levels, ranges and wording; the Siege Crossbow; the essences' real tiers), labelled "not checked in game" wherever a range from it is shown. Still open:

1. **Weights are made up** (250, 500, then 1000): the game files hold none, and the published estimates may not be copied (§4.1). The craft simulator's badge says its odds are made up.
2. **"Loads an additional bolt" / "Loads 2 additional bolts" are missing** from the crossbow pool: their two tiers are worded differently, and a pack family holds one wording for all its tiers. Needs the pack format to allow wording per tier.
3. **Demo listings can show numbers no tier allows.** `DemoMarket` spreads each stat from 0.65 to 1.2 times the practice item's own roll, so a crossbow listing can show 20–21 % attack speed (the most a crossbow rolls is 19 %). They are labelled demo, but should stay inside what the game can roll.

### 10.3 Item parser (`src/StillSaneExile.Domain/Items/`)

Fixed on 28 Nov 2026: thousands separators, `Sanctified` and `Unidentified (Tier N)`, ` — Unscalable Value`, element tags on properties, `(unmet)` requirements and the comment on reductions. Still open:

1. **Unrevealed desecrated modifiers** (`Desecrated Prefix` / `Desecrated Suffix`, and `{ Prefix Modifier "Veiled" }` in an advanced copy) become an explicit modifier with the text "Desecrated Prefix" instead of an unrevealed slot.
2. **Catalyst headers** `{ … — Attack — 60% Increased }`: the third part lands in the tag list.

### 10.4 Practice items (`fixtures/items/`)

Fixed on 28 Nov 2026: real 0.5.5 bases (Siege Crossbow, Dragonscale Boots, Gladiatorial Helm with its Evasion line, requirements and item level), property lines worked out from the base and the stats (§1.8; `PracticeItemTests`), the rune's weapon form (`Adds 26 to 32 Fire Damage (rune)` from two Greater Desert Runes, with its `Fire Damage:` line), the bare `{ Implicit Modifier }` header, and the game's tiers and ranges for every stat. The items, their names and rolls stay made up, as intended (the unique on the real Gold Ring too). Still open:

1. **Not checked against real copies.** The tag words in the advanced headers (`— Defences, Armour, Evasion`, `— Attack, Critical`) and the untagged `"of Plunder"` follow Path of Building's modifier tags and the copies in §2.3; whether the game prints exactly these is not seen in a copy.

### 10.5 Live prices (`data/poe-ninja.json`)

Fixed on 28 Nov 2026: `Abyss`, `Breach` and `UniqueSanctumRelics` are fetched.

## Sources

Patch notes are on GGG's [PoE2 patch notes forum](https://www.pathofexile.com/forum/view-forum/2212). Real copies: [Exiled Exchange 2][ee2] issues and `renderer/specs/`, and the forum threads below. Game data: [poe2db][db-cur] and [Path of Building PoE2][pob].

[pn020]: https://www.pathofexile.com/forum/view-thread/3740562
[pn021]: https://www.pathofexile.com/forum/view-thread/3787044
[pn030]: https://www.pathofexile.com/forum/view-thread/3826682
[pn031]: https://www.pathofexile.com/forum/view-thread/3862213
[pn040]: https://www.pathofexile.com/forum/view-thread/3883495
[pn050]: https://www.pathofexile.com/forum/view-thread/3932540
[pn054]: https://www.pathofexile.com/forum/view-thread/3975218
[pn055]: https://www.pathofexile.com/forum/view-thread/4000864
[pn055h]: https://www.pathofexile.com/forum/view-thread/4016665
[fr]: https://www.pathofexile.com/forum/view-thread/3999865
[date10]: https://www.pathofexile.com/forum/view-thread/3999366
[press10]: https://www.pathofexile.com/forum/view-thread/4013917
[steam10]: https://store.steampowered.com/news/app/2694490/view/1848664643347683
[f3739189]: https://www.pathofexile.com/forum/view-thread/3739189
[f3848640]: https://www.pathofexile.com/forum/view-thread/3848640
[f3849983]: https://www.pathofexile.com/forum/view-thread/3849983
[f3851632]: https://www.pathofexile.com/forum/view-thread/3851632
[f3891367]: https://www.pathofexile.com/forum/view-thread/3891367
[db-cur]: https://poe2db.tw/us/Stackable_Currency
[db-cur-010]: https://web.archive.org/web/20241213004045/https://poe2db.tw/us/Stackable_Currency
[db-cur-030]: https://web.archive.org/web/20250901114621/https://poe2db.tw/us/Stackable_Currency
[db-cur-040]: https://web.archive.org/web/20260124105712/https://poe2db.tw/us/Stackable_Currency
[db-omen]: https://poe2db.tw/us/Omen
[db-ess]: https://poe2db.tw/us/Essence
[db-des]: https://poe2db.tw/us/Desecrated_Modifiers
[db-mml]: https://poe2db.tw/us/Minimum_Modifier_Level
[db-xbow]: https://poe2db.tw/us/Crossbows
[db-helm]: https://poe2db.tw/us/Helmets_str
[db-gdr]: https://poe2db.tw/us/Greater_Desert_Rune
[db-w]: https://poe2db.tw/us/weightings
[db-way]: https://poe2db.tw/us/Waystones
[db-uncut]: https://poe2db.tw/us/Uncut_Skill_Gem
[db-sup]: https://poe2db.tw/us/Support_Gems
[db-lin]: https://poe2db.tw/us/Lineage_Supports
[db-act]: https://poe2db.tw/us/Act_1
[wiki-corr]: https://www.poe2wiki.net/wiki/Corrupted
[ee2]: https://github.com/Kvan7/Exiled-Exchange-2
[ee2-426]: https://github.com/Kvan7/Exiled-Exchange-2/issues/426
[ee2-573]: https://github.com/Kvan7/Exiled-Exchange-2/issues/573
[ee2-617]: https://github.com/Kvan7/Exiled-Exchange-2/issues/617
[ee2-776]: https://github.com/Kvan7/Exiled-Exchange-2/issues/776
[ee2-954]: https://github.com/Kvan7/Exiled-Exchange-2/issues/954
[ee2-956]: https://github.com/Kvan7/Exiled-Exchange-2/issues/956
[ee2-997]: https://github.com/Kvan7/Exiled-Exchange-2/issues/997
[pob]: https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2
[coe]: https://craftofexile.com/weightings?game=poe2
[dev-data]: https://www.pathofexile.com/developer/docs/data
[dev-ref]: https://www.pathofexile.com/developer/docs/reference
[ninja-index]: https://poe.ninja/poe2/api/data/index-state
[ninja-unique]: https://poe.ninja/poe2/api/economy/stash/current/item/overview?league=Forbidden%20Rites&type=UniqueWeapons
[trade-filters]: https://www.pathofexile.com/api/trade2/data/filters
[trade-stats]: https://www.pathofexile.com/api/trade2/data/stats
[trade-static]: https://www.pathofexile.com/api/trade2/data/static
[trade-leagues]: https://www.pathofexile.com/api/trade2/data/leagues
[maxroll-corr]: https://maxroll.gg/poe2/resources/corruption-outcomes
[maxroll-asc]: https://maxroll.gg/poe2/getting-started/trials-of-ascendancy
[maxroll-perm]: https://maxroll.gg/poe2/getting-started/permanent-stats-from-campaign
[maxroll-10]: https://maxroll.gg/poe2/news/everything-new-in-path-of-exile-2-1-0
[switchblade]: https://www.switchbladegaming.com/path-of-exile-2/vaal-orb-outcomes/
[metabot]: https://metabot.gg/en/path-of-exile-2/campaign/act-1
[rpgsite]: https://www.rpgsite.net/news/21188-path-exile-2-reaches-its-10-full-release-on-december-11
