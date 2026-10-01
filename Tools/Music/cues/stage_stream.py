"""
stage_stream — 산골 계곡 (mountain stream). E minor (aeolian, pentatonic colour), 96 bpm, 32 bars = 80 s.

Form (the same in every stem, so day / night / fight line up bar for bar):
  A    0-7    Em  C   Bm7  Em  | G   Am7 C   D      theme on shakuhachi (i bVI v7 i | III iv7 bVI bVII)
  A'   8-15   Em  C   D    Bm7 | C   Am7 Bm7 Em     variation on pan flute, closes on the tonic
  B   16-23   G   D/F# Em  Am7 | C   D   Am7 Bm7    relative-major colour, climax E6 in bar 20, v7 back to A''
  A'' 24-31   Em  C   D    Em  | Am7 G   C   D      recap; bVI-bVII-(i) turns the loop round to bar 0

Harmony trick: each chord has a pentatonic "safe set" (chord tones + colours):
  Em / G -> E G A B D,  C / Am7 -> C D E G A,  D / Bm7 -> D E F# A B.
Every note that sounds on a beat is taken from the bar's set, so no stem rubs against another (and the fight
layer fits under both bases). E, A and D are in every set. Off-beat passing tones stay short.

Registers: day melody E5-E6 over koto / guitar (<= D5 / F#4); at night the low flute (E4-B4) keeps the top, so the
night strings are voiced at or under D4 and the night koto ducks under D4 in the flute's bars.

Pitched drums: in FluidR3 the GM taiko and melodic tom follow the key at half scale (about 50 cents a key; measured:
taiko A2 sounds ~A1, D3 ~B1, E3 ~C2; melodic tom A4 ~A3, E4 ~G3-46c, D4 ~F#3-40c). So the keys below are picked by the
pitch they actually sound, and on-beat strokes stay on E / A / D keys so the lint (which reads MIDI keys) stays clean.
"""

from fk_music import DRUMS, GM, Key, Song, n

KEY = Key("E", "minor")
BPB = 4


def pcs(names):
    return {n(x + "4") % 12 for x in names.split()}


class Chord:
    def __init__(self, root, bass, voice, tones, colours):
        self.root = n(root + "4") % 12
        self.bass = n(bass)                       # day guitar thumb / night low strings (E2..D3)
        self.voice = [n(x) for x in voice]        # mid voicing, chord tones only (G3..F#4)
        self.tones = pcs(tones)
        self.safe = self.tones | pcs(colours)     # the pentatonic set: clash-free against everything

    def root_near(self, p):
        """The pitch of the root nearest p (the lower one on a tie)."""
        return min((q for q in range(p - 6, p + 7) if q % 12 == self.root), key=lambda q: (abs(q - p), q))

    def ladder(self, near, count=9):
        """The safe set upward from the root nearest `near` (index 0 = root)."""
        out, p = [], self.root_near(near)
        while len(out) < count:
            if p % 12 in self.safe:
                out.append(p)
            p += 1
        return out

    def fifth_bass(self):
        """The chord's fifth in the bass register, the one nearest the bass note."""
        f = (self.root + 7) % 12
        return min((q for q in range(n("E2"), n("E3") + 1) if q % 12 == f), key=lambda q: abs(q - self.bass))


CHORDS = {
    "Em":   Chord("E", "E2",  ["G3", "B3", "E4"],  "E G B",    "A D"),
    "C":    Chord("C", "C3",  ["G3", "C4", "E4"],  "C E G",    "A D"),
    "D":    Chord("D", "D3",  ["A3", "D4", "F#4"], "D F# A",   "E B"),
    "G":    Chord("G", "G2",  ["G3", "B3", "D4"],  "G B D",    "A E"),
    "Am7":  Chord("A", "A2",  ["G3", "C4", "E4"],  "A C E G",  "D"),
    "Bm7":  Chord("B", "B2",  ["A3", "D4", "F#4"], "B D F# A", "E"),
    "D/F#": Chord("D", "F#2", ["A3", "D4", "F#4"], "D F# A",   "E B"),
}

