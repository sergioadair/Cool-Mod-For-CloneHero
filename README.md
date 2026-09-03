# Cool Mod For Clone Hero

A set of quality-of-life features I always wanted Clone Hero to have. It
**fills in the difficulties a song is missing** so you can play that
Expert-only chart on Medium, gives every song a real **difficulty score** and a
profile of *why* it is hard, throws a **note streak** callout while you play,
and adds **custom menu backgrounds**, a background **slideshow**, a
**Favorites** filter and a **custom sound** when you finish a song.

The 1.1.0.6142 build also **updates itself** from this repo with one menu
option, and is built to stay out of the way while you are playing.

Two builds are available, one per game version:

| Game version | Folder | How it works |
|---|---|---|
| **1.1.0.6142-final** | [`v1.1.0.6142-final/`](v1.1.0.6142-final/) | MelonLoader mod (`.dll` you drop in) |
| **1.0.0.4080-final** | [`v1.0.0.4080-final/`](v1.0.0.4080-final/) | patched `Assembly-CSharp.dll` |

> Check your version in the upper corner of the main menu. Pick the folder that
> matches — they are **not** interchangeable.

---

## ✨ Features

### 🎸 Generate missing difficulties

Half the charts out there are Expert only. If you are not an Expert player,
that is half your library you cannot touch.

Press `Select` on any song and pick **`Generate Missing Difficulties`**. The mod
writes the Hard, Medium and Easy that were never charted, for every instrument
the song has.

They are not random. The rules come from measuring **3,175 official charts** —
Guitar Hero 1 through Warriors of Rock, World Tour, Live, Rock Band — to see what real charters actually do when they write an easier
version of a song:

- they **keep the notes on the strong beats** and drop the ones in between
- they **thin it out** to about three quarters of the notes, then two thirds again
- they **shrink chords** — three notes on Expert, two on Hard and Medium, single
  notes on Easy
- they **use fewer frets** — all five on Hard, four on Medium, three on Easy

Which is exactly what the mod does. Checked against the real thing, the
generated Hard puts a note in the same place as the human charter **85% of the
time**.

**Your original is safe.** It gets backed up next to the song before anything is
written, and **`Restore Song Chart`** puts it back whenever you want. Works with
plain song folders and with `.sng` files. Run `Scan Songs` afterwards to see the
new difficulties.

> Only downwards, and only for instruments the song already has. Easy cannot be
> turned into Expert — those notes do not exist anywhere — and a song with no
> bass does not get one invented.

---

### 🎯 Difficulty — a real 0–100 score for every song

Clone Hero's built-in *Intensity* is typed in by hand by whoever made the chart,
so it means something different from one song to the next. **Difficulty** is
measured from the chart itself — how fast you actually have to play during its
densest stretch — so it means the same thing across your whole library.

It shows up under the instrument icons in the song panel:

```
Difficulty: 73
```

**Generate it**: `Settings > General > Calculate Difficulty`. It spreads the work
across your CPU cores, leaves one free so the game stays smooth, and skips songs
it has already done — running it again after adding a few songs takes seconds.

**Sort by it**: `(Hold) Sort List > Sort Options > Difficulty`, grouped in tens.
**Hide it**: `Settings > Video > Show Difficulty`.

---

### 📊 Difficulty profile — *how* a song is hard, not just how much

Two charts can both score 73 and feel nothing alike. Hold the blue button on any
song — the same **Show Scoring Info** you already use — and you get this too:

```
Difficulty 36                      336 notes
Expert Keys                 1.3 avg NPS   5.4 max

Chords      ▓░░░░░░░░░
Technical   ▓▓▓▓▓▓▓░░░
Endurance   ▓▓▓▓▓░░░░░
Guitar 39   Bass 33   Keys 34   Drums 37
Hardest stretch at 4:44
```

- **Chords** — how much of it is more than one note at a time
- **Technical** — how much your hand has to move around the frets
- **Endurance** — whether it is relentless all the way through, or just spiky

The three bars are independent of the score on purpose: a song can be easy
overall and still be the most technical thing in your library.

**It describes the chart you are about to play.** Switch instrument or
difficulty and the numbers follow — the line under the score says which chart
they belong to. Pick something the song does not have and it says so, the way
the game says *No Part*.

The row above it scores every instrument the song does have, so you can see at a
glance that the drums are the hard part and the bass is a warm-up.

And **Hardest stretch** points at the worst part of that chart, so you can jump
straight there in practice mode instead of hunting for it.

---

### 🔥 Note streak callout

A
**`50 Note Streak!`** flying across the screen when you hit 50 notes without
missing, then 100, then every 100 after that.

It animates in, holds, and fades out — gold with a black outline so it stays
readable over bright backgrounds. Miss a note and the count starts over.

Toggle it at `Settings > Gameplay > Show Cool Note Streak`, and restyle it in
`settings.ini` if you want:

