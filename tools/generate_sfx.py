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
BG_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "Music", "bg")

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

def write_wav(name, samples, peak=0.8, out_dir=OUT_DIR):
    path = os.path.normpath(os.path.join(out_dir, name))
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




# ------------------------------------------------------- cracks + room music

def build_crack_sfx():
    """Short, dry glass failures synced to the bad ending's visual cracks."""
    def crack(name, seed, duration, hits, ping_count, peak):
        rng = random.Random(seed)
        out = zeros(duration)
        for at, gain in hits:
            burst = env_exp(filt(white(0.11), "hp", 1650), 0.0005, 0.020)
            add_into(out, burst, at, gain)
            add_into(out, env_exp(filt(white(0.05), "bp", 2800, q=1.1), 0.0005, 0.010), at, gain * 0.7)
        for k in range(ping_count):
            freq = rng.uniform(2100, 4300) * (0.96 ** k)
            add_into(out, env_exp(sine(freq, 0.16), 0.0007, 0.035),
                     rng.uniform(0.02, max(0.03, duration - 0.18)), rng.uniform(0.05, 0.13))
        add_into(out, crackle(duration, 18 + ping_count * 3, gain=0.035), 0, 0.65)
        add_into(out, thump(72, 0.28, sweep_to=0.55), 0, 0.28)
        write_wav(name, filt(out, "lp", 6200), peak=peak)

    crack("screen_crack_1.wav", 1101, 0.55, [(0.00, 0.55)], 3, 0.48)
    crack("screen_crack_2.wav", 1102, 0.85, [(0.00, 0.48), (0.13, 0.62), (0.31, 0.34)], 6, 0.55)
    crack("screen_crack_3.wav", 1103, 1.35, [(0.00, 0.62), (0.09, 0.52), (0.24, 0.72), (0.48, 0.38)], 11, 0.62)


def build_footstep_sfx():
    """Journey footsteps, voiced for the exact painted floors.

    All three hallway paintings carry a carpet runner down the middle:
    each indoor step is therefore a soft textile thud with only a hint
    of the boards working underneath. The road is unpaved and autumnal,
    so its steps are loose dirt/grit grains with a dry-leaf tail.
    Two alternating samples per surface keep the cadence human.
    """

    def local_noise(rng, seconds):
        return [rng.uniform(-1.0, 1.0) for _ in range(sec(seconds))]

    def carpet_step(name, seed, body_hz, board_hz, creak_gain):
        rng = random.Random(seed)
        out = zeros(0.26)

        # The weight of the step, swallowed first by carpet pile.
        add_into(out, thump(body_hz, 0.20, sweep_to=0.62), 0.0, 0.58)
        pile = env_exp(filt(local_noise(rng, 0.11), "lp", 620, passes=2),
                       0.002, 0.026)
        add_into(out, pile, 0.0, 1.0)

        # A much quieter board knock and hairline creak under the runner.
        knock = env_exp(filt(local_noise(rng, 0.055), "bp", board_hz, q=1.1),
                        0.001, 0.014)
        add_into(out, knock, 0.012, 0.30)
        creak = env_exp(filt(tone_wow(board_hz * 0.52, 0.10, depth=0.055, rate=2.4),
                             "bp", board_hz * 0.9, q=2.0), 0.012, 0.035)
        add_into(out, creak, 0.028, creak_gain)

        env_ar(out, 0.0015, 0.055)
        write_wav(name, filt(out, "lp", 900, passes=1), peak=0.48)

    def dirt_step(name, seed, body_hz, leaf_gain):
        rng = random.Random(seed)
        out = zeros(0.34)

        # Packed earth takes the weight; loose grit gives way around it.
        add_into(out, thump(body_hz, 0.22, sweep_to=0.58), 0.0, 0.40)
        base = env_exp(filt(local_noise(rng, 0.16), "bp", 1050, q=0.55),
                       0.001, 0.042)
        add_into(out, base, 0.0, 0.86)

        # Individual grains and tiny stones shifting after first contact.
        for _ in range(17):
            at = rng.uniform(0.003, 0.125)
            grain = env_exp(
                filt(local_noise(rng, rng.uniform(0.012, 0.030)),
                     "bp", rng.uniform(900, 3300), q=rng.uniform(0.8, 1.7)),
                0.0006, rng.uniform(0.005, 0.013))
            add_into(out, grain, at, rng.uniform(0.12, 0.34))

        # Brittle leaves drag for a fraction after the heel lands.
        leaves = env_ar(filt(local_noise(rng, 0.23), "bp", 2100, q=0.45),
                        0.018, 0.13)
        add_into(out, leaves, 0.018, leaf_gain)

        env_ar(out, 0.001, 0.075)
        write_wav(name, filt(out, "lp", 4300, passes=1), peak=0.50)

    carpet_step("footstep_carpet_a.wav", 2401, 84, 330, 0.10)
    carpet_step("footstep_carpet_b.wav", 2402, 76, 285, 0.16)
    dirt_step("footstep_dirt_a.wav", 2411, 72, 0.22)
    dirt_step("footstep_dirt_b.wav", 2412, 66, 0.28)


