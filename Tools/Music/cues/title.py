"""
title — the game's theme (README §3.1): G major, 92 bpm, 16 bars, a seamless loop, one `main` stem.

Form (4-bar phrases):
  A   0-3   G  | Em D | G Em | D G        flute: the full MOTIF (perfect cadence on do)
  A'  4-7   C  | D    | Em   | Am D       ocarina answers: MOTIF bar 1 sequenced on IV and V, a falling line,
                                          half cadence on re
  B   8-11  Em | C D  | Bm Em | C D       flute climbs to the climax (E6, bar 10), ocarina holds a second voice
  A   12-15 G  | C D  | G Em | D G D7     flute + glockenspiel restate the MOTIF, the ocarina adds a second voice
                                          (sixths below, a descant over the low bar 13); the last do is cut short and
                                          D7 on beat 4 of bar 15 (ocarina pickup A-C) leads back to bar 0
Under it: nylon guitar (fingerpicked eighths, strummed in B), pizzicato (off-beat chops, eighth arpeggios in B;
voiced low under the low tunes of A' and the reprise), acoustic bass, a soft warm pad that ties common tones,
shaker (+ a light kick from bar 8, a triangle on the sections).
"""

from fk_music import Song, Key, GM, DRUMS, DR, MOTIF, n

KEY = Key("G", "major")

# harmony: per bar, (beat, chord) changes; a chord lasts until the next change or the bar line
HARM = [
    [(0, "G")], [(0, "Em"), (2, "D")], [(0, "G"), (2, "Em")], [(0, "D"), (2, "G")],              # A
    [(0, "C")], [(0, "D")], [(0, "Em")], [(0, "Am"), (2, "D")],                                   # A'
    [(0, "Em")], [(0, "C"), (2, "D")], [(0, "Bm"), (2, "Em")], [(0, "C"), (2, "D")],              # B
    [(0, "G")], [(0, "C"), (2, "D")], [(0, "G"), (2, "Em")], [(0, "D"), (2, "G"), (3, "D7")],     # A
]
SECTION = ["A"] * 4 + ["A'"] * 4 + ["B"] * 4 + ["A2"] * 4

# voicings: guitar C3..A4 (under the tunes), pad and pizzicato in the middle (A3..C5)
GUITAR = {"G": "G3 B3 D4 G4", "C": "C3 G3 C4 E4", "D": "D3 A3 D4 F#4", "D7": "D3 A3 C4 F#4",
          "Em": "E3 B3 E4 G4", "Am": "A3 C4 E4 A4", "Bm": "F#3 B3 D4 F#4"}
PAD = {"G": "B3 D4 G4", "C": "C4 E4 G4", "D": "A3 D4 F#4", "D7": "A3 C4 F#4",
       "Em": "B3 E4 G4", "Am": "A3 C4 E4", "Bm": "B3 D4 F#4"}
PIZZ = {"G": "D4 G4 B4", "C": "E4 G4 C5", "D": "D4 F#4 A4", "D7": "D4 F#4 C5",
        "Em": "E4 G4 B4", "Am": "E4 A4 C5", "Bm": "D4 F#4 B4"}

# bass, one bar each: roots on the chord changes, fifths on the off-beats, steps (off-beat) into the next root
BASS = [
    [("G2", 1.5), ("D2", .5), ("G2", 1.5), ("F#2", .5)],
    [("E2", 1.5), ("B2", .5), ("D3", 1.5), ("A2", .5)],
    [("G2", 1.5), ("D3", .5), ("E3", 1.5), ("B2", .5)],
    [("D3", 1.5), ("A2", .5), ("G2", 1), ("A2", .5), ("B2", .5)],
    [("C3", 1.5), ("G2", .5), ("C3", 1.5), ("C#3", .5)],              # chromatic step into D
    [("D3", 1.5), ("A2", .5), ("F#2", 1.5), ("D2", .5)],
    [("E2", 1.5), ("B2", .5), ("E3", 1.5), ("B2", .5)],
    [("A2", 1.5), ("E3", .5), ("D3", 1.5), ("F#2", .5)],
    [("E2", 1.5), ("B2", .5), ("E3", 1.5), ("D3", .5)],
    [("C3", 1.5), ("G2", .5), ("D3", 1.5), ("A2", .5)],
    [("B2", 1.5), ("F#2", .5), ("E2", 1.5), ("B2", .5)],
    [("C3", 1.5), ("G2", .5), ("D2", 1), ("E2", .5), ("F#2", .5)],    # walk up into the reprise
    [("G2", 1.5), ("D3", .5), ("G2", 1), ("B2", 1)],
    [("C3", 1.5), ("G2", .5), ("D3", 1.5), ("A2", .5)],
    [("G2", 1), ("A2", .5), ("B2", .5), ("E2", 2)],
    [("D2", 1.5), ("A2", .5), ("G2", 1), ("D2", 1)],                  # V on beat 4 -> bar 0
]

