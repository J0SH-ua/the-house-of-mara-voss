#!/usr/bin/env python3
"""
Procedural SFX generator for The House of Mara Voss.

Synthesizes a small, cohesive, melancholic sound pack (mono 22050 Hz WAVs)
into assets/Music/SFX/ using only the Python standard library.
Re-run any time:  python3 tools/generate_sfx.py

Design notes
------------
* Everything is kept quiet and warm: soft attacks, lowpassed transients,
  music-box partials for rewards, breathy filtered noise for corruption.
* Ambience / heartbeat are generated seamless (wrap crossfade / one full
  cycle) so AudioManager can loop them.
* Peaks are normalised per-file to a deliberate target so loudness staging
  lives here, not scattered through game code.
"""

import math
import os
import random
import struct
import wave

SR = 22050
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "Music", "SFX")

random.seed(20261024)  # deterministic pack


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
        wav_setparams = (1, 2, SR, len(samples), "NONE", "not compressed")
        w.setparams(wav_setparams)
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
    """Linear attack, linear release at the tail."""
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


def adsr_swell(samples, attack, hold_peak_at=0.6):
    """Slow swell peaking at hold_peak_at (fraction), then gentle fall."""
    n = len(samples)
    peak_i = int(n * hold_peak_at)
    for i in range(n):
        if i < peak_i:
            g = 0.5 - 0.5 * math.cos(math.pi * i / max(1, peak_i))
        else:
            k = (i - peak_i) / max(1, n - peak_i)
            g = 0.5 + 0.5 * math.cos(math.pi * k)
        samples[i] *= g
    return samples


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
    elif kind == "bp":  # constant skirt gain
        b0, b1, b2 = alpha, 0.0, -alpha
    elif kind == "notch":
        b0, b1, b2 = 1.0, -2 * cw, 1.0
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

def bell(freq, dur, partials=((1.0, 1.0), (2.76, 0.45), (5.4, 0.22), (8.9, 0.08)),
         tau=0.35):
    """Music-box / bell tone: inharmonic partials, higher ones decay faster."""
    out = zeros(dur)
    for k, (ratio, amp) in enumerate(partials):
        tone = sine(freq * ratio, dur)
        env_exp(tone, attack=0.001, decay_tau=tau / (1.0 + 0.9 * k))
        add_into(out, tone, 0.0, amp)
    return out


def thump(freq=64.0, dur=0.35, sweep_to=0.6):
    """Sine thump with falling pitch (drawer knock, heartbeat, denied)."""
    n = sec(dur)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / SR
        f = freq * (sweep_to + (1 - sweep_to) * math.exp(-t * 18))
        phase += 2 * math.pi * f / SR
        out.append(math.sin(phase))
    return env_exp(out, attack=0.001, decay_tau=dur * 0.45)


def metallic_tick(freq=2600.0, dur=0.045):
    tone = sine(freq, dur)
    tone = add_into(tone, sine(freq * 1.51, dur), 0, 0.5)
    tone = env_exp(tone, attack=0.0005, decay_tau=0.012)
    noise = filt(white(dur), "bp", freq, q=2.0)
    env_exp(noise, attack=0.0005, decay_tau=0.008)
    return add_into(tone, noise, 0, 0.6)


def seamless(samples, keep_seconds, fade_seconds):
    """Turn the tail into a wrap-crossfade so samples[:keep] loops cleanly."""
    keep, fade = sec(keep_seconds), sec(fade_seconds)
    out = list(samples[:keep])
    for i in range(fade):
        k = i / fade
        out[i] = samples[i] * k + samples[keep + i] * (1 - k)
    return out


# ------------------------------------------------------------------ the pack