def build_truth_sfx():
    """Diegetic one-shots for the finale's truth replay.

    No score colours these: a child's wet shoes on the stone canal
    bridge, old railing wood deciding all at once that it is finished,
    and dark water receiving the fall, then closing over it.
    """

    def local_noise(rng, seconds):
        return [rng.uniform(-1.0, 1.0) for _ in range(sec(seconds))]

    # --- Ida crossing the stone bridge ---------------------------------
    rng = random.Random(4471)
    steps = zeros(5.4)
    contact_times = [0.25, 0.92, 1.58, 2.27, 2.94, 3.65, 4.36]
    for i, at in enumerate(contact_times):
        even = i % 2 == 0
        gain = 0.92 if even else 1.0
        add_into(steps,
                 thump(118 if even else 108, 0.18, sweep_to=0.55),
                 at, 0.38 * gain)
        contact = env_exp(
            filt(local_noise(rng, 0.09), "bp", 1250, q=0.7),
            0.001, 0.022)
        add_into(steps, contact, at, 0.85 * gain)
        tick = env_exp(
            filt(local_noise(rng, 0.035), "bp", 2600, q=1.4),
            0.0007, 0.008)
        add_into(steps, tick, at + 0.004, 0.28 * gain)
        scuff = env_ar(
            filt(local_noise(rng, 0.16), "bp", 760, q=0.8),
            0.025, 0.10)
        add_into(steps, scuff, at + 0.035, 0.16 * gain)
        for _ in range(rng.randrange(1, 4)):
            grit = env_exp(
                filt(local_noise(rng, rng.uniform(0.010, 0.024)),
                     "bp", rng.uniform(1300, 3400), q=rng.uniform(0.9, 1.8)),
                0.0006, rng.uniform(0.004, 0.010))
            add_into(steps, grit, at + rng.uniform(0.01, 0.11),
                     rng.uniform(0.08, 0.20))
    steps = echo_taps(steps, delay=0.19, gains=(0.10, 0.035))
    env_ar(steps, 0.01, 0.25)
    write_wav("truth_bridge_steps.wav",
              filt(steps, "lp", 3600), peak=0.46)

    # --- the rotten wooden railing gives -------------------------------
    rng = random.Random(4472)
    rail = zeros(1.65)
    strain = env_ar(
        filt(tone_wow(145, 0.38, depth=0.12, rate=2.8),
             "bp", 360, q=1.6),
        0.08, 0.15)
    add_into(rail, strain, 0.0, 0.42)
    for at in (0.18, 0.27, 0.33):
        fibres = env_exp(
            filt(local_noise(rng, rng.uniform(0.025, 0.045)),
                 "bp", rng.uniform(700, 1700), q=1.1),
            0.0007, rng.uniform(0.007, 0.014))
        add_into(rail, fibres, at, rng.uniform(0.25, 0.42))

    for at, body_hz, gain in ((0.38, 96, 0.72), (0.54, 82, 1.0)):
        add_into(rail, thump(body_hz, 0.34, sweep_to=0.45), at, 0.58 * gain)
        snap = env_exp(
            filt(local_noise(rng, 0.20), "bp", 950, q=0.65),
            0.0007, 0.038)
        add_into(rail, snap, at, gain)
        splinters = env_exp(
            filt(local_noise(rng, 0.10), "bp", 2450, q=1.0),
            0.0006, 0.018)
        add_into(rail, splinters, at + 0.008, 0.42 * gain)

    for at, hz in ((0.86, 520), (1.08, 430), (1.29, 360)):
        falling_wood = env_exp(
            filt(local_noise(rng, 0.075), "bp", hz, q=1.5),
            0.001, 0.018)
        add_into(rail, falling_wood, at, 0.30)
    env_ar(rail, 0.001, 0.18)
    write_wav("truth_railing_break.wav",
              filt(rail, "lp", 3200), peak=0.62)

    # --- the fall, then the canal ---------------------------------------
    rng = random.Random(4473)
    water = zeros(3.3)
    falling_air = env_ar(
        filt_sweep(local_noise(rng, 0.55), "lp", 1600, 480, q=0.8),
        0.12, 0.10)
    add_into(water, falling_air, 0.0, 0.22)

    add_into(water, thump(58, 0.55, sweep_to=0.42), 0.55, 0.50)
    impact = env_exp(
        filt(local_noise(rng, 1.15), "lp", 1900, passes=1),
        0.002, 0.28)
    add_into(water, impact, 0.55, 1.0)
    churn = env_exp(
        filt(local_noise(rng, 0.85), "bp", 820, q=0.55),
        0.018, 0.32)
    add_into(water, churn, 0.62, 0.48)

    for _ in range(42):
        droplet = env_exp(
            filt(local_noise(rng, rng.uniform(0.018, 0.055)),
                 "bp", rng.uniform(1500, 4200), q=rng.uniform(0.8, 1.7)),
            0.0007, rng.uniform(0.006, 0.020))
        add_into(water, droplet, rng.uniform(0.58, 2.05),
                 rng.uniform(0.05, 0.20))

    ripples = env_ar(
        filt(local_noise(rng, 2.35), "lp", 650, passes=2),
        0.35, 1.45)
    add_into(water, ripples, 0.72, 0.22)
    env_ar(water, 0.004, 0.45)
    write_wav("truth_water_splash.wav", water, peak=0.66)