# flute in B (scale degrees, octave 5: 0 = G5): long notes on chord tones, rising to E6 in bar 10
BRIDGE = [
    ([(-2, .5), (0, .5), (2, 2.0), (1, .5), (0, .5)], 84),              # Em: E G B— A G
    ([(3, 1.5), (2, .5), (1, 1.0), (4, 1.0)], 88),                      # C D: C— B A D
    ([(4, 1.0), (2, .5), (4, .5), (5, 1.5), (4, .5)], 94),              # Bm Em: D B D E— D   (climax)
    ([(3, 1.0), (1, .5), (0, .5), (1, 1.0), (-1, .5), (-2, .5)], 86),   # C D: C A G A F# E -> D (bar 12)
]
# ocarina under the reprise: sixths/thirds below the MOTIF, a descant over its low bar 13 (G5 -> F#5 -> G5),
# a cadence B4 on the G and a pickup A4 C5 on the D7 that hands over to the flute's D5 at bar 0
REPRISE2 = [
    ("B4", 1.5, 64), ("D5", 2.5, 62),                                    # G
    ("G5", 2.0, 64), ("F#5", 2.0, 66),                                   # C D
    ("G5", 1.0, 68), ("D5", 1.0, 62), ("E5", 2.0, 66),                   # G Em
    ("F#5", 1.0, 64), ("D5", .5, 60), ("C5", .5, 58), ("B4", 1.0, 62),   # D G
    ("A4", .5, 56), ("C5", .5, 60),                                      # D7 -> bar 0
]
# MOTIF bar 4 with its final do shortened to a beat, so beat 4 of bar 15 is free for the D7 turnaround
LAST = [(1, 1.0), (2, .5), (1, .5), (0, 1.0), (None, 1.0)]


def voice(s):
    return [n(x) for x in s.split()]


def segments(bars):
    """(bar, beat, length, chord) for every chord of the given bars."""
    for b in bars:
        ch = HARM[b]
        for i, (beat, name) in enumerate(ch):
            end = ch[i + 1][0] if i + 1 < len(ch) else 4
            yield b, beat, end - beat, name


def fingerpick(gt, bars, vel=60):
    """Rolling eighths: bass string, then the upper strings. The bass string rings 1.5 beats (clear of the bass's
    passing notes on the &, e.g. C3 under the C#3 of bar 4), the upper strings a beat; never past a chord change."""
    order = [0, 2, 1, 3]
    for b, beat, ln, ch in segments(bars):
        ps = voice(GUITAR[ch])
        pos, k = 0.0, 0
        while pos < ln - 1e-6:
            i = order[k % len(order)]
            ring = min(1.5 if i == 0 else 1.0, ln - pos) - 0.03
            gt.note(b, beat + pos, ps[i], ring, vel + (8 if i == 0 else 0) + (4 if pos == 0 else 0))
            pos += 0.5
            k += 1


def strum(gt, bars, vel=62):
    """Folk strum per two beats: down (1) - down (2) - up (2&); the up-strum catches the upper strings only."""
    for b, beat, ln, ch in segments(bars):
        ps = voice(GUITAR[ch])
        for s in range(0, int(ln), 2):
            for (x, d, v, up) in ((0, 1.0, vel + 8, False), (1.0, .5, vel - 6, False), (1.5, .5, vel, True)):
                gt.chord(b, beat + s + x, ps[1:] if up else ps, d - 0.05, v, strum=0.025)


def chops(pz, bars, beats=(1, 3), bounce=False, vel=56, low=False):
    """Off-beat pizzicato dyads: the chord's upper two notes, or with low=True (under a low tune) its lower two
    (D7 keeps its seventh); bounce adds the low note on the 4&."""
    for b, beat, ln, ch in segments(bars):
        ps = voice(PIZZ[ch])
        pair = ps[:2] if low and ch != "D7" else ps[1:]
        for x in beats:
            if beat <= x < beat + ln:
                pz.chord(b, x, pair, 0.4, vel)
        if bounce and beat <= 3.5 < beat + ln:
            pz.note(b, 3.5, ps[0], 0.4, vel - 8)


