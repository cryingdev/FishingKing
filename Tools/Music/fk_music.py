"""
fk_music — the BGM toolkit for 낚시왕: compose in Python, write MIDI, render with FluidSynth, cut seamless loops,
normalize, encode OGG and register the cue in the game's music manifest.

A cue is one `Song`: a tempo, a length in bars and one or more *stems* (vertical layers that the game plays in sync
and mixes at run time, e.g. a stage's `day` / `night` bases and its `fight` layer). Every track belongs to one stem.
All stems of a cue are rendered separately but from the same timeline, so they have exactly the same length in
samples and stay locked together when the game starts them on the same DSP tick.

    from fk_music import *
    s = Song("stage_lake", bpm=84, bars=24, key=Key("D", "major"), stems=["day", "night", "fight"])
    g = s.track("guitar", GM["nylon_guitar"], stem="day", vol=96, pan=-18, reverb=50)
    g.note(0, 0, s.key.deg(0, 4), 2, 80)            # bar 0, beat 0, the tonic in octave 4, 2 beats long
    ...
    s.build()                                        # midi + render + ogg + manifest (see build.py)

Conventions
- bars and beats are 0-based; beats are floats in quarter notes; durations are in beats.
- `kind="loop"` cues loop seamlessly: the piece is rendered three times over and one middle repetition is cut out,
  so reverb tails and held notes wrap round the loop point; a short crossfade at the start hides any chorus phase
  difference. `kind="sting"` cues play once (rendered once, trailing silence trimmed).
- Stems in `Song.mixes` are the stem combinations the game can play together; the loudest of them sets the peak,
  `Song.ref` (default: the first mix) sets the loudness (`Song.loudness`, dBFS RMS).
- Everything is deterministic: humanize() and friends draw from a generator seeded by the cue id.

Outputs (paths relative to the repository root)
- Tools/Music/midi/<cue>.mid                 the whole cue, all stems, one repetition (open it in a DAW)
- Assets/Resources/Audio/Music/<cue>_<stem>.ogg
- Assets/Resources/Data/music.json           the manifest the game reads (Music.cs)
- Tools/Music/_tmp/                          scratch: stem midis, wavs, lint reports (git-ignored)

Requires: python3 with mido + numpy, fluidsynth (2.x), oggenc (vorbis-tools), a GM SoundFont
(FluidR3_GM.sf2, MIT licence; path from $FK_SF2 or the Debian/Ubuntu default /usr/share/sounds/sf2/FluidR3_GM.sf2).
"""

import json
import math
import os
import random
import shutil
import subprocess
import wave

import mido
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
MIDI_DIR = os.path.join(HERE, "midi")
TMP = os.path.join(HERE, "_tmp")
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Music")
AUDIO_RES = "Audio/Music"  # Resources.Load path prefix
MANIFEST = os.path.join(ROOT, "Assets", "Resources", "Data", "music.json")

RATE = 44100
TPB = 480                     # MIDI ticks per quarter note
OGG_QUALITY = 2               # oggenc -q (≈ 96 kbps stereo; Unity re-encodes on import anyway)
PEAK = 0.89                   # -1 dBFS
SEAM_FADE = 0.012             # s: crossfade at a loop's start (see Song._cut_loop)
SF2_DEFAULT = "/usr/share/sounds/sf2/FluidR3_GM.sf2"

# FluidSynth effects: one global reverb / chorus (the per-track send is CC91 / CC93)
REVERB = {"synth.reverb.room-size": 0.62, "synth.reverb.damp": 0.35, "synth.reverb.width": 0.9, "synth.reverb.level": 0.75}
CHORUS = {"synth.chorus.nr": 3, "synth.chorus.level": 1.2, "synth.chorus.speed": 0.3, "synth.chorus.depth": 6.0}


def sf2_path():
    p = os.environ.get("FK_SF2", SF2_DEFAULT)
    if not os.path.isfile(p):
        raise FileNotFoundError(f"GM SoundFont not found: {p} (set FK_SF2)")
    return p


# ---------------------------------------------------------------------------------------------------------- pitches

NOTE_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
_PC = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6, "G": 7, "G#": 8,
       "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11, "Cb": 11}


def n(name):
    """'C4' -> 60, 'F#3' -> 54, 'Bb5' -> 82 (octave 4 = middle C)."""
    name = name.strip()
    i = 2 if len(name) > 2 and name[1] in "#b" else 1
    return _PC[name[:i]] + 12 * (int(name[i:]) + 1)


def name_of(p):
    return f"{NOTE_NAMES[p % 12]}{p // 12 - 1}"


SCALES = {
    "major": [0, 2, 4, 5, 7, 9, 11],
    "minor": [0, 2, 3, 5, 7, 8, 10],          # natural minor
    "harmonic_minor": [0, 2, 3, 5, 7, 8, 11],
    "dorian": [0, 2, 3, 5, 7, 9, 10],
    "mixolydian": [0, 2, 4, 5, 7, 9, 10],
    "lydian": [0, 2, 4, 6, 7, 9, 11],
    "phrygian": [0, 1, 3, 5, 7, 8, 10],
    "pent_major": [0, 2, 4, 7, 9],
    "pent_minor": [0, 3, 5, 7, 10],
}