def build_room_music():
    """Six seamless, room-specific loops in the same F-minor felt/music-box world."""
    print("Generating room music ->", os.path.normpath(BG_DIR))
    cache = {}

    def pad_note(midi, dur):
        key = ("pad", midi, round(dur, 2))
        if key not in cache:
            out = zeros(dur)
            for ratio, amp in [(1.0, 0.82), (2.0, 0.27), (3.0, 0.09)]:
                tone = tone_wow(mf(midi) * ratio, dur, depth=0.003, rate=0.23)
                env_ar(tone, min(1.25, dur * 0.30), min(1.7, dur * 0.36))
                add_into(out, tone, 0, amp)
            cache[key] = filt(out, "lp", 1750)
        return cache[key]

    def voice(kind, midi, dur=2.4, tau=0.72):
        key = (kind, midi, round(dur, 2), round(tau, 2))
        if key not in cache:
            if kind == "piano":
                cache[key] = felt_piano(midi, dur, tau)
            elif kind == "box":
                cache[key] = music_box(midi, dur, tau)
            else:
                cache[key] = pad_note(midi, dur)
        return cache[key]

    def put(track, kind, midi, at, gain, dur=2.4, tau=0.72):
        if midi is not None:
            add_into(track, voice(kind, midi, dur, tau), at, gain)

    def chord(track, notes, at, bar, pad_gain=0.075, bass_gain=0.20, piano_gain=0.0):
        put(track, "piano", notes[0] - 12, at, bass_gain, min(3.6, bar), 1.15)
        for j, midi in enumerate(notes):
            if pad_gain:
                add_into(track, pad_note(midi, bar * 1.08), at, pad_gain)
            if piano_gain:
                put(track, "piano", midi, at + j * 0.16, piano_gain, 2.8, 0.9)

    def sequence(track, notes, at, step, kind, gain, dur=2.2, tau=0.62):
        for j, midi in enumerate(notes):
            put(track, kind, midi, at + j * step, gain, dur, tau)

    def finish(name, track, duration, lp=2500, crackle_count=34, brown_gain=0.0, dips=None):
        add_into(track, crackle(duration + 2.0, crackle_count, gain=0.030), 0, 0.55)
        if brown_gain:
            add_into(track, filt(brown(duration + 2.0), "lp", 520), 0, brown_gain)
        out = seamless(track, duration, 2.0)
        if dips:
            out = dropout(out, dips)
        write_wav(name, filt(out, "lp", lp), peak=0.42, out_dir=BG_DIR)

    fm = [53, 56, 60]
    db = [49, 53, 56]
    ab = [56, 60, 63]
    eb = [51, 55, 58]
    cm = [48, 51, 55]
    bbm = [46, 49, 53]

    # Room 1 — kitchen morning: felt-piano arpeggio and a tired clock.
    duration, bar = 48.0, 6.0
    track = zeros(duration + 2.5)
    tick = aged_tick(880, 0.05)
    motifs = [[65, 63, 60], [61, 60, 56], [63, 60, 56], [58, 60, 63],
              [65, 63, 60], [61, 60, 56], [55, 58, 60], [56, None, 53]]
    for i, ch in enumerate([fm, db, ab, eb, fm, db, cm, fm]):
        at = i * bar
        chord(track, ch, at, bar, pad_gain=0.055, bass_gain=0.19)
        for j, idx in enumerate([0, 1, 2, 1]):
            put(track, "piano", ch[idx] + 12, at + 0.3 + j * 1.38, 0.105, 2.2, 0.72)
        sequence(track, motifs[i], at + 0.85, 1.72, "piano", 0.085, 2.5, 0.82)
    for at in range(0, int(duration), 2):
        add_into(track, tick, at, 0.030)
    finish("room_1.wav", track, duration, lp=2350, crackle_count=28)

    # Room 2 — study/hearth: music box over low piano, warmer and closer.
    duration, bar = 48.0, 6.0
    track = zeros(duration + 2.5)
    motifs = [[68, 65, 63, 60], [63, 60, 58], [61, 65, 68, 65], [63, 58, 55],
              [68, 65, 63, 60], [61, 60, 56], [63, 65, 68], [65, 63, 60, 56]]
    for i, ch in enumerate([fm, ab, db, eb, fm, db, ab, fm]):
        at = i * bar
        chord(track, ch, at, bar, pad_gain=0.068, bass_gain=0.20, piano_gain=0.055)
        sequence(track, motifs[i], at + 0.55, 1.22, "box", 0.095, 2.3, 0.58)
    finish("room_2.wav", track, duration, lp=2750, crackle_count=95, brown_gain=0.012)

    # Room 3 — bedroom: a sparse 3/4 lullaby, mostly music box.
    duration, bar = 44.0, 5.5
    track = zeros(duration + 2.5)
    for i, ch in enumerate([fm, db, fm, ab, db, eb, cm, fm]):
        at = i * bar
        chord(track, ch, at, bar, pad_gain=0.052, bass_gain=0.16)
        for j, idx in enumerate([0, 1, 2, 1, 2, 1]):
            put(track, "box", ch[idx] + 24, at + 0.25 + j * 0.82, 0.065, 1.9, 0.52)
        if i in (1, 4, 6):
            sequence(track, [77, 75, 72], at + 1.4, 1.35, "box", 0.055, 2.4, 0.66)
    finish("room_3.wav", track, duration, lp=2650, crackle_count=22)

    # Room 4 — threshold: low clusters, long silences, tape failing.
    duration, bar = 40.0, 5.0
    track = zeros(duration + 2.5)
    for i, ch in enumerate([fm, fm, db, cm, fm, bbm, eb, fm]):
        at = i * bar
        put(track, "piano", ch[0] - 12, at, 0.24, 4.2, 1.35)
        for midi in ch:
            add_into(track, pad_note(midi - 12, bar * 1.05), at, 0.082)
        add_into(track, thump(46, 0.65, sweep_to=0.5), at + 0.05, 0.10)
        if i % 2 == 0:
            sequence(track, [63, 61, 60, 56], at + 0.8, 0.92, "box", 0.062, 2.0, 0.48)
        else:
            put(track, "piano", ch[1] + 12, at + 2.35, 0.065, 2.8, 0.9)
    finish("room_4.wav", track, duration, lp=2050, crackle_count=52,
           dips=[(13.0, 0.16, 0.62), (29.0, 0.22, 0.78)])

    # Room 5 — the emptied room: swelling pads and one piano answer per bar.
    duration, bar = 44.0, 5.5
    track = zeros(duration + 2.5)
    answers = [65, 63, 61, 60, 58, 60, 56, 53]
    for i, ch in enumerate([fm, eb, db, cm, fm, db, bbm, fm]):
        at = i * bar
        chord(track, ch, at, bar, pad_gain=0.083, bass_gain=0.13)
        put(track, "piano", answers[i], at + 2.65, 0.095, 3.2, 1.0)
    finish("room_5.wav", track, duration, lp=2200, crackle_count=26)

    # Room 6 — a child's tune with notes missing; piano answers too late.
    duration, bar = 44.0, 5.5
    track = zeros(duration + 2.5)
    tune = [77, 75, 72, 75, 77, 80, 77, 75]
    for i, ch in enumerate([fm, ab, fm, db, fm, cm, db, fm]):
        at = i * bar
        chord(track, ch, at, bar, pad_gain=0.045, bass_gain=0.15)
        notes = tune if i not in (4, 5) else [77, None, 72, None, 77, None, None, 75]
        sequence(track, notes, at + 0.35, 0.58, "box", 0.075, 1.8, 0.50)
        put(track, "piano", ch[0] + 12, at + 4.15, 0.070, 2.6, 0.86)
    finish("room_6.wav", track, duration, lp=2500, crackle_count=88,
           dips=[(17.0, 0.12, 0.55), (33.0, 0.18, 0.68)])

