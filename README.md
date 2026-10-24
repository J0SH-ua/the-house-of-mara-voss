# The House of Mara Voss

A psychological point-and-click narrative in Godot 4.6 (C#). You are not
Mara Voss — you are the part of her that still knows something is wrong.
Examine the rooms of a house that misremembers itself, spend **Clarity**
to see objects as they truly are, and recover six memory fragments —
while watching the memory **corrupt** the deeper you insist on looking.

## Controls

| Input | Action |
| --- | --- |
| Left click | Examine / interact · advance dialogue (1st click completes the line, 2nd advances) |
| Right click | Release armed Clarity (free) |
| Space / Enter | Advance dialogue |
| End | Skip the current dialogue sequence |
| C | Arm / release Clarity (or click the eye toggle in-room) |
| Tab (hold) | Reveal remaining hotspots |
| H | Hint (progressive: nudge → names the object → rings it) |
| J / M | Memory Journal — fragments, echoes, auto-noted clues (incl. puzzle arrangements), and a free notes page kept in your save |
| Q / Backspace | Step back — leave a puzzle for its room, or return to the previous room (also the on-screen Step Back button, bottom-left) |
| Click / Esc (cutscene) | Advance / skip the post-Play memory cutscene (New Game only; Continue skips it) |
| Esc / P | Pause menu (Esc first releases Clarity if armed) |

## Systems (what lives where)

| System | File | Notes |
| --- | --- | --- |
| Input actions | `scripts/managers/InputSetup.cs` + `project.godot [input]` | Declared in the project file; any missing action is re-created at runtime. |
| Settings | `scripts/managers/Settings.cs` | `user://settings.cfg` — volumes, text speed, auto-advance, fullscreen, reduce-motion, corruption intensity, hotspot hints. |
| Audio | `scripts/managers/AudioManager.cs` | Autoload. Creates Music/SFX/Ambience buses at runtime if no bus layout exists; crossfading music, polyphonic SFX pool, looping room tone, corruption heartbeat + music detune/muffle. |
| SFX pack | `assets/Music/SFX/*.wav`, `tools/generate_sfx.py` | 30 procedurally synthesised sounds, tuned to the score: spectral analysis puts the menu track in **F minor** (~359 Hz centroid, ~65 BPM), so the pack is voiced as felt piano / worn music box in F minor, corruption as warped tape. Regenerate with `python3 tools/generate_sfx.py`; drop-in replace any file to art-direct it. |
| Run state & saves | `scripts/managers/GameState.cs` | `user://mara_voss_save.json` — shards, examine progress, corruption, clarity, journal echoes, stats. Autosaves on scene change / shard / room completion. `Globals.cs` remains the raw field store older scenes read directly. |
| Clarity: Recollection | `GameState.CanRecollect/PerformRecollection`, `Hotspots.RefreshPersistent` | With ≥4 charges and a nearly-known room (≥60% examined, max 5), arming Clarity offers a choice: spend **all** charges to ring every remaining examinable in clarity-blue until faced (same ring animation as Tab hints). The next room starts with (revealed + 1) Clarity. Rings and the carry persist in the save. |
| Corruption | `GameState` tiers + `assets/shaders/corruption.gdshader` | Examining past an object's first layer adds corruption → tiers (Intact/Strained/Fractured/Shattered) drive the screen shader, dialogue text glitches, heartbeat and music decay. Clarity reveals also relieve corruption (−2). If corruption empties the MEMORY meter, the memory **shatters**: the room tears apart on screen and the bad ending (The Hollow Ending) plays, with a choice to gather the pieces (resume at 13 corruption) or let it go (menu). |
| Clarity | `scripts/ui/ClaritySystem.cs` | Arm free, spend on reveal, cancel free, +1 charge per completed room (max 5). |
| Dialogue | `scripts/ui/Dialogue.cs` | Click-to-advance typewriter with blips; `ShowText` API unchanged for existing call sites; calls serialise; every line is logged to the journal. |
| HUD / menus | `scripts/ui/GameUI.cs` | Autoload `CanvasLayer`, everything built in code: objective, clarity pips, integrity meter, fragment counter, toasts, pause, settings, journal, corruption overlay. |
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
- **Collection is idempotent.** Shards go through
  `GameState.CollectShard` (this fixed a double-fired brass-key handler
  and an arrow that granted the shard for free).

## Verifying in the editor (suggested pass)

1. Open the project (Godot 4.6 mono). First import will process the new
   WAVs and the shader — watch for import errors.
2. Build the C# solution; fix anything the compiler flags (this pass was
   written and syntax-checked outside Godot).
3. Play: intro (click to skip) → menu music + Settings button → Start.
4. Room 1: HUD objective counts up as you examine; drawer explains its
   lock; hold Tab; press H three times; arm Clarity (C), right-click to
   release, then clarify the toast; over-examine to hear corruption rise.
5. Padlock: dials tick; Esc backs out; dial `71483` → an **Unlock** button appears → drawer opens → **click the key** → Room 2. (Physics picking is switched on in GameManager — SubViewports default it off, which kills `_InputEvent` targets like the key/arrows.) Stepping back to the kitchen afterwards shows the cabinet still open.
6. Wrong 4-letter word rattles; `HOME` opens. Photo → Room 3 → jars →
   Room 4; hover the photographs in order (chimes ascend).
7. Esc → pause: save, journal, settings sliders (volumes should respond
   live), quit to menu → **Continue** appears and resumes the room.

## Known limitations

- Continue restores progress/flags, but examine layers that freed
  themselves mid-room reappear visually (tracker still gates correctly).
- Rooms 5–7 were already works-in-progress; Room 7 shard placement and
  its completion flow are implemented, but shards 3–6 have no collectors
  yet — the journal lists them as unremembered.
- Procedural SFX are tasteful placeholders with correct loudness staging,
  meant to be replaced file-by-file as real recordings land.
