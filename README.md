# The House of Mara Voss

A psychological point-and-click narrative in Godot 4.6 (C#). You are not
Mara Voss — you are the part of her that still knows something is wrong.
Examine the rooms of a house that misremembers itself, spend **Clarity**
to see objects as they truly are, learn four late-game **Memory Verbs**, and
recover six fragments — while watching the memory **corrupt** the deeper
you insist on looking. The completed route runs through all seven rooms to
one of three endings.

## Controls

| Input | Action |
| --- | --- |
| Left click | Examine / interact · advance dialogue (1st click completes the line, 2nd advances) |
| Right click | Release armed Clarity or cancel an armed Memory Verb (free) |
| Space / Enter | Advance dialogue |
| End | Skip the current dialogue sequence |
| C | Arm / release Clarity (or click the eye toggle in-room) |
| E / A / T / R | **Echo / Anchor / Trace / Release** when learned — hear order, hold one truth still, connect causes, or decide a completed memory (also available from the bottom verb bar) |
| Tab (hold) | Reveal remaining hotspots |
| H | Hint (progressive: nudge → names the object → rings it) |
| J / M | Memory Journal — fragments, echoes, auto-noted clues (incl. puzzle arrangements), and a free notes page kept in your save |
| B / I or Bag button | Mara's Bag / inventory — carried, missing, and returned fragments plus learned Memory Verbs; available in every room |
| Q / Backspace | Step back — leave a puzzle for its room, or return to the previous room (also the on-screen Step Back button, bottom-left) |
| Click / Esc (cutscene) | Advance / skip the post-Play memory cutscene (New Game only; Continue skips it) |
| Esc / P | Pause menu (Esc first releases Clarity or a Memory Verb if armed) |

## Systems (what lives where)

| System | File | Notes |
| --- | --- | --- |
| Input actions | `scripts/managers/InputSetup.cs` + `project.godot [input]` | Declared in the project file; any missing action is re-created at runtime. |
| Settings | `scripts/managers/Settings.cs` | `user://settings.cfg` — volumes, text speed, auto-advance, fullscreen, reduce-motion, corruption intensity, hotspot hints. |
| Audio | `scripts/managers/AudioManager.cs` | Autoload. Creates Music/SFX/Ambience buses at runtime if no bus layout exists; crossfading music, polyphonic SFX pool, looping room tone, corruption heartbeat + music detune/muffle. Rooms 1–6 each have a distinct seamless generated loop (`assets/Music/bg/room_1.wav` … `room_6.wav`); Room 7 keeps the finale track. |
| SFX pack | `assets/Music/SFX/*.wav`, `tools/generate_sfx.py` | 33 procedurally synthesised sounds, tuned to the score: spectral analysis puts the menu track in **F minor** (~359 Hz centroid, ~65 BPM), so the pack is voiced as felt piano / worn music box in F minor, corruption as warped tape. Regenerate with `python3 tools/generate_sfx.py`; drop-in replace any file to art-direct it. |
| Run state & saves | `scripts/managers/GameState.cs` | `user://mara_voss_save.json` (save v2) — carried/exactly placed fragments, examine progress, corruption, clarity, learned verbs and use counts, story flags, journal echoes/clues/notes, completed rooms and stats. Autosaves on scene change / fragment / room completion. `Globals.cs` remains the raw field store older scenes read directly. Endings already found persist across runs in settings. |
| Clarity: Recollection | `GameState.CanRecollect/PerformRecollection`, `Hotspots.RefreshPersistent` | With ≥4 charges and a nearly-known room (≥60% examined, max 5), arming Clarity offers a choice: spend **all** charges in a custom in-game panel to reveal every remaining examinable in its true Clarify form, one by one, and ring it in clarity-blue until faced (same ring animation as Tab hints). The next room starts with (revealed + 1) Clarity. Rings and the carry persist in the save. Rooms 5–6 deliberately exclude Recollection because their authored Memory Verb puzzles should not be bypassed. |
| Corruption | `GameState` tiers + `assets/shaders/corruption.gdshader` | Examining past an object's first layer adds corruption → tiers (Intact/Strained/Fractured/Shattered) drive the screen shader, dialogue text glitches, heartbeat and music decay. Clarity reveals also relieve corruption (−2). If corruption empties the MEMORY meter, the memory **shatters**: the room tears apart on screen, glass cracks spread across the screen in sync with crack/shatter sounds, and the bad ending (The Hollow Ending) plays, with a choice to gather the pieces (resume at 13 corruption) or let it go (menu). |
| Clarity | `scripts/ui/ClaritySystem.cs` | Arm free, spend on reveal, cancel free, +1 charge per completed room (max 5). |
| Dialogue | `scripts/ui/Dialogue.cs` | Click-to-advance typewriter with blips; `ShowText` API unchanged for existing call sites; calls serialise; every line is logged to the journal. |
| HUD / menus | `scripts/ui/GameUI.cs` | Autoload `CanvasLayer`, everything built in code: objective, clarity pips, integrity meter, fragment counter, persistent Bag/inventory toggle, Memory Verb bar, toasts, pause, settings, journal, corruption overlay, Hollow shatter sequence, and the authored Waking / Kept House ending presentation. |
| Memory Verbs | `scripts/ui/MemoryVerbSystem.cs`, `GameState` | Four earned interaction modes with hotkeys and bottom-bar buttons: **Echo** (sound/order), **Anchor** (stabilise one truth), **Trace** (connect caused moments), and **Release** (resolve the assembled memory). Arming one disarms Clarity and vice versa; right-click/Esc cancels free. |
| Final act | `scripts/world/final_act/MemoryRoomBase.cs`, `room4/Room4Puzzle.cs`, `room5/Room5Final.cs`, `room6/Room6Final.cs`, `room7/Room7Final.cs` | Room 4's hallway sentence and brass door-knock, Room 5's anchored lamp/apology, Room 6's traced last afternoon, and Room 7's exact six-fragment lived-order finale. |
| Hotspots | `scripts/ui/Hotspots.cs` | Pulsing rings drawn with `_Draw`; hold-Tab reveal + level-3 hint pulse + examine click ripple. |

## Design rules implemented

- **Over-examining costs.** First look is free; deeper layers corrode the
  memory until text glitches, the score sours and you hear a heartbeat.
- **Clarity is an economy.** Spend it to see truly *and* to steady the
  memory; earn it back by finishing rooms.
- **Nothing fails silently.** Locked drawers explain themselves with
  progress (`3/7`), wrong padlock words rattle and clear, empty Clarity
  tells you how to recover it.
- **Everything read can be re-read** in the Memory Journal (J), including
  lines from examine layers that free themselves.
- **Inventory is global.** The old inventory icons were duplicated inside
  Rooms 1–3 only; Mara's Bag (B/I) is part of the persistent HUD and works
  in every room, distinguishing fragments still carried from ones returned.
- **Collection is idempotent.** Shards go through
  `GameState.CollectShard` (this fixed a double-fired brass-key handler
  and an arrow that granted the shard for free).
- **Later rooms change the verb, not just the lock.** Echo teaches order,
  Anchor teaches stillness, Trace teaches causality, and Release asks what
  a whole memory is for.
- **Three endings remember the player.** Memory shatter gives the Hollow
  Ending; completing Room 7 offers **Waking** or **The Kept House**. Found
  endings persist between New Games.

## Verifying in the editor (suggested pass)

1. Open the project (Godot 4.6 mono). First import will process the new
   WAVs, generated room/ending art and the shader — watch for import errors.
2. Build the C# solution; fix anything the compiler flags (this pass was
   written and syntax-checked outside Godot).