# ------------------------------------------------------------------ the pack


def build_collapse_music():
    """The Clarity-collapse countdown bed: 15 seconds, looped seamlessly.

    Same F-minor felt world as the room loops, but the grieving calm is gone:
    a low F pedal pounds like a slow heart under a detuned music-box that
    climbs F-Ab-C-Eb and never resolves, wood ticks mark the seconds, and a
    warped-record sag keeps pulling the pitch down. Designed to sit under the
    GameUI countdown ticks, not replace them.
    """
    print("Generating collapse music ->", os.path.normpath(BG_DIR))
    dur = 17.0
    track = zeros(dur)

    # Low pounding pedal: felt-piano F every 0.75s, alternating weight.
    t = 0.0
    beat = 0
    while t < dur - 1.0:
        add_into(track, felt_piano(note("F", -1), 1.1, 0.42), t, 0.55 if beat % 2 == 0 else 0.34)
        add_into(track, thump(49, 0.32, sweep_to=0.55), t, 0.42 if beat % 2 == 0 else 0.20)
        t += 0.75
        beat += 1

    # Detuned music-box climb that never lands (two passes, second higher).
    climb = [note("F", 1), note("Ab", 1), note("C", 2), note("Eb", 2), note("Db", 2), note("C", 2)]
    for j, midi in enumerate(climb):
        add_into(track, music_box(midi, 1.6, tau=0.5), 0.4 + j * 1.5, 0.30)
        add_into(track, music_box(midi + 12, 1.4, tau=0.42), 8.2 + j * 1.35, 0.20)

    # Wood ticks: one per second early, doubling late in the loop.
    for sec_i in range(15):
        add_into(track, aged_tick(1500, 0.05), sec_i * 1.0, 0.20)
        if sec_i >= 10:
            add_into(track, aged_tick(1750, 0.05), sec_i * 1.0 + 0.5, 0.16)

    # Dissonant held cluster breathing underneath (F + Gb-ish neighbour).
    for midi, g in [(note("F", 0), 0.16), (note("F", 0) + 1, 0.10), (note("Ab", 0), 0.11)]:
        tone = tone_wow(mf(midi), dur, depth=0.006, rate=0.31)
        env_ar(tone, 1.4, 1.8)
        add_into(track, tone, 0, g)

    add_into(track, crackle(dur, 60, gain=0.05), 0, 0.8)
    track = sag_resample(track, min_rate=0.93, center=0.6, width=0.35)
    track = (track + zeros(dur))[: sec(dur)]
    track = seamless(track, 15.0, 2.0)
    write_wav("collapse.wav", filt(track, "lp", 2800), peak=0.62, out_dir=BG_DIR)



