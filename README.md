# The House of Mara Voss

A psychological point-and-click narrative in Godot 4.6 (C#). You are not
Mara Voss — you are the part of her that still knows something is wrong.
Examine the rooms of a house that misremembers itself, spend **Clarity**
to see objects as they truly are, learn four late-game **Memory Verbs**, and
recover six fragments — while watching the memory **corrupt** the deeper
you insist on looking. The completed route runs through all seven rooms —
plus a landing flashback, Mara's present-day apartment and the Eldfen porch
at 4:47 — to one of three endings. Dialogue never tells you how to play;
the house is meant to be figured out.

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
| B / I or the satchel icon (top-right) | Mara's Bag / inventory — every fragment is a visible keepsake (key, photograph, jar, frame, letter, apple); carried, missing, and returned fragments plus learned Memory Verbs; available in every room |
| Q / Backspace | Step back — leave a puzzle for its room, or return to the previous room (also the on-screen Step Back button, bottom-left). Finished rooms you step back into are **sealed** (nothing in them answers anymore); the **Return →** button (bottom-right) takes you back to the furthest, newest room reached |
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
| Clarity: Recollection | `GameState.CanRecollect/PerformRecollection`, `Hotspots.RefreshPersistent` | With ≥3 charges and a nearly-known room (≥60% examined, max 5), **Remember it all** is offered — as a gold HUD button beside the bag, and again (retried until a free moment) when Clarity is armed: spend **all** charges: spend **all** charges in a custom in-game panel to reveal every remaining examinable in its true Clarify form, one by one, and ring it in clarity-blue until faced (same ring animation as Tab hints). The next room starts with (revealed + 1) Clarity. Rings and the carry persist in the save. Rooms 5–6 deliberately exclude Recollection because their authored Memory Verb puzzles should not be bypassed. |
| Corruption | `GameState` tiers + `assets/shaders/corruption.gdshader` | Examining past an object's first layer adds corruption → tiers (Intact/Strained/Fractured/Shattered) drive the screen shader, dialogue text glitches, heartbeat and music decay. Clarity reveals also relieve corruption (−2). If corruption empties the MEMORY meter, the memory **shatters**: the room tears apart on screen, glass cracks spread across the screen in sync with crack/shatter sounds, and the bad ending (The Hollow Ending) plays, with a choice to gather the pieces (resume at 13 corruption) or let it go (menu). |
| Clarity | `scripts/ui/ClaritySystem.cs` | Arm free, spend on reveal, cancel free, +1 charge per completed room (max 5). Spending the **last** charge in an unfinished TileMap room triggers the collapse (below). |
| Clarity collapse | `GameState.TickCollapse`, `GameUI` collapse overlay, `assets/Music/bg/collapse.wav` | At 0 Clarity the house takes the room back: a visible **15-second** blood-red count with warning text, drowning red tint, spreading screen cracks, the corruption shader closing to full, tick/crack/whisper SFX and a tense generated F-minor bed (15 s seamless loop). Corruption seeps +1 every 3 s; solving the room's lock / recovering its fragment / regaining a charge rescues it; expiry forces the shatter (Hollow Ending). The count only drains while the player can act — never while paused, reading dialogue, or behind a panel. Fleeing the room ends it at +2 corruption. |
| Dialogue | `scripts/ui/Dialogue.cs` | Click-to-advance typewriter with blips; `ShowText` API unchanged for existing call sites; calls serialise; every line is logged to the journal. |
| HUD / menus | `scripts/ui/GameUI.cs` | Autoload `CanvasLayer`, everything built in code: objective, clarity pips, integrity meter, fragment counter, icon-only Bag toggle, the **Remember it all** button, Memory Verb bar, toasts, pause, settings, journal, corruption + collapse overlays, Hollow shatter sequence, and the authored Waking / Kept House ending presentation. **How to Play is visual**: each page leads with the game's own art/UI — the kitchen, the cracking MEMORY meter, the Clarity eye + pips + red 0:15 count, verb key chips, the bag, and the six fragment keepsakes — with two or three caption lines underneath. Floating HUD chips are translucent so the room stays visible under them; modal panels stay solid. All confirmations (New Game over a save, the first-run “would you like to be shown how remembering works?” tutorial question) use the game's own styled card — never the engine default dialog. The tutorial is **asked about**, once, after the New Game cutscene; either answer is remembered. |
| Memory Verbs | `scripts/ui/MemoryVerbSystem.cs`, `GameState` | Four earned interaction modes with hotkeys and bottom-bar buttons: **Echo** (sound/order), **Anchor** (stabilise one truth), **Trace** (connect caused moments), and **Release** (resolve the assembled memory). Arming one disarms Clarity and vice versa; right-click/Esc cancels free. |
| Final act | `scripts/world/final_act/MemoryRoomBase.cs`, `room4/Room4Puzzle.cs`, `room5/Room5Final.cs`, `room5/ApartmentNow.cs`, `room6/Room6Final.cs`, `room6/Porch447.cs`, `room7/Room7Final.cs` | Room 4's hallway sentence and brass door-knock, Room 5's anchored lamp/apology, Room 6's traced last afternoon, and Room 7's six-fragment lived-order finale. Two more locations deepen the road: the **Apartment, Now** (through Room 5's rain window once it holds still — unopened mail, a silent phone, the chair in the corner) and the **Porch, at 4:47** (the main-path stop between the garden road and the last room). Room 7's frames are **not numbered or graded** — the house answers only once, when all six stand together. A true Release then plays a visual **truth reveal** in the game's own rooms, offers the choice, and plays a first-person conversation with Ida (three expression stills per branch, crossfaded as she listens/speaks): letting go fades through light into the Waking ending and Mara's smiling face (`ending_release.wav`, minor but relieved); keeping the house cuts to black into the Kept House ending and Mara curled in her corner (`ending_kept.wav`). The Hollow ending is unchanged. |
| Flashback: the Landing | `scripts/world/room4/FlashbackLanding.cs`, `scenes/world/flashback_landing.tscn` | A story location inside Room 4's mirror (look twice). The evening Ida didn't come home, in held-breath blue: the last photograph, the wall light their mother left burning, and the carpet runner where an illusion of Ida still slides in her socks. No puzzle — the gradual-reveal keystone between the hallway sentence and Rooms 5–7, whose monologues continue it. Writes “The landing, that evening” to Notes & Clues. Room 4 also gained its missing examinables: mirror, the locksmith's drawer of blank keys, and the two turned frames (Pic 11/12). |
| Hotspots | `scripts/ui/Hotspots.cs` | Pulsing rings drawn with `_Draw`; hold-Tab reveal + level-3 hint pulse + examine click ripple. |

## Design rules implemented

- **Over-examining costs.** First look is free; deeper layers corrode the
  memory until text glitches, the score sours and you hear a heartbeat.
- **Clarity is an economy.** Spend it to see truly *and* to steady the
  memory; earn it back by finishing rooms. Spend the last of it too early
  and the house gives you fifteen seconds to finish the room anyway.
- **Clues never spell the answer.** Journal cards, objectives and all
  three hint tiers point at where the truth is kept and how to read it;
  combinations and words are only ever *read off the things themselves*
  (a stopped clock, a letter's last line). Padlock **Unlock** buttons are
  always visible and always pressable — a wrong code rattles, says so,
  and sends you to the Journal (J) instead of hiding the way forward.
- **The story is told in order, slowly.** A plain-spoken intro cutscene;
  the house's kindnesses noticed one by one; Room 4's mirror opening onto
  the landing flashback; Mara monologues at Rooms 5–7's thresholds;
  suspicion aimed where it belongs — at a memory that edits, lovingly.
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
- **Dialogue does not teach.** Story lines name what wakes (Echo, Anchor,
  Trace, Release) and how it felt — never which key to press or what to
  click next. How things work lives in the visual How to Play, the Journal's
  “way of remembering” cards, and the H hints: places the player chooses
  to consult. Puzzle feedback stays in the fiction (a rising note, a line
  that breaks, a knock no one answers).
- **Finished rooms rest.** Step back into a room whose work is done and
  nothing in it is interactable anymore — exits and the Return → button
  (furthest room reached) are the way back to where you were.
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
5. Padlock: dials tick; Esc backs out; **Unlock** is visible from the start — press it with a wrong code first (rattle + “check your Journal”), then dial `71483` → Unlock → drawer opens → **click the key** → open the satchel (top-right): the brass key is in the bag → Room 2. (Physics picking is switched on in GameManager — SubViewports default it off, which kills `_InputEvent` targets like the key/arrows.) Stepping back to the kitchen afterwards shows the cabinet still open.
6. Type a wrong 4-letter word and press **Unlock** (typing alone is never judged): rattle + Journal pointer; `HOME` opens. To see the collapse: in Room 1 or 2, spend Clarity down to 0 before finishing — warning card + red 15-count + tension music; open the lock in time to be rescued, or let it expire into the shatter. Check the count holds while dialogue/journal are open. With ≥3 charges in a mostly-examined room, the gold **Remember it all** button should appear beside the bag. Photo → Room 3 → jars give
   the third fragment as **Echo** wakes (named once, in-story; its card is in
   Journal → Notes & Clues, and How to Play shows the verb chips). Room 4:
   ordinary photo hover only shows labels; arm **Echo (E)** and drift
   Pic 1→10 to hear the sentence and recover the hallway fragment.
   In Room 4, also: open the hall drawer (blank keys), click the two dead-end
   frames Pic 11/12 (turned frames), and click the mirror twice — the second
   look steps into the **landing flashback**; face its three looks, then leave
   by the far end of the hall (or Q).
7. The Room 4 door close-up: with **Echo** armed, knock brass plate →
   keyhole beneath it, three pairs left-to-right (ordinary clicks examine).
   Room 5: face everything, **Anchor (A)** the lamp, open the desk drawer
   for the apology; click the rain window again to step into the
   **Apartment, Now**, face its three looks, and leave by the entry door.
   Then the wardrobe. Room 6: face each stop, then **Trace (T)** basket →
   swing → gate → road; the road now bends to the **Porch** — face the lit
   window, steps and path, then take the front door. Step back into any
   finished room en route: it should be sealed quiet, with **Return →**
   (bottom-right) bringing you back.
8. Room 7: choose exact fragments from the bottom tray and place/reclaim
   freely — no numerals, no per-piece grading; the house answers only when
   all six stand together. In lived order, arm **Release (R)** and click
   the assembled memory: the **truth reveal** plays, then the choice.
   **Let Mara wake** → warm first-person Ida (listening/speaking/smiling),
   a fade through light, the Waking ending, and Mara's smiling face last.
   **Keep the house** → the same conversation gone gloomy (Ida turns away),
   a cut to black, the Kept House ending, and Mara curled in her corner
   last. The title card records the ending and clears only that run's save.
9. Esc → pause: save, journal, settings sliders (volumes should respond
   live), quit to menu → **Continue** appears and resumes the room.

## Known limitations

- Continue restores progress/flags, but examine layers that freed
  themselves mid-room reappear visually (tracker still gates correctly).
- The collapse countdown is runtime-only (not saved): quitting mid-count
  and continuing resumes at 0 Clarity without the timer. Reloading is mercy.
- This final-act pass was written and statically/syntax checked outside
  Godot. A local Godot .NET build and full playthrough remain necessary;
  in particular, confirm the generated Room 5/6 art aligns with the coded
  hotspot rectangles at 1152×648.
- Procedural SFX are tasteful placeholders with correct loudness staging,
  meant to be replaced file-by-file as real recordings land.
