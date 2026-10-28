# EnDetailer

A DPS meter for FFXIV that shows **what you are doing right now** — not what the whole
fight averaged out to.

![icon](EnDetailer/images/icon.png)

## Why

Every parser shows the same number: total damage divided by fight duration. After eight
minutes that value barely moves. You can stop attacking entirely and it still reads
`16K` half a minute later. As a live display it tells you nothing, because it cannot
fall.

EnDetailer shows a **real-time value** instead. Stop attacking and it drops. Burst and it
climbs. In a real fight you see roughly 20K during burst, 10K during downtime and
something in between while your rotation runs — all in the same fight where a
conventional meter shows a flat 16K.

The fight average is still there, one click away, because it answers a different
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

- **Real-time DPS** that rises with burst and falls when you stop, with an adjustable
  reaction time (4–30 s, default 10 s)
- **Fight average** as an alternative view, calculated like ACT and FFLogs
- **Damage, healing and damage taken**, switchable from the window header
- **Fight graph** — a slim line of the party's real-time value across the fight, so
  burst windows show up as peaks
- **Details on hover** — share, fight DPS, biggest hit with its ability, crit, direct hit
  and crit direct hit; matching values for healing and damage taken
- **History of the last ten fights**, browsable from the header or the right-click menu
- **Encounters survive cutscenes**, boss phases and downtime. The fight ends when you
  are actually out of combat, not when the game briefly says so
- **Numbers freeze** at the last hit when the fight ends and reset only when the next one
  begins
- **Show it when you want it** — always, in combat (with a short linger), or in duties
  only; optionally hidden in cutscenes. Never on the title or loading screens
- **Sortable table**, job colours and icons, own row highlighted, adjustable
  transparency, lockable click-through window
- **Preview** with a made-up party to set up the look without a fight

## Using it

The icons in the window header switch between damage, healing and damage taken. The
arrows next to them step through earlier fights. The gear opens the settings.

**Right-click anywhere in the window** for the menu: measure, real-time or fight average,
history, lock and settings.

**Click a column header** to sort by it, click again to reverse. The bar length follows
whatever you sorted by. **Hover a row** for that player's details.

If you lock the window it becomes click-through, so buttons and the menu are unavailable
until you unlock it — `/endetailer lock` toggles that. The header still shows which
measure is active.

## Commands

| Command | Effect |
|---|---|
| `/endetailer` | Show or hide the meter |
| `/endetailer config` | Open settings |
| `/endetailer lock` | Lock the window (click-through) |
| `/endetailer preview` | Fill the meter with a made-up party |

## A note on accuracy

Everything is computed from IINACT's per-second snapshots; nothing is estimated or
predicted. The real-time value is a smoothed rate: each hit fades in and out over the
reaction time instead of jumping, which keeps the number readable without hiding a
burst. The fight average is unsmoothed — total divided by the time up to the last hit.

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
