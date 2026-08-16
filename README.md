# Cool Mod For Clone Hero

A set of quality-of-life features I always wanted Clone Hero to have: a real
**difficulty score** for every song, **custom menu backgrounds**, a background
**slideshow**, a **Favorites** filter and a **custom sound** when you finish a
song.

Two builds are available, one per game version:

| Game version | Folder | How it works |
|---|---|---|
| **1.1.0.6142-final** | [`v1.1.0.6142-final/`](v1.1.0.6142-final/) | MelonLoader mod (`.dll` you drop in) |
| **1.0.0.4080-final** | [`v1.0.0.4080-final/`](v1.0.0.4080-final/) | patched `Assembly-CSharp.dll` |

> Check your version in the bottom corner of the main menu. Pick the folder that
> matches — they are **not** interchangeable.

---

## ✨ Features

### 🎯 Difficulty — a real 0–100 score for every song

Clone Hero's built-in *Intensity* is a number the charter types in by hand, so
it means something different from one chart to the next. **Difficulty** is
calculated from the chart itself, so it is consistent across your whole library.

It shows up right under the instrument icons in the song panel:

```
Difficulty: 73
```

**How it is calculated** — difficulty is how fast you actually have to play
during the song's densest stretch:

| Step | What happens |
|---|---|
| 1 | Every note time is read from the `.chart` / `.mid`, converted to real seconds (BPM changes included). **Chords count as one note** |
| 2 | A **10-second sliding window** finds the densest stretch → peak notes per second |
| 3 | Per instrument, the difficulties are averaged with ascending weights, so Expert dominates |
| 4 | **25 %** the average across instruments + **75 %** the single hardest chart |
| 5 | Scaled with **14 NPS = 100**, clamped and rounded |

Why the peak and not the average: a calm song with one brutal solo *does* play
hard. Measured against `diff_guitar` over ~3600 songs, the average correlates
0.36 and the peak 0.58.

On a typical library the median song lands around **42**, the top 10 % above
**65**, and the top 1 % above **89**.

**Generate it**: `Settings > General > Calculate Difficulty`. A progress panel
shows while it runs (~40 s for 3600 songs) and the value is written to each
`song.ini` as `diff_global`. Restart or rescan afterwards so the game reloads
them.

**Sort by it**: `(Hold) Sort List > Sort Options > Difficulty`, grouped in tens
— `90-99 Difficulty`, `80-89 Difficulty`… and `No Difficulty` at the end.

---

### 🖼️ Custom menu backgrounds

Drop any `.png` / `.jpg` / `.jpeg` into your **Menu Backgrounds** folder and
they show up as extra options in `Settings > Video > Menu Backgrounds`, listed
by file name. Your choice is remembered between sessions.

A starter pack is included in [`assets/Menu Backgrounds/`](assets/Menu%20Backgrounds/).

### 🔀 Menu background slideshow

`Settings > Video > Menu BG Slideshow` — rotates through your custom
backgrounds automatically. The interval is configurable (15 minutes by default).

### ⭐ Favorites filter

The game already lets you favorite songs and sort by it, but there is no way to
**filter** by it. This adds `Favorites` to `(Hold) Sort List > Filter Options`.

It reuses the game's own favorites, so nothing is stored separately and your
existing favorites just work.

### 🔊 Custom sound when you finish a song

Plays the classic **"You Rock"** sound from GH3 when the results screen appears.
Replace the file to use your own — `.opus`, `.ogg`, `.mp3` and `.wav` all work.

Toggle it at `Settings > Audio > Finished Song SFX`.

---

## 📥 Installation

### For game version 1.1.0.6142-final

This build is a **MelonLoader mod**, so it does not touch any game file.

1. **Install MelonLoader 0.7.3 or newer** — download from
   [github.com/LavaGang/MelonLoader](https://github.com/LavaGang/MelonLoader/releases)
   and extract `MelonLoader.x64.zip` into your Clone Hero folder (the one with
   `Clone Hero.exe`).
2. **Run the game once** and wait at the main menu. MelonLoader generates the
   files it needs — this takes a couple of minutes the first time. Close it.
3. Copy [`v1.1.0.6142-final/CloneHeroMod.dll`](v1.1.0.6142-final/) into the
   `Mods` folder that MelonLoader created.
4. Copy [`assets/Sounds/yourock.opus`](assets/Sounds/) into
   `\Custom\Sounds\`.
5. *(Optional)* Copy the images from [`assets/Menu Backgrounds/`](assets/Menu%20Backgrounds/)
   into `\Custom\Menu Backgrounds\`.

> Where is `PlayerData`? On a **portable** install it sits next to
> `Clone Hero.exe`. On a normal install use `Documents\Clone Hero\` instead.

**To uninstall**, delete `Mods\CloneHeroMod.dll`. To remove everything, delete
the `MelonLoader` and `Mods` folders and `version.dll`.

### For game version 1.0.0.4080-final

This build replaces a game file, so **make a backup first**.

1. Back up
   `…\Clone Hero\Clone Hero_Data\Managed\Assembly-CSharp.dll`.
2. Replace it with [`v1.0.0.4080-final/Assembly-CSharp.dll`](v1.0.0.4080-final/).
3. Copy [`assets/Sounds/yourock.opus`](assets/Sounds/) into
   `Documents\Clone Hero\Custom\Sounds\` (create the folder if needed).
4. *(Optional)* Copy the images into
   `Documents\Clone Hero\Custom\Menu Backgrounds\`.

---

## ⚙️ Settings

The 1.1.0.6142 build keeps its settings in a `[mods]` section of your
`settings.ini`, next to the game's own. Everything has a menu option except the
slideshow interval and the sound volume.

| Key | Default | What it does |
|---|---|---|
| `difficulty_reference_nps` | `14` | Notes per second that equals 100/100. Raise it to compress the scale, lower it to expand |
| `menu_bg_slideshow` | `0` | Background slideshow on/off |
| `menu_bg_slideshow_seconds` | `900` | Seconds between background changes |
| `menu_background_custom` | — | Which custom background is selected (written by the mod) |
| `finished_song_sfx` | `1` | End-of-song sound on/off |
| `finished_song_sfx_volume` | `1` | Multiplies that sound's volume |

On the 1.0.0.4080 build the slideshow interval lives under `[video]` as
`menu_bg_slideshow_seconds`.

---

## ⚠️ Known limitation (1.1.0.6142 build)

**While sorting by `Difficulty`, song filters do not update the list on screen.**
Every other sort criterion works normally, and no other feature is affected.

The song sections *are* rebuilt correctly behind the scenes — the game just does
not repaint them, because its refresh routine only knows about its own sort
criteria. Several approaches were tried and documented; if you know the engine
well and have an idea, PRs are welcome.

---

## 🔧 Building from source

The 1.1.0.6142 mod source is in [`v1.1.0.6142-final/src/`](v1.1.0.6142-final/src/).
You need the .NET 6 SDK and a copy of the game with MelonLoader already run once
(the build references the interop assemblies it generates).

```bash
dotnet build src/CloneHeroMod.csproj -c Release -p:GameDir="C:\path\to\Clone Hero"
```

The resulting `.dll` is copied into the game's `Mods` folder automatically.

> **If you edit the source**: the `.cs` files **must** be saved as UTF-8 **with
> BOM**. They contain the game's obfuscated identifiers as Unicode modifier
> letters, and without a BOM the compiler reads them with the system ANSI
> codepage and silently corrupts them.
