#!/usr/bin/env python3
"""
Procedural SFX generator for The House of Mara Voss — v2, tuned to the score.

Analysis of the shipped music (STFT chroma + Krumhansl-Schmuckler):
  * "Glance Out A Casement Window" (menu/rooms): F minor pitch-class profile
    (F, C, Db, Ab dominant), spectral centroid ~359 Hz, ~65 BPM.
  * "conclusion.mp3" (intro/finale): G minor (r=0.82), centroid ~293 Hz, ~46 BPM.

So the whole pack is voiced accordingly — nostalgic, depressing, worn:
  * All pitched sounds live in F natural minor (the gameplay track's key);
    rewards use bittersweet i/add9 colours and deliberately don't resolve.
  * Instruments: felt piano, a slightly detuned music box with tape wow,
    wood, paper, aged iron. Nothing bright, digital or arcade.
  * Corruption is a *warped record* (pitch sag, tape dropouts, crackle),
    not a bit-crusher.
  * Lowpass ceilings keep everything near the score's ~300-360 Hz centroid.

Same filenames/ids as v1, so no game code changes are needed. Re-run any time:
    python3 tools/generate_sfx.py
Mono 22050 Hz WAVs into assets/Music/SFX/, stdlib only.
"""

import math
import os
import random
import struct
import wave

SR = 22050
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "Music", "SFX")

random.seed(20261024)  # deterministic pack

# F natural minor, anchored at F3 (MIDI 53 = 174.61 Hz)
F3 = 53
SCALE = {"F": 0, "G": 2, "Ab": 3, "Bb": 5, "C": 7, "Db": 8, "Eb": 10}


def mf(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def note(name, octave_up=0):
    """MIDI number for a scale degree name relative to F3."""
    return F3 + SCALE[name] + 12 * octave_up


# ---------------------------------------------------------------- primitives

def write_wav(name, samples, peak=0.8):
    path = os.path.normpath(os.path.join(OUT_DIR, name))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if samples:
        m = max(abs(s) for s in samples)
        if m > 0:
            g = peak / m
            samples = [s * g for s in samples]
    frames = b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples
    )
    with wave.open(path, "wb") as w:
        w.setparams((1, 2, SR, len(samples), "NONE", "not compressed"))
        w.writeframes(frames)
    print(f"  {name:28s} {len(samples) / SR:5.2f}s  peak={peak}")


def sec(t):
    return int(t * SR)


def zeros(t):
    return [0.0] * sec(t)


def white(t):
    return [random.uniform(-1.0, 1.0) for _ in range(sec(t))]


def brown(t, leak=0.985):
    out, last = [], 0.0
    for _ in range(sec(t)):
        last = (last + 0.04 * random.uniform(-1.0, 1.0)) * leak
        out.append(last * 6.0)
    return out


def sine(freq, t, phase=0.0):
    n = sec(t)
    return [math.sin(2 * math.pi * freq * i / SR + phase) for i in range(n)]


def add_into(dst, src, at=0.0, gain=1.0):
    start = sec(at)
    need = start + len(src)
    if need > len(dst):
        dst.extend([0.0] * (need - len(dst)))
    for i, s in enumerate(src):
        dst[start + i] += s * gain
    return dst


def env_exp(samples, attack=0.002, decay_tau=None):
    n = len(samples)
    for i in range(n):
        t = i / SR
        a = 1.0 if t >= attack else (t / attack if attack > 0 else 1.0)
        d = math.exp(-t / decay_tau) if decay_tau else 1.0
        samples[i] *= a * d
    return samples


def env_ar(samples, attack, release):
    n = len(samples)
    na, nr = sec(attack), sec(release)
    for i in range(n):
        g = 1.0
        if i < na:
            g *= i / max(1, na)
        if i > n - nr:
            g *= max(0.0, (n - i) / max(1, nr))
        samples[i] *= g
    return samples


