#!/usr/bin/env python3
"""Build the hedge-maze ambience from the garden and porch source loops."""
from pathlib import Path
import math
import struct
import wave

SFX = Path(__file__).resolve().parents[1] / "assets" / "Music" / "SFX"
SOURCES = (SFX / "ambience_garden.wav", SFX / "ambience_porch.wav")
OUTPUT = SFX / "ambience_hedge_maze.wav"


def read_mono_pcm16(path: Path):
    with wave.open(str(path), "rb") as source:
        params = source.getparams()
        if params.nchannels != 1 or params.sampwidth != 2:
            raise ValueError(f"Expected mono 16-bit PCM: {path}")
        frames = source.readframes(source.getnframes())
    return params, struct.unpack("<" + "h" * (len(frames) // 2), frames)


def main() -> None:
    (params, garden), (porch_params, porch) = map(read_mono_pcm16, SOURCES)
    if params != porch_params or len(garden) != len(porch):
        raise ValueError("Garden and porch loops must have matching formats and lengths")

    count = len(garden)
    duration = count / params.framerate
    result = []
    for index, (leaf_wind, crickets) in enumerate(zip(garden, porch)):
        # Let the leaf-and-wind texture and distant crickets gently trade
        # prominence over a complete loop, keeping its ends continuous.
        phase = math.tau * (index / params.framerate) / duration
        sway = 0.08 * math.sin(phase)
        sample = 0.56 * leaf_wind * (1.0 + sway) + 0.44 * crickets * (1.0 - sway)
        result.append(max(-32768, min(32767, round(sample))))

    with wave.open(str(OUTPUT), "wb") as destination:
        destination.setparams(params)
        destination.writeframes(struct.pack("<" + "h" * count, *result))
    print(f"Wrote {OUTPUT} ({duration:.1f}s at {params.framerate} Hz)")


if __name__ == "__main__":
    main()
