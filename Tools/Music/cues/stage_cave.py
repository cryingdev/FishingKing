"""
stage_cave — 수정 동굴 (crystal cave, glowing crystals). C# minor, 76 bpm, 24 bars = 75.8 s, loop.

Form (8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A  (0-7)    C#m  Aadd9  E  B     | C#m  F#m7  A  G#sus4-G#      i-VI-III-VII, half cadence
  A' (8-15)   C#m  Aadd9  E  B     | A  E/G#  F#m7  Bsus4-B       bass falls A-G#-F#, then V of E
  B  (16-23)  E  G#m7  Aadd9  B    | C#m  A  F#m7  G#sus4-G#      I-iii-IV-V of E major, deceptive to C#m (climax E6
                                                                 in bar 20), iv7-V back to bar 0 (B# leads to C#)
day    low marimba bass, rolling marimba arpeggios with a lot of reverb, crystal pad, kalimba melody,
       glockenspiel glints in the kalimba's rests (and doubling its long notes at the climax)
night  the same bass thinned out, a few marimba "drips", a lower choir pad, vibraphone keeping only the long notes
       (lower and without the climax in B)
fight  low floor toms + woodblocks (3-3-2 accents), synth bass eighths on the roots under the base bass,
       marimba ostinato in sixteenths on the chord tones (3-3-2 grouping), bell tree at each section
The kalimba rests in bars 3, 7, 11, 15, 22, 23 (25 %), the vibraphone in nine bars, so the cave drips can be heard.
No chord holds a semitone (add9 instead of maj7), every track puts only chord tones on the beats and passing tones
are off-beat eighths, so the stems never rub.
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("C#", "minor")


def _pcs(names):
    return [n(x + "0") % 12 for x in names]


# chord -> (chord tones, colour tones that sit a whole tone from every chord tone). B# is spelled C.
CH = {c: (_pcs(t), _pcs(col)) for c, (t, col) in {
    "C#m": (["C#", "E", "G#"], ["B"]),
    "A": (["A", "C#", "E"], []),
    "Aadd9": (["A", "C#", "E"], ["B"]),
    "E": (["E", "G#", "B"], ["F#"]),
    "E/G#": (["E", "G#", "B"], []),
    "B": (["B", "D#", "F#"], ["C#"]),
    "Bsus4": (["B", "E", "F#"], []),
    "F#m7": (["F#", "A", "C#", "E"], []),
    "G#m7": (["G#", "B", "D#", "F#"], []),
    "G#sus4": (["G#", "C#", "D#"], []),
    "G#": (["G#", "C", "D#"], []),
}.items()}

# second bass note of the base stems (a fourth / fifth / third away, kept under G3)
ALT = {"C#m": "G#2", "A": "E2", "Aadd9": "E2", "E": "B2", "E/G#": "B2", "B": "F#2", "Bsus4": "F#2",
       "F#m7": "C#3", "G#m7": "D#3", "G#sus4": "D#3", "G#": "D#3"}

# one row per bar: (beat, chord, bass of the base stems)
A_SEC = [
    [(0, "C#m", "C#3")],
    [(0, "Aadd9", "A2")],
    [(0, "E", "E2")],
    [(0, "B", "B2")],
    [(0, "C#m", "C#3")],
    [(0, "F#m7", "F#2")],
    [(0, "A", "A2")],
    [(0, "G#sus4", "G#2"), (2, "G#", "G#2")],
]
A2_SEC = A_SEC[:4] + [
    [(0, "A", "A2")],
    [(0, "E/G#", "G#2")],
    [(0, "F#m7", "F#2")],
    [(0, "Bsus4", "B2"), (2, "B", "B2")],
]
B_SEC = [
    [(0, "E", "E2")],
    [(0, "G#m7", "G#2")],
    [(0, "Aadd9", "A2")],
    [(0, "B", "B2")],
    [(0, "C#m", "C#3")],
    [(0, "A", "A2")],
    [(0, "F#m7", "F#2")],
    [(0, "G#sus4", "G#2"), (2, "G#", "G#2")],
]
PROG = A_SEC + A2_SEC + B_SEC
BARS = len(PROG)


def section(bar):
    return "A" if bar < 8 else ("A2" if bar < 16 else "B")


def segs(bar):
    """[(start, end, chord, bass)] for one bar (bass as a MIDI number)."""
    row = PROG[bar % BARS]
    return [(b, row[i + 1][0] if i + 1 < len(row) else 4, c, n(bs)) for i, (b, c, bs) in enumerate(row)]


def seg_at(bar, beat):
    for sg in segs(bar):
        if sg[0] <= beat < sg[1]:
            return sg
    return segs(bar)[-1]


def tones(chord, color=False, triad=False):
    t, col = CH[chord]
    return (t[:3] if triad else t) + (col if color else [])


def voicing(chord, lo, count, color=False, triad=False):
    """The first `count` chord tones at or above `lo` (closed position)."""
    pcs, out, p = tones(chord, color, triad), [], lo
    while len(out) < count:
        if p % 12 in pcs:
            out.append(p)
        p += 1
    return out


def clip(bar, beat, dur):
    """Shorten a note so it never rings into the next chord."""
    return max(0.1, min(dur, seg_at(bar, beat)[1] - beat - 0.05))


def approach(bar, target, lo, hi):
    """An off-beat note leading into `target` (next bar's bass): a chord tone of this bar's last chord a step
    or a whole tone away (below first), else this chord's bass."""
    ch = seg_at(bar, 3.5)[2]
    for p in (target - 1, target + 1, target - 2, target + 2):
        if lo <= p <= hi and p % 12 in tones(ch, color=True):
            return p
    return seg_at(bar, 3.5)[3]


def bass_pitch(bar, beat, what, lo=36, hi=54):
    """'R' root (bass of the chord), 'F' its alternate, 'N' an approach into the next bar."""
    st, en, ch, bass = seg_at(bar, beat)
    if what == "R":
        return bass
    if what == "F":
        return n(ALT[ch])
    return approach(bar, segs(bar + 1)[0][3], lo, hi)


# ------------------------------------------------------------------------------------------ melody (kalimba)
# (pitch | None, beats) per bar; bars not listed are rests
MELODY = {
    # A: a rising arpeggio that falls back, answered by a higher one (B5, the minor seventh) and a fall to C#
    0: [(None, 1), ("G#4", .5), ("C#5", .5), ("E5", 1), ("G#5", 1)],
    1: [("E5", 1.5), ("F#5", .5), ("E5", 1), ("C#5", 1)],
    2: [("G#4", 1.5), ("A4", .5), ("B4", 2)],
    4: [(None, 1), ("C#5", .5), ("E5", .5), ("G#5", 1), ("B5", 1)],
    5: [("A5", 1.5), ("G#5", .5), ("F#5", 1), ("E5", 1)],
    6: [("E5", 1.5), ("B4", .5), ("C#5", 2)],
    # A': the same opening with a passing D#, then up to the add9 (B5) and C#6; ends on F# into Bsus4
    8: [(None, .5), ("G#4", .5), ("C#5", .5), ("D#5", .5), ("E5", 1), ("G#5", 1)],
    9: [("B5", 1.5), ("A5", .5), ("E5", 1), ("C#5", 1)],
    10: [("B4", 1), ("E5", .5), ("F#5", .5), ("G#5", 2)],
    12: [(None, 1), ("C#5", .5), ("E5", .5), ("A5", 1), ("C#6", 1)],
    13: [("B5", 1.5), ("G#5", .5), ("E5", 1), ("G#5", 1)],
    14: [("E5", 1), ("A5", 1), ("F#5", 2)],
    # B: a rising sequence over E-G#m-A-B, the climax E6 on the deceptive C#m, then a long fall
    16: [("B4", .5), ("E5", .5), ("G#5", 1.5), ("F#5", .5), ("E5", 1)],
    17: [("D#5", .5), ("F#5", .5), ("B5", 1.5), ("A5", .5), ("G#5", 1)],
    18: [("E5", .5), ("A5", .5), ("C#6", 1), ("B5", .5), ("C#6", .5), ("A5", 1)],
    19: [("F#5", .5), ("B5", .5), ("D#6", 1.5), ("C#6", .5), ("D#6", 1)],
    20: [("E6", 2), ("C#6", .5), ("B5", .5), ("G#5", 1)],
    21: [("C#6", 1), ("A5", 1), ("E5", 2)],
}

# glockenspiel: glints in the kalimba's rests, soft doubling of the climax's long notes
GLOCK = {
    3: [(None, 2), ("B5", .5), ("D#6", .5), ("F#6", 1)],
    11: [(None, 1), ("F#6", .5), ("D#6", .5), ("B6", 1)],
    18: [(None, 1), ("C#6", 1)],
    19: [(None, 1), ("D#6", 1.5)],
    20: [("E6", 2)],
    22: [(None, .5), ("E6", .5), (None, .5), ("C#6", .5), (None, 1), ("A5", 1)],
}

# night: the vibraphone keeps the long notes, skips A' up to bar 12 and stays low in B (no climax)
NIGHT_MELODY = {
    0: [(None, 2), ("E5", 1), ("G#5", 1)],
    1: [("E5", 2), ("C#5", 2)],
    2: [("G#4", 2), ("B4", 2)],
    4: [(None, 2), ("G#5", 1), ("B5", 1)],
    5: [("A5", 2), ("F#5", 1), ("E5", 1)],
    6: [("E5", 2), ("C#5", 2)],
    12: [(None, 2), ("A4", 1), ("C#5", 1)],
    13: [("B4", 2), ("G#4", 1), ("B4", 1)],
    14: [("E5", 1), ("A4", 1), ("C#5", 2)],
    16: [(None, 1), ("G#4", 1), ("B4", 2)],
    17: [("D#5", 2), ("B4", 2)],
    18: [("C#5", 2), ("E5", 2)],
    19: [("D#5", 2), ("F#5", 2)],
    20: [("E5", 2), ("C#5", 1), ("G#4", 1)],
    21: [("A4", 2), ("C#5", 2)],
}

SEC_VEL = {"A": 74, "A2": 78, "B": 84}


def write_line(track, mel, shift=0, slope=0.6, legato=0.95):
    """Phrase dynamics: louder sections, higher notes a little louder."""
    for bar, items in mel.items():
        pos = 0.0
        for (nm, d) in items:
            if nm is not None:
                p = n(nm)
                track.note(bar, pos, p, d * legato, int(SEC_VEL[section(bar)] + shift + slope * (p - 76)))
            pos += d


# ------------------------------------------------------------------------------------------------- day stem
# bass patterns: (beat, 'R' | 'F' | 'N', beats)
DAY_BASS = {
    "A": [(0, "R", 2), (2, "F", 2)],
    "A2": [(0, "R", 1.5), (1.5, "R", .5), (2, "F", 1.5), (3.5, "N", .5)],
    "B": [(0, "R", 1.5), (1.5, "F", 1), (2.5, "R", 1), (3.5, "N", .5)],
}
# marimba arpeggio in eighths: indices into a 4-note voicing (4 = the second voice an octave up, None = rest)
ARP = {
    "A": [0, 1, 2, 3, 2, None, 1, None],
    "A2": [0, 1, 2, 3, 2, 1, 2, 3],
    "B": [0, 2, 1, 3, 2, 4, 3, 2],
}


def day(s):
    mlo = s.track("marimba_bass", GM["marimba"], stem="day", vol=112, pan=-4, reverb=58)
    arp = s.track("marimba", GM["marimba"], stem="day", vol=96, pan=-22, reverb=100)
    pad = s.track("crystal", GM["crystal"], stem="day", vol=74, pan=8, reverb=96, chorus=30)
    kal = s.track("kalimba", GM["kalimba"], stem="day", vol=112, pan=16, reverb=80)
    glk = s.track("glock", GM["glockenspiel"], stem="day", vol=64, pan=30, reverb=100)

    for bar in range(BARS):
        sec = section(bar)
        # bass: two notes in A, a pickup and an approach note later
        for (bt, what, d) in DAY_BASS[sec]:
            mlo.note(bar, bt, bass_pitch(bar, bt, what), clip(bar, bt, d), 74 if bt == 0 else (66 if bt % 1 == 0 else 60))

        # arpeggio: eighths, accented on the beats, a little louder each section
        base = {"A": 44, "A2": 48, "B": 52}[sec]
        for i, k in enumerate(ARP[sec]):
            if k is None:
                continue
            bt = 0.5 * i
            vc = voicing(seg_at(bar, bt)[2], 56, 4)
            p = vc[1] + 12 if k == 4 else vc[k]
            arp.note(bar, bt, p, 0.48, base + (6 if bt % 1 == 0 else 0))

        # crystal pad: one triad per chord, the add9 / colour on top from A'
        for (st, en, ch, bass) in segs(bar):
            vc = voicing(ch, 59, 3, triad=True)
            col = CH[ch][1]
            if sec != "A" and col:
                top = vc[-1] + 1
                while top % 12 != col[0]:
                    top += 1
                vc = vc + [top]
            pad.chord(bar, st, vc, en - st - 0.08, {"A": 44, "A2": 48, "B": 52}[sec])

    write_line(kal, MELODY)
    write_line(glk, GLOCK, shift=-18, slope=0.3)

    # the pad breathes up into B and back down by the loop point
    pad.cc(0, 0, 11, 96)
    pad.swell(16, 0, 16, 96, 120)
    pad.swell(20, 0, 16, 120, 96)


# ----------------------------------------------------------------------------------------------- night stem
NIGHT_BASS = {
    "A": [(0, "R", 4)],
    "A2": [(0, "R", 2), (2, "F", 2)],
    "B": [(0, "R", 2), (2, "F", 1.5), (3.5, "N", .5)],
}
# marimba drips: (beat, index into a 3-note voicing from C#4); busier in A' where the vibraphone is silent
DRIPS = {
    "A": [(0.5, 2), (2.5, 1)],
    "A2": [(0.5, 2), (1.5, 1), (2.5, 2), (3.5, 0)],
    "B": [(0.5, 1), (2, 2), (3.5, 1)],
}


def night(s):
    mlo = s.track("n_marimba_bass", GM["marimba"], stem="night", vol=108, pan=-4, reverb=62)
    drp = s.track("n_drips", GM["marimba"], stem="night", vol=86, pan=-22, reverb=112)
    cho = s.track("choir", GM["choir_pad"], stem="night", vol=100, pan=-6, reverb=100)
    vib = s.track("vibes", GM["vibraphone"], stem="night", vol=92, pan=16, reverb=86)

    for bar in range(BARS):
        sec = section(bar)
        for (bt, what, d) in NIGHT_BASS[sec]:
            mlo.note(bar, bt, bass_pitch(bar, bt, what), clip(bar, bt, d), 70 if bt == 0 else 60)
        if sec == "A" and len(segs(bar)) > 1:
            mlo.note(bar, 2, n("D#3"), 1.9, 58)              # the half cadence: G#2 - D#3

        for (bt, k) in DRIPS[sec]:
            vc = voicing(seg_at(bar, bt)[2], 61, 3)
            drp.note(bar, bt, vc[k], 0.9 if bt % 1 == 0 else 0.48, {"A": 46, "A2": 50, "B": 50}[sec])

        # choir: low closed triads (F#3-E4), a fourth voice in B
        for (st, en, ch, bass) in segs(bar):
            vc = voicing(ch, 54, 4 if sec == "B" else 3, triad=True)
            cho.chord(bar, st, vc, en - st - 0.06, {"A": 52, "A2": 56, "B": 58}[sec])

    write_line(vib, NIGHT_MELODY, shift=-12, slope=0.4, legato=0.97)

    # the choir breathes in two-bar waves
    for bar in range(0, BARS, 4):
        cho.swell(bar, 0, 8, 84, 108)
        cho.swell(bar + 2, 0, 8, 108, 84)


# ----------------------------------------------------------------------------------------------- fight stem
DRUM_PATS = {
    "A": {"low_tom": "X.......x.......", "tom2": "......x.......x.", "hi_wood": "x..x..x.x..x..x."},
    "A2": {"low_tom": "X.....x.x.......", "tom2": "....x.......x.x.", "hi_wood": "x.xx.xx.x.xx.xx.",
           "lo_wood": "..x.......x....."},
    "B": {"low_tom": "X.....x.X.....x.", "tom2": "....x.......x...", "hi_wood": "XxxXxxXxXxxXxxXx"},
}
DRUM_VEL = {"low_tom": 84, "tom2": 68, "hi_wood": 46, "lo_wood": 56}
# synth bass eighths: 'R' root, 'O' octave, '5' a fourth / fifth up (a chord tone), 'N' approach
FBASS = {
    "A": ["R", "R", "R", "R", "R", "R", "R", "N"],
    "A2": ["R", "R", "O", "R", "R", "R", "O", "N"],
    "B": ["R", "O", "R", "5", "R", "O", "5", "N"],
}
FBASS_VEL = [96, 66, 76, 88, 72, 68, 86, 74]      # 3-3-2 accents
# marimba ostinato: sixteenths over two beats (3 = the lowest voice an octave up), grouped 3-3-2
OST = {
    "A": [0, 1, 2, 0, 1, 2, 1, 2],
    "A2": [0, 1, 2, 0, 1, 2, 3, 2],
    "B": [0, 2, 3, 0, 2, 3, 1, 3],
}
OST_ACC = (0, 3, 6)


def fight_root(p):
    """A base-stem bass note moved into G#1..G2."""
    while p > 43:
        p -= 12
    while p < 32:
        p += 12
    return p


def fight(s):
    dr = s.track("drums", DRUMS, stem="fight", vol=98, pan=0, reverb=44)
    bs = s.track("synth_bass", GM["synth_bass"], stem="fight", vol=80, pan=0, reverb=12)
    ost = s.track("ostinato", GM["marimba"], stem="fight", vol=92, pan=24, reverb=64)

    for bar in range(BARS):
        sec = section(bar)
        # --- drums: toms as kick / snare, woodblocks as the hats
        if bar in (0, 8, 16):
            dr.note(bar, 0, DR["bell_tree"], 2, 60)
        pats = dict(DRUM_PATS[sec])
        cut = {7: 12, 15: 8, 23: 8}.get(bar)
        if cut:
            pats = {k: v[:cut] for k, v in pats.items()}
        for key, pat in pats.items():
            dr.hits(bar, pat, key, vel=DRUM_VEL[key])
        if bar == 7:                                           # one-beat tom fill
            for i, (k, v) in enumerate([("mid_tom", 70), ("mid_tom", 66), ("tom2", 78), ("low_tom", 86)]):
                dr.note(bar, 3 + 0.25 * i, DR[k], 0.22, v)
        if bar == 15:                                          # two-beat fill down the toms
            toms = ["high_tom", "high_tom", "mid_tom", "mid_tom", "tom2", "tom2", "low_tom", "low_tom"]
            for i, (k, v) in enumerate(zip(toms, [66, 62, 72, 68, 78, 74, 86, 82])):
                dr.note(bar, 2 + 0.25 * i, DR[k], 0.22, v)
            dr.hits(bar, "........x...x...", "hi_wood", vel=50)
        if bar == 23:                                          # floor-tom roll into the loop point
            for i in range(8):
                dr.note(bar, 2 + 0.25 * i, DR["low_tom" if i % 2 == 0 else "tom2"], 0.22, 58 + 5 * i)

        # --- synth bass: eighths under the base bass
        for i, what in enumerate(FBASS[sec]):
            bt = 0.5 * i
            st, en, ch, bass = seg_at(bar, bt)
            root = fight_root(bass)
            if what == "O":
                p = root + 12
            elif what == "5":
                p = next(q for q in range(root + 5, root + 9) if q % 12 in tones(ch, triad=True))
            elif what == "N":
                p = approach(bar, fight_root(segs(bar + 1)[0][3]), 30, 45)
            else:
                p = root
            if bar == 23 and bt >= 2:
                p = root if bt < 3.5 else p                    # under the roll: G# to the leading tone
            bs.note(bar, bt, p, 0.44, FBASS_VEL[i] + (4 if sec == "B" else 0))

        # --- marimba ostinato: sixteenths on the chord tones (triads), accented 3-3-2
        base = {"A": 52, "A2": 56, "B": 58}[sec]
        for half in (0, 2):
            for i, k in enumerate(OST[sec]):
                bt = half + 0.25 * i
                vc = voicing(seg_at(bar, bt)[2], 58, 3, triad=True)
                p = vc[0] + 12 if k == 3 else vc[k]
                ost.note(bar, bt, p, 0.22, base + (20 if i in OST_ACC else 0) + (6 if i == 0 and half == 0 else 0))


def songs():
    s = Song("stage_cave", bpm=76, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Crystal cave: reverberant marimba arpeggios, crystal pad, kalimba melody and glockenspiel glints "
                  "in C# minor; night: thinner marimba drips, low choir pad, vibraphone; "
                  "fight: low toms and woodblocks, synth bass eighths, marimba ostinato")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