PROG = ["Em", "C", "Bm7", "Em",  "G", "Am7", "C", "D",
        "Em", "C", "D", "Bm7",   "C", "Am7", "Bm7", "Em",
        "G", "D/F#", "Em", "Am7", "C", "D", "Am7", "Bm7",
        "Em", "C", "D", "Em",    "Am7", "G", "C", "D"]

# the tune: per bar, (scale degree of E minor | None, beats); degree 0 = E5 by day, E4 at night.
# beats 1 and 3 are chord tones, the rest comes from the bar's safe set; shakuhachi and pan flute share it (LEAD).
MELODY = {
    0:  [(0, 1.5), (2, 1), (3, .5), (4, 1)],          # E . G— A B
    1:  [(2, 1.5), (3, .5), (0, 2)],                  # G . A E—
    2:  [(4, 1), (3, .5), (0, .5), (-1, 2)],          # B A E D—   (over Bm7: all chord tones on the beats)
    4:  [(4, 1.5), (6, .5), (4, 1), (3, 1)],          # B . D B A  (answer, higher)
    5:  [(3, 1.5), (2, .5), (0, 2)],                  # A . G E—
    6:  [(-2, 1), (0, 1), (2, 2)],                    # C E G—     open ending (half cadence via D)
    8:  [(0, 1), (2, .5), (3, .5), (4, 2)],           # A': same contour, quicker rise
    9:  [(5, 1.5), (3, .5), (2, 2)],                  # C . A G—   (A, not B: no maj7 rub on the guitar's C chick)
    10: [(3, 1.5), (4, .5), (3, 1), (0, 1)],
    12: [(0, 1.5), (-1, .5), (0, 1), (2, 1)],
    13: [(3, 2), (2, 1), (0, 1)],
    14: [(-1, 1.5), (0, .5), (1, 1), (-1, 1)],
    15: [(0, 3), (None, 1)],                          # cadence on the tonic
    17: [(-1, 1), (0, 1), (3, 2)],                    # B: rising from D
    18: [(4, 1.5), (3, .5), (4, 1), (6, 1)],
    19: [(3, 1.5), (2, .5), (0, 1), (-1, 1)],
    20: [(2, 1), (3, 1), (7, 2)],                     # climax E6
    21: [(6, 1.5), (7, .5), (6, 1), (3, 1)],
    22: [(2, 2), (3, 1), (0, 1)],
    24: [(0, 1.5), (2, 1), (3, .5), (4, 1)],          # A'': the opening phrase again
    25: [(2, 1), (3, 1), (0, 2)],
    26: [(-1, 3), (None, 1)],                         # D——— (sags at the end)
    28: [(0, 1), (2, 1), (3, 2)],                     # pan flute's last answer
    29: [(4, 1.5), (3, .5), (2, 2)],
}
# melody rests (by day) in bars 3 7 11 16 23 27 30 31 = 8/32; the koto answers there, except in the two BREATH
# bars where only a soft open dyad rings (real air for the stream / wind ambience)
BREATH = {11, 27}
LEAD = {b: ("pan_flute" if 8 <= b <= 15 or b >= 28 else "shakuhachi") for b in MELODY}
NIGHT_BARS = {0, 1, 2, 4, 5, 6, 12, 13, 14, 15, 17, 18, 19, 20, 21, 22, 24, 25, 26}   # 13/32 rest at night

SLIDES = {(0, 0), (4, 0), (17, 0), (20, 2), (24, 0)}  # shakuhachi scoops (bar, beat)
FALLS = {(2, 2), (6, 2), (26, 0)}                     # phrase-final notes that sag at the release