def swell(samples, peak_at=0.6):
    n = len(samples)
    peak_i = int(n * peak_at)
    for i in range(n):
        if i < peak_i:
            g = 0.5 - 0.5 * math.cos(math.pi * i / max(1, peak_i))
        else:
            k = (i - peak_i) / max(1, n - peak_i)
            g = 0.5 + 0.5 * math.cos(math.pi * k)
        samples[i] *= g
    return samples


def echo_taps(samples, delay=0.42, gains=(0.20, 0.09)):
    """A couple of quiet repeats — a big, empty, remembered house."""
    out = list(samples)
    for k, g in enumerate(gains, start=1):
        add_into(out, samples, delay * k, g)
    return out


def crackle(dur, count, gain=0.05):
    """Sparse vinyl pops."""
    out = zeros(dur)
    for _ in range(count):
        pos = random.randrange(len(out))
        amp = random.uniform(0.3, 1.0) * gain * random.choice([-1, 1])
        for j in range(random.choice([1, 2, 3])):
            if pos + j < len(out):
                out[pos + j] += amp * (1.0 - j * 0.4)
    return filt(out, "lp", 2800)


# ------------------------------------------------------------------- filters

def _biquad(kind, fc, q):
    fc = max(20.0, min(fc, SR * 0.45))
    w = 2 * math.pi * fc / SR
    alpha = math.sin(w) / (2 * q)
    cw = math.cos(w)
    if kind == "lp":
        b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
    elif kind == "hp":
        b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
    elif kind == "bp":
        b0, b1, b2 = alpha, 0.0, -alpha
    else:
        raise ValueError(kind)
    a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    return b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0


def filt(samples, kind, fc, q=0.707, passes=1):
    b0, b1, b2, a1, a2 = _biquad(kind, fc, q)
    x = samples
    for _ in range(passes):
        y = [0.0] * len(x)
        x1 = x2 = y1 = y2 = 0.0
        for i, x0 in enumerate(x):
            y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            y[i] = y0
            x2, x1 = x1, x0
            y2, y1 = y1, y0
        x = y
    return x


def filt_sweep(samples, kind, f0, f1, q=1.2, block=256):
    out = [0.0] * len(samples)
    x1 = x2 = y1 = y2 = 0.0
    log0, log1 = math.log(f0), math.log(f1)
    for start in range(0, len(samples), block):
        frac = start / max(1, len(samples))
        fc = math.exp(log0 + (log1 - log0) * frac)
        b0, b1, b2, a1, a2 = _biquad(kind, fc, q)
        for i in range(start, min(start + block, len(samples))):
            x0 = samples[i]
            y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            out[i] = y0
            x2, x1 = x1, x0
            y2, y1 = y1, y0
    return out


# ------------------------------------------------------------- tone builders

def tone_wow(freq, dur, depth=0.004, rate=0.7):
    """Sine partial with slow tape-wow pitch drift."""
    n = sec(dur)
    out, phase = [], 0.0
    for i in range(n):
        t = i / SR
        f = freq * (1 + depth * math.sin(2 * math.pi * rate * t + freq * 0.01))
        phase += 2 * math.pi * f / SR
        out.append(math.sin(phase))
    return out


def felt_piano(midi, dur=1.4, tau=0.55):
    """Felt piano: few partials, fast-decaying highs, soft hammer thump, wow."""
    base = mf(midi)
    out = zeros(dur)
    for k, (ratio, amp) in enumerate([(1.0, 1.0), (2.0, 0.48), (3.0, 0.20), (4.0, 0.08), (5.004, 0.03)]):
        tone = tone_wow(base * ratio, dur)
        env_exp(tone, attack=0.003, decay_tau=tau / (1.0 + 0.85 * k))
        add_into(out, tone, 0.0, amp)
    hammer = env_exp(filt(white(0.03), "lp", 650), 0.001, 0.008)
    add_into(out, hammer, 0.0, 0.22)
    return filt(out, "lp", 2400)