def build_ending_music():
    """The two authored-ending beds, under the last conversation with Ida.

    ending_release.wav — still F minor, but relieved: the harmony keeps
    leaning toward the relative major (Db-Ab-Eb), the felt piano answers
    higher each bar and the music box is allowed a little light. Grief,
    breathing again.

    ending_kept.wav — the same room giving up: a slow descending line that
    never finishes, detuned wow, a low neighbour-tone cluster, tape dropouts.
    Nothing resolves because nothing does.
    Both are seamless loops in the room loops' felt/music-box world.
    """
    print("Generating ending music ->", os.path.normpath(BG_DIR))

    def pad(midi, dur, gain, track, at, lp=1600):
        tone = tone_wow(mf(midi), dur, depth=0.003, rate=0.23)
        env_ar(tone, min(1.3, dur * 0.3), min(1.8, dur * 0.36))
        add_into(track, filt(tone, "lp", lp), at, gain)

    fm = [53, 56, 60]
    db = [49, 53, 56]
    ab = [56, 60, 63]
    eb = [51, 55, 58]
    cm = [48, 51, 55]
    bbm = [46, 49, 53]

    # ---- Release: F minor leaning home toward Ab major, rising answers.
    duration, bar = 36.0, 6.0
    track = zeros(duration + 2.5)
    answers = [65, 68, 70, 72, 75, 72]
    sparkle = [[77, 75, 72], [79, 77, 75], [80, 79, 77], [82, 80, 79], [84, 82, 80], [82, 79, 77]]
    for i, ch in enumerate([fm, db, ab, eb, ab, fm]):
        at = i * bar
        add_into(track, felt_piano(ch[0] - 12, 3.8, 1.2), at, 0.20)
        for midi in ch:
            pad(midi, bar * 1.08, 0.070, track, at, lp=1900)
        for j, idx in enumerate([0, 1, 2, 1]):
            add_into(track, felt_piano(ch[idx] + 12, 2.4, 0.8), at + 0.3 + j * 1.35, 0.090)
        add_into(track, felt_piano(answers[i], 3.2, 1.0), at + 2.7, 0.110)
        for j, midi in enumerate(sparkle[i]):
            add_into(track, music_box(midi, 2.0, tau=0.55), at + 0.9 + j * 1.55, 0.060)
    add_into(track, crackle(duration + 2.0, 20, gain=0.028), 0, 0.5)
    out = seamless(track, duration, 2.0)
    write_wav("ending_release.wav", filt(out, "lp", 3100), peak=0.48, out_dir=BG_DIR)

    # ---- Kept house: descending, detuned, dropping out.
    duration, bar = 36.0, 6.0
    track = zeros(duration + 2.5)
    descent = [65, 63, 61, 60, 58, 56]
    for i, ch in enumerate([fm, cm, bbm, fm, db, fm]):
        at = i * bar
        add_into(track, felt_piano(ch[0] - 12, 4.4, 1.45), at, 0.24)
        for midi in ch:
            pad(midi - 12, bar * 1.05, 0.082, track, at, lp=1300)
        # The neighbour that will not resolve (F's semitone shadow).
        pad(ch[0] - 11, bar * 1.05, 0.035, track, at, lp=1100)
        add_into(track, thump(44, 0.7, sweep_to=0.5), at + 0.05, 0.09)
        add_into(track, felt_piano(descent[i], 3.4, 1.1), at + 2.9, 0.085)
        if i % 2 == 0:
            add_into(track, music_box(descent[i] + 12, 2.2, tau=0.5), at + 1.1, 0.045)
    add_into(track, crackle(duration + 2.0, 70, gain=0.045), 0, 0.7)
    add_into(track, filt(brown(duration + 2.0), "lp", 420), 0, 0.020)
    track = sag_resample(track, min_rate=0.94, center=0.55, width=0.4)
    track = (track + zeros(duration + 2.5))[: sec(duration + 2.5)]
    out = seamless(track, duration, 2.0)
    out = dropout(out, [(11.0, 0.18, 0.65), (24.0, 0.24, 0.8)])
    write_wav("ending_kept.wav", filt(out, "lp", 1700), peak=0.46, out_dir=BG_DIR)