```ini
note_streak_size = 72        ; 20 to 200
note_streak_color = FFD14A   ; RRGGBB
note_streak_font =           ; empty = the game's own
```

The font has to be one the game ships — Windows fonts are not reachable from a
mod here. Partial names work, so any of these will do:

`Lato-Heavy` (the default) · `Lato-Bold` · `Lato-Regular` · `Lato-Light` ·
`LiberationSans`

> It reads the streak counter the game already draws under your score, so
> nothing about scoring is reimplemented or altered. With the option off, the
> code does not run at all — see below.

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

Plays the classic **"You Rock"** sound from GH3 when the results screen appears
— unless you failed the song, in which case the game's own failure sound gets
the last word. Replace the file to use your own: `.opus`, `.ogg`, `.mp3` and
`.wav` all work.

Toggle it at `Settings > Audio > Finished Song SFX`.

### 🔄 One-click updates

`Settings > General > Update Cool Mod` downloads the latest build straight from
this repo and installs it. The row tells you how it went — `up to date`,
`done, restart`, or `failed` — and the details go to `MelonLoader\Latest.log`.

There is no version number to keep track of: the mod compares what it downloads
with what you have installed, byte for byte. Restart the game to load the new
build.

You do not have to remember to check, either. On startup the mod looks for a
newer build, and if there is one the game's version label in the top right
corner turns into **Update CoolMod now!** until you take it. Set
`check_for_updates = 0` in `settings.ini` if you would rather it stayed quiet.

### ⚡ Stays out of the way while you play

Every one of the mod's per-frame checks is switched off the moment the gameplay
scene loads, and switched back on when you return to the menus. No frame drops,
no stutter.

The note streak callout is the only part that runs during a song, and it is
budgeted for it: with the option off it costs a single boolean check per frame,
and with it on, reading one integer.

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
4. Copy [`assets/Sounds/yourock.opus`](assets/Sounds/) into your
   **`Custom\Sounds\`** folder.
5. *(Optional)* Copy the images from [`assets/Menu Backgrounds/`](assets/Menu%20Backgrounds/)
   into your **`Custom\Menu Backgrounds\`** folder.

> **Where is that folder?** It depends on how the game is installed, and the
> mod detects it automatically — including when Documents is redirected to
> OneDrive:
>
> | Install | Folder |
> |---|---|
> | Portable | `PlayerData\Custom\` — next to `Clone Hero.exe` |
> | Normal | `Documents\Clone Hero\Custom\` |
> | Normal + OneDrive | `OneDrive\Documents\Clone Hero\Custom\` |
>
> The same applies to `settings.ini`. `MelonLoader\Latest.log` says which one
> it picked on startup — look for a `[Rutas]` line.
>
> If detection ever fails, it logs every path it tried and you can point it
> manually: create `MelonLoader\clone-hero-data-folder.txt` containing the
> full path to your `Clone Hero` data folder.

> After this first install you never have to download the `.dll` by hand
> again — `Settings > General > Update Cool Mod` does it for you.

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
`settings.ini` (in the folder shown above), next to the game's own. Everything
has a menu option except the slideshow interval and the sound volume.

| Key | Default | What it does |
|---|---|---|
| `difficulty_reference_nps` | `14` | Notes per second that equals 100/100. Raise it to compress the scale, lower it to expand |
| `menu_bg_slideshow` | `0` | Background slideshow on/off |
| `menu_bg_slideshow_seconds` | `900` | Seconds between background changes |
| `menu_background_custom` | — | Which custom background is selected (written by the mod) |
| `finished_song_sfx` | `1` | End-of-song sound on/off |
| `finished_song_sfx_volume` | `1` | Multiplies that sound's volume |
| `show_difficulty` | `1` | Show the `Difficulty: X` label on the song panel |
| `show_cool_note_streak` | `1` | Show the note streak callout during a song |
| `note_streak_size` | `72` | Size of the note streak text |
| `note_streak_color` | `FFD14A` | Its colour, `RRGGBB` |
| `note_streak_font` | — | One of the game's fonts; empty uses the default |
| `check_for_updates` | `1` | Look for a newer build on startup |
| `difficulty_last_ref` | — | Reference used on the last calculation (written by the mod) |

On the 1.0.0.4080 build the slideshow interval lives under `[video]` as
`menu_bg_slideshow_seconds`.

---

## ⚠️ Known limitation (1.1.0.6142 build)

**When you switch to the `Difficulty` sort, the list only redraws once you move
the selection.** Press up or down and the sections appear. Everything else about
it works — filters included.

The sections are rebuilt correctly the moment you pick the criterion; the game
simply does not repaint the song list by itself, because its own refresh routine
only knows about its built-in sort criteria. Calling that routine directly does
not help — the redraw is driven by the song select screen, not by the library.
If you know the engine well and have an idea, PRs are welcome.

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