def music_box(midi, dur=1.4, tau=0.45):
    """Worn music box: bell partials, a few cents sharp, gentle tremor-wow."""
    detune = 1.0 + random.uniform(-0.004, 0.005)
    base = mf(midi) * detune
    out = zeros(dur)
    for k, (ratio, amp) in enumerate([(1.0, 1.0), (2.76, 0.38), (5.4, 0.16), (8.9, 0.05)]):
        tone = tone_wow(base * ratio, dur, depth=0.0022, rate=4.2)
        env_exp(tone, attack=0.001, decay_tau=tau / (1.0 + 0.9 * k))
        add_into(out, tone, 0.0, amp)
    return filt(out, "lp", 3400)


def thump(freq=64.0, dur=0.35, sweep_to=0.6):
    n = sec(dur)
    out, phase = [], 0.0
    for i in range(n):
        t = i / SR
        f = freq * (sweep_to + (1 - sweep_to) * math.exp(-t * 18))
        phase += 2 * math.pi * f / SR
        out.append(math.sin(phase))
    return env_exp(out, attack=0.001, decay_tau=dur * 0.45)


def aged_tick(freq=1350.0, dur=0.05):
    """Dulled metallic tick with a sagging tail — old, tired iron."""
    n = sec(dur)
    out, phase = [], 0.0
    for i in range(n):
        t = i / SR
        f = freq * (1 - 0.22 * (t / dur))
        phase += 2 * math.pi * f / SR
        out.append(math.sin(phase))
    env_exp(out, 0.0008, 0.014)
    noise = env_exp(filt(white(dur), "bp", freq * 0.8, q=1.6), 0.0008, 0.009)
    return filt(add_into(out, noise, 0, 0.5), "lp", 2400)


def sag_resample(samples, min_rate=0.86, center=0.55, width=0.20):
    """Warped-record pitch sag: playback rate dips and recovers."""
    total = len(samples)
    out = []
    pos = 0.0
    t = 0.0
    while pos < total - 1:
        rate = 1 - (1 - min_rate) * math.exp(-((t - center) ** 2) / (2 * width ** 2))
        i = int(pos)
        frac = pos - i
        out.append(samples[i] * (1 - frac) + samples[i + 1] * frac)
        pos += rate
        t += 1 / SR
    return out


def dropout(samples, dips):
    """Smooth tape dropouts: list of (time_s, width_s, depth 0..1)."""
    out = list(samples)
    for (tc, width, depth) in dips:
        c, w = sec(tc), max(1, sec(width))
        for i in range(max(0, c - w), min(len(out), c + w)):
            k = abs(i - c) / w
            g = 1 - depth * (0.5 + 0.5 * math.cos(math.pi * k))
            out[i] *= g
    return out


def seamless(samples, keep_seconds, fade_seconds):
    keep, fade = sec(keep_seconds), sec(fade_seconds)
    out = list(samples[:keep])
    for i in range(fade):
        k = i / fade
        out[i] = samples[i] * k + samples[keep + i] * (1 - k)
    return out


# ------------------------------------------------------------------ the pack

