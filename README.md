# The House of Mara Voss

A psychological point-and-click narrative in Godot 4.6 (C#). You are not
Mara Voss — you are the part of her that still knows something is wrong.
Examine the rooms of a house that misremembers itself, spend **Clarity**
to see objects as they truly are, learn four late-game **Memory Verbs**, and
recover six fragments — while watching the memory **corrupt** the deeper
you insist on looking. The completed route runs through all seven rooms —
plus a landing flashback, Mara's present-day apartment, the Eldfen porch
at 4:47, and an optional way under the house that has to be assembled from
practical things found in three different places — to one of three
endings. Dialogue never tells you how to play; the house is meant to be
figured out.

## Controls

| Input | Action |
| --- | --- |
| Left click | Examine / interact · advance dialogue (1st click completes the line, 2nd advances). In an active room, clicking empty air strains MEMORY (**2.0×** the old repeat-look unit; another loose click in nearly the same place is **2.3×**), and deliberately re-facing an already-faced examinable is **3.0×** |
| Right click | Release armed Clarity or cancel an armed Memory Verb (free) |
| Space / Enter | Advance dialogue |
| End | Skip the current dialogue sequence |
| C | Arm / release Clarity (or click the eye toggle in-room) |
| E / A / T / R | **Echo / Anchor / Trace / Release** when learned — hear order, hold one truth still, connect causes, or decide a completed memory (also available from the collapsible **MEMORY** tab at the bottom) |
| A / D + hold-and-drag mouse (at a lock) | Lockpicking: **A/D** rocks the pick across the pins; holding the left mouse button and dragging sideways turns the tension wrench. Let go when the pick starts complaining — forcing a bind can snap one of the four picks allowed per sitting |
| Mouse (in the vent / basement) | Aim Mara's flashlight. In the duct the beam leans toward where you are listening; below the house it follows her hand directly |
| Tab (hold) | Reveal remaining hotspots |
| H | Hint (progressive: nudge → names the object → rings it) |
| J / M | Memory Journal — fragments, echoes, auto-noted clues (incl. puzzle arrangements), and a free notes page kept in your save |
| B / I or the satchel icon (top-right) | Mara's Bag / inventory — every fragment is a visible keepsake (key, photograph, jar, frame, letter, apple); carried, missing, and returned fragments, learned Memory Verbs, and practical things also carried (Father's lockpick set, the flashlight and its stronger battery, the iron cellar key, the tin of negatives); available in every room |
| Q / Backspace | Step back — leave a puzzle for its room, or return to the previous room (also the on-screen Step Back button, bottom-left). Finished rooms you step back into are **sealed** (nothing in them answers anymore); the **Return →** button (bottom-right) takes you back to the furthest, newest room reached |
| Click / Space / Enter (introduction after Start) | Advance one still at a time — each post-Start introduction caption stays readable until you continue; after the still and caption have settled, the bare words **Click to continue** (no box or background) slowly fade in beneath them; Esc, End, or Skip skips it (New Game only; Continue skips it) |
| Esc / P | Pause menu (Esc first releases Clarity or a Memory Verb if armed) |

## Systems (what lives where)

