# EnDetailer

A DPS meter for FFXIV that shows **what you are doing right now** — not what the whole
fight averaged out to.

![icon](EnDetailer/images/icon.png)

## Why

Every parser shows the same number: total damage divided by fight duration. After eight
minutes that value barely moves. You can stop attacking entirely and it still reads
`16K` half a minute later. As a live display it tells you nothing, because it cannot
fall.

EnDetailer averages over a **rolling time window** instead. Stop attacking and the value
drops. Burst and it climbs. In a real fight you see roughly 20K during burst, 10K during
downtime and something in between while your rotation runs — all in the same fight where
a conventional meter shows a flat 16K.

The encounter average is still there, one dropdown away, because it answers a different
question: how the fight went overall, comparable to what everyone else posts.

## Requirements

- [Dalamud](https://github.com/goatcorp/Dalamud) (XIVLauncher)
- [IINACT](https://github.com/marzent/IINACT) — the data source

## Installation

Add this repository to Dalamud under *Settings → Experimental → Custom Plugin
Repositories*:

```
https://raw.githubusercontent.com/Enjuchan/EnjuPlugins/main/repo.json
```

Then install **EnDetailer** from the plugin installer.

## Setup

One IINACT setting matters: turn **off** *"End encounter automatically after leaving
combat"*. EnDetailer decides when a fight is over on its own — and it does so better,
because it can read the game state directly instead of guessing from combat data.

If you run LMeter alongside, switch off its *"Force ACT to end encounter after combat"*
as well, so only one plugin cuts encounters.

## Features

- **Rolling DPS** over a configurable window (5–120 s, default 25 s)
- **Two weighting modes** — a flat window, or a weighted one where recent damage counts
  more and older damage fades out instead of dropping off a cliff
- **Encounters survive cutscenes**, boss phases and downtime. The fight ends when you
  are actually out of combat, not when the game briefly says so
- **Numbers freeze** when the fight ends and reset only when the next one begins
- **Sortable table** — name, total damage, crit rate, direct hit rate, DPS
- Job colours and icons, own row highlighted, adjustable transparency, accent colour,
  lockable click-through window

## Using it

**Right-click anywhere in the window** for the menu: switch between damage, healing and
damage taken, and choose whether the rate is averaged over the last few seconds or the
whole encounter. Which one is active is always visible in the last column header — DPS,
HPS or DTPS.

**Click a column header** to sort by it, click again to reverse. The bar length follows
whatever you sorted by.

The gear icon opens the settings. If you lock the window it becomes click-through, which
means the right-click menu is unavailable until you unlock it again — `/endetailer lock`
toggles that.

## Commands

| Command | Effect |
|---|---|
| `/endetailer` | Show or hide the meter |
| `/endetailer config` | Open settings |
| `/endetailer lock` | Lock the window (click-through, no title bar) |

## A note on accuracy

The rolling value is computed from IINACT's per-second snapshots. Everything shown is
measured — nothing is estimated, predicted or smoothed into the numbers themselves. The
optional easing settings only affect how the display *moves* towards a value, never the
value itself.

## Building

Requires the .NET 10 SDK. Dalamud's development assemblies are expected in the usual
location (`%AppData%\XIVLauncher\addon\Hooks\dev`).

```bash
dotnet build
dotnet test
```

`EnDetailer.Core` holds all the calculation logic and has no Dalamud reference, which is
what makes the encounter and DPS logic testable without the game running.

## Licence

GNU AGPL-3.0 — see [LICENSE](LICENSE).

In short: use it, change it, share it. If you distribute a modified version, that
version has to stay open source under the same licence. The point is to keep the code
available to everyone rather than to restrict what you do with it.