class Key:
    """A tonic and a 7-note (or pentatonic) scale. deg(d, octave): scale degree d (0 = tonic; may be negative or
    past 6) as a MIDI pitch, the tonic of `octave` being deg(0, octave)."""

    def __init__(self, tonic="C", scale="major"):
        self.tonic = tonic
        self.scale_name = scale
        self.steps = SCALES[scale]
        self.pc = _PC[tonic]

    def deg(self, d, octave=4, acc=0):
        k = len(self.steps)
        o, i = divmod(d, k)
        return self.pc + 12 * (octave + 1 + o) + self.steps[i] + acc

    def chord(self, d, octave=3, size=3, inv=0, spread=False):
        """The diatonic chord on degree d (stacked thirds): size 3 = triad, 4 = seventh. inv = inversion.
        spread=True opens the voicing (the third up an octave)."""
        ps = [self.deg(d + 2 * i, octave) for i in range(size)]
        for _ in range(inv):
            ps = ps[1:] + [ps[0] + 12]
        if spread and len(ps) >= 3:
            ps = [ps[0], ps[2], ps[1] + 12] + ps[3:]
        return ps

    def in_key(self, p):
        return (p - self.pc) % 12 in self.steps

    def __repr__(self):
        return f"{self.tonic} {self.scale_name}"


def triad(root, quality="maj"):
    """A chromatic triad on a MIDI root: maj, min, dim, aug, sus2, sus4, maj7, min7, dom7, min9, add9."""
    q = {"maj": [0, 4, 7], "min": [0, 3, 7], "dim": [0, 3, 6], "aug": [0, 4, 8], "sus2": [0, 2, 7],
         "sus4": [0, 5, 7], "maj7": [0, 4, 7, 11], "min7": [0, 3, 7, 10], "dom7": [0, 4, 7, 10],
         "min9": [0, 3, 7, 10, 14], "add9": [0, 4, 7, 14], "dim7": [0, 3, 6, 9], "m7b5": [0, 3, 6, 10]}[quality]
    return [root + x for x in q]


# ------------------------------------------------------------------------------------------------- General MIDI

GM = {
    # pianos / keys / mallets
    "piano": 0, "bright_piano": 1, "epiano": 4, "epiano2": 5, "harpsichord": 6, "clavi": 7,
    "celesta": 8, "glockenspiel": 9, "music_box": 10, "vibraphone": 11, "marimba": 12, "xylophone": 13,
    "tubular_bells": 14, "dulcimer": 15,
    # organ / accordion
    "drawbar_organ": 16, "rock_organ": 18, "church_organ": 19, "reed_organ": 20, "accordion": 21, "harmonica": 22,
    # guitars / basses
    "nylon_guitar": 24, "steel_guitar": 25, "jazz_guitar": 26, "clean_guitar": 27, "muted_guitar": 28,
    "acoustic_bass": 32, "finger_bass": 33, "pick_bass": 34, "fretless_bass": 35, "slap_bass": 36, "synth_bass": 38,
    # strings / ensembles
    "violin": 40, "viola": 41, "cello": 42, "contrabass": 43, "tremolo_strings": 44, "pizzicato": 45, "harp": 46,
    "timpani": 47, "strings": 48, "slow_strings": 49, "synth_strings": 50, "choir": 52, "voice_oohs": 53,
    "synth_voice": 54, "orchestra_hit": 55,
    # brass / reeds / pipes
    "trumpet": 56, "trombone": 57, "tuba": 58, "muted_trumpet": 59, "french_horn": 60, "brass": 61,
    "synth_brass": 62, "soprano_sax": 64, "alto_sax": 65, "oboe": 68, "english_horn": 69, "bassoon": 70,
    "clarinet": 71, "piccolo": 72, "flute": 73, "recorder": 74, "pan_flute": 75, "bottle": 76, "shakuhachi": 77,
    "whistle": 78, "ocarina": 79,
    # synths / pads / fx
    "square_lead": 80, "saw_lead": 81, "calliope": 82, "chiff_lead": 83, "fifths_lead": 86,
    "new_age_pad": 88, "warm_pad": 89, "polysynth": 90, "choir_pad": 91, "bowed_pad": 92, "metallic_pad": 93,
    "halo_pad": 94, "sweep_pad": 95, "rain": 96, "soundtrack": 97, "crystal": 98, "atmosphere": 99,
    "brightness": 100, "goblins": 101, "echoes": 102, "sci_fi": 103,
    # ethnic / percussive
    "sitar": 104, "banjo": 105, "shamisen": 106, "koto": 107, "kalimba": 108, "bagpipe": 109, "fiddle": 110,
    "shanai": 111, "tinkle_bell": 112, "agogo": 113, "steel_drums": 114, "woodblock": 115, "taiko": 116,
    "melodic_tom": 117, "synth_drum": 118, "reverse_cymbal": 119, "seashore": 122, "bird": 123,
}
DRUMS = "drums"  # program for a GM percussion track (MIDI channel 10)