| System | File | Notes |
| --- | --- | --- |
| Input actions | `scripts/managers/InputSetup.cs` + `project.godot [input]` | Declared in the project file; any missing action is re-created at runtime. |
| Settings | `scripts/managers/Settings.cs` | `user://settings.cfg` — volumes, text speed, auto-advance, fullscreen, reduce-motion, corruption intensity, hotspot hints. |
| Audio | `scripts/managers/AudioManager.cs` | Autoload. Creates Music/SFX/Ambience buses at runtime if no bus layout exists; crossfading music, polyphonic SFX pool, looping room tone, corruption heartbeat + music detune/muffle. Rooms 1–6 each have a distinct seamless generated loop (`assets/Music/bg/room_1.wav` … `room_6.wav`); Room 7 keeps the finale track. The score is mixed **beneath the place itself**: room/place ambience sits in front, ordinary room music is well under it, and puzzle/under-house music is quieter still. Journeys remain music- and ambience-free, but dialogue blips/advances are audible again alongside the footsteps. |
| SFX pack | `assets/Music/SFX/*.wav`, `tools/generate_sfx.py` | 49 procedurally synthesised WAV assets (one-shots plus room/place ambience and surface footsteps), tuned to the score: spectral analysis puts the menu track in **F minor** (~359 Hz centroid, ~65 BPM), so the pack is voiced as felt piano / worn music box in F minor, corruption as warped tape. The under-house batch adds close dry lockpick ticks/snaps, a heavy flashlight switch, duct-crawl metal/cloth, and a dripping basement loop. Regenerate with `python3 tools/generate_sfx.py`; drop-in replace any file to art-direct it. |
| Run state & saves | `scripts/managers/GameState.cs` | `user://mara_voss_save.json` (save v2) — carried/exactly placed fragments, examine progress, corruption (including the fractional remainder from 2.3× loose clicks), clarity, learned verbs and use counts, story flags/practical items, journal echoes/clues/notes, completed rooms and stats. Autosaves on scene change / fragment / room completion. `Globals.cs` remains the raw field store older scenes read directly. Endings already found persist across runs in settings. Older saves that already opened Room 5's desk drawer can still find the lockpicks beneath where the letters lay. |
| Clarity: Recollection | `GameState.CanRecollect/PerformRecollection`, `Hotspots.RefreshPersistent` | With ≥3 charges and a nearly-known room (≥60% examined, max 5), **Remember it all** is offered — as a gold HUD button beside the bag, and again (retried until a free moment) when Clarity is armed: spend **all** charges in a custom in-game panel to reveal every remaining examinable in its true Clarify form, one by one, and ring it in clarity-blue until faced (same ring animation as Tab hints). The next room starts with (revealed + 1) Clarity. Spending down to zero is spending the last charge: in an unfinished room the 15-second collapse begins the moment the revealing ends (the count holds while input is blocked, dialogue shows, or a panel is open) — finishing the lock in time is the price of remembering everything at once, and the panel now warns of it. Rings and the carry persist in the save. Rooms 5–6 deliberately exclude Recollection because their authored Memory Verb puzzles should not be bypassed. |
| Corruption | `GameState` tiers + `assets/shaders/corruption.gdshader` | Looking carelessly adds corruption: a loose click on non-interactable room air costs **2.0×**, another loose click in nearly the same place within 1.4 s costs **2.3×** (carried fractionally until a whole MEMORY point falls), and a deliberate repeat look at an already-faced examinable costs **3.0×**. Examining past an object's first layer also corrodes. Tiers (Intact/Strained/Fractured/Shattered) drive the screen shader, dialogue text glitches, heartbeat and music decay. Clarity reveals also relieve corruption (−2). If corruption empties the MEMORY meter, the memory **shatters**: the room tears apart on screen, glass cracks spread across the screen in sync with crack/shatter sounds, and the bad ending (The Hollow Ending) plays, with a choice to gather the pieces (resume at 13 corruption) or let it go (menu). |
| Clarity | `scripts/ui/ClaritySystem.cs` | Arm free, spend on reveal, cancel free, +1 charge per completed room (max 5). Spending the **last** charge in an unfinished TileMap room triggers the collapse (below). |
| Clarity collapse | `GameState.TickCollapse`, `GameUI` collapse overlay, `assets/Music/bg/collapse.wav` | At 0 Clarity the house takes the room back: a visible **15-second** blood-red count with warning text, drowning red tint, spreading screen cracks, the corruption shader closing to full, tick/crack/whisper SFX and a tense generated F-minor bed (15 s seamless loop). Corruption seeps +1 every 3 s; solving the room's lock / recovering its fragment / regaining a charge rescues it; expiry forces the shatter (Hollow Ending). The count only drains while the player can act — never while paused, reading dialogue, or behind a panel. Fleeing the room ends it at +2 corruption. |
| Dialogue | `scripts/ui/Dialogue.cs` | Click-to-advance typewriter with blips; `ShowText` API unchanged for existing call sites; calls serialise; every line is logged to the journal. |
| HUD / menus | `scripts/ui/GameUI.cs` | Autoload `CanvasLayer`, everything built in code: objective, clarity pips, integrity meter, fragment counter, icon-only Bag toggle, the **Remember it all** button, Memory Verb bar, toasts, pause, settings, journal, corruption + collapse overlays, Hollow shatter sequence, and the authored Waking / Kept House ending presentation. **How to Play is visual**: each page leads with the game's own art/UI — the kitchen, the cracking MEMORY meter, the Clarity eye + pips + red 0:15 count, verb key chips, the bag, and the six fragment keepsakes — with two or three caption lines underneath. Floating HUD chips are translucent so the room stays visible under them; modal panels stay solid. All confirmations (New Game over a save, the first-run “would you like to be shown how remembering works?” tutorial question) use the game's own styled card — never the engine default dialog. The tutorial is **asked about**, once, when Room 1 is reached; either answer is remembered. The main-menu window now has animated rain, Mara coffee steams, and each menu button's hover/click area is tightened to its resting label rather than the whole menu column. The main menu's **Full Story** and **About** are bound as an old journal — worn leather cover, parchment pages with the corresponding scene/room picture pasted in, flipped a page at a time (Earlier/Later buttons or the arrow keys) instead of scrolled; Full Story's pages are rewritten room-for-room to match the finished game, About keeps its document, paginated. |
| Memory Verbs | `scripts/ui/MemoryVerbSystem.cs`, `GameState` | Four earned interaction modes with hotkeys and bottom-bar buttons: **Echo** (sound/order), **Anchor** (stabilise one truth), **Trace** (connect caused moments), and **Release** (resolve the assembled memory). Arming one disarms Clarity and vice versa; right-click/Esc cancels free. The **MEMORY** title is the bar's own collapse/expand handle; it starts collapsed in Room 5 and folds away whenever a verb is armed, leaving only a slim tab (which still names the held verb) so floor-level things — the Room 5 photographs above all — stay clickable. |
| Final act | `scripts/world/final_act/MemoryRoomBase.cs`, `room4/Room4Puzzle.cs`, `room5/Room5Final.cs`, `room5/ApartmentNow.cs`, `room6/Room6Final.cs`, `room6/Porch447.cs`, `room7/Room7Final.cs` | Room 4's hallway sentence and brass door-knock, Room 5's anchored lamp/apology, Room 6's traced last afternoon, and Room 7's six-fragment lived-order finale. Two more locations deepen the road: the **Apartment, Now** (through Room 5's rain window once it holds still — unopened mail, a silent phone, the chair in the corner) and the **Porch, at 4:47** (the main-path stop between the garden road and the last room; its front door refuses Mara until the lit window, steps and garden path have all been faced). Room 5's wardrobe is entered, not cut away from: after its lines, Mara crosses the floor with audible soft steps while the whole room zooms into the garden showing between the coats. Room 7's frames are **not numbered or graded** — the house answers only once, when all six stand together. A true Release then fades to white into a first-person **truth replay** of that day — Mara arrives in the garden ("Where... am I?" … "Wai—"), cannot be heard, and watches it whole: the morning Ida left home, the promise at four, the waiting on the swing, her own ordinary lateness, the walk home, the bridge over the Harren canal where the rotten railing gave, and 4:47. Its score is **cut** for the replay: garden air, room tone and gathering rain carry the stills, Ida's shoes cross the stone bridge, the old railing breaks and the water takes the fall — and only at that death shot does the background music open again. It then offers the choice, and plays a first-person conversation with Ida (three expression stills per branch, crossfaded as she listens/speaks): letting go fades through light into the Waking ending and Mara's smiling face (`ending_release.wav`, minor but relieved); keeping the house cuts to black into the Kept House ending and Mara curled in her corner (`ending_kept.wav`). The Hollow ending is unchanged. |
| Under the house (optional detour) | `scripts/world/room5/LockpickPuzzle.cs`, `VentCrawl.cs`, `Basement.cs`; `scenes/world/lockpick_puzzle.tscn`, `vent_crawl.tscn`, `basement.tscn` | A cross-room practical chain, none of it required for an ending. Room 5's desk drawer hides **Father's lockpick set** beneath the apology letters. Low in the wall beneath that same desk is a latched vent; the reusable lockpick close-up uses A/D for the pick and a held mouse-drag for the tension wrench, with pin travel, binding, stress, snapped picks and four picks per sitting. The duct then refuses Mara until she brings the **flashlight** from the present-day Apartment's cupboard. The first-person vent crawl ends at a lit grille she clicks open into a basement whose darkness is pushed back only by the mouse-led beam. Its required looks are the cold furnace, preserve shelves and toy crate; a second, harder lockpick sitting opens the trunk (a tin of straight-on photograph negatives: MEMORY eased, Clarity +1), while the **iron cellar key** hidden under a loose stone in Room 6's garden wall opens the tool cabinet's stronger battery (a braver beam, MEMORY eased, Clarity +1). Practical pickups are listed in the Bag; clues/objectives/hints cover the chain without handing over its answers. |
| Flashback: the Landing | `scripts/world/room4/FlashbackLanding.cs`, `scenes/world/flashback_landing.tscn` | A story location inside Room 4's mirror (look twice). The evening Ida didn't come home, in held-breath blue: the last photograph, the wall light their mother left burning, and the carpet runner where an illusion of Ida still slides in her socks. No puzzle — the gradual-reveal keystone between the hallway sentence and Rooms 5–7, whose monologues continue it. Writes “The landing, that evening” to Notes & Clues. Room 4 also gained its missing examinables: mirror, the locksmith's drawer of blank keys, and the two turned frames (Pic 11/12). |
| Room journeys | `scripts/world/transitions/RoomJourney.cs`, `scenes/world/journey_to_*.tscn` | First completion of Rooms 1–3 now travels three visibly different first-person routes: **straight** to Room 2, **left at the bent runner** to Room 3, and **right at the photographs** to Room 4, each ending on its painted door for the player to click open. The first walks run slower — 7.8 s straight, 8.7 s left, 8.7 s right, and 12.0 s on the empty road — so the thresholds feel farther away; repeat walks are capped at 4.2 s. Mara sometimes gets a fuller walking monologue with one extra route-specific thought, and sometimes keeps only a threshold sentence — or, on repeats, walks without speaking. The journeys have **no background music or ambience**: the previous room's score and loop fade away, leaving Mara footsteps on the surface actually painted under her and the ordinary dialogue sounds when she speaks — soft carpet-runner thuds with a trace of board in the three hallways, and looser dirt, grit and dry leaves on the empty road. Before every first-arrival door (or the road's porch light), Mara **always** stops, takes the just-recovered fragment in her palm, and wonders what carrying it can possibly be for. Only the migraine is random: when it strikes, the room it came from flickers through — colour split, closing red edge and camera jolts — until she shakes it off; when it does not, she gives the fragment one calm, steadying thought and lowers her hand. Room 6 keeps its distinct, longer empty-road walk. Repeat journeys stay shortened and never get the migraine. The bedroom door still opens impossibly into the kitchen, Room 4's brass door must be clicked after its locks answer, and Room 5 keeps its wardrobe/garden passage. |
| Room arrivals | `scripts/ui/GameUI.cs`, `assets/shaders/corruption.gdshader` | Mara now has one short first-person threshold monologue the first time she enters each later room and side place (the sitting room, Ida's room, the hall/landing, the split bedroom, her apartment, the garden, the porch, and the last room), before that room's own authored lesson. Once per run, in one **randomly chosen main room other than the kitchen**, the room flickers black-and-white for a blink — seven hard changes in 0.445 s — and every still-unfaced exploration point rings itself; colour is back before Mara asks what that was, turning the flashback into a found clue rather than an instruction. The chosen room is saved with the run, and Reduce Motion softens the flicker to a 0.35 s held monochrome fade. |
| Hotspots | `scripts/ui/Hotspots.cs` | Pulsing rings drawn with `_Draw`; hold-Tab reveal + level-3 hint pulse + examine click ripple. |

## Design rules implemented

- **Looking has a cost, and clicking carelessly costs more.** A first look
  is free. In an active room, a loose click on empty air costs 2.0× the old
  repeat-look unit; another loose click in nearly the same place costs
  2.3×; deliberately facing an already-faced examinable again costs 3.0×.
  Deeper layers also corrode the memory until text glitches, the score
  sours and you hear a heartbeat. Dialogue, panels, HUD furniture, sealed
  rooms, journeys/cutscenes and puzzle thresholds do not punish clicks.
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
  in every room, distinguishing fragments still carried from ones returned
  and listing practical things — tools, keys and lights — whose uses may
  be rooms away from where they were found.
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
3. Play: launch intro (click to skip) → menu music + Settings button → Start. On the menu, check that rain moves inside both window panes, steam rises from the coffee, and buttons only hover/click when the pointer is directly over the visible resting label.
4. Pressing Start/New Game plays the original memory introduction first: each still and caption should remain on screen until you click (Space/Enter also work); the image and caption appear first, then the plain words **Click to continue** — no border or background — slowly fade in centered beneath the caption; only Esc/End/Skip should bypass the rest. Only after that introduction does Mara wake in her childhood bedroom — first-person, blinking up at a ceiling crack she knows by heart. This is the live tutorial: try the door early (it refuses and names what still waits), examine all five things (bed, window, nightstand, her own childhood drawing, wardrobe), then take the door and land straight in the kitchen with no hallway in between. She reacts to that wrongness before Room 1 asks the usual tutorial question. Room 1: HUD objective counts up as you examine; drawer explains its
   lock; hold Tab; press H three times; arm Clarity (C), right-click to
   release, then clarify the toast; press B to open/close the empty bag; over-examine to hear corruption rise.
5. Padlock: dials tick; Esc backs out; **Unlock** is visible from the start — press it with a wrong code first (rattle + “check your Journal”), then dial `71483` → Unlock → drawer opens → **click the key** → open the satchel (top-right): the brass key is in the bag → walk **straight** down the hallway, a little more slowly than before. The kitchen music and room tone should fade out on the journey, leaving alternating soft steps on the carpet runner (listen for the faint boards underneath) and the ordinary dialogue blips if she speaks; the later empty-road journey swaps those steps for dirt/grit-and-leaf steps. The first walk should feel slow — about eight seconds before the fragment pause. Mara may add one extra thought while walking, and she should **always** stop before the door with the key in her palm and wonder what it is for; only sometimes, by a saved random roll, does that inspection flicker back through the kitchen as a migraine she shakes off — on calm runs she steadies the key with one quiet line instead. Entering Room 2 should give her a short sitting-room monologue. In exactly one randomly chosen non-kitchen main room in the run, that first entry first flickers black-and-white for well under a second with every unfaced exploration point ringed; colour should already be back while she asks what that was. (Physics picking is switched on in GameManager — SubViewports default it off, which kills `_InputEvent` targets like the key/arrows.) Stepping back to the kitchen afterwards shows the cabinet still open.
6. Type a wrong 4-letter word and press **Unlock** (typing alone is never judged): rattle + Journal pointer; `HOME` opens. To see the collapse: in Room 1 or 2, spend Clarity down to 0 before finishing — warning card + red 15-count + tension music; open the lock in time to be rescued, or let it expire into the shatter. Check the count holds while dialogue/journal are open. The rooms breathe a little, too — steam off both cups on the kitchen table (only while each cup's first layer shows — turn one over, empty it, or clarify it and the steam is gone, as the tile art itself insists), the sitting-room fire lit, rain crossing two windows, moths worrying at Room 5's lamp and the porch lantern, leaves coming down and a branch swaying in the garden, dust in the light — small three-frame motions everywhere they belong, all on a canvas layer under the speech bubble (held still by Reduce Motion), with sound to match (procedural rain in Room 5 and the Apartment, fire crackle in Room 2, garden wind, porch crickets), crossfaded per scene and mixed in front of a deliberately quieter background score. And every recovered fragment is now picked up in first person — resting in Mara palm as her hand rises into frame, then lifted large with one line of hers — and the house waits for it: a doorway asked for too soon is quietly held (deferred, not blocked on) until a click sends the fragment flying into the bag, and only then does the next room load. Mara talks to herself in short asides after examines throughout the game (her examine lines in Rooms 4–6, the Apartment, the Porch and the landing are in her own first-person voice now, worded a little plainer). With ≥3 charges in a mostly-examined room, the gold **Remember it all** button should appear beside the bag. Photo → slower **left-turn** hallway journey (Mara always pauses to inspect the photograph; whether it turns migraine is random, as are any extra walking thoughts) → click the door into Room 3 → jars give
   the third fragment, followed by the slower **right-turn** journey — again, the jar is always checked, but the migraine may or may not strike — before Room 4, as **Echo** wakes (named once, in-story; its card is in
   Journal → Notes & Clues, and How to Play shows the verb chips). Room 4:
   ordinary photo hover only shows labels; arm **Echo (E)** and drift
   Pic 1→10 to hear the sentence and recover the hallway fragment.
   In Room 4, also: open the hall drawer (blank keys), click the two dead-end
   frames Pic 11/12 (turned frames), and click the mirror twice — the second
   look steps into the **landing flashback**; face its three looks, then leave
   by the far end of the hall (or Q).
7. The Room 4 door close-up: with **Echo** armed, knock brass plate →
   keyhole beneath it, three pairs left-to-right (ordinary clicks examine); when the locks answer, nothing auto-opens — click the door itself to go through.
   Room 5: the Memory tab should arrive **collapsed** to a slim handle left of the photograph scatter (click **MEMORY +** to expand Echo/Anchor, **MEMORY −** to fold it away again; arming a verb folds it for you); hold Tab and confirm a ring sits on **each painted photograph** in the scatter in front of the desk — not on the empty floor to the right — and that each photograph is directly clickable, then inspect things and watch them: everything examined starts
   **shifting** between its childhood and adult selves (each individual floor photograph shifts in place), except the lamp, which never changes — and which will not be touched (examined or Anchored) until every shifting thing has been faced: reach for it early and Mara stops her own hand, out loud. Once all of them are faced, touch it and Mara notices herself that it's the only thing in the room that isn't weird (she never says what to do about it). **Anchor (A)** the lamp (the
   shifting stops), open the desk drawer for the apology; click the rain
   window again to step into the **Apartment, Now**, face its three looks,
   and leave by the entry door. With the letters in hand, the wardrobe's
   inside — past the coats and the raincoat — has become the garden; click
   it and, after Mara lines, she should cross toward it with soft steps
   while the room zooms into the wardrobe before Room 6 loads. Room 6: face each stop, then **Trace (T)** basket →
   swing → gate → road; leave through the gate and walk the empty road until the distant house resolves, pause while Mara always inspects the apple — sometimes through the garden's migraine flicker, sometimes with only a calm line — then click the porch light to arrive at the **Porch**. Try the front door immediately: it should refuse. Face the lit
   window, steps and path, and only then take the front door. Step back into any
   finished room en route: it should be sealed quiet, with **Return →**
   (bottom-right) bringing you back.

   **Under-house detour (optional, but test it before Room 7):** after the
   Room 5 drawer gives up the apology letters, keep clicking through — the
   oilcloth roll underneath should grant **Father's lockpick set** and its
   Journal card (on an older save that already took the letters, reopening
   the empty drawer should find the same roll). Under the desk, low in the
   wall, the code-drawn **Low Vent** should sit beneath the desk drawer and
   stop at the floor line. With the picks, take it into the lockpick
   close-up: **A/D** should rock the pick (and must not arm Anchor), a held
   sideways mouse-drag should turn the plug, wrong angles should bind and
   complain, forcing one should eventually snap a pick, and **Put the
   tools away** should return to Room 5. On the sweet spot the grille
   opens; without a light Mara should refuse the dark. Step through the
   rain window to the Apartment and open the far-left cupboard for the
   **flashlight**, then return to the vent. The crawl should take about
   fourteen seconds the first time (six on repeats), pause for her lines,
   lean its beam toward the mouse, and end on a lit grille to click. In
   the **Basement**, sweep the beam over the furnace, preserve shelves and
   toy crate; then pick the trunk's harder lock, return, and click the open
   trunk to take the negatives (MEMORY eased, Clarity +1 if not full). The
   little cabinet should refuse without a key. Later, in Room 6, pry **A
   Loose Stone** in the right-hand garden wall by the gate post for the
   iron cellar key — it should work with Trace armed or not and never break
   the Trace line — then step back Room 5 → vent → basement and unlock the
   cabinet: the beam should visibly widen, with MEMORY eased and Clarity
   +1 if not full. Open the Bag along the way: every practical pickup
   should be listed under “Also carried”.
8. Room 7: choose exact fragments from the bottom tray and place/reclaim
   freely — no numerals, no per-piece grading; the house answers only when
   all six stand together. The tray (like the Bag and the Journal list)
   shows the fragments **jumbled** — a per-run shuffle saved with the run,
   never the lived order — so the order has to come from remembering. In lived order, arm **Release (R)** and click
   the assembled memory: the screen fades to white into the first-person replay of that day — Mara recognizes the garden, calls to a sister who can't hear her, and watches the whole of it, morning to bridge. There should be **no background music** through the garden, rooms and gathering rain; listen for Ida's wet steps on the stone bridge, then the wooden railing snapping and water closing over the fall — the score opens again only on that shot — then the choice.
   **Let Mara wake** → warm first-person Ida (listening/speaking/smiling),
   a fade through light, the Waking ending, and Mara last — seated on the steps, cheek on her hand, smiling into the distance.
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
- This final-act/under-house pass was written and statically/syntax
  checked outside Godot. A local Godot .NET build and full playthrough
  remain necessary; in particular, confirm the generated Room 5/6 and
  basement/vent/lockpick art aligns with the coded hotspot rectangles at
  1152×648, and that the lockpick drag/stress timing feels fair on a real
  mouse.
- Procedural SFX are tasteful placeholders with correct loudness staging,
  meant to be replaced file-by-file as real recordings land.