def build():
    print("Generating SFX pack ->", os.path.normpath(OUT_DIR))

    # -- UI ---------------------------------------------------------------
    s = add_into(env_exp(sine(1750, 0.06), 0.0005, 0.014),
                 env_exp(filt(white(0.06), "hp", 3000), 0.0005, 0.008), 0, 0.5)
    write_wav("ui_click.wav", s, peak=0.45)

    write_wav("ui_hover.wav", env_exp(sine(2400, 0.03), 0.0005, 0.008), peak=0.16)

    s = add_into(env_exp(sine(880, 0.09), 0.0005, 0.025),
                 env_exp(filt(white(0.09), "hp", 2200), 0.0005, 0.012), 0, 0.35)
    write_wav("ui_back.wav", s, peak=0.4)

    # -- Dialogue ---------------------------------------------------------
    blip = env_exp(sine(680, 0.035), 0.0008, 0.010)
    add_into(blip, sine(1360, 0.035), 0, 0.25)
    env_exp(blip, 0.0008, 0.012)
    write_wav("dialogue_blip.wav", blip, peak=0.35)

    swish = filt_sweep(white(0.14), "bp", 700, 2600, q=1.1)
    env_ar(swish, 0.01, 0.08)
    add_into(swish, metallic_tick(1500, 0.04), 0.09, 0.4)
    write_wav("dialogue_advance.wav", swish, peak=0.4)

    page = filt_sweep(white(0.45), "bp", 500, 1800, q=0.9)
    adsr_swell(page, 0.15, 0.45)
    add_into(page, filt(white(0.05), "hp", 2500), 0.03, 0.15)
    write_wav("page_turn.wav", page, peak=0.4)

    # -- Examine / clarity --------------------------------------------------
    ex = env_exp(filt(white(0.35), "lp", 900), 0.01, 0.16)
    add_into(ex, thump(170, 0.12), 0.0, 0.5)
    write_wav("examine.wav", ex, peak=0.5)

    n = sec(0.55)
    arm, phase = [], 0.0
    for i in range(n):
        t = i / SR
        f = 520 * math.pow(2, 1.2 * t / 0.55)
        phase += 2 * math.pi * f / SR
        arm.append(math.sin(phase) + 0.35 * math.sin(2 * phase) + 0.12 * math.sin(3 * phase))
    adsr_swell(arm, 0.3, 0.75)
    write_wav("clarify_arm.wav", filt(arm, "lp", 5200), peak=0.45)

    rev = zeros(1.4)
    for j, fq in enumerate([659.25, 783.99, 987.77, 1318.5]):  # E5 G5 B5 E6
        add_into(rev, bell(fq, 1.2, tau=0.5), 0.09 * j, 0.55)
    add_into(rev, filt_sweep(white(0.8), "bp", 3000, 6000, q=3.0), 0.05, 0.06)
    write_wav("clarify_reveal.wav", rev, peak=0.55)

    den = add_into(thump(115, 0.28, sweep_to=0.7),
                   env_exp(filt(sine(88, 0.22), "lp", 300), 0.002, 0.09), 0, 0.6)
    write_wav("denied.wav", den, peak=0.6)

    write_wav("word_chime.wav", bell(659.25, 0.7, tau=0.28), peak=0.5)  # E5, repitched in code

    # -- Corruption ---------------------------------------------------------
    gl = zeros(0.5)
    t = 0.0
    while t < 0.42:
        seg_len = random.choice([0.02, 0.03, 0.05, 0.08])
        kind = random.choice(["noise", "tone", "mute"])
        if kind == "noise":
            seg = filt(white(seg_len), "bp", random.uniform(400, 4500), q=2.5)
            crush = random.choice([2, 3, 4, 6])     # sample-crush
            seg = [seg[i - (i % crush)] for i in range(len(seg))]
            add_into(gl, seg, t, random.uniform(0.3, 0.8))
        elif kind == "tone":
            seg = sine(random.choice([110.0, 116.54, 220.0, 233.08]), seg_len)
            seg = [max(-0.7, min(0.7, x * 1.8)) for x in seg]  # hard clip
            add_into(gl, seg, t, 0.5)
        t += seg_len
    env_ar(gl, 0.002, 0.06)
    write_wav("corruption_glitch.wav", gl, peak=0.55)

    wh = white(1.8)
    wh = filt_sweep(wh, "bp", 550, 1900, q=4.5)
    wh2 = filt_sweep(white(1.8), "bp", 1900, 480, q=4.5)
    wh = add_into(wh, wh2, 0, 0.8)
    for i in range(len(wh)):  # breathy wavering
        tt = i / SR
        wh[i] *= 0.65 + 0.35 * math.sin(2 * math.pi * 5.3 * tt) * math.sin(2 * math.pi * 0.7 * tt)
    adsr_swell(wh, 0.9, 0.65)
    wh = env_ar(wh, 0.15, 0.25)
    write_wav("corruption_whisper.wav", wh, peak=0.4)

    # -- Locks, drawers, doors ----------------------------------------------
    write_wav("dial_tick.wav", metallic_tick(2650, 0.04), peak=0.45)

    key = add_into(env_exp(filt(white(0.05), "bp", 1400, q=1.5), 0.0005, 0.012),
                   thump(240, 0.06), 0, 0.35)
    write_wav("key_type.wav", key, peak=0.45)

    lo = zeros(0.55)
    add_into(lo, metallic_tick(1800, 0.05), 0.0, 0.8)
    add_into(lo, thump(95, 0.22), 0.07, 1.0)
    add_into(lo, metallic_tick(3200, 0.06), 0.13, 0.5)
    add_into(lo, bell(1900, 0.35, tau=0.12), 0.15, 0.25)
    write_wav("lock_open.wav", lo, peak=0.65)

    rt = zeros(0.45)
    for j in range(4):
        add_into(rt, metallic_tick(2100 - j * 180, 0.045), j * 0.085, 0.85 - j * 0.15)
        add_into(rt, thump(140, 0.07), j * 0.085, 0.35)
    write_wav("lock_rattle.wav", rt, peak=0.55)

    dr = filt_sweep(white(0.8), "lp", 1100, 420, q=1.0)
    adsr_swell(dr, 0.25, 0.55)
    env_ar(dr, 0.03, 0.12)
    add_into(dr, thump(120, 0.15), 0.68, 0.8)
    write_wav("drawer_open.wav", dr, peak=0.55)

    dr2 = filt_sweep(white(0.5), "lp", 500, 1000, q=1.0)
    env_ar(dr2, 0.03, 0.05)
    add_into(dr2, thump(100, 0.2), 0.42, 1.0)
    write_wav("drawer_close.wav", dr2, peak=0.55)

    n = sec(1.5)
    creak, phase = [], 0.0
    for i in range(n):
        tt = i / SR
        f = 95 + 55 * math.sin(2 * math.pi * 0.5 * tt) + 22 * math.sin(2 * math.pi * 2.7 * tt) + 40 * (tt / 1.5)
        phase += 2 * math.pi * max(40.0, f) / SR
        saw = 2.0 * (phase / (2 * math.pi) - math.floor(0.5 + phase / (2 * math.pi)))
        pulse = 0.35 + 0.65 * abs(math.sin(math.pi * (0.5 * tt + 0.35 * tt * tt))) ** 1.5
        creak.append(saw * pulse)
    creak = filt(creak, "bp", 700, q=1.4)
    env_ar(creak, 0.08, 0.25)
    add_into(creak, metallic_tick(1200, 0.05), 1.42, 0.5)
    write_wav("door_creak.wav", creak, peak=0.4)

    du = zeros(0.65)
    add_into(du, metallic_tick(1500, 0.04), 0.0, 0.6)
    add_into(du, filt_sweep(white(0.14), "bp", 900, 2000, q=2.0), 0.05, 0.35)
    add_into(du, metallic_tick(1100, 0.05), 0.22, 0.7)
    add_into(du, thump(80, 0.3), 0.3, 1.0)
    write_wav("door_unlock.wav", du, peak=0.65)

    # -- Rewards --------------------------------------------------------------
    sc = zeros(1.8)
    for j, fq in enumerate([1046.5, 1318.5, 1567.98, 2093.0]):  # C6 E6 G6 C7
        add_into(sc, bell(fq, 1.5, tau=0.6), 0.11 * j, 0.5)
    shimmer = filt_sweep(white(1.0), "bp", 5000, 8000, q=4.0)
    env_exp(shimmer, 0.05, 0.5)
    add_into(sc, shimmer, 0.0, 0.08)
    write_wav("shard_collect.wav", sc, peak=0.6)

    sp = zeros(0.8)
    glass = sine(2450, 0.3)
    add_into(glass, sine(3180, 0.3), 0, 0.6)
    add_into(glass, sine(4320, 0.3), 0, 0.3)
    env_exp(glass, 0.0005, 0.07)
    add_into(sp, glass, 0.0, 0.7)
    add_into(sp, bell(880, 0.6, tau=0.3), 0.03, 0.45)
    add_into(sp, thump(300, 0.05), 0.0, 0.25)
    write_wav("shard_place.wav", sp, peak=0.55)

    ph = zeros(0.55)
    for j in range(3):
        flut = filt(white(0.09), "bp", 1600 + j * 300, q=1.2)
        env_ar(flut, 0.015, 0.05)
        add_into(ph, flut, j * 0.09, 0.55)
    add_into(ph, metallic_tick(1300, 0.04), 0.3, 0.35)
    add_into(ph, bell(1174.66, 0.4, tau=0.2), 0.32, 0.25)  # D6
    write_wav("photo_pickup.wav", ph, peak=0.5)

    sol = zeros(1.1)
    for j, fq in enumerate([523.25, 659.25, 783.99]):  # C5 E5 G5
        tone = sine(fq, 0.85)
        add_into(tone, sine(fq * 2, 0.85), 0, 0.3)
        env_exp(tone, 0.004, 0.38)
        add_into(sol, tone, 0.09 * j, 0.6)
    add_into(sol, bell(1046.5, 0.9, tau=0.45), 0.27, 0.35)
    write_wav("puzzle_solve.wav", sol, peak=0.6)

    rc = zeros(2.4)
    for fq, amp in [(220.0, 0.5), (261.63, 0.4), (329.63, 0.4), (493.88, 0.3), (440.0, 0.22)]:
        tone = sine(fq, 2.3)
        add_into(tone, sine(fq * 2.003, 2.3), 0, 0.18)  # gentle chorus beat
        add_into(rc, adsr_swell(tone, 0.7, 0.45), 0.05, amp)
    add_into(rc, bell(1318.5, 1.6, tau=0.7), 0.55, 0.35)
    env_ar(rc, 0.05, 0.4)
    write_wav("room_complete.wav", filt(rc, "lp", 3600), peak=0.55)

    # -- Loops ------------------------------------------------------------------
    cycle = 60.0 / 72.0  # 72 bpm
    hb = zeros(cycle)
    add_into(hb, thump(58, 0.3, sweep_to=0.55), 0.0, 1.0)
    add_into(hb, thump(52, 0.26, sweep_to=0.55), 0.27, 0.6)
    hb = filt(hb, "lp", 240, passes=2)
    write_wav("heartbeat.wav", hb, peak=0.85)

    amb = brown(12.0)
    amb = filt(amb, "lp", 420, passes=2)
    hiss = filt(white(12.0), "bp", 6000, q=0.6)
    add_into(amb, hiss, 0, 0.045)
    hum = sine(59.0, 12.0)
    add_into(amb, hum, 0, 0.05)
    for i in range(len(amb)):  # very slow breathing of the room tone
        amb[i] *= 0.85 + 0.15 * math.sin(2 * math.pi * 0.07 * (i / SR))
    amb = seamless(amb, 10.0, 2.0)
    write_wav("ambience_room_tone.wav", amb, peak=0.5)

    tick = metallic_tick(1700, 0.035)
    write_wav("clock_tick.wav", tick, peak=0.35)

    print("Done.")


if __name__ == "__main__":
    build()
