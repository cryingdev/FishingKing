"""
legend_golden_carp — 황금잉어 (lake legend) fight loop: auspicious and festive. 132 bpm, 32 bars, E major with a
pentatonic (E F# G# B C#) melody.

The legend's own motif is the "leaping carp": an eighth-note pickup, a fourth up, then an octave above the pickup,
held (B4 E5 B5—). It opens every phrase of A, leaps higher each time, and returns as a rising sequence in the
turnaround (the carp climbing the Dragon Gate).

Form (one stem "main"):
  A1  0-7   the theme on shakuhachi; koto 8th ostinato, pizzicato bass, soft string pad; taiko + woodblock (bangzi);
            gong (china cymbal + tubular bells) on phrase starts; pan flute echoes the leap in the held bars
  A2  8-15  the theme again leaping higher, strings double it an octave down, "dong-dong-qiang" taiko/cymbal;
            vi-IV-ii-V-I cadence in E with a tom fill into B
  B1 16-23  contrast: broad strings theme in octaves (horns on the low octave) over IV-V-iii-vi, half-time drums;
            shakuhachi / pan flute answer in the held bars
  B2 24-27  climax: strings + shakuhachi in unison take the leap up to G#6, horns hold the harmony, driving 8th
            taiko, backbeat snare
  T  28-31  turnaround: the leap motif in a rising sequence over F#m G#m A B, horns swell, taiko/snare crescendo,
            tom fill into the gong at bar 0
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

BPM, BARS = 132, 32

# one chord per bar, or (symbol, beats) pairs
PROG = [
    [("E", 4)], [("E", 4)], [("A", 4)], [("B", 4)],                      # A1: I I IV V
    [("C#m", 4)], [("G#m", 4)], [("A", 4)], [("B", 4)],                  #     vi iii IV V (half cadence)
    [("E", 4)], [("E/G#", 4)], [("A", 4)], [("B", 4)],                   # A2: I I6 IV V
    [("C#m", 4)], [("A", 4)], [("F#m", 2), ("B", 2)], [("E", 4)],        #     vi IV ii V | I
    [("A", 4)], [("B", 4)], [("G#m", 4)], [("C#m", 4)],                  # B1: IV V iii vi
    [("F#m", 4)], [("B", 4)], [("G#m", 4)], [("C#m", 4)],                #     ii V iii vi
    [("A", 4)], [("B", 4)], [("E/G#", 4)], [("C#m", 4)],                 # B2: IV V I6 vi (climax)
    [("F#m", 4)], [("G#m", 4)], [("A", 4)], [("B", 4)],                  # T:  rising bass F# G# A B -> E
]

# per chord: pizzicato bass root, pad / horn voicing (mid register), koto voicing (around E4)
CH = {
    "E":    ("E2",  ["G#3", "B3", "E4"],  ["E4", "G#4", "B4", "E5"]),
    "E/G#": ("G#2", ["G#3", "B3", "E4"],  ["E4", "G#4", "B4", "E5"]),
    "A":    ("A2",  ["A3", "C#4", "E4"],  ["E4", "A4", "C#5", "E5"]),
    "B":    ("B2",  ["F#3", "B3", "D#4"], ["D#4", "F#4", "B4", "D#5"]),
    "C#m":  ("C#3", ["G#3", "C#4", "E4"], ["E4", "G#4", "C#5", "E5"]),
    "G#m":  ("G#2", ["G#3", "B3", "D#4"], ["D#4", "G#4", "B4", "D#5"]),
    "F#m":  ("F#2", ["F#3", "A3", "C#4"], ["F#4", "A4", "C#5", "F#5"]),
}

# the pizzicato's upper / lower tones for an inverted chord (the root-position default is octave + fifth)
BASS_TONES = {"E/G#": ("E3", "B2")}

# melody lines, one string per bar: "pitch/beats" tokens, r = rest
LEAD = {  # shakuhachi
    0: "B4/.5 E5/.5 B5/1.5 C#6/.25 B5/.25 G#5/.5 F#5/.5",     # the leap
    1: "E5/1 F#5/.5 G#5/.5 B5/1 G#5/1",
    2: "C#6/1.5 B5/.5 C#6/.5 E6/.5 C#6/.5 B5/.5",
    3: "B5/1.5 G#5/.5 F#5/2",
    4: "G#4/.5 C#5/.5 G#5/1.5 B5/.25 G#5/.25 E5/.5 C#5/.5",   # the leap a third lower
    5: "B4/1 C#5/.5 D#5/.5 G#5/1 B5/1",
    6: "C#6/1 E6/1.5 C#6/.5 B5/.5 C#6/.5",
    7: "B5/.5 C#6/.5 B5/.5 G#5/.5 F#5/2",                       # half cadence
    8: "E5/.5 B5/.5 E6/1.5 F#6/.25 E6/.25 B5/.5 G#5/.5",       # the leap, higher
    9: "B5/1.5 C#6/.5 B5/1 G#5/.5 F#5/.5",
    10: "A5/.5 C#6/.5 E6/1 C#6/.5 B5/.5 C#6/.5 E6/.5",
    11: "D#6/1.5 C#6/.5 B5/1 F#5/1",
    12: "E5/.5 G#5/.5 C#6/1 E6/.5 C#6/.5 G#5/.5 B5/.5",
    13: "C#6/1 E6/1.5 F#6/.5 E6/1",                             # top of A
    14: "C#6/1 A5/.5 F#5/.5 B5/1 A5/1",                          # A (7th of B7) -> G#
    15: "G#5/1.5 F#5/.25 G#5/.25 E5/2",                         # cadence on the tonic
    19: "r/1 G#5/.5 B5/.5 C#6/.5 E6/.5 C#6/.5 B5/.5",           # answer to the strings
}
STRINGS_B = {  # strings theme (upper voice, doubled an octave below); 24-27 also on shakuhachi
    16: "C#5/1.5 B4/.5 C#5/1 E5/1",
    17: "F#5/3 G#5/.5 F#5/.5",
    18: "G#5/1.5 F#5/.5 D#5/1 B4/1",
    19: "C#5/3 r/1",
    20: "A4/1 C#5/1 F#5/1.5 E5/.5",
    21: "D#5/1.5 F#5/.5 B5/2",
    22: "D#6/1 B5/1 G#5/1.5 F#5/.5",
    23: "E5/3 r/1",
    24: "E5/.5 A5/.5 E6/2 C#6/.5 B5/.5",                         # climax: the leap in the strings
    25: "D#6/1.5 C#6/.5 B5/1 F#5/1",
    26: "G#5/.5 B5/.5 G#6/2 F#6/.5 E6/.5",                       # the peak
    27: "C#6/1.5 B5/.5 G#5/1 E5/1",
    28: "C#5/.5 F#5/.5 C#6/3",                                   # turnaround: the leap climbing
    29: "D#5/.5 G#5/.5 D#6/3",
    30: "E5/.5 A5/.5 E6/3",
    31: "F#5/.5 B5/.5 F#6/2 D#6/.5 B5/.5",
}
FLUTE = {  # pan flute: echoes and answers
    3: "r/2 B5/.5 D#6/.5 F#6/1",
    7: "r/2 F#6/.5 D#6/.5 C#6/.5 B5/.5",
    23: "r/1 E6/.5 C#6/.5 B5/.5 G#5/.5 B5/.5 C#6/.5",
}

# tubular bells with the china cymbal = the gong (bar: pitches)
BELLS = {0: ["E4", "B4"], 4: ["G#4", "C#5"], 8: ["E4", "B4"], 12: ["G#4", "C#5"], 16: ["A4", "C#5"],
         20: ["F#4", "C#5"], 24: ["A4", "E5"], 26: ["G#4", "E5"], 28: ["C#5"], 29: ["D#5"], 30: ["E5"],
         31: ["F#4", "B4"]}

# pitched taiko: low "don", high "ka", and a deep stroke under the gong (B / F# / C# never rub against the harmony)
DON, KA, BIG = n("F#2"), n("C#3"), n("B1")


def line(tr, bar, text, vel, legato=0.92, transpose=0):
    """Play one bar of "pitch/beats" tokens with a light metric accent (downbeats up, 16ths down)."""
    pos = 0.0
    for tok in text.split():
        p, d = tok.split("/")
        d = float(d)
        if p != "r":
            v = vel + (6 if pos == 0 else 3 if pos == 2 else 0) - (6 if d < 0.5 else 0)
            tr.note(bar, pos, n(p) + transpose, d * legato, v)
        pos += d
    assert abs(pos - 4) < 1e-6, f"bar {bar}: {pos} beats"


def chords_of(bar):
    """[(beat, beats, symbol)] for a bar."""
    out, b = [], 0.0
    for sym, d in PROG[bar]:
        out.append((b, d, sym))
        b += d
    return out


def songs():
    s = Song("legend_golden_carp", bpm=BPM, bars=BARS, key=Key("E", "major"), loudness=-17.0,
             desc="Golden carp legend fight: festive E-major pentatonic, shakuhachi + strings 'leaping carp' "
                  "theme, koto ostinato, taiko and gong (china + tubular bells)")

    lead = s.track("shakuhachi", GM["shakuhachi"], vol=112, pan=0, reverb=55)
    flute = s.track("pan_flute", GM["pan_flute"], vol=96, pan=22, reverb=68)
    vln = s.track("strings", GM["strings"], vol=127, pan=-14, reverb=55)
    pad = s.track("pad", GM["slow_strings"], vol=78, pan=10, reverb=70)
    horns = s.track("horns", GM["french_horn"], vol=84, pan=-26, reverb=60)
    koto = s.track("koto", GM["koto"], vol=112, pan=30, reverb=42)
    pizz = s.track("pizz_bass", GM["pizzicato"], vol=96, pan=-6, reverb=28)
    cb = s.track("contrabass", GM["contrabass"], vol=74, pan=4, reverb=25)
    taiko = s.track("taiko", GM["taiko"], vol=104, pan=-4, reverb=40)
    bells = s.track("bells", GM["tubular_bells"], vol=74, pan=16, reverb=80)
    kit = s.track("kit", DRUMS, vol=92, reverb=45)

    # ---- melody
    for bar, txt in LEAD.items():
        line(lead, bar, txt, 92 if bar < 8 else 96 if bar < 16 else 86)
    for bar in range(8, 16):                                    # A2: strings an octave under the theme
        line(vln, bar, LEAD[bar], 64, legato=0.96, transpose=-12)
    for bar, txt in STRINGS_B.items():
        v = 100 if bar < 24 else 106 if bar < 28 else 98 + 4 * (bar - 28)
        line(vln, bar, txt, v, legato=0.97)
        line(vln, bar, txt, v - 10, legato=0.97, transpose=-12)
        if bar < 24:                                            # B1: horns on the theme with the low strings
            line(horns, bar, txt, 80, legato=0.95, transpose=-12)
        if 24 <= bar < 28:                                      # climax: shakuhachi joins in unison
            line(lead, bar, txt, 100)
        if bar >= 28:                                           # turnaround: pan flute on the leap
            line(flute, bar, txt, 90 + 3 * (bar - 28))
    for bar, txt in FLUTE.items():
        line(flute, bar, txt, 82)
    vln.cc(0, 0, 11, 100)
    vln.swell(28, 0, 16, 92, 120)

    # ---- harmony and engine
    for bar in range(BARS):
        A, B, climax, turn = bar < 16, 16 <= bar < 24, 24 <= bar < 28, bar >= 28
        for beat, d, sym in chords_of(bar):
            root, voicing, kv = CH[sym]
            r = n(root)
            if sym in BASS_TONES:                               # an inversion: other chord tones, not the bass's fifth
                hi, lo5 = (n(x) for x in BASS_TONES[sym])
            else:
                hi = r + 12 if r + 12 <= 54 else (r + 7 if r + 7 <= 54 else r - 12)
                lo5 = r - 5 if r - 5 >= 36 else r + 7
            # pizzicato bass: 8ths on root / octave / fifth
            steps = int(d * 2)
            pat = [r, r, hi, r, lo5, r, hi, lo5] if A else [r, r, hi, r, r, r, hi, r]
            for i in range(steps):
                on_beat = i % 2 == 0
                pizz.note(bar, beat + i * 0.5, pat[i], 0.4, (84 if on_beat else 70) + (6 if climax or turn else 0))
            # contrabass: the root held, an octave lower when the pizzicato sits high
            cb.note(bar, beat, r - 12 if r > n("G#2") else r, d * 0.98, 66 if A else 72)
            # koto ostinato: 8ths, 16ths at the climax and the end of the turnaround
            kp = [n(x) for x in kv]
            if climax or bar >= 30:
                koto.arp(bar, beat, kp, [0, 2, 3, 2, 1, 2, 3, 2], 0.25, d, vel=68 if bar >= 30 else 64, gate=0.8, accent=12)
            elif B:
                koto.arp(bar, beat, kp, [3, 1, 2, 0, 3, 1, 2, 1], 0.5, d, vel=68, gate=0.85, accent=10)
            else:
                koto.arp(bar, beat, kp, [0, 2, 3, 2, 1, 2, 3, 2], 0.5, d, vel=70, gate=0.85, accent=10)
            # sustained harmony: string pad under A and B1, horns from the climax on
            if A or B:
                pad.chord(bar, beat, [n(x) for x in voicing], d * 0.98, 56 if A else 60)
            else:
                horns.chord(bar, beat, [n(x) for x in voicing], d * 0.97, 74 if climax else 68)
    pad.cc(0, 0, 11, 96)
    for b in (3, 7, 11):
        pad.swell(b, 0, 4, 96, 118)
        pad.cc(b + 1, 0, 11, 96)
    pad.swell(14, 0, 8, 96, 116)
    pad.swell(16, 0, 4, 116, 96)                                # settle back under the B theme
    pad.swell(19, 0, 4, 96, 112)
    pad.cc(20, 0, 11, 96)
    pad.swell(23, 0, 4, 96, 112)
    horns.cc(16, 0, 11, 104)
    horns.swell(24, 0, 16, 92, 112)
    horns.swell(28, 0, 16, 74, 118)

    # gong: tubular bells (+ china in the kit)
    for bar, ps in BELLS.items():
        bells.chord(bar, 0, [n(x) for x in ps], 2.0, 78 if bar < 28 else 70 + 4 * (bar - 28))

    # ---- percussion
    for bar in range(BARS):
        if bar < 8:          # A1: taiko groove + woodblock offbeats, a splash on every other bar
            taiko.hits(bar, "X.....x.X.x.....", DON, vel=86)
            taiko.hits(bar, "....x.......x..x", KA, vel=70)
            kit.hits(bar, "..x...x...x...x.", "hi_wood", vel=50)
            if bar % 2 == 1:
                kit.hits(bar, "............x...", "splash", vel=62)
        elif bar < 16:       # A2: dong-dong-qiang (taiko pairs, cymbal on 2 and 4)
            taiko.hits(bar, "X.x...x.X.x...x.", DON, vel=84)
            taiko.hits(bar, "....x.......x...", KA, vel=72)
            kit.hits(bar, "....x.......x...", "splash", vel=56)
            kit.hits(bar, "..x...x...x...x.", "hi_wood", vel=48)
        elif bar < 24:       # B1: half time, snare on 3, hats
            taiko.hits(bar, "X.....x...x.....", DON, vel=88)
            taiko.hits(bar, "..............x.", KA, vel=70)
            kit.hits(bar, "........X.......", "snare", vel=66)
            kit.hits(bar, "x.x.x.x.x.x.x.x.", "hat", vel=46, accent=4)
        elif bar < 28:       # B2: driving 8ths, kick, backbeat
            taiko.hits(bar, "X.x.X.x.X.x.X.x.", DON, vel=80)
            kit.hits(bar, "X.......X.......", "kick", vel=72)
            kit.hits(bar, "....X.......X...", "snare", vel=66)
            kit.hits(bar, "x.x.x.x.x.x.x.x.", "ride", vel=52, accent=4)
        else:                # T: building
            k = bar - 28
            taiko.hits(bar, "X.x.X.x.X.x.X.x.", DON, vel=74 + 6 * k)
            if k < 3:                                           # bar 31 leaves room for the roll
                kit.hits(bar, "x.x.x.x.x.x.x.x.", "hat", vel=44 + 3 * k, accent=4)
            if k == 1:
                taiko.hits(bar, "...x...x...x...x", KA, vel=66)
            if k == 2:
                taiko.hits(bar, "..x...x...x...x.", KA, vel=72)
                for i in range(8):
                    kit.note(bar, i * 0.5, DR["snare"], 0.2, 58 + 4 * i)   # snare 8ths, crescendo

    # gong strokes and crashes on phrase starts
    for bar in (0, 8, 16, 24):
        kit.note(bar, 0, DR["china"], 1.0, 108 if bar != 24 else 98)
        taiko.note(bar, 0, BIG, 1.0, 108 if bar != 24 else 98)
    for bar in (12, 20, 26):
        kit.note(bar, 0, DR["crash"], 1.0, 88)
    kit.note(28, 0, DR["splash"], 1.0, 70)                      # the turnaround starts lighter

    # fills
    def fill(bar, beat, names, v0, v1):
        """16th drum fill from bar/beat, velocity ramping v0 -> v1."""
        for i, nm in enumerate(names):
            kit.note(bar, beat + i * 0.25, DR[nm], 0.2, v0 + (v1 - v0) * i / max(1, len(names) - 1))

    fill(3, 3, ["mid_tom", "mid_tom", "low_tom", "low_tom"], 66, 82)
    taiko.hits(7, "........xxxxxxxx", KA, vel=62)                 # taiko roll into A2
    fill(11, 3, ["high_tom", "mid_tom", "mid_tom", "low_tom"], 66, 84)
    fill(15, 2, ["snare"] * 4 + ["high_tom", "high_tom", "mid_tom", "low_tom"], 64, 96)
    fill(19, 3, ["high_tom", "mid_tom", "low_tom", "low_tom"], 64, 80)
    fill(23, 2, ["snare", "snare", "snare", "snare", "mid_tom", "mid_tom", "low_tom", "low_tom"], 60, 90)
    fill(27, 3, ["snare"] * 4, 70, 92)
    fill(31, 0, ["snare"] * 8, 46, 76)                           # snare roll ...
    fill(31, 2, ["high_tom", "high_tom", "high_tom2", "high_tom2", "mid_tom", "mid_tom", "low_tom", "low_tom"], 64, 88)
    taiko.hits(31, "X...X...X..xX.xx", DON, vel=70)              # ... over a taiko build into bar 0

    s.humanize(timing=0.01, velocity=5)
    return [s]