3. Play: intro (click to skip) → menu music + Settings button → Start.
4. Room 1: HUD objective counts up as you examine; drawer explains its
   lock; hold Tab; press H three times; arm Clarity (C), right-click to
   release, then clarify the toast; press B to open/close the empty bag; over-examine to hear corruption rise.
5. Padlock: dials tick; Esc backs out; dial `71483` → an **Unlock** button appears → drawer opens → **click the key** → the bottom-right Bag count becomes 1 → Room 2. (Physics picking is switched on in GameManager — SubViewports default it off, which kills `_InputEvent` targets like the key/arrows.) Stepping back to the kitchen afterwards shows the cabinet still open.
6. Wrong 4-letter word rattles; `HOME` opens. Photo → Room 3 → jars give
   Fragment III/Echo. In Room 4, ordinary photo hover only shows labels;
   arm **Echo (E)** and drift Pic 1→10 to hear the sentence and recover IV.
7. The Room 4 door opens its close-up: Echo brass plate → keyhole beneath
   it, three pairs left-to-right. In Room 5 examine the six required objects,
   **Anchor (A)** the lamp, open the desk drawer for V, then use the wardrobe.
   In Room 6 examine all four stops, **Trace (T)** basket → swing → gate →
   road for VI, then follow the road.
8. In Room 7 choose exact fragments from the bottom tray, place/reclaim
   them until all six stand I→VI, arm **Release (R)**, click the assembled
   memory, and choose **Let Mara wake** or **Keep the house**. The title
   card records the ending and clears only that run's save.
9. Esc → pause: save, journal, settings sliders (volumes should respond
   live), quit to menu → **Continue** appears and resumes the room.

## Known limitations

- Continue restores progress/flags, but examine layers that freed
  themselves mid-room reappear visually (tracker still gates correctly).
- This final-act pass was written and statically/syntax checked outside
  Godot. A local Godot .NET build and full playthrough remain necessary;
  in particular, confirm the generated Room 5/6 art aligns with the coded
  hotspot rectangles at 1152×648.
- Procedural SFX are tasteful placeholders with correct loudness staging,
  meant to be replaced file-by-file as real recordings land.