def held_chords(tr, voicings, vel):
    """Sustain every chord of HARM, tying the tones the next chord keeps (a smooth pad)."""
    held = {}  # pitch -> start (absolute beats)

    def close(p, end):
        st = held.pop(p)
        tr.note(int(st // 4), st % 4, p, end - st - 0.04, vel)

    for b, beat, ln, ch in segments(range(16)):
        t0 = b * 4 + beat
        ps = set(voice(voicings[ch]))
        for p in [p for p in held if p not in ps]:
            close(p, t0)
        for p in ps:
            held.setdefault(p, t0)
    for p in list(held):
        close(p, 64)


def songs():
    s = Song("title", bpm=92, bars=16, key=KEY, loudness=-19,
             desc="Title theme: the full MOTIF on flute, an ocarina answer, a bridge to E6 and the reprise with "
                  "glockenspiel and an ocarina second voice; nylon guitar, pizzicato, warm pad, acoustic bass, shaker")

    fl = s.track("flute", GM["flute"], vol=104, pan=-6, reverb=55)
    oc = s.track("ocarina", GM["ocarina"], vol=98, pan=14, reverb=58)
    gl = s.track("glock", GM["glockenspiel"], vol=104, pan=10, reverb=60)
    gt = s.track("guitar", GM["nylon_guitar"], vol=90, pan=-26, reverb=45, chorus=10)
    pz = s.track("pizz", GM["pizzicato"], vol=90, pan=28, reverb=40)
    pad = s.track("pad", GM["warm_pad"], vol=82, pan=0, reverb=70, chorus=35)
    bs = s.track("bass", GM["acoustic_bass"], vol=94, pan=0, reverb=22)
    pc = s.track("perc", DRUMS, vol=98, pan=12, reverb=30)

    # --- melody
    fl.motif(0, KEY, octave=5, vel=82)                                      # A: the full MOTIF
    oc.degs(4, 0, KEY, MOTIF[0], octave=4, vel=76, transpose=3)             # A': MOTIF bar 1 on IV ...
    oc.degs(5, 0, KEY, MOTIF[0], octave=4, vel=80, transpose=4)             # ... and on V
    oc.degs(6, 0, KEY, [(7, 1.5), (6, .5), (5, 1.0), (4, 1.0)], octave=4, vel=84)   # Em: G F# E D
    oc.degs(7, 0, KEY, [(3, 1.5), (2, .5), (1, 2.0)], octave=4, vel=74)             # Am D: C B A— (half cadence)
    for i, (items, v) in enumerate(BRIDGE):                                 # B
        fl.degs(8 + i, 0, KEY, items, octave=5, vel=v)
    oc.seq(8, 0, [("B4", 4), ("E5", 2), ("F#5", 4), ("G5", 2), ("E5", 2), ("D5", 1.5)], vel=62, legato=0.97)
    fl.motif(12, KEY, octave=5, phrases=(0, 1, 2), vel=86)                  # A: the reprise
    fl.degs(15, 0, KEY, LAST, octave=5, vel=84)
    oc.seq(12, 0, REPRISE2, legato=0.95)                                    # ... with the ocarina's second voice

    # glockenspiel: bells on the bridge's long notes, then doubling the reprise
    for (b, beat, p) in ((8, 1, "B5"), (9, 0, "C6"), (10, 2, "E6"), (11, 0, "C6")):
        gl.note(b, beat, p, 1.0, 64)
    gl.motif(12, KEY, octave=5, phrases=(0, 1, 2), vel=68)
    gl.degs(15, 0, KEY, LAST, octave=5, vel=66)

    # --- accompaniment
    fingerpick(gt, list(range(0, 8)) + list(range(12, 16)))
    strum(gt, range(8, 12))
    chops(pz, range(0, 4), vel=50)
    chops(pz, range(4, 8), bounce=True, vel=54, low=True)       # under the ocarina's low answer
    for b, beat, ln, ch in segments(range(8, 12)):                          # B: rising eighth arpeggios
        pz.arp(b, beat, voice(PIZZ[ch]), [0, 1, 2, 1], 0.5, ln, vel=50, gate=0.8, accent=6)
    chops(pz, range(12, 16), vel=56, low=True)                  # under the reprise's two voices

    held_chords(pad, PAD, 54)
    pad.cc(0, 0, 11, 74)
    pad.swell(7, 0, 4, 74, 104)          # opens up into the bridge
    pad.swell(11, 2, 2, 104, 90)
    pad.swell(15, 0, 3.9, 90, 74)        # back to the opening level for the loop

    for b, line in enumerate(BASS):
        bs.seq(b, 0, [(p, d, 86 if i == 0 else 74) for i, (p, d) in enumerate(line)], legato=0.92)

    # --- percussion (one GM drum track: shaker throughout, a light kick from the bridge, triangle on sections)
    shaker = {"A": "x.X.x.X.x.X.x.X.", "A'": "x.X.x.Xxx.X.x.Xx", "B": "xxXxxxXxxxXxxxXx", "A2": "x.Xxx.Xxx.Xxx.Xx"}
    for b in range(16):
        pc.hits(b, shaker[SECTION[b]], "shaker", 0.25, 36)
    for b in range(8, 16):
        pc.hits(b, "x.......x......." if b in (11, 15) else "x.......x.x.....", "kick2", 0.25, 46)
    for (b, v) in ((0, 38), (8, 50), (12, 44)):
        pc.note(b, 0, DR["triangle"], 1.0, v)            # the triangle marks the sections

    s.humanize(timing=0.01, velocity=5)
    return [s]
