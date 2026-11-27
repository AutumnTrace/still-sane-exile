# Campaign guide

The guide page tracks one route through the campaign for each of your characters, and can pin two short lines over the game: where you are and what to do there, then what comes next.

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

Line 1 is the zone and the first open step there (or the leg's hint). Line 2 is the route's next stop. Placement (top left, below the buffs) has not been measured on real screens yet (#5).
