# Campaign guide

The guide page tracks one route through the campaign for each of your characters, turns your own build notes into gem steps that come up one stage at a time, and can pin two short lines over the game: where you are and what to do there, then your next gem step or the route's next stop.

SSE never reads the game's memory, files or logs, so it does not know which zone you are in. You tell it, with the zone list or Next and Back. Picking a zone only moves you: it never ticks a step. Everything works offline.

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
- Past the last leg you set the act yourself, so later build-plan stages know how far along you are.

## HUD lines

Off by default; switch them on in the guide page. They show only while the game is in front (or for a few seconds over SSE when you press Preview), in a topmost, click-through window that never takes focus. They step aside while the item price card is showing and come back when it hides, and they hide on minimize and lock. Changing zone only changes the text: nothing pops up.

Line 1 is the zone and the first open step there (or the leg's hint). Line 2 is the build plan's next gem step when one is due ("Next gem · Upgrade Entangle"), otherwise the route's next stop. Placement (top left, below the buffs) has not been measured on real screens yet (#5).

## Build plans

### The guide link: save and open, nothing else

Mobalytics' terms restrict software access, downloads, mirroring and framing, and there is no authorized export. So SSE only **saves the link you paste** and, when you click "Open in browser", asks Windows to open it in your default browser. It never fetches, embeds, frames or reads the page. Any `https://` link is accepted (Mobalytics or another planner; the same rule holds for all of them); `http`, `file`, `javascript`, addresses with a user name or password, and bare IP addresses are refused. An automatic import will only be added if Mobalytics offers an authorized one.

### Your notes become the checklist

You type or paste your own steps, one per line. `PlanNotesParser` reads them locally and the page shows a preview of every line next to what SSE made of it; lines it cannot read say what to write instead. Nothing is saved until you press Save, so you correct the notes while watching the preview.

| You write | SSE reads |
|---|---|
| `cut Entangle` | Cut a skill gem |
| `cut support Zenith I` | Cut a support gem |
| `upgrade Entangle to level 5` | Upgrade a gem (with the uncut gem's level) |
| `socket Zenith I into Entangle` | Socket a support into a skill |
| `move Zenith I: Entangle → Essence Drain` (or `move … from … to …`) | Move a support to another skill |
| `reserve Herald of Ash` | Reserve spirit for a gem |
| `respec …` | Respec, with whatever you wrote |
| `Act 2:` or `after ascendancy:` (alone or in front of a step) | The stage, from that line down |
| `at Mud Burrow`, `(level 3 uncut)`, `(needs Essence Drain)`, `(priority: high)`, `(optional)` | Where, uncut gem type and level, prerequisites, the author's priority |

Bullets, numbering and tick boxes are ignored, and so are lines starting with `#` or `//`. A step with no priority shows "Author did not specify". A place that names a zone in the route pack makes the step wait until you reach that zone; any other place (an NPC) is shown as written.

Each step is a milestone: stage, zone or NPC, trigger (stage, or zone), prerequisites, action, gem, target skill, uncut gem type and level, and priority.

### Stage gating

A step comes up only when all of these hold:

- **Its stage is reached.** The act comes from the zone you picked (or the act you set past the route); ascendancies are the count you set on the page. Stage labels only move forward through the notes, and an act label after "after ascendancy" keeps the ascendancy. So entering Act 2 keeps the pre-ascendancy plan going until you say the ascendancy is done, and steps for later stages stay hidden: they are counted, never shown as a prompt and never put on the HUD.
- **Every gem it acts on is in hand.** A gem the plan cuts is in hand once that cut is ticked. A gem the plan never cuts is taken as already in hand. So `move Zenith I: Entangle → Essence Drain` waits until `cut Essence Drain` is ticked, whatever the act.
- **Its zone is reached**, if it names one.

The first step that passes, in note order, is the HUD's "Next gem". The acceptance journey is a unit test (`PlanGateTests`) with notes written for SSE: cut Entangle, then Contagion, and the Zenith I move only once Essence Drain exists. It was not copied from any guide.

### Revisions

Each save is a revision, stored with the steps as they were read, so a later version of the parser never changes a saved plan. The first plan is used straight away. When you change a plan in use, the new revision waits: the page lists what was added, removed and changed (a new target, stage, level, place or priority), and the old revision stays in use, on the HUD too, until you press "Use revision N". "Keep revision N" drops the new one. Ticks are kept per step, so they carry over to every step that did not change.