# GM percussion keys (channel 10)
DR = {
    "kick": 36, "kick2": 35, "stick": 37, "snare": 38, "clap": 39, "snare2": 40, "low_tom": 41, "hat": 42,
    "tom2": 43, "pedal_hat": 44, "mid_tom": 45, "open_hat": 46, "tom3": 47, "high_tom": 48, "crash": 49,
    "high_tom2": 50, "ride": 51, "china": 52, "ride_bell": 53, "tambourine": 54, "splash": 55, "cowbell": 56,
    "crash2": 57, "vibraslap": 58, "ride2": 59, "hi_bongo": 60, "lo_bongo": 61, "mute_conga": 62, "hi_conga": 63,
    "lo_conga": 64, "hi_timbale": 65, "lo_timbale": 66, "hi_agogo": 67, "lo_agogo": 68, "cabasa": 69,
    "maracas": 70, "whistle_s": 71, "whistle_l": 72, "guiro_s": 73, "guiro_l": 74, "claves": 75, "hi_wood": 76,
    "lo_wood": 77, "mute_cuica": 78, "open_cuica": 79, "mute_triangle": 80, "triangle": 81, "shaker": 82,
    "jingle": 83, "bell_tree": 84, "castanets": 85, "mute_surdo": 86, "surdo": 87,
}

# a comfortable written range per program (lint warns outside it)
RANGES = {
    "default": (n("A1"), n("C7")),
    0: (n("A0"), n("C8")), 4: (n("E1"), n("G7")), 5: (n("E1"), n("G7")), 8: (n("C4"), n("C8")),
    9: (n("G4"), n("C8")), 10: (n("C4"), n("C8")), 11: (n("F3"), n("F6")), 12: (n("C2"), n("C7")),
    13: (n("F4"), n("C8")), 14: (n("C4"), n("F5")), 24: (n("E2"), n("B5")), 25: (n("E2"), n("B5")),
    26: (n("E2"), n("B5")), 27: (n("E2"), n("B5")), 32: (n("E1"), n("G3")), 33: (n("E1"), n("G3")),
    34: (n("E1"), n("G3")), 35: (n("E1"), n("G3")), 38: (n("C1"), n("C4")), 40: (n("G3"), n("A7")),
    41: (n("C3"), n("E6")), 42: (n("C2"), n("A5")), 43: (n("E1"), n("G3")), 44: (n("C2"), n("C7")),
    45: (n("C2"), n("C7")), 46: (n("C1"), n("G7")), 47: (n("D2"), n("C4")), 48: (n("C2"), n("C7")),
    49: (n("C2"), n("C7")), 52: (n("C3"), n("A5")), 53: (n("C3"), n("A5")), 56: (n("F#3"), n("D6")),
    57: (n("E2"), n("F5")), 58: (n("D1"), n("F4")), 60: (n("B1"), n("F5")), 61: (n("E2"), n("C6")),
    68: (n("Bb3"), n("A6")), 70: (n("Bb1"), n("Eb5")), 71: (n("E3"), n("C7")), 72: (n("D5"), n("C8")),
    73: (n("C4"), n("C7")), 74: (n("C4"), n("D7")), 75: (n("C4"), n("C7")), 77: (n("C4"), n("C7")),
    79: (n("C4"), n("C7")), 105: (n("C3"), n("C6")), 107: (n("C3"), n("C7")), 108: (n("C4"), n("C7")),
    116: (n("C1"), n("C4")), 117: (n("C2"), n("C5")),
}

# the game's main motif (scale degrees of a major key, 0 = tonic; beats). Quote it in the title, the map and the
# fanfares so the score hangs together: sol do re mi(long) re do | la sol mi sol re(long) | sol do re mi sol mi | re mi re do(long)
MOTIF = [
    [(-3, 0.5), (0, 0.5), (1, 0.5), (2, 1.5), (1, 0.5), (0, 0.5)],
    [(-2, 1.0), (-3, 0.5), (-5, 0.5), (-3, 1.0), (-6, 1.0)],
    [(-3, 0.5), (0, 0.5), (1, 0.5), (2, 1.0), (4, 0.5), (2, 1.0)],
    [(1, 1.0), (2, 0.5), (1, 0.5), (0, 2.0)],
]


# ------------------------------------------------------------------------------------------------------- tracks