def build():
    print("Generating SFX pack v2 (F minor, felt & worn) ->", os.path.normpath(OUT_DIR))

    # -- UI: felt, paper, wood — nothing digital ------------------------------
    s = add_into(felt_piano(note("F"), 0.18, tau=0.05),
                 env_exp(filt(white(0.05), "lp", 900), 0.001, 0.012), 0, 0.35)
    write_wav("ui_click.wav", s, peak=0.30)

    write_wav("ui_hover.wav", env_exp(filt(white(0.035), "lp", 750, passes=2), 0.002, 0.010), peak=0.09)

    s = add_into(thump(mf(note("F", -1)), 0.12, sweep_to=0.75),
                 env_exp(filt(white(0.06), "lp", 700), 0.001, 0.016), 0, 0.4)
    write_wav("ui_back.wav", s, peak=0.30)

    # -- Dialogue: muted piano-string taps --------------------------------------
    blip = felt_piano(note("F"), 0.09, tau=0.028)
    write_wav("dialogue_blip.wav", blip, peak=0.22)

    swish = filt_sweep(white(0.20), "bp", 330, 1050, q=1.0)
    env_ar(swish, 0.02, 0.11)
    write_wav("dialogue_advance.wav", filt(swish, "lp", 1100, passes=2), peak=0.28)

    page = filt_sweep(white(0.55), "bp", 300, 900, q=0.9)
    swell(page, 0.45)
    add_into(page, filt(white(0.05), "lp", 1400), 0.04, 0.10)
    write_wav("page_turn.wav", filt(page, "lp", 1100, passes=2), peak=0.28)

    # -- Examine / clarity ---------------------------------------------------------
    ex = env_exp(filt(white(0.42), "lp", 480), 0.015, 0.18)
    add_into(ex, thump(mf(note("F", -1)), 0.16, sweep_to=0.7), 0.01, 0.55)
    write_wav("examine.wav", ex, peak=0.36)

    # Clarity arm = memory surfacing: reversed-feel swell of the F-minor triad.
    arm = zeros(0.85)
    for nm in ["F", "Ab", "C"]:
        tone = felt_piano(note(nm), 0.85, tau=2.5)      # near-sustained
        swell(tone, 0.8)                                 # slow attack, late peak
        add_into(arm, tone, 0.0, 0.5)
    env_ar(arm, 0.02, 0.10)
    write_wav("clarify_arm.wav", filt(arm, "lp", 2000), peak=0.32)

    # Clarity reveal: felt arpeggio i–III–V–i with a faint music-box octave ghost.
    rev = zeros(1.9)
    for j, nm in enumerate(["F", "Ab", "C", "F"]):
        oct_up = 1 if j == 3 else 0
        add_into(rev, felt_piano(note(nm, oct_up), 1.6, tau=0.7), 0.17 * j, 0.55)
        add_into(rev, music_box(note(nm, oct_up + 1), 1.2, tau=0.4), 0.17 * j, 0.14)
    write_wav("clarify_reveal.wav", echo_taps(rev, 0.42, (0.16, 0.07)), peak=0.42)

    # Denied: a dull knock, then the tonic sighs down a semitone. Sad, not harsh.
    den = zeros(0.8)
    add_into(den, thump(95, 0.22, sweep_to=0.7), 0.0, 0.8)
    add_into(den, felt_piano(note("F"), 0.7, tau=0.3), 0.05, 0.45)
    add_into(den, felt_piano(note("F") - 1, 0.7, tau=0.38), 0.28, 0.4)
    write_wav("denied.wav", den, peak=0.36)

    # Room 4 word note: felt F4 with music-box ghost; pitched up a minor scale in code.
    wc = add_into(felt_piano(note("F", 1), 0.8, tau=0.4),
                  music_box(note("F", 2), 0.8, tau=0.35), 0, 0.22)
    write_wav("word_chime.wav", wc, peak=0.40)

    # -- Corruption: warped record + distant breath -----------------------------
    frag = zeros(1.5)
    for j, (nm, octv) in enumerate([("F", 0), ("Ab", 0), ("G", 1), ("F", 1)]):  # Fm add9 fragment
        add_into(frag, felt_piano(note(nm, octv), 1.3, tau=0.6), 0.16 * j, 0.55)
    frag = sag_resample(filt(frag, "lp", 2150), min_rate=0.84, center=0.85, width=0.24)
    frag = dropout(frag, [(0.45, 0.09, 0.75), (1.05, 0.13, 0.9)])
    add_into(frag, crackle(len(frag) / SR, 26, gain=0.10), 0, 1.0)
    env_ar(frag, 0.005, 0.12)
    write_wav("corruption_glitch.wav", frag, peak=0.45)

    wh = filt_sweep(white(2.0), "bp", 330, 1050, q=4.0)
    wh2 = filt_sweep(white(2.0), "bp", 1050, 290, q=4.0)
    wh = add_into(wh, wh2, 0, 0.8)
    for i in range(len(wh)):
        tt = i / SR
        wh[i] *= 0.65 + 0.35 * math.sin(2 * math.pi * 3.9 * tt) * math.sin(2 * math.pi * 0.5 * tt)
    swell(wh, 0.65)
    env_ar(wh, 0.15, 0.3)
    write_wav("corruption_whisper.wav", filt(wh, "lp", 1800), peak=0.30)

    # -- Locks, drawers, doors: aged iron and old wood ---------------------------
    write_wav("dial_tick.wav", aged_tick(1320, 0.05), peak=0.30)

    key = add_into(env_exp(filt(white(0.06), "bp", 750, q=1.2), 0.001, 0.02),
                   thump(150, 0.07), 0, 0.4)
    write_wav("key_type.wav", filt(key, "lp", 1600), peak=0.32)

    lo = zeros(0.85)
    scrape = filt_sweep(white(0.28), "bp", 650, 300, q=1.6)
    env_ar(scrape, 0.02, 0.06)
    add_into(lo, scrape, 0.0, 0.5)
    add_into(lo, aged_tick(950, 0.06), 0.24, 0.5)
    add_into(lo, thump(66, 0.3, sweep_to=0.6), 0.30, 1.0)
    add_into(lo, felt_piano(note("F"), 0.5, tau=0.22), 0.34, 0.20)  # distant, as if through the house
    write_wav("lock_open.wav", filt(lo, "lp", 2200), peak=0.48)

    rt = zeros(0.55)
    for j in range(4):
        add_into(rt, thump(115 - j * 9, 0.09), j * 0.105, 0.85 - j * 0.15)
        add_into(rt, env_exp(filt(white(0.05), "lp", 750), 0.001, 0.02), j * 0.105, 0.35)
    write_wav("lock_rattle.wav", rt, peak=0.40)

    dr = filt_sweep(white(0.85), "lp", 720, 280, q=1.0)
    swell(dr, 0.55)
    env_ar(dr, 0.03, 0.12)
    creaklet = filt(tone_wow(135, 0.5, depth=0.06, rate=2.2), "bp", 420, q=2.0)
    env_exp(creaklet, 0.05, 0.22)
    add_into(dr, creaklet, 0.15, 0.25)
    add_into(dr, thump(105, 0.16), 0.72, 0.8)
    write_wav("drawer_open.wav", dr, peak=0.42)

    dr2 = filt_sweep(white(0.5), "lp", 320, 650, q=1.0)
    env_ar(dr2, 0.03, 0.05)
    add_into(dr2, thump(88, 0.22), 0.42, 1.0)
    write_wav("drawer_close.wav", dr2, peak=0.42)

    n = sec(1.9)
    creak, phase = [], 0.0
    for i in range(n):
        tt = i / SR
        f = 72 + 34 * math.sin(2 * math.pi * 0.4 * tt) + 14 * math.sin(2 * math.pi * 1.9 * tt) + 26 * (tt / 1.9)
        phase += 2 * math.pi * max(38.0, f) / SR
        saw = 2.0 * (phase / (2 * math.pi) - math.floor(0.5 + phase / (2 * math.pi)))
        pulse = 0.35 + 0.65 * abs(math.sin(math.pi * (0.4 * tt + 0.28 * tt * tt))) ** 1.5
        creak.append(saw * pulse)
    creak = filt(creak, "bp", 430, q=1.3)
    creak = filt(creak, "lp", 1500)
    env_ar(creak, 0.1, 0.3)
    add_into(creak, aged_tick(900, 0.05), 1.8, 0.45)
    write_wav("door_creak.wav", creak, peak=0.32)

    du = zeros(0.9)
    scrape2 = filt_sweep(white(0.2), "bp", 700, 380, q=1.6)
    env_ar(scrape2, 0.015, 0.05)
    add_into(du, scrape2, 0.03, 0.45)
    add_into(du, aged_tick(800, 0.055), 0.26, 0.6)
    add_into(du, thump(58, 0.35, sweep_to=0.55), 0.34, 1.0)
    add_into(du, felt_piano(note("F", -1), 0.55, tau=0.25), 0.38, 0.22)
    write_wav("door_unlock.wav", filt(du, "lp", 2000), peak=0.48)

    # -- Rewards: bittersweet, unresolved -------------------------------------------
    sc = zeros(2.4)
    for j, (nm, octv) in enumerate([("F", 2), ("C", 2), ("Ab", 2), ("G", 2)]):  # ends on the 9th
        add_into(sc, music_box(note(nm, octv), 1.9, tau=0.65), 0.24 * j, 0.5)
        add_into(sc, felt_piano(note(nm, octv - 1), 1.6, tau=0.6), 0.24 * j, 0.18)
    add_into(sc, crackle(2.4, 8, gain=0.03), 0, 1.0)
    write_wav("shard_collect.wav", filt(echo_taps(sc, 0.46, (0.15, 0.06)), "lp", 2500), peak=0.40)

    sp = zeros(1.1)
    add_into(sp, felt_piano(note("F"), 0.9, tau=0.4), 0.0, 0.55)
    add_into(sp, music_box(note("F", 2), 0.9, tau=0.4), 0.02, 0.35)
    add_into(sp, aged_tick(1100, 0.04), 0.0, 0.3)
    write_wav("shard_place.wav", sp, peak=0.38)

    ph = zeros(0.9)
    for j in range(3):
        flut = filt(white(0.10), "bp", 520 + j * 130, q=1.1)
        env_ar(flut, 0.02, 0.06)
        add_into(ph, flut, j * 0.10, 0.5)
    add_into(ph, music_box(note("Ab", 2), 0.55, tau=0.3), 0.34, 0.28)
    write_wav("photo_pickup.wav", filt(ph, "lp", 1500), peak=0.34)

    sol = zeros(1.9)
    for j, (nm, octv) in enumerate([("F", 0), ("C", 1), ("Ab", 1), ("G", 1)]):  # Fm add9, left hanging
        add_into(sol, felt_piano(note(nm, octv), 1.6, tau=0.65), 0.18 * j, 0.55)
    write_wav("puzzle_solve.wav", echo_taps(sol, 0.42, (0.14, 0.05)), peak=0.42)

    rc = zeros(3.1)
    for nm, octv, amp in [("F", -1, 0.5), ("F", 0, 0.45), ("Ab", 0, 0.38), ("C", 1, 0.34), ("G", 1, 0.26)]:
        tone = tone_wow(mf(note(nm, octv)), 3.0, depth=0.0035, rate=0.5)
        add_into(tone, sine(mf(note(nm, octv)) * 2.002, 3.0), 0, 0.15)
        add_into(rc, swell(tone, 0.42), 0.05, amp)
    add_into(rc, music_box(note("G", 2), 2.0, tau=0.8), 0.75, 0.22)
    env_ar(rc, 0.05, 0.5)
    write_wav("room_complete.wav", filt(rc, "lp", 2300), peak=0.44)

    # -- Loops -------------------------------------------------------------------------
    cycle = 1.0  # 60 bpm — a resting, grieving heart
    hb = zeros(cycle)
    add_into(hb, thump(52, 0.3, sweep_to=0.55), 0.0, 1.0)
    add_into(hb, thump(47, 0.26, sweep_to=0.55), 0.30, 0.55)
    hb = filt(hb, "lp", 150, passes=2)
    write_wav("heartbeat.wav", hb, peak=0.70)

    amb = brown(12.0)
    amb = filt(amb, "lp", 320, passes=2)
    hiss = filt(white(12.0), "bp", 4800, q=0.6)
    add_into(amb, hiss, 0, 0.030)
    add_into(amb, sine(59.0, 12.0), 0, 0.03)
    add_into(amb, crackle(12.0, 46, gain=0.045), 0, 1.0)  # vinyl in another room
    for i in range(len(amb)):
        amb[i] *= 0.85 + 0.15 * math.sin(2 * math.pi * 0.07 * (i / SR))
    amb = seamless(amb, 10.0, 2.0)
    write_wav("ambience_room_tone.wav", amb, peak=0.42)

    tick = add_into(env_exp(filt(white(0.04), "lp", 950), 0.001, 0.012),
                    env_exp(sine(480, 0.04), 0.001, 0.014), 0, 0.5)
    write_wav("clock_tick.wav", tick, peak=0.24)

    print("Done.")


if __name__ == "__main__":
    build()
