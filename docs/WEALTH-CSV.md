# Wealth CSV import

> **My wealth is switched off for now** while it is rethought: the app has no page to import from. Holdings imported or typed in with earlier versions stay in `sse.db`, untouched. This page describes the format as it was, for when the page comes back.

**My wealth → Import CSV…** reads a file of holdings, checks every row, shows you the result, and stores nothing until you press **Apply**. Apply stores all the file's new rows in one transaction, or none of them.

The file stays on your computer. SSE keeps the rows in its local database (`sse.db`) with the file's name, never its folder, and sends nothing anywhere.

## Example

```csv
league,tab,item_id,variant,quantity,snapshot_time
Demo League (synthetic),Currency,divine,,14,2026-11-27T10:00:00Z
Demo League (synthetic),Currency,exalted,,385,2026-11-27T10:00:00Z
Demo League (synthetic),Runes,greater-iron-rune,,18,2026-11-27T12:05:00+02:00
Demo League (synthetic),"Rings, keep",unique.sanitys-last-loop,,1,2026-11-27T10:05:00Z
```

A longer one is [`fixtures/wealth/example.csv`](../fixtures/wealth/example.csv), and [`fixtures/wealth/needs-fixing.csv`](../fixtures/wealth/needs-fixing.csv) has the common mistakes. Both are in the demo profile under **Practice files**.

## Columns

The first row is the header. Columns can come in any order; names ignore case, spaces, hyphens and underscores, so `Item ID` is `item_id`.

| Column | Required | What it holds |
|---|---|---|
| `league` | yes | The league the count is for, as the game shows it. Must be the league you are on in SSE (case and surrounding spaces don't matter). |
| `tab` | yes | The stash tab's name, up to 64 characters. Matched exactly: `Currency` and `currency` are two tabs. |
| `item_id` | yes | The price book's id for the item, e.g. `divine`, `greater-iron-rune`, `unique.sanitys-last-loop`. Trimmed and stored in lower case; no spaces. |
| `variant` | no | For items the price book splits into variants. Empty means none. Stored in lower case. Leave the column out if you never use it. |
| `quantity` | yes | How many you had then: a whole number, digits only, from 0 to 1,000,000,000. |
| `snapshot_time` | yes | When you counted, in ISO 8601 with `Z` or an offset. |

Any other column is an error, not ignored: a misspelt `varient` would otherwise drop every variant without a word.

An id the active price book doesn't know is fine. The preview marks the row **Unpriced**, and the holding is counted but not valued. A live league has no prices yet, so every row there is Unpriced for now.

## Values

- **Quantity** is a stack count. `1200` is right; `1,200`, `1 200` and `1.200` are refused because the separator means a thousand in some places and a decimal point in others. Decimals (`2.5`) and negatives (`-3`) are refused too. `0` means none left: the holding drops off the list but stays in the history.
- **Snapshot time** needs a time zone: `2026-11-27T10:00:00Z`, `2026-11-27T11:00:00+01:00`, `2026-11-27T11:00+0100` and `2026-11-27 10:00Z` all mean the same instant and are stored as that instant in UTC. `2026-11-27T10:00:00` is refused: without a zone it could be any of 24 hours. Fractions of a second are kept. A time more than 5 minutes ahead of the clock is refused.
- **Quotes**: a value with a comma, a double quote or a line break goes in double quotes, with any quote inside doubled: `"Bob's ""best"" tab"`. Text after a closing quote is an error. A line break inside a value is an error in every column.

## The file

- UTF-8, with or without a byte-order mark. In Excel: **Save As → CSV UTF-8 (Comma delimited)**. A file separated by semicolons is refused with that hint.
- Lines can end in CRLF, LF or CR. Blank lines, and lines of only commas, are skipped.
- At most 10,000 rows and 4 MB. Split a bigger file; each part is checked and applied on its own.
- Row numbers in the preview are line numbers in the file, header included, so they match a spreadsheet's row numbers.

## Checks

A problem with the file as a whole stops the preview with one message: no header, an unknown, duplicate or unnamed column, a missing required column, semicolons, not UTF-8, too big, too many rows, or a header with no rows under it.

Everything else is reported per row, every problem in the row at once:

- a wrong number of values (usually an unquoted comma, as in `1,200`),
- an unclosed quote (reported on the row where it starts; it swallows the rest of the file),
- an empty or malformed field, as above,
- **another league**: `This row is for league "Standard"; you are on "Demo League (synthetic)".` Rows are never carried from one league to another, and the demo profile never takes live rows or the other way round. Switch league in Settings, or fix the file. When the whole file is for another league, the preview says so at the top.
- **the same row twice**: two rows with the same tab, item, variant and snapshot time. The later one is the error, pointing at the first. Two spellings of one instant (`11:00+01:00` and `10:00Z`) count as the same time.
- **a different count already stored**: the database has another quantity for the same tab, item, variant and time. Two counts can't both be right at one instant, so fix the file.

**Apply** stays off while any row has an error, and says why. Nothing is half-imported.

## Importing again

Each row is an observation: "at this time, this tab held this many". The database keeps every observation and never edits one; a holding's current quantity is its latest observation.

So importing the same file twice is harmless: rows already stored with the same quantity show as **Already stored** and are skipped. A file can also carry history: several rows for one item at different times all go in, and the latest is what the list shows. A row older than what is already stored becomes history and doesn't change the current count; the preview says so.

A count typed into the **Type a count** form is an observation at the moment you save it.

## Demo and live

In the demo profile the league is `Demo League (synthetic)`, and the demo clock starts at 2026-11-27 11:30 UTC on every run, so snapshot times must be before that. Demo holdings are stored apart from live ones. **Settings → Remove demo prices and holdings I added** deletes them, and never touches a live profile.