# koto answers in the bars the melody rests (all notes from the bar's safe set)
FILLS = {
    3:  [("E5", .5), ("D5", .5), ("B4", 1), ("A4", .5), ("B4", .5), ("E4", 1)],
    7:  [("D5", .5), ("E5", .5), ("F#5", 1), ("E5", .5), ("D5", .5), ("B4", 1)],
    11: [("F#4", 1), ("A4", .5), ("B4", .5), ("D5", 1), ("B4", 1)],
    16: [("G4", .5), ("A4", .5), ("B4", .5), ("D5", .5), ("E5", 1), ("D5", 1)],
    23: [("D5", .5), ("B4", .5), ("A4", 1), ("F#4", 1), ("A4", .5), ("B4", .5)],
    27: [("B4", 1), ("D5", .5), ("E5", .5), ("D5", 1), ("B4", 1)],
    30: [("E5", 1.5), ("D5", .5), ("C5", 1), ("A4", 1)],
    31: [("A4", .5), ("B4", .5), ("D5", 1), ("E5", 1), ("B4", .5), ("D5", .5)],   # steps up into bar 0
}
NIGHT_FILLS = (3, 7, 11, 16, 23, 27, 31)


# ------------------------------------------------------------------------------------------------ ornaments

def slide_in(t, bar, beat, semis=1.0, beats=0.25, steps=6):
    """Scoop up into a note from `semis` below (pitch wheel range is +-2 semitones)."""
    v0 = -int(4096 * semis)
    t.bend(bar, beat - 0.02 if (bar, beat) != (0, 0) else 0, v0)
    for i in range(1, steps + 1):
        t.bend(bar, beat + beats * i / steps, int(v0 * (1 - i / steps)))


def fall(t, bar, beat, dur, semis=0.6, beats=0.4, steps=6):
    """Let a long note sag at its release (a Korean-style 꺾는 소리), then reset the wheel."""
    end = beat + dur
    for i in range(steps + 1):
        t.bend(bar, end - beats + beats * i / steps, -int(4096 * semis * i / steps))
    t.bend(bar, end + 0.03, 0)


def vibrato(t, bar, beat, dur, depth=56, delay=0.6):
    """Delayed vibrato (mod wheel) on a long note."""
    if dur <= delay + 0.3:
        return
    k = 6
    for i in range(k + 1):
        t.cc(bar, beat + delay + (dur - delay - 0.1) * i / k, 1, depth * i / k)
    t.cc(bar, beat + dur - 0.02, 1, 0)


def night_line(items):
    """The night flute's simpler line: off-beat half-beat notes are tied into the note before."""
    out, pos = [], 0.0
    for deg, d in items:
        if out and d == 0.5 and pos % 1 == 0.5 and out[-1][0] is not None:
            out[-1] = (out[-1][0], out[-1][1] + d)
        else:
            out.append((deg, d))
        pos += d
    return out


def play_line(t, bar, items, octave, vel, legato=0.93, ornaments=True, slides=(), falls=()):
    pos = 0.0
    for deg, d in items:
        if deg is not None:
            p = KEY.deg(deg, octave)
            v = vel + 2 * deg + (4 if pos % 2 == 0 else 0)   # higher = a little louder, downbeats lean
            t.note(bar, pos, p, d * legato, v)
            if ornaments and (bar, pos) in slides:
                slide_in(t, bar, pos)
            if ornaments and (bar, pos) in falls:
                fall(t, bar, pos, d * legato)
            if d >= 2:
                vibrato(t, bar, pos, d * legato)
        pos += d


def held(t, chords, pitches_of, vel, break_every=8):
    """Sustain a pad: each pitch of pitches_of(chord) is held over the bars it stays in (common tones tie),
    re-struck at section starts."""
    active = {}
    for bar in range(len(chords) + 1):
        cur = set(pitches_of(chords[bar])) if bar < len(chords) else set()
        for p in sorted(active):
            if p not in cur or bar % break_every == 0:
                b0 = active.pop(p)
                t.note(b0, 0, p, (bar - b0) * BPB - 0.1, vel)
        for p in cur:
            active.setdefault(p, bar)


