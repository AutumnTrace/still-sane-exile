# Campaign guide

The guide page tracks one route through the campaign for each of your characters, reads your build from the Path of Building code you paste and turns its gems into steps that come up one stage at a time, and can pin two short lines over the game: where you are and what to do there, then your next gem step or the route's next stop.

SSE never reads the game's memory, files or logs, so it does not know which zone you are in. You tell it, with the zone list or Next and Back. Picking a zone only moves you: it never ticks a step. Everything works offline: the build code is read on your PC and no guide site is ever opened.

## Route packs

A route pack is a JSON file in `data/routes/` (copied next to the app on build and in the installer). The loader (`RoutePackLoader`) checks the whole pack before it is used and rejects it otherwise. A rejected pack is never partly applied: the last good one stays in use, in memory and across restarts (a copy is kept in `route-cache\` in the data folder), and the page says why.

| Field | Meaning |
|---|---|
| `schemaVersion` | Must be `1`. Any other version is rejected. |
| `id`, `revision` | Stable pack id and a revision number. An older revision never replaces a newer one. |
| `status` | `unverified` (written from general knowledge, not walked in game), `verified` (needs `verifiedDate`) or `synthetic` (test data). Anything but `verified` is labelled on the page and on the HUD lines. |
| `game`, `patch`, `league` | Must be `Path of Exile 2`; the patch and league the pack was written for. |
| `source`, `rights` | Where the facts came from and who may use the text. Required. |
| `acts` | The acts the pack covers, with titles. |
| `zones` | The graph: stable `id` (for example `a1.clearfell`), `act`, `name`, optional `aliases`, `town`, `waypoint` (left out when nobody has checked) and `edges` to neighbouring zones. Edges are two-way. Names and aliases must tell zones apart. |
| `route` | The legs in order. Each leg names a `zone`, how it is reached (`via`: `walk`, the default, needs an edge; `waypoint` needs a stated waypoint at both ends; `portal` goes to or from town), a short `hint`, and its `steps`. A zone can appear in several legs: going into a dead end and back out is two legs. |
| `steps` | Stable `id`, `kind` (`objective`, `bonus` for optional extras, `handIn` with an `npc`), short original `text`, and `rewards` (`text`, `permanent`). |

Route text is written for SSE in a few words per leg. No guide prose is copied; a community guide may inform the structure only (see [SOURCES.md](SOURCES.md), row 10).

### The Act 1 draft

`data/routes/act1-opening.json` covers the start of Act 1: the Riverbank, Clearfell Encampment, Clearfell, the Mud Burrow, the Grelwood and the Red Vale, with the way back out of the Mud Burrow and the Red Vale. It is an **unverified draft** for patch 0.5.x, to be checked after the 11 December 2026 full release, written from general campaign knowledge.

It keeps to zone names, order, connections and the main boss per zone. Left out until someone checks them in game: waypoints outside town, quest hand-ins and NPCs, every permanent reward, the Grelwood's own objectives and boss, and everything after the Red Vale. So its list of missed permanent rewards stays empty for now and says so.

## Progress

Progress is per character and stays on this PC (see [CAPTURE-AND-PRIVACY.md](CAPTURE-AND-PRIVACY.md)).

- Each tick, skip or reopen is saved as a numbered mark. The state of a step is its last mark, so **Undo** removes the last mark, and it still works after a restart. Moving between zones is not undone; Next and Back do that.
- **Resume**: the character's leg, zone and act are saved on every move, and the marks are replayed when the page opens.
- **Zone list**: picking a zone looks ahead first (the next visit from where you are), then back (the latest visit behind you). Coming back out of the Mud Burrow lands on the second Clearfell visit, not the first.
- Steps left open on an earlier visit to the same zone show up again when you come back.
- **Missed permanent rewards** lists rewards you skipped and rewards still open on legs you have moved past. Skipped steps can be reopened.
- Past the last leg you set the act yourself, so the build's later stages know how far along you are.

## HUD lines

Off by default; switch them on in the guide page. They show only while the game is in front (or for a few seconds over SSE when you press Preview), in a topmost, click-through window that never takes focus. They step aside while the item price card is showing and come back when it hides, and they hide on minimize and lock. Changing zone only changes the text: nothing pops up.

Line 1 is the zone and the first open step there (or the leg's hint). Line 2 is the build's next gem step when one is due ("Next gem · Cut support Zenith I"), otherwise the route's next stop. Placement (top left, below the buffs) has not been measured on real screens yet (#5).

## Your build

### Paste a Path of Building code, nothing else

The page has one input for the build: a Path of Building code. Copy it from Path of Building (Import/Export Build, Generate) or from your guide's Path of Building export, paste it and press **Read build**. Mobalytics' terms bar software access, downloads, mirroring and framing, and there is no authorized export, so SSE never opens, fetches or shows a guide page; the code is your own text and is read on your PC (`PobCode`).

A code is the build's XML (root `PathOfBuilding2`), zlib-compressed, then base64 with `-` and `_` in place of `+` and `/`: Path of Building's `ImportTab.lua` and SimpleGraphic's `Deflate` (see [SOURCES.md](SOURCES.md), row 22). SSE also takes plain base64, a missing `=` padding, line breaks and gzip. What it says when the text is not a build:

| You paste | SSE says |
|---|---|
| A link (pobb.in, poe.ninja, Maxroll, Pastebin, any site) | It is a link: copy the code itself. Nothing is opened. |
| A Mobalytics link | Use the page's Path of Building export and paste that code. |
| Text that is not base64, or base64 that is not compressed | Not a Path of Building code, and what one looks like |
| A code cut short or damaged | Cut short or damaged: copy all of it again |
| A Path of Exile 1 code (root `PathOfBuilding`) | It is a Path of Exile 1 build |

A code that unpacks to more than 32 MB, or that carries a document type (which could pull in other files), is refused. Nothing is saved until a code reads.

### What the build gives the guide

- **Gem steps**, from two places, both the author's own:
  - **The build's notes**, read by the same reader as before (`PlanNotesParser`: `cut Entangle`, `cut support Zenith I`, `socket Zenith I into Entangle`, `move Zenith I: Entangle → Essence Drain`, `upgrade …`, `reserve …`, `respec …`, stage labels like `Act 2:` or `after ascendancy:`, and `at Mud Burrow`, `(needs …)`, `(priority: …)`). A line counts only when every gem it names is in the build, so prose like "Move on to the Grelwood" never becomes a step.
  - **The skill sets.** A set's stage comes from its title: "Act 2", "Acts 4-6", "Leveling (Act 1)", "after ascendancy", "Act 3, post-ascendancy". The first set counts from the start when its title names none. For each set in order: cut each skill and support not cut yet, socket each support into its group's skill (the group's first skill), and move a support that now sits in another skill. A support in two groups of one set goes into the first. Switched-off groups and skills granted by items give no steps. **A later set whose title names no stage ("Endgame", "Level 40") gives no steps**, so it never leaks into Act 1; the page says which sets those are and that naming them after a stage in Path of Building and pasting again brings them in.
  - Within a stage the notes' steps come first. Gem levels are not turned into upgrades, because Path of Building often sets every gem to its top level whatever the stage. Whether a gem is a support comes from the ids Path of Building writes (`Metadata/Items/Gem/SupportGem…`, a `Support…` granted effect); SSE ships no gem data and claims nothing else about a gem, so a step says "Cut Entangle", never which uncut gem or level it takes.
- **The passive tree**, drawn on the game's tree (below).
- **Skills and supports**, one skill set at a time with a picker: each skill with its supports, the group's label, the level Path of Building has for it, and the author's gem notes. The set shown is the latest one for a stage you have reached, else the one the author had open.
- **The notes** as written, with Path of Building's colour codes taken out.
- **Items**: the item set the author had open, slot by slot, with names and base types only.
- **A few numbers** Path of Building worked out when the code was made (Life, Energy Shield, Mana, Spirit), labelled as Path of Building's.

Each step is a milestone: stage, zone or NPC, trigger (stage, or zone), prerequisites, action, gem, target skill, uncut gem type and level (only when the notes give them), the author's priority or "Author did not specify", and where in the build it came from.

### The passive tree

The build's tree is drawn on Path of Exile 2's own tree: `data/tree/poe2-tree.json`, a trim of Grinding Gear Games' official export (see [SOURCES.md](SOURCES.md), row 23), refreshed by hand with `scripts/update-passive-tree.ps1` after a patch. It ships with the app; nothing is downloaded.

- Every node and connection sits where the export puts it, curves along their orbits; the build's points and the connections between them are gold, notables and keystones larger, jewel sockets blue. The shapes are drawn in the game's colours; the game's art is not used (an owner decision, row 22).
- It opens on the build's points. The wheel zooms around the pointer and dragging moves it; **−**, **+**, **Fit** and **Whole tree** do the same from the keyboard. Pointing at a node shows its name and stats under the drawing.
- When the build has several trees (Path of Building's "Act 1", "Act 2", …), a picker shows each; the one shown first is the latest whose title names a stage you have reached, else the one the author had open. A tick elsewhere on the page never changes the drawing.
- The ascendancy is drawn on its own, under the main tree, because the export places ascendancies far outside it.
- Under the drawing: how many points the tree takes (the class and ascendancy starts are not counted), how many of them belong to weapon set 1 or 2, then the keystones and notables by name with their stats and the author's notes on them. That list is what a screen reader reads; the drawing is not a tab stop.
- A tree Path of Building made for another game version (`treeVersion` "0_4" against the shipped 0.5.5) says so. Points the shipped tree does not have are counted and named as missing, never placed by guesswork.

### Stage gating

A step comes up only when all of these hold:

- **Its stage is reached.** The act comes from the zone you picked (or the act you set past the route); ascendancies are the count you set on the page. Stages only move forward through the notes and through the skill sets, and an act label after "after ascendancy" keeps the ascendancy. So entering Act 2 keeps the pre-ascendancy steps going until you say the ascendancy is done, and steps for later stages stay hidden: they are counted, never shown as a prompt and never put on the HUD.
- **Every gem it acts on is in hand.** A gem the build cuts is in hand once that cut is ticked. A gem it never cuts is taken as already in hand. So `move Zenith I: Entangle → Essence Drain` waits until `cut Essence Drain` is ticked, whatever the act.
- **Its zone is reached**, if it names one.

The first step that passes, in order, is the HUD's "Next gem". The acceptance journey is a unit test on the synthetic practice build (`PobPlannerTests`, `fixtures/builds/practice-witch.xml`, and `PlanGateTests` on notes): cut Entangle, then Contagion, and the Zenith I move only once Essence Drain exists. It was not copied from any guide.

### Revisions

Each code you read is a revision, stored with the code and the steps as they were read, so a later version of SSE never changes a saved plan. The first build is used straight away, and so is a newer code whose steps are the same (new items or tree only). When a newer code changes the steps, it waits: the page lists what was added, removed and changed (a new target, stage, level, place or priority), and the old revision stays in use, on the HUD too, until you press "Use revision N". "Keep revision N" drops the new one. Ticks are kept per step, so they carry over to every step that did not change.

### Steps written in earlier versions

SSE 0.6 and earlier had you type your gem steps and save a guide link. That editor is gone: the pasted code replaces both. Steps you saved then stay in use, ticks included, until you paste a build, which then waits as a revision like any other change. A guide link saved then stays in your data folder but is no longer shown or opened.
