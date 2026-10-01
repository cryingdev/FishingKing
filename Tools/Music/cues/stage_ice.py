"""
stage_ice — 얼음 호수 (frozen lake, ice-hole fishing). B minor, 66 bpm, 24 bars = 87 s, loop.

Form (8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A  (0-7)    Bm  A  Gadd9  D/F# | Em7  D  Gadd9  F#sus4-F#       lament bass B-A-G-F#-E-D, half cadence
  A' (8-15)   Bm7 A  Gadd9  D/F# | Em7  G  F#sus4-F#7  Bm-Bm7/A   the sus4 resolves in the melody, i, bass walks to G
  B  (16-23)  Gadd9  A  Dadd9  Bm7 | Em7  A  Gadd9  F#sus4-F#     IV-V-I of the relative major, climax F#6 in bar 18,
                                                                  the same half cadence leads back to bar 0
day    harp (bass + arpeggios, fuller each section), celesta melody, glockenspiel "ice drops" on the celesta's long
       notes (doubling it at the climax), choir pad, halo pad from A'
night  low slow strings (bass), bowed pad voiced lower, music box with a thinner melody (silent in bars 8-11)
       and a two/three-note broken chord under it
fight  timpani on the roots (rolls into each section), low string ostinato in sixteenths, contrabass eighths an
       octave under the base bass (3-3-2 in B), tubular bells tolling the roots, shaker / jingle / triangle
The celesta rests in bars 3, 7, 11, 15, 19, 23 (25 %), the music box in nine bars, so the wind over the ice breathes.
No chord holds a semitone (add9 instead of maj7), every track puts only chord tones on the beats and passing tones
are off-beat eighths, so the stems never rub.
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("B", "minor")


def _pcs(names):
    return [n(x + "0") % 12 for x in names]


# chord -> (triad pitch classes, colour tones); triads for harp / ostinato, triad + colour for pads and melody
CHORDS = {
    "Bm": (["B", "D", "F#"], []), "Bm7": (["B", "D", "F#"], ["A"]),
    "A": (["A", "C#", "E"], []),
    "G": (["G", "B", "D"], []), "Gadd9": (["G", "B", "D"], ["A"]),
    "D": (["D", "F#", "A"], []), "Dadd9": (["D", "F#", "A"], ["E"]),
    "Em7": (["E", "G", "B"], ["D"]),
    "F#sus4": (["F#", "B", "C#"], []), "F#": (["F#", "A#", "C#"], []), "F#7": (["F#", "A#", "C#"], ["E"]),
}
TRI = {c: _pcs(t) for c, (t, _) in CHORDS.items()}
FULL = {c: _pcs(t + x) for c, (t, x) in CHORDS.items()}

# one row per bar: (beat, chord, bass of the base stems, alternate bass for the harp in B)
A_SEC = [
    [(0, "Bm", "B2", "F#2")],
    [(0, "A", "A2", "E2")],
    [(0, "Gadd9", "G2", "D3")],
    [(0, "D", "F#2", "A2")],                       # D/F#
    [(0, "Em7", "E2", "B2")],
    [(0, "D", "D2", "A2")],
    [(0, "Gadd9", "G2", "D3")],
    [(0, "F#sus4", "F#2", None), (2, "F#", "F#2", None)],
]
A2_SEC = [
    [(0, "Bm7", "B2", "F#2")],
    [(0, "A", "A2", "E2")],
    [(0, "Gadd9", "G2", "D3")],
    [(0, "D", "F#2", "A2")],
    [(0, "Em7", "E2", "B2")],
    [(0, "G", "G2", "D3")],
    [(0, "F#sus4", "F#2", None), (2, "F#7", "F#2", None)],
    [(0, "Bm", "B2", None), (2, "Bm7", "A2", None)],     # Bm - Bm7/A, the bass walks down to G
]
B_SEC = [
    [(0, "Gadd9", "G2", "D3")],
    [(0, "A", "A2", "E3")],
    [(0, "Dadd9", "D3", "A2")],
    [(0, "Bm7", "B2", "F#2")],
    [(0, "Em7", "E2", "B2")],
    [(0, "A", "A2", "E2")],
    [(0, "Gadd9", "G2", "D3")],
    [(0, "F#sus4", "F#2", None), (2, "F#", "F#2", None)],   # + A#2 on beat 3.5 leading to B2
]
PROG = A_SEC + A2_SEC + B_SEC
BARS = len(PROG)


def section(bar):
    return "A" if bar < 8 else ("A2" if bar < 16 else "B")


def segs(bar):
    """[(start, end, chord, bass, alt)] for one bar (pitches as MIDI numbers; alt = bass when not given)."""
    row = PROG[bar % BARS]
    out = []
    for i, (b, c, bass, alt) in enumerate(row):
        e = row[i + 1][0] if i + 1 < len(row) else 4
        out.append((b, e, c, n(bass), n(alt) if alt else n(bass)))
    return out


def seg_at(bar, beat):
    for s in segs(bar):
        if s[0] <= beat < s[1]:
            return s
    return segs(bar)[-1]


def voicing(pcs, lo, count):
    """The first `count` pitches at or above `lo` whose pitch class is in `pcs` (closed position)."""
    out, p = [], lo
    while len(out) < count:
        if p % 12 in pcs:
            out.append(p)
        p += 1
    return out


def place(pc, lo):
    """The pitch of class `pc` in [lo, lo + 11]."""
    return lo + (pc - lo) % 12


def clip(bar, beat, dur):
    """Shorten a note so it never rings into the next chord."""
    end = seg_at(bar, beat)[1]
    return max(0.1, min(dur, end - beat - 0.04))


def write_line(track, mel, vel_of, legato=0.95):
    """mel = {bar: [(name | None, beats), ...]}."""
    for bar, items in mel.items():
        pos = 0.0
        for (nm, d) in items:
            if nm is not None:
                p = n(nm)
                track.note(bar, pos, p, d * legato, vel_of(bar, p))
            pos += d


# ------------------------------------------------------------------------------------------ melody (celesta)
# (pitch | None, beats) per bar; bars not listed are rests. Bars 0 / 4 / 8 / 20 share one rhythm with new pitches.
MELODY = {
    # A: rise to B5, fall to C#5, settle on B4 over G; the answer climbs to D6 and hangs on B5 (the sus4 to come)
    0: [(None, 1), ("D5", .5), ("F#5", .5), ("B5", 1.5), ("A5", .5)],
    1: [("E5", 1.5), ("F#5", .5), ("E5", 1), ("C#5", 1)],
    2: [("D5", 1), ("B4", .5), ("A4", .5), ("B4", 2)],
    4: [(None, 1), ("E5", .5), ("G5", .5), ("B5", 1.5), ("D6", .5)],
    5: [("A5", 1.5), ("B5", .5), ("A5", 1), ("F#5", .5), ("A5", .5)],
    6: [("D6", 1), ("B5", .5), ("A5", .5), ("B5", 2)],
    # A': a third higher, E6 in bar 13, the sus4 (B5) resolves to A#5 over F#7
    8: [(None, 1), ("F#5", .5), ("A5", .5), ("D6", 1.5), ("C#6", .5)],
    9: [("A5", 1.5), ("B5", .5), ("C#6", 1), ("A5", 1)],
    10: [("B5", 1), ("A5", .5), ("G5", .5), ("D5", 2)],
    12: [(None, .5), ("B4", .5), ("E5", .5), ("G5", .5), ("B5", 1), ("D6", 1)],
    13: [("D6", 1.5), ("E6", .5), ("D6", 1), ("B5", 1)],
    14: [("C#6", 1.5), ("B5", .5), ("A#5", 2)],
    # B: a stately rise in quarters to the climax F#6 over D, then the answer falls back to A's B4 cadence
    16: [("D5", 1), ("G5", 1), ("A5", 1), ("B5", 1)],
    17: [("C#6", 1.5), ("B5", .5), ("C#6", 1), ("E6", 1)],
    18: [("F#6", 1.5), ("E6", .5), ("D6", 2)],
    20: [(None, 1), ("G5", .5), ("B5", .5), ("E6", 1.5), ("D6", .5)],
    21: [("C#6", 1.5), ("B5", .5), ("A5", 1), ("E5", 1)],
    22: [("D5", 1.5), ("E5", .5), ("D5", 1), ("B4", 1)],
}

# night: the music box keeps the long notes only and leaves out the first half of A'
NIGHT_MELODY = {
    0: [(None, 1), ("D5", 1), ("B5", 2)],
    1: [("E5", 2), ("C#5", 2)],
    2: [("D5", 2), ("B4", 2)],
    4: [(None, 1), ("E5", 1), ("B5", 2)],
    5: [("A5", 2), ("F#5", 2)],
    6: [("D6", 1), ("B5", 3)],
    12: [(None, 1), ("E5", 1), ("B5", 1), ("D6", 1)],
    13: [("D6", 2), ("B5", 2)],
    14: [("C#6", 2), ("A#5", 2)],
    16: [("D5", 1), ("G5", 1), ("B5", 2)],
    17: [("C#6", 2), ("E6", 2)],
    18: [("F#6", 2), ("D6", 2)],
    20: [(None, 1), ("G5", 1), ("E6", 2)],
    21: [("C#6", 2), ("A5", 1), ("E5", 1)],
    22: [("D5", 2), ("B4", 2)],
}

# glockenspiel drops: (beat, pitch) on chord tones while the celesta holds a long note; bars 16-18 double it
GLOCK = {
    0: [(2.5, "F#6")], 2: [(3, "D6")], 4: [(2.5, "G6")], 6: [(2.5, "D6")],
    8: [(2.5, "F#6"), (3.5, "A6")], 10: [(2.5, "B5"), (3.5, "G5")], 14: [(2.5, "C#6")],
    15: [(0, "B5"), (1.5, "F#5")],                 # the A' cadence lands here while the celesta rests
    20: [(2.5, "G6")],
}

SEC_VEL = {"A": 72, "A2": 76, "B": 82}


def mel_vel(shift=0):
    def f(bar, p):
        return int(SEC_VEL[section(bar)] + shift + 0.5 * (p - 80))
    return f


# ------------------------------------------------------------------------------------------------- day stem
# harp patterns: (beat, voice, beats); "A" = alternate bass, 0-4 = triad tones from F#3 up
HARP = {
    "A": [(1, 0, 1.5), (1.5, 1, 1.5), (2, 2, 2), (3, 3, 1)],
    "A2": [(0.5, 0, 1), (1, 1, 1), (1.5, 2, 1.5), (2, 3, 2), (2.5, 2, 1), (3, 1, 1), (3.5, 2, .5)],
    "B": [(0.5, 0, 1), (1, 1, 1), (1.5, 2, 1.5), (2, "A", 2), (2.5, 3, 1), (3, 4, 1), (3.5, 3, .5)],
}


def day(s):
    harp = s.track("harp", GM["harp"], stem="day", vol=104, pan=-20, reverb=62)
    cel = s.track("celesta", GM["celesta"], stem="day", vol=112, pan=12, reverb=70)
    glk = s.track("glock", GM["glockenspiel"], stem="day", vol=118, pan=28, reverb=82)
    choir = s.track("choir", GM["choir_pad"], stem="day", vol=84, pan=-6, reverb=84)
    halo = s.track("halo", GM["halo_pad"], stem="day", vol=74, pan=10, reverb=86, chorus=30)

    for bar in range(BARS):
        sec = section(bar)
        single = len(segs(bar)) == 1
        # harp bass: every chord; in B a whole-bar chord gets the alternate bass on beat 2
        for (st, en, ch, bass, alt) in segs(bar):
            if sec == "B" and single:
                harp.note(bar, 0, bass, 1.96, 66)
            else:
                harp.note(bar, st, bass, en - st - 0.04, 66)
        for (bt, which, d) in HARP[sec]:
            st, en, ch, bass, alt = seg_at(bar, bt)
            if which == "A":
                if not single:
                    continue
                p, v = alt, 60
            else:
                p = voicing(TRI[ch], n("F#3"), 5)[which]
                v = (52 if bt % 1 else 56) + (4 if sec == "B" else 0)
            if bar == 23 and bt == 3.5:
                continue
            harp.note(bar, bt, p, clip(bar, bt, d), v)
        if bar == 23:
            harp.note(bar, 3.5, n("A#2"), 0.45, 58)       # leading tone back to B2 at bar 0

        # pads: choir on the triad throughout, halo on the upper chord tones (with the colour) from A'
        for (st, en, ch, bass, alt) in segs(bar):
            choir.chord(bar, st, voicing(TRI[ch], n("F#3"), 3), en - st - 0.08, {"A": 44, "A2": 48, "B": 54}[sec])
            if sec != "A":
                halo.chord(bar, st, voicing(FULL[ch], n("F#4"), 2), en - st - 0.08, {"A2": 42, "B": 48}[sec])

    write_line(cel, MELODY, mel_vel())

    # glockenspiel: drops, and the climax doubled in unison
    for bar, items in GLOCK.items():
        for (bt, nm) in items:
            glk.note(bar, bt, n(nm), clip(bar, bt, 1.5), 62 if section(bar) == "A" else 66)
    write_line(glk, {b: MELODY[b] for b in (16, 17, 18)}, mel_vel(-20))

    # pads breathe in four-bar waves; the halo opens up through B
    for bar in range(0, BARS, 4):
        choir.swell(bar, 0, 8, 88, 108)
        choir.swell(bar + 2, 0, 8, 108, 88)
    halo.cc(0, 0, 11, 90)
    halo.swell(16, 0, 12, 90, 120)
    halo.swell(20, 0, 14, 120, 90)


# ----------------------------------------------------------------------------------------------- night stem
# music box broken chord under the melody: (beat, triad tone from D4 up) per section
NIGHT_BOX = {"A": [(1, 2), (2.5, 1)], "A2": [(1, 0), (2, 1), (3, 2)], "B": [(1, 1), (2, 2), (2.5, 1), (3.5, 0)]}


def night(s):
    low = s.track("lowstrings", GM["slow_strings"], stem="night", vol=104, pan=-8, reverb=66)
    pad = s.track("bowed", GM["bowed_pad"], stem="night", vol=100, pan=8, reverb=82)
    box = s.track("musicbox", GM["music_box"], stem="night", vol=116, pan=18, reverb=84)
    box2 = s.track("musicbox_low", GM["music_box"], stem="night", vol=112, pan=-22, reverb=84)

    for bar in range(BARS):
        sec = section(bar)
        for (st, en, ch, bass, alt) in segs(bar):
            low.note(bar, st, bass, en - st - 0.06, 54)
            # bowed pad: the triad from E3 (darker than the day's choir), the colour tone on top in B
            vc = voicing(TRI[ch], n("E3"), 3)
            colour = [pc for pc in FULL[ch] if pc not in TRI[ch]]
            if sec == "B" and colour:
                vc = vc + voicing(colour, vc[-1] + 1, 1)
            pad.chord(bar, st, vc, en - st - 0.08, {"A": 46, "A2": 48, "B": 52}[sec])
        for (bt, k) in NIGHT_BOX[sec]:
            ch = seg_at(bar, bt)[2]
            box2.note(bar, bt, voicing(TRI[ch], n("D4"), 3)[k], clip(bar, bt, 1.5), 40 if bt % 1 else 44)
        if bar == 23:
            low.note(bar, 3.5, n("A#2"), 0.45, 46)

    write_line(box, NIGHT_MELODY, mel_vel(-6), legato=0.98)

    for bar in range(0, BARS, 4):
        pad.swell(bar, 0, 8, 84, 106)
        pad.swell(bar + 2, 0, 8, 106, 84)
    low.cc(0, 0, 11, 96)
    low.swell(16, 0, 8, 96, 112)
    low.swell(22, 0, 8, 112, 96)


# ----------------------------------------------------------------------------------------------- fight stem
# string ostinato in sixteenths: indices into [bass, chord tones above ...] (0 bass, 2 fifth / sixth, 3 octave)
OSTINATO = {
    "A": [0, 2, 3, 2] * 4,
    "A2": [0, 2, 3, 2] * 3 + [0, 2, 4, 3],
    "B": [0, 2, 3, 4, 3, 2, 0, 2, 0, 2, 3, 4, 5, 4, 3, 2],
}
# contrabass: (beat, beats, velocity); eighths in A / A', a 3-3-2 drive in B
BASS = {
    "A": [(0.5 * i, 0.42, v) for i, v in enumerate([88, 64, 76, 64, 84, 64, 76, 68])],
    "B": [(0, 0.66, 92), (0.75, 0.66, 78), (1.5, 0.44, 74), (2, 0.66, 88), (2.75, 0.66, 78), (3.5, 0.44, 74)],
}
TIMP = {"A": "X.......x.....x.", "A2": "X.....x.X.....x.", "B": "X..x..x.X..x..x."}


def fight(s):
    tim = s.track("timpani", GM["timpani"], stem="fight", vol=94, pan=-10, reverb=52)
    ost = s.track("ostinato", GM["strings"], stem="fight", vol=98, pan=-22, reverb=40)
    cb = s.track("contrabass", GM["contrabass"], stem="fight", vol=70, pan=6, reverb=24)
    bell = s.track("bells", GM["tubular_bells"], stem="fight", vol=86, pan=24, reverb=72)
    perc = s.track("perc", DRUMS, stem="fight", vol=94, pan=0, reverb=40)

    for bar in range(BARS):
        sec = section(bar)
        cadence = bar in (7, 15, 23)

        # --- timpani on the base bass's pitch class, a 32nd roll into the next section
        pat = TIMP[sec]
        if cadence:
            pat = pat[:8] + "........"
        for i, ch in enumerate(pat):
            if ch in "xX":
                bt = 0.25 * i
                p = place(seg_at(bar, bt)[3] % 12, n("D2"))
                tim.note(bar, bt, p, 0.24, 92 if ch == "X" else 74)
        if cadence:
            for i in range(16):
                bt = 2 + 0.125 * i
                tim.note(bar, bt, place(seg_at(bar, bt)[3] % 12, n("D2")), 0.12, 54 + 2 * i)

        # --- low string ostinato
        for i, k in enumerate(OSTINATO[sec]):
            bt = 0.25 * i
            st, en, ch, bass, alt = seg_at(bar, bt)
            base = place(bass % 12, n("F#2"))
            tones = voicing(TRI[ch] + [bass % 12], base, 6)
            v = (72 if i % 4 == 0 else 58 + (4 if i % 2 == 0 else 0)) + (6 if sec == "B" else 0)
            ost.note(bar, bt, tones[k], 0.2, v)

        # --- contrabass an octave under the base bass, octave pops in A'
        for (bt, d, v) in BASS["B" if sec == "B" else "A"]:
            p = place(seg_at(bar, bt)[3] % 12, n("E1"))
            if sec == "A2" and bt in (1.5, 3.5):
                p += 12
            if bar == 23 and bt == 3.5:
                p = n("A#1")                                   # leading tone into B1 at bar 0
            cb.note(bar, bt, p, d, v)

        # --- tubular bells toll the root: every other bar in A / A', every bar in B, the A' cadence on bar 15
        if sec == "B" or bar % 2 == 0 or bar == 15:
            st, en, ch, bass, alt = segs(bar)[0]
            bell.note(bar, 0, place(TRI[ch][0], n("E4")), en - 0.1, 70 if bar % 8 == 0 else 62)

        # --- percussion: shaker eighths in A, sixteenths later; jingle and triangle in B; crash per section
        if bar % 8 == 0:
            perc.note(bar, 0, DR["crash"], 2, 76)
        shaker = "x.x.x.x.x.x.x.x." if sec == "A" else "xxxxxxxxxxxxxxxx"
        if cadence:
            shaker = shaker[:12] + "...."
        perc.hits(bar, shaker, "shaker", vel=38, accent=4)
        if sec == "B":
            perc.hits(bar, "x.x.x.x.x.x.x.x.", "jingle", vel=40, accent=4)
            perc.hits(bar, "....x.......x...", "triangle", vel=44)


def songs():
    s = Song("stage_ice", bpm=66, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Frozen lake: harp arpeggios, celesta melody and glockenspiel drops over choir and halo pads "
                  "on a B-minor lament bass; night: music box over a bowed pad and low strings; "
                  "fight: timpani, low string ostinato, contrabass drive and tubular bells")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