# --------------------------------------------------------------------------------------------------- day

def day(s, chords):
    shaku = s.track("shakuhachi", GM["shakuhachi"], stem="day", vol=95, pan=8, reverb=72)
    panfl = s.track("pan_flute", GM["pan_flute"], stem="day", vol=104, pan=-10, reverb=72)
    koto = s.track("koto_day", GM["koto"], stem="day", vol=111, pan=-30, reverb=55)
    gtr = s.track("guitar", GM["steel_guitar"], stem="day", vol=99, pan=24, reverb=42)

    for bar, items in MELODY.items():
        lead = shaku if LEAD[bar] == "shakuhachi" else panfl
        play_line(lead, bar, items, 5, 78 if lead is shaku else 74, slides=SLIDES, falls=FALLS,
                  ornaments=lead is shaku)

    for bar, c in enumerate(chords):
        sec = bar // 8
        # guitar: thumb bass on 1 and 3 under a soft strum; the B section is fingerpicked eighths
        alt = c.fifth_bass()
        v = c.voice
        if sec == 2:
            gtr.note(bar, 0, c.bass, 1.9, 64).note(bar, 2, alt, 1.9, 58)
            for i, p in enumerate([v[0], v[2], v[1], None, v[0], v[2], v[1]]):
                if p is not None:   # let each pluck ring, but not over the next bar line
                    gtr.note(bar, 0.5 + 0.5 * i, p, min(0.9, 3.45 - 0.5 * i), 46 + (4 if i % 2 else 0))
        else:
            gtr.note(bar, 0, c.bass, 1.9, 64).chord(bar, 0, v, 3.6, 50, strum=0.04)
            gtr.note(bar, 2, alt, 1.8, 56)
            if sec == 1:      # A': a light "chick" on the off-beats
                gtr.chord(bar, 1.5, v[1:], 0.9, 36).chord(bar, 3, v[1:], 0.9, 38)
            elif bar % 2 == 1:
                gtr.chord(bar, 3, v[1:], 0.9, 40)
            if bar == len(chords) - 1:   # F#2 walks down into the loop's E2
                gtr.note(bar, 3.5, n("F#2"), 0.45, 54)

        # koto: answers in the melody's rests, otherwise a gayageum-like figure on the bar's pentatonic set
        k = c.ladder(n("C4"))
        if bar in BREATH:     # a breath: root + fifth-ish dyad left to ring, one soft pluck after it
            koto.chord(bar, 0, [k[0], k[3]], 3.0, 50, strum=0.06).note(bar, 2.5, k[1], 1.4, 42)
            continue
        if bar in FILLS:
            koto.seq(bar, 0, [(n(p), d) for p, d in FILLS[bar]], vel=72, legato=1.0)
            continue
        if sec == 1:          # A': a rippling up-and-down line in eighths
            for i, j in enumerate([0, 2, 3, 4, 5, 4, 3, 2]):
                koto.note(bar, 0.5 * i, k[j], 0.6, 56 + (6 if i % 2 == 0 else 0))
        elif sec == 2:        # B: plucked dyads, a syncopated reply to the guitar's eighths
            koto.chord(bar, 0, [k[0], k[2]], 1.4, 64).chord(bar, 1.5, [k[3]], 0.9, 58)
            koto.chord(bar, 2.5, [k[1], k[3]], 1.4, 60)
        elif bar % 2 == 0:
            for (bt, j, d) in [(0, 0, 1), (0.5, 2, 1), (1, 4, 1.5), (2.5, 3, 0.5), (3, 2, 1)]:
                koto.note(bar, bt, k[j], d, 66 if bt % 1 == 0 else 58)
        else:
            for (bt, j, d) in [(0, 0, 0.5), (0.5, 1, 0.5), (1, 2, 1), (2, 4, 1), (3, 3, 1)]:
                koto.note(bar, bt, k[j], d, 64 if bt % 1 == 0 else 56)


# ------------------------------------------------------------------------------------------------- night