def build():
    print("Generating SFX pack v2 (F minor, felt & worn) ->", os.path.normpath(OUT_DIR))

    # -- UI: felt, paper, wood — nothing digital ------------------------------
    s = add_into(felt_piano(note("F"), 0.18, tau=0.05),
                 env_exp(filt(white(0.05), "lp", 900), 0.001, 0.012), 0, 0.35)
    write_wav("ui_click.wav", s, peak=0.50)

    write_wav("ui_hover.wav", env_exp(filt(white(0.035), "lp", 750, passes=2), 0.002, 0.010), peak=0.22)

    s = add_into(thump(mf(note("F", -1)), 0.12, sweep_to=0.75),
                 env_exp(filt(white(0.06), "lp", 700), 0.001, 0.016), 0, 0.4)
    write_wav("ui_back.wav", s, peak=0.42)

    # -- Dialogue: muted piano-string taps --------------------------------------
    blip = felt_piano(note("F", 1), 0.09, tau=0.028)  # F4: audible on small speakers, still felt
    write_wav("dialogue_blip.wav", blip, peak=0.40)

    swish = filt_sweep(white(0.20), "bp", 330, 1050, q=1.0)
    env_ar(swish, 0.02, 0.11)
    write_wav("dialogue_advance.wav", filt(swish, "lp", 1100, passes=2), peak=0.42)

    page = filt_sweep(white(0.55), "bp", 300, 900, q=0.9)
    swell(page, 0.45)
    add_into(page, filt(white(0.05), "lp", 1400), 0.04, 0.10)
    write_wav("page_turn.wav", filt(page, "lp", 1100, passes=2), peak=0.40)

    # -- Examine / clarity ---------------------------------------------------------
    ex = env_exp(filt(white(0.42), "lp", 480), 0.015, 0.18)
    add_into(ex, thump(mf(note("F", -1)), 0.16, sweep_to=0.7), 0.01, 0.55)
    write_wav("examine.wav", ex, peak=0.50)

    # Clarity arm = memory surfacing: reversed-feel swell of the F-minor triad.
    arm = zeros(0.85)
    for nm in ["F", "Ab", "C"]:
        tone = felt_piano(note(nm), 0.85, tau=2.5)      # near-sustained
        swell(tone, 0.8)                                 # slow attack, late peak
        add_into(arm, tone, 0.0, 0.5)
    env_ar(arm, 0.02, 0.10)
    write_wav("clarify_arm.wav", filt(arm, "lp", 2000), peak=0.42)

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
    write_wav("dial_tick.wav", aged_tick(1320, 0.05), peak=0.45)

    key = add_into(env_exp(filt(white(0.06), "bp", 750, q=1.2), 0.001, 0.02),
                   thump(150, 0.07), 0, 0.4)
    write_wav("key_type.wav", filt(key, "lp", 1600), peak=0.45)

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
    write_wav("clock_tick.wav", tick, peak=0.36)

    # ---- memory_shatter.wav: the bad ending. A music-box phrase sags out of
    #      tune while glassy pings scatter down, then the floor drops out.
    sh = zeros(3.8)
    phrase = zeros(3.8)
    for j, nm in enumerate(["F", "Ab", "C", "Eb", "Db"]):
        add_into(phrase, music_box(note(nm, 1), 1.6, tau=0.5), 0.16 * j, 0.5)
    phrase = sag_resample(phrase, min_rate=0.8, center=0.7, width=0.4)
    phrase = (phrase + zeros(3.8))[: sec(3.8)]
    add_into(sh, phrase, 0, 0.8)
    rng_sh = random.Random(41)
    for k in range(9):
        f = 3200 * (0.93 ** k) * rng_sh.uniform(0.97, 1.03)
        add_into(sh, env_exp(sine(f, 0.2), 0.001, 0.045), 0.5 + 0.11 * k * rng_sh.uniform(0.8, 1.3), 0.14)
    add_into(sh, crackle(3.0, 55, gain=0.055), 0.4, 0.8)
    add_into(sh, thump(46, 0.8, sweep_to=0.45), 0.55, 0.95)
    sh = dropout(sh, [(0.95, 0.07, 0.85), (1.4, 0.05, 0.7), (2.1, 0.09, 0.9)])
    write_wav("memory_shatter.wav", filt(sh, "lp", 5200), peak=0.6)

    build_crack_sfx()
    build_footstep_sfx()
    build_truth_sfx()
    build_room_music()
    build_collapse_music()
    build_ending_music()

    print("Done.")


if __name__ == "__main__":
    build()