class Track:
    def __init__(self, song, name, program, stem, channel, vol, pan, reverb, chorus):
        self.song, self.name, self.program, self.stem, self.channel = song, name, program, stem, channel
        self.vol, self.pan, self.reverb, self.chorus = vol, pan, reverb, chorus
        self.notes = []   # (tick, dur_ticks, pitch, vel)
        self.ccs = []     # (tick, cc, value)
        self.bends = []   # (tick, value -8192..8191)

    # --- time
    def t(self, bar, beat=0.0):
        return self.song.tick(bar, beat)

    # --- notes
    def note(self, bar, beat, pitch, dur, vel=80):
        """One note: pitch is a MIDI number or a name ('C4'); dur in beats."""
        if isinstance(pitch, str):
            pitch = n(pitch)
        if pitch is None:
            return self
        st = self.t(bar, beat)
        d = max(1, int(round(dur * TPB)))
        self.notes.append((st, d, int(pitch), int(max(1, min(127, vel)))))
        return self

    def chord(self, bar, beat, pitches, dur, vel=72, strum=0.0):
        """Several notes at once; strum (beats) delays each next note a little (low to high)."""
        for i, p in enumerate(sorted(n(x) if isinstance(x, str) else x for x in pitches)):
            self.note(bar, beat + i * strum, p, max(0.05, dur - i * strum), vel)
        return self

    def seq(self, bar, beat, items, vel=80, legato=0.95):
        """A run of (pitch | None for a rest, beats) items from bar/beat; returns the (bar, beat) after it."""
        pos = bar * self.song.bpb + beat
        for item in items:
            p, d = item[0], item[1]
            v = item[2] if len(item) > 2 else vel
            if p is not None:
                b, bt = divmod(pos, self.song.bpb)
                self.note(int(b), bt, p, d * legato, v)
            pos += d
        b, bt = divmod(pos, self.song.bpb)
        return int(b), bt

    def degs(self, bar, beat, key, items, octave=4, vel=80, legato=0.95, transpose=0):
        """Like seq() but with scale degrees of `key` ((degree | None, beats[, vel]))."""
        conv = [((key.deg(it[0] + transpose, octave) if it[0] is not None else None),) + tuple(it[1:]) for it in items]
        return self.seq(bar, beat, conv, vel, legato)

    def motif(self, bar, key, octave=5, phrases=(0, 1, 2, 3), vel=84, transpose=0, legato=0.92):
        """The main motif (MOTIF) from `bar`, one phrase per bar."""
        for i, ph in enumerate(phrases):
            self.degs(bar + i, 0, key, MOTIF[ph], octave, vel, legato, transpose)
        return self

    def arp(self, bar, beat, pitches, pattern, step, length, vel=70, gate=0.9, accent=0):
        """Arpeggiate `pitches` over `length` beats in notes of `step` beats; pattern = indices into pitches
        (negative = rest). accent adds velocity on each beat."""
        pos, i = 0.0, 0
        while pos < length - 1e-6:
            k = pattern[i % len(pattern)]
            if k is not None and k >= 0:
                v = vel + (accent if abs((beat + pos) % 1.0) < 1e-6 else 0)
                self.note(bar, beat + pos, pitches[k % len(pitches)] + 12 * (k // len(pitches)), step * gate, v)
            pos += step
            i += 1
        return self

    def hits(self, bar, pattern, key, step=0.25, vel=90, accent=None):
        """Drum pattern for one bar: a string, one char per step: 'x' hit, 'X' accent, '.' rest
        ('x...x...' = quarter notes at step 0.25). key = a DR name or a MIDI note."""
        p = DR.get(key, key) if isinstance(key, str) else key
        for i, ch in enumerate(pattern):
            if ch in "xX":
                v = vel + (18 if ch == "X" else 0)
                if accent is not None and i % accent == 0:
                    v += 8
                self.note(bar, i * step, p, step * 0.9, v)
        return self

    # --- controllers
    def cc(self, bar, beat, number, value):
        self.ccs.append((self.t(bar, beat), int(number), int(max(0, min(127, value)))))
        return self

    def swell(self, bar, beat, beats, v0, v1, steps=None):
        """Expression (CC11) ramp from v0 to v1 over `beats`."""
        steps = steps or max(2, int(beats * 8))
        for i in range(steps + 1):
            self.cc(bar, beat + beats * i / steps, 11, v0 + (v1 - v0) * i / steps)
        return self

    def bend(self, bar, beat, value):
        self.bends.append((self.t(bar, beat), int(max(-8192, min(8191, value)))))
        return self


# -------------------------------------------------------------------------------------------------------- songs

class Song:
    def __init__(self, cue, bpm, bars, key=None, stems=("main",), kind="loop", bpb=4, loudness=-20.0,
                 mixes=None, ref=None, desc="", tail=6.0):
        if kind not in ("loop", "sting"):
            raise ValueError("kind must be 'loop' or 'sting'")
        self.cue, self.bpm, self.bars, self.key, self.kind, self.bpb = cue, bpm, bars, key, kind, bpb
        self.stems = list(stems)
        self.loudness = loudness                # dBFS RMS of the reference mix
        self.mixes = mixes or [self.stems]      # stem sets the game plays together (peak check)
        self.ref = ref or self.mixes[0]         # the stem set the loudness is measured on
        self.desc = desc
        self.tail = tail                        # s of release / reverb after a sting's last note
        self.tracks = []
        self.tempo_us = int(round(60_000_000 / bpm))
        self.rng = random.Random(cue)
        self._chan = 0
        for m in self.mixes + [self.ref]:
            for s in m:
                if s not in self.stems:
                    raise ValueError(f"{cue}: unknown stem {s!r} in mixes/ref")

    # --- timeline
    @property
    def beats(self):
        return self.bars * self.bpb

    def tick(self, bar, beat=0.0):
        return int(round((bar * self.bpb + beat) * TPB))

    @property
    def loop_ticks(self):
        return self.beats * TPB

    @property
    def loop_seconds(self):
        return self.beats * self.tempo_us / 1e6

    @property
    def loop_samples(self):
        return int(round(self.loop_seconds * RATE))

    # --- tracks
    def track(self, name, program, stem=None, vol=100, pan=0, reverb=40, chorus=0, channel=None):
        """A new track. program = a GM number (GM[...]) or DRUMS. pan -64..63."""
        stem = stem or self.stems[0]
        if stem not in self.stems:
            raise ValueError(f"{self.cue}: unknown stem {stem!r}")
        if program == DRUMS:
            channel = 9
        elif channel is None:
            channel = self._next_channel()
        t = Track(self, name, program, stem, channel, vol, pan, reverb, chorus)
        self.tracks.append(t)
        return t

    def _next_channel(self):
        used = {t.channel for t in self.tracks}
        for c in range(16):
            if c != 9 and c not in used:
                return c
        raise RuntimeError(f"{self.cue}: more than 15 melodic tracks")

    # --- humanize
    def humanize(self, timing=0.012, velocity=6, tracks=None):
        """Small deterministic timing (beats) / velocity jitter; never moves a note before tick 0 or past the loop."""
        for t in tracks or self.tracks:
            if t.program == DRUMS and timing > 0.006:
                tm = timing * 0.5
            else:
                tm = timing
            out = []
            for (st, d, p, v) in t.notes:
                dt = int(round(self.rng.uniform(-tm, tm) * TPB)) if st > 0 else 0
                st2 = st + dt
                if self.kind == "loop":
                    st2 = max(0, min(self.loop_ticks - 1, st2))
                else:
                    st2 = max(0, st2)
                v2 = int(max(1, min(127, v + self.rng.randint(-velocity, velocity))))
                out.append((st2, d, p, v2))
            t.notes = out

    # --- midi
    def _midi(self, tracks, repeats=1, tail_s=0.0):
        mf = mido.MidiFile(type=1, ticks_per_beat=TPB)
        meta = mido.MidiTrack()
        meta.append(mido.MetaMessage("track_name", name=self.cue, time=0))
        meta.append(mido.MetaMessage("set_tempo", tempo=self.tempo_us, time=0))
        meta.append(mido.MetaMessage("time_signature", numerator=self.bpb, denominator=4, time=0))
        mf.tracks.append(meta)
        span = self.loop_ticks if self.kind == "loop" else max([s + d for t in self.tracks for (s, d, _, _) in t.notes] + [self.loop_ticks])
        tail_ticks = int(round(tail_s * 1e6 / self.tempo_us * TPB))
        end = span * repeats + tail_ticks
        for t in tracks:
            mt = mido.MidiTrack()
            mt.append(mido.MetaMessage("track_name", name=f"{t.stem}:{t.name}", time=0))
            ev = []  # (tick, order, msg)
            ch = t.channel
            if t.program != DRUMS:
                ev.append((0, 0, mido.Message("program_change", channel=ch, program=t.program)))
            for (num, val) in ((7, t.vol), (10, max(0, min(127, 64 + t.pan))), (91, t.reverb), (93, t.chorus),
                               (11, 127), (101, 0), (100, 0), (6, 2), (38, 0), (101, 127), (100, 127)):
                ev.append((0, 1, mido.Message("control_change", channel=ch, control=num, value=int(val))))
            if t.program != DRUMS:
                ev.append((0, 1, mido.Message("pitchwheel", channel=ch, pitch=0)))
            for r in range(repeats):
                off = r * span
                for (tk, num, val) in t.ccs:
                    ev.append((off + tk, 2, mido.Message("control_change", channel=ch, control=num, value=val)))
                for (tk, val) in t.bends:
                    ev.append((off + tk, 2, mido.Message("pitchwheel", channel=ch, pitch=val)))
                for (st, d, p, v) in t.notes:
                    ev.append((off + st, 3, mido.Message("note_on", channel=ch, note=p, velocity=v)))
                    ev.append((off + st + d, 1, mido.Message("note_off", channel=ch, note=p, velocity=0)))
            # note-offs before note-ons at the same tick (re-striking a held pitch)
            ev.sort(key=lambda e: (e[0], e[1]))
            last = 0
            for (tk, _, msg) in ev:
                mt.append(msg.copy(time=tk - last))
                last = tk
            mt.append(mido.MetaMessage("end_of_track", time=max(0, end - last)))
            mf.tracks.append(mt)
        meta.append(mido.MetaMessage("end_of_track", time=end))
        return mf

    # --- render
    def _render_wav(self, mid_path, wav_path):
        cmd = ["fluidsynth", "-ni", "-q", "-g", "0.5", "-r", str(RATE), "-O", "s16", "-T", "wav", "-F", wav_path,
               "-o", "synth.polyphony=512", "-o", "synth.reverb.active=1", "-o", "synth.chorus.active=1"]
        for k, v in list(REVERB.items()) + list(CHORUS.items()):
            cmd += ["-o", f"{k}={v}"]
        cmd += [sf2_path(), mid_path]
        r = subprocess.run(cmd, capture_output=True, text=True)
        if r.returncode != 0 or not os.path.isfile(wav_path):
            raise RuntimeError(f"fluidsynth failed for {mid_path}: {r.stderr[-800:]}")
        with wave.open(wav_path, "rb") as w:
            assert w.getframerate() == RATE and w.getnchannels() == 2 and w.getsampwidth() == 2
            a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0
        return a.reshape(-1, 2)

    def _cut_loop(self, a):
        """Three repetitions rendered: take the middle one, and crossfade its first SEAM_FADE seconds from the
        render's true continuation of its end (the start of the third) into its own start, so the wrap is
        continuous whatever the chorus / reverb state."""
        L = self.loop_samples
        need = 2 * L + int(SEAM_FADE * RATE) + 1
        if len(a) < need:
            a = np.vstack([a, np.zeros((need - len(a), 2))])
        seg = a[L:2 * L].copy()
        N = int(SEAM_FADE * RATE)
        w = (0.5 - 0.5 * np.cos(np.linspace(0, math.pi, N)))[:, None]
        seg[:N] = a[2 * L:2 * L + N] * (1 - w) + a[L:L + N] * w
        return seg

    def _cut_sting(self, a):
        thr = 10 ** (-62 / 20)
        mag = np.abs(a).max(axis=1)
        idx = np.nonzero(mag > thr)[0]
        end = (idx[-1] + int(0.05 * RATE)) if len(idx) else int(0.1 * RATE)
        a = a[:min(len(a), end)].copy()
        f = min(len(a), int(0.03 * RATE))
        a[-f:] *= np.linspace(1, 0, f)[:, None]
        return a

    def build(self, encode=True, report=True):
        """Write the midi, render every stem, normalize, encode and register. Returns a report dict."""
        if not self.tracks:
            raise RuntimeError(f"{self.cue}: no tracks")
        for s in self.stems:
            if not any(t.stem == s for t in self.tracks):
                raise RuntimeError(f"{self.cue}: stem {s!r} has no tracks")
        os.makedirs(MIDI_DIR, exist_ok=True)
        os.makedirs(os.path.join(TMP, self.cue), exist_ok=True)
        os.makedirs(AUDIO_DIR, exist_ok=True)
        self._midi(self.tracks, 1, 0.0 if self.kind == "loop" else 1.0).save(os.path.join(MIDI_DIR, self.cue + ".mid"))

        audio = {}
        for s in self.stems:
            trs = [t for t in self.tracks if t.stem == s]
            mp = os.path.join(TMP, self.cue, f"{s}.mid")
            wp = os.path.join(TMP, self.cue, f"{s}_raw.wav")
            if self.kind == "loop":
                self._midi(trs, 3, 1.0).save(mp)
                audio[s] = self._cut_loop(self._render_wav(mp, wp))
            else:
                self._midi(trs, 1, self.tail).save(mp)
                audio[s] = self._render_wav(mp, wp)
        if self.kind == "sting":
            ln = max(len(x) for x in audio.values())
            audio = {s: np.vstack([x, np.zeros((ln - len(x), 2))]) for s, x in audio.items()}
            mix_all = sum(audio.values())
            cut = self._cut_sting(mix_all)
            audio = {s: x[:len(cut)] for s, x in audio.items()}
            f = min(len(cut), int(0.03 * RATE))
            for x in audio.values():
                x[-f:] *= np.linspace(1, 0, f)[:, None]

        def mix(stems):
            return sum(audio[s] for s in stems)

        def rms(x):
            return float(np.sqrt(np.mean(x ** 2))) if len(x) else 0.0

        ref_rms = rms(mix(self.ref))
        peak = max(float(np.abs(mix(m)).max()) for m in self.mixes)
        if ref_rms <= 1e-9:
            raise RuntimeError(f"{self.cue}: silent render")
        gain = min(10 ** (self.loudness / 20) / ref_rms, PEAK / peak)
        rep = {"cue": self.cue, "kind": self.kind, "bpm": self.bpm, "bars": self.bars, "seconds": round(len(next(iter(audio.values()))) / RATE, 3),
               "loopSamples": self.loop_samples if self.kind == "loop" else len(next(iter(audio.values()))),
               "gain_db": round(20 * math.log10(gain), 2), "peak_limited": gain < 10 ** (self.loudness / 20) / ref_rms - 1e-12,
               "stems": {}}
        for s in self.stems:
            x = audio[s] * gain
            st = {"rms_db": round(20 * math.log10(max(rms(x), 1e-9)), 1), "peak_db": round(20 * math.log10(max(float(np.abs(x).max()), 1e-9)), 1)}
            if self.kind == "loop":
                d = np.abs(np.diff(x, axis=0)).max(axis=1)
                typical = float(np.percentile(d, 99.5)) + 1e-9
                seam = float(np.abs(x[0] - x[-1]).max())
                st["seam_ratio"] = round(seam / typical, 2)   # the jump across the loop point vs the piece's 99.5 % step
            wav_out = os.path.join(TMP, self.cue, f"{s}.wav")
            _write_wav(wav_out, x)
            if encode:
                ogg = os.path.join(AUDIO_DIR, f"{self.cue}_{s}.ogg")
                r = subprocess.run(["oggenc", "-Q", "-q", str(OGG_QUALITY), "-o", ogg, wav_out], capture_output=True, text=True)
                if r.returncode != 0:
                    raise RuntimeError(f"oggenc failed: {r.stderr}")
                st["ogg_kb"] = round(os.path.getsize(ogg) / 1024)
                _ensure_meta(ogg)
            rep["stems"][s] = st
        lens = {len(audio[s]) for s in self.stems}
        if len(lens) != 1:
            raise RuntimeError(f"{self.cue}: stems differ in length {lens}")
        if encode:
            register(self)
        if report:
            text = self.describe() + "\n\n" + "\n".join(self.lint()) + "\n\n" + json.dumps(rep, indent=1, ensure_ascii=False)
            os.makedirs(os.path.join(TMP, "reports"), exist_ok=True)
            with open(os.path.join(TMP, "reports", self.cue + ".txt"), "w", encoding="utf-8") as f:
                f.write(text)
        return rep

    # --- inspection
    def describe(self, max_bars=None):
        """A text piano roll: per track and bar, the notes by beat (for reviewing a cue without listening)."""
        lines = [f"# {self.cue}  {self.kind}  {self.bpm} bpm  {self.bars} bars of {self.bpb}  key {self.key}  "
                 f"{self.loop_seconds:.2f} s  stems {self.stems}", self.desc]
        for t in self.tracks:
            prog = "drums" if t.program == DRUMS else next((k for k, v in GM.items() if v == t.program), t.program)
            ps = [p for (_, _, p, _) in t.notes]
            rng = f"{name_of(min(ps))}..{name_of(max(ps))}" if ps else "-"
            lines.append(f"\n## [{t.stem}] {t.name}  ({prog}, ch {t.channel + 1}, vol {t.vol}, pan {t.pan}, rev {t.reverb})  "
                         f"{len(t.notes)} notes  range {rng}")
            by_bar = {}
            for (st, d, p, v) in sorted(t.notes):
                b = st // (self.bpb * TPB)
                by_bar.setdefault(b, []).append((st, d, p, v))
            for b in range(self.bars if max_bars is None else min(self.bars, max_bars)):
                ev = by_bar.get(b, [])
                if not ev:
                    lines.append(f"  {b:3d} | -")
                    continue
                cells = []
                for (st, d, p, v) in ev:
                    beat = (st - b * self.bpb * TPB) / TPB
                    nm = (next((k for k, x in DR.items() if x == p), str(p)) if t.program == DRUMS else name_of(p))
                    cells.append(f"{beat:g}:{nm}/{d / TPB:g}")
                lines.append(f"  {b:3d} | " + " ".join(cells))
        return "\n".join(lines)

    def lint(self):
        """Automatic checks: out-of-key notes, ranges, clashes on strong beats, empty bars, loop wrap leaps."""
        out = [f"# lint {self.cue}"]
        bar_t = self.bpb * TPB
        if self.kind == "loop":
            for t in self.tracks:
                late = [x for x in t.notes if x[0] >= self.loop_ticks]
                if late:
                    out.append(f"ERROR [{t.name}] {len(late)} notes start at/after the loop end")
        for t in self.tracks:
            if t.program == DRUMS:
                continue
            lo, hi = RANGES.get(t.program, RANGES["default"])
            bad = [p for (_, _, p, _) in t.notes if p < lo or p > hi]
            if bad:
                out.append(f"WARN [{t.name}] {len(bad)} notes outside {name_of(lo)}..{name_of(hi)} (e.g. {name_of(bad[0])})")
            if self.key is not None and self.key.scale_name in ("major", "minor", "dorian", "mixolydian", "lydian", "phrygian", "harmonic_minor"):
                ok = sum(1 for (_, _, p, _) in t.notes if self.key.in_key(p))
                if t.notes and ok < len(t.notes):
                    chrom = sorted({name_of(p)[:-1] for (_, _, p, _) in t.notes if not self.key.in_key(p)})
                    out.append(f"INFO [{t.name}] {len(t.notes) - ok}/{len(t.notes)} notes outside {self.key}: {chrom}")
        # sounding pitches on each beat (non-drum), minor-2nd / major-7th / tritone clashes between different tracks
        sounding = {}
        for t in self.tracks:
            if t.program == DRUMS:
                continue
            for (st, d, p, v) in t.notes:
                # a note counts on a beat when it starts within an eighth after it or is still held an eighth past it
                for k in range(st // TPB, (st + d - 1) // TPB + 2):
                    if st <= k * TPB + TPB // 8 and st + d > k * TPB + TPB // 8:
                        sounding.setdefault(k, []).append((p, t.name, t.stem))
        clashes = 0
        examples = []
        for k, ps in sorted(sounding.items()):
            for i in range(len(ps)):
                for j in range(i + 1, len(ps)):
                    a, b = ps[i], ps[j]
                    if a[1] == b[1]:
                        continue
                    iv = abs(a[0] - b[0]) % 12
                    if iv in (1, 11) and abs(a[0] - b[0]) < 24:
                        clashes += 1
                        if len(examples) < 6:
                            examples.append(f"bar {k // self.bpb} beat {k % self.bpb}: {a[1]} {name_of(a[0])} vs {b[1]} {name_of(b[0])}")
        if clashes:
            out.append(f"WARN {clashes} minor-2nd/major-7th clashes on beats (between tracks): " + "; ".join(examples))
        for s in self.stems:
            trs = [t for t in self.tracks if t.stem == s]
            filled = {st // bar_t for t in trs for (st, _, _, _) in t.notes}
            empty = [b for b in range(self.bars) if b not in filled]
            if empty:
                out.append(f"INFO [stem {s}] empty bars: {empty}")
        if self.kind == "loop":
            for t in self.tracks:
                if t.program == DRUMS or not t.notes:
                    continue
                ns = sorted(t.notes)
                first, last = ns[0], ns[-1]
                if first[0] < TPB and last[0] > self.loop_ticks - 2 * TPB and abs(first[2] - last[2]) > 12:
                    out.append(f"INFO [{t.name}] leap {name_of(last[2])} -> {name_of(first[2])} across the loop point")
        # repetition: identical bars across the whole cue
        for s in self.stems:
            sig = {}
            for b in range(self.bars):
                key = tuple(sorted((t.name, st - b * bar_t, d, p) for t in self.tracks if t.stem == s
                                   for (st, d, p, _) in t.notes if b * bar_t <= st < (b + 1) * bar_t))
                if key:
                    sig.setdefault(key, []).append(b)
            uniq = len(sig)
            total = sum(len(v) for v in sig.values())
            if total and uniq / total < 0.35:
                out.append(f"INFO [stem {s}] only {uniq} distinct bars out of {total} (very repetitive)")
        if len(out) == 1:
            out.append("ok")
        return out


# ----------------------------------------------------------------------------------------------------- outputs

def _write_wav(path, x):
    y = np.clip(x, -1.0, 1.0)
    pcm = (y * 32767.0).round().astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def _ensure_meta(asset_path):
    """A minimal Unity .meta (a fresh guid) next to a new asset, as the rest of the repository has; Unity fills in
    the importer settings (see Assets/Editor/MusicImporter.cs)."""
    meta = asset_path + ".meta"
    if os.path.exists(meta):
        return
    import uuid
    with open(meta, "w", encoding="utf-8") as f:
        f.write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n")


def register(song):
    """Add / replace the cue in Assets/Resources/Data/music.json (sorted by id). Several builds may run at once:
    the read-modify-write holds an exclusive lock on Tools/Music/_tmp/manifest.lock."""
    import fcntl
    os.makedirs(TMP, exist_ok=True)
    with open(os.path.join(TMP, "manifest.lock"), "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            _register(song)
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


def _register(song):
    data = {"rate": RATE, "cues": []}
    if os.path.isfile(MANIFEST):
        with open(MANIFEST, encoding="utf-8") as f:
            data = json.load(f)
    cues = [c for c in data.get("cues", []) if c.get("id") != song.cue]
    loop_samples = song.loop_samples if song.kind == "loop" else 0
    cues.append({
        "id": song.cue, "kind": song.kind, "bpm": song.bpm, "beatsPerBar": song.bpb, "bars": song.bars,
        "loopSamples": loop_samples, "desc": song.desc,
        "stems": [{"name": s, "clip": f"{AUDIO_RES}/{song.cue}_{s}"} for s in song.stems],
    })
    cues.sort(key=lambda c: c["id"])
    data = {"rate": RATE, "cues": cues}
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    new = not os.path.exists(MANIFEST)
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")
    if new:
        _ensure_meta(MANIFEST)


def tools_ok():
    """True when fluidsynth, oggenc and the SoundFont are present."""
    try:
        sf2_path()
    except FileNotFoundError:
        return False
    return shutil.which("fluidsynth") is not None and shutil.which("oggenc") is not None