def night(s, chords):
    flute = s.track("low_flute", GM["flute"], stem="night", vol=106, pan=4, reverb=78)
    koto = s.track("koto_night", GM["koto"], stem="night", vol=116, pan=-26, reverb=68)
    strings = s.track("strings", GM["slow_strings"], stem="night", vol=88, pan=16, reverb=70)
    low = s.track("strings_lo", GM["slow_strings"], stem="night", vol=81, pan=-6, reverb=60)

    for bar, items in MELODY.items():
        if bar in NIGHT_BARS:
            play_line(flute, bar, night_line(items), 4, 70, legato=0.95, ornaments=False)

    # darker, lower voicing than the guitar's: anything above D4 drops an octave (E3..D4), under the flute
    held(strings, chords, lambda c: sorted(p - 12 if p > n("D4") else p for p in c.voice), 50)
    held(low, chords, lambda c: [c.bass], 60)
    # the strings breathe a little louder through B
    strings.cc(0, 0, 11, 96)
    strings.swell(16, 0, 16, 96, 120)
    strings.swell(21, 0, 12, 120, 96)

    for bar, c in enumerate(chords):
        if bar in NIGHT_FILLS:
            koto.seq(bar, 0, [(n(p) - 12, d) for p, d in FILLS[bar]], vel=60, legato=1.0)
            continue
        k = c.ladder(n("A3"))
        if bar in NIGHT_BARS:   # under the flute: notes above D4 drop an octave (they stay in the safe set)
            k = [p - 12 if p > n("D4") else p for p in k]
        if bar % 2 == 0:
            for (bt, j, d) in [(0, 0, 2), (1.5, 2, 1.5), (3, 4, 1)]:
                koto.note(bar, bt, k[j], d, 58 if bt % 1 == 0 else 52)
        else:
            koto.note(bar, 0, k[0], 2, 56).note(bar, 2, k[3], 2, 52)


# ------------------------------------------------------------------------------------------------- fight

TAIKO = {  # 16 sixteenths: X = deep (key A2, sounds ~A1), x = higher (key D3, sounds ~B1: the fifth of E, no rub on Em / G / Bm7)
    "A": "X..x..x.X...x...",
    "B": "X..x..x.X..x..x.",
}
BASS = {   # 8 eighths: l = low bass note (the base's bass pitch class), h = octave, f = the chord's fifth above it
    "A": "llhlllhl",
    "A2": "llhllhlh",
    "B": "llhlflhl",
}
KOTO16 = [[0, 3, 2, 3], [1, 3, 2, 3], [0, 3, 2, 3], [1, 4, 3, 2]]
FILL_BARS = {7: 3, 15: 3, 23: 3, 31: 2}   # bar -> beat where the tom fill starts
# melodic tom keys by sounding pitch (see the docstring): A4 ~A3, B3 ~E3, G3 ~D3, C3 ~B2. The fill falls through the
# E minor pentatonic A3 E3 D3 B2; every on-beat stroke is the A4 key (A is safe on every chord)
TOM_A, TOM_RUN = n("A4"), [n(x) for x in ("A4", "B3", "G3", "C3")]


def fight(s, chords):
    taiko = s.track("taiko", GM["taiko"], stem="fight", vol=72, pan=0, reverb=45)
    toms = s.track("toms", GM["melodic_tom"], stem="fight", vol=90, pan=-14, reverb=40)
    kit = s.track("kit", DRUMS, stem="fight", vol=110, pan=0, reverb=30)
    bass = s.track("bass", GM["finger_bass"], stem="fight", vol=76, pan=0, reverb=10)
    koto = s.track("koto_fight", GM["koto"], stem="fight", vol=98, pan=34, reverb=35)
    stabs = s.track("stabs", GM["strings"], stem="fight", vol=93, pan=-26, reverb=45)

    deep, high = n("A2"), n("D3")
    for bar, c in enumerate(chords):
        sec = bar // 8
        fill_at = FILL_BARS.get(bar, BPB)

        # taiko: 3+3+2 drive; the second half of the bar gets the same push in B and A''
        pat = TAIKO["B" if sec >= 2 else "A"]
        for i, ch in enumerate(pat):
            if ch != "." and i * 0.25 < fill_at:
                taiko.note(bar, i * 0.25, deep if ch == "X" else high, 0.24, 92 if ch == "X" else 76)

        # toms: backbeat in B (sounds A3), falling pentatonic fills at the ends of the 8-bar sections
        if sec == 2 and bar not in FILL_BARS:
            toms.note(bar, 1, TOM_A, 0.24, 76).note(bar, 3, TOM_A, 0.24, 80)
        if bar in FILL_BARS:
            steps = int((BPB - fill_at) * 4)
            for i in range(steps):
                toms.note(bar, fill_at + 0.25 * i, TOM_RUN[i % 4], 0.24, 64 + 3 * i)

        # kit: side stick on 2 and 4 (A, A', A''), shaker drive from A', kick under the taiko in B / A''
        if sec != 2:
            kit.hits(bar, "....x.......x..." if bar not in FILL_BARS else "....x...........", "stick", vel=58)
        if sec >= 1:
            kit.hits(bar, "xxxxxxxxxxxxxxxx" if sec == 2 else "x.x.x.x.x.x.x.x.", "shaker", vel=34, accent=4)
        if sec >= 2:
            kit.hits(bar, "x.......x.......", "kick", vel=70)
        if bar in (0, 16):
            kit.hits(bar, "x", "china", vel=60)

        # bass: eighth-note drive on the bases' bass note (D/F# keeps its F#: G-F#-E steps down in B), an octave
        # (or two) under the day guitar / night low strings
        lo = min(q for q in range(n("E1"), n("E2")) if q % 12 == c.bass % 12)
        fifth = next(q for q in range(lo, lo + 12) if q % 12 == (c.root + 7) % 12)
        pat = BASS["B" if sec == 2 else ("A2" if sec == 1 else "A")]
        for i, ch in enumerate(pat):
            p = {"l": lo, "h": lo + 12, "f": fifth}[ch]
            bass.note(bar, 0.5 * i, p, 0.42, 80 if i % 2 == 0 else 70)

        # koto ostinato on the bar's safe set: eighths in A and B, sixteenths in A' and A''
        k = c.ladder(n("D4"))     # roots A3..G4: the ostinato stays under the tune
        if sec in (1, 3):
            for beat, cell in enumerate(KOTO16):
                for j, idx in enumerate(cell):
                    koto.note(bar, beat + 0.25 * j, k[idx], 0.22, 70 if j == 0 else 58)
        else:
            cell = [0, 3, 2, 3, 1, 3, 2, 4] if sec == 0 else [0, 2, 4, 2, 3, 1, 2, 4]
            for i, idx in enumerate(cell):
                koto.note(bar, 0.5 * i, k[idx], 0.45, 70 if i % 2 == 0 else 60)

        # string stabs in the same 3+3+2 as the taiko (B, and the build-up of A'')
        if sec == 2 or bar in (28, 29, 30):
            v = c.voice + [c.voice[0] + 12]
            for bt in (0, 1.5, 3):
                stabs.chord(bar, bt, v, 0.3, 76 if bt == 0 else 66)


def songs():
    s = Song("stage_stream", bpm=96, bars=32, key=KEY, stems=["day", "night", "fight"],
             mixes=[["day", "fight"], ["night", "fight"]], ref=["day"], loudness=-21,
             desc="Mountain stream: E minor pentatonic, shakuhachi / pan flute over koto and guitar; "
                  "night: low flute, koto, slow strings; fight: taiko, toms, driving bass, koto ostinato")
    chords = [CHORDS[c] for c in PROG]
    day(s, chords)
    night(s, chords)
    fight(s, chords)
    s.humanize(timing=0.01, velocity=5)
    return [s]
