"""
legend_blue_marlin — the fight with the blue marlin (청새치), the open-ocean legend (README §3.3): D mixolydian,
150 bpm, 32 bars, a seamless loop, one `main` stem.

Speed and heroism. The engine is a cello gallop (8th + two 16ths on every beat) over a contrabass in 3+3+2 / driving
8ths, timpani on the downbeats and a kit of backbeat snare with 16th ghosts, 16th hats and tom fills; violins add a
16th chord ostinato in A' and B'.
The marlin's own motif is in the trumpet: a 16th "rip" up a fourth (the marlin breaking the surface) into a held
chord tone, answered by a falling tail that touches the mixolydian flat seventh (C natural over the C chord).

Form (one chord per bar):
  A   0-7   D C G D | D C Em A            the theme, trumpet solo over horn chords; trombone stabs on the downbeats;
                                          violins enter softly at bar 4; half cadence on A (C#5 -> D5 into bar 8)
  A'  8-15  D C G/B D/A | Bb C D D        horns double the theme an octave down, violin ostinato, a descending
                                          bass D C B A Bb C D; the rip climbs in sequence (bVI bVII I) to D6 at bar
                                          14 with a second trumpet and glockenspiel; snare / tom fill into B
  B   16-23 Bm G C D | Bm G Am C          contrast: the trumpets rest, a broad violin + horn tune (peak C6 at 22),
                                          toms-and-ride groove, softer gallop; a violin scale run into B'
  B'  24-31 Bb C D D7/C | Bb C A A7       the theme returns in two trumpets over bVI bVII I (D6 again at 26-27),
                                          then the turnaround: the motif's answer over Bb C, the rip on A, a bass walk
                                          A B C#, a violin run, a timpani roll and a tom fill into the crash at bar 0
"""

import itertools

from fk_music import Song, Key, GM, DRUMS, DR, n, triad

KEY = Key("D", "mixolydian")

PROG = ["D", "C", "G", "D", "D", "C", "Em", "A",          # A
        "D", "C", "G", "D", "Bb", "C", "D", "D",          # A'
        "Bm", "G", "C", "D", "Bm", "G", "Am", "C",        # B
        "Bb", "C", "D", "D7", "Bb", "C", "A", "A7"]       # B' (turnaround)

# the contrabass line (inversions at 10, 11, 27)
BASS = ["D2", "C2", "G1", "D2", "D2", "C2", "E2", "A1",
        "D2", "C2", "B1", "A1", "Bb1", "C2", "D2", "D2",
        "B1", "G1", "C2", "D2", "B1", "G1", "A1", "C2",
        "Bb1", "C2", "D2", "C2", "Bb1", "C2", "A1", "A1"]

CHORDS = {"D": ("D", "maj"), "D7": ("D", "dom7"), "C": ("C", "maj"), "G": ("G", "maj"), "Em": ("E", "min"),
          "A": ("A", "maj"), "A7": ("A", "dom7"), "Bb": ("Bb", "maj"), "Bm": ("B", "min"), "Am": ("A", "min")}


def pcs(ch):
    r, q = CHORDS[ch]
    return {p % 12 for p in triad(n(r + "0"), q)}


def root_pc(ch):
    return n(CHORDS[ch][0] + "0") % 12


def near(pc, prev, lo, hi):
    """The pitch of class pc inside lo..hi closest to prev (or to the middle of the range)."""
    ref = prev if prev is not None else (lo + hi) // 2
    return min((p for p in range(lo, hi + 1) if p % 12 == pc), key=lambda p: (abs(p - ref), p))


def voicing(ch, prev, lo, hi):
    """A close voicing (span <= an octave) of `ch` inside lo..hi, moving as little as possible from `prev`."""
    opts = [[p for p in range(lo, hi + 1) if p % 12 == pc] for pc in sorted(pcs(ch))]
    best, score = None, None
    for combo in itertools.product(*opts):
        v = sorted(combo)
        if v[-1] - v[0] > 12:
            continue
        s = sum(abs(a - b) for a, b in zip(v, prev)) if prev else abs(sum(v) / len(v) - (lo + hi) / 2)
        if score is None or s < score:
            best, score = v, s
    return best


def below(p, ch):
    """A harmony voice: the nearest chord tone a minor third to a major sixth under p."""
    for q in range(p - 3, p - 10, -1):
        if q % 12 in pcs(ch):
            return q
    return p - 3


# --- the trumpet theme: (note, beats) per bar. "rip" = four 16ths up into the held note (the marlin's leap)
THEME = {
    # A: the leap and its answer (the flat seventh C over the C chord)
    0: [("D5", .25), ("E5", .25), ("F#5", .25), ("G5", .25), ("A5", 1.5), ("F#5", .5), ("D5", .5), ("E5", .5)],
    1: [("G5", 1.5), ("E5", .5), ("C5", 1.5), ("A4", .5)],
    2: [("G4", .25), ("A4", .25), ("B4", .25), ("C5", .25), ("D5", 1.5), ("B4", .5), ("D5", .5), ("B4", .5)],
    3: [("A4", 2.5), ("D5", .5), ("E5", .5), ("F#5", .5)],
    4: [("A5", 1.5), ("B5", .5), ("A5", .5), ("F#5", .5), ("D5", 1)],
    5: [("G5", 1.5), ("A5", .5), ("G5", .5), ("E5", .5), ("C5", 1)],
    6: [("B4", .5), ("E5", .5), ("G5", 1.5), ("F#5", .5), ("E5", .5), ("D5", .5)],
    7: [("E5", 1.5), ("D5", .5), ("C#5", 2)],                                         # half cadence, C# -> D
    # A': the leap again, then the rip in a rising sequence over bVI bVII I to the climax D6
    8: [("D5", .25), ("E5", .25), ("F#5", .25), ("G5", .25), ("A5", 1.5), ("F#5", .5), ("D5", .5), ("E5", .5)],
    9: [("G5", 1.5), ("E5", .5), ("G5", .5), ("A5", .5), ("G5", .5), ("E5", .5)],
    10: [("D5", .5), ("G5", .5), ("B5", 1.5), ("A5", .5), ("G5", .5), ("D5", .5)],
    11: [("F#5", 1.5), ("E5", .5), ("D5", 1), ("A4", .5), ("C5", .5)],
    12: [("Bb4", .25), ("C5", .25), ("D5", .25), ("E5", .25), ("F5", 2.5), ("D5", .5)],
    13: [("C5", .25), ("D5", .25), ("E5", .25), ("F#5", .25), ("G5", 2.5), ("E5", .5)],
    14: [("D5", .25), ("E5", .25), ("F#5", .25), ("G5", .25), ("A5", 1), ("D6", 2)],  # climax
    15: [("D6", 1.5), ("C6", .5), ("A5", .5), ("G5", .5), ("F#5", 1)],
    # B': the sequence with busier tails, the D7 arpeggio to D6, then the turnaround
    24: [("Bb4", .25), ("C5", .25), ("D5", .25), ("E5", .25), ("F5", 1.5), ("D5", .5), ("F5", .5), ("D5", .5)],
    25: [("C5", .25), ("D5", .25), ("E5", .25), ("F#5", .25), ("G5", 1.5), ("E5", .5), ("G5", .5), ("E5", .5)],
    26: [("D5", .5), ("F#5", .5), ("A5", .5), ("C6", .5), ("D6", 2)],
    27: [("D6", 1.5), ("C6", .5), ("A5", .5), ("F#5", .5), ("A5", 1)],
    28: [("F5", 1.5), ("D5", .5), ("Bb4", 1), ("C5", .5), ("D5", .5)],
    29: [("G5", 1.5), ("E5", .5), ("C5", 1.5), ("B4", .5)],                            # the answer of bar 1
    30: [("A4", .25), ("B4", .25), ("C#5", .25), ("D5", .25), ("E5", 1.5), ("C#5", .5), ("E5", .5), ("G5", .5)],
    31: [("A5", 1.5), ("G5", .5), ("E5", 1), ("C#5", 1)],                              # A7, C# -> D at bar 0
}
THEME_VEL = {**{b: 94 for b in range(0, 8)}, **{b: 97 for b in range(8, 12)}, **{b: 101 for b in range(12, 16)},
             **{b: 100 for b in range(24, 28)}, **{b: 96 for b in range(28, 31)}, 31: 92}

# --- the B tune (violins; horns an octave down): broad, rising to C6 at bar 22, a scale run into B'
TUNE = {
    16: [("B4", 1), ("D5", 1), ("F#5", 2)],
    17: [("G5", 1.5), ("F#5", .5), ("D5", 1), ("B4", 1)],
    18: [("C5", 1), ("E5", 1), ("G5", 1.5), ("A5", .5)],
    19: [("A5", 2.5), ("G5", .5), ("F#5", 1)],
    20: [("D5", 1), ("F#5", 1), ("B5", 2)],
    21: [("B5", 1.5), ("A5", .5), ("G5", 1), ("D5", 1)],
    22: [("E5", 1), ("A5", 1), ("C6", 1.5), ("B5", .5)],
    23: [("C6", 1.5), ("G5", .5), ("C5", .25), ("D5", .25), ("E5", .25), ("F5", .25),     # C mixolydian run
         ("G5", .25), ("A5", .25), ("Bb5", .25), ("C6", .25)],
}


def play(tr, bar, items, vel, legato=.92, extra=()):
    """A bar of (note, beats). Rips (16ths) crescendo into the held note; downbeats and long notes lean.
    extra = (track, shift | "harm", vel) doublings that skip the 16ths (they enter on the held note)."""
    pos, run = 0.0, 0
    for (p, d) in items:
        p = n(p)
        if d <= .25:
            v = vel - 16 + 4 * run
            run += 1
        else:
            v = min(vel + 8, vel + (6 if pos % 2 == 0 else 0) + (4 if d >= 1.5 else 0) - (5 if d <= .5 else 0))
            run = 0
        tr.note(bar, pos, p, d * legato, v)
        if d > .25:
            for (t2, shift, v2) in extra:
                q = below(p, PROG[bar]) if shift == "harm" else p + shift
                t2.note(bar, pos, q, d * legato, v2 + (v - vel))
        pos += d


def songs():
    s = Song("legend_blue_marlin", bpm=150, bars=32, key=KEY, stems=["main"], loudness=-17.0,
             desc="Blue marlin fight: heroic D mixolydian chase at 150 bpm; the marlin's leaping 16th-rip trumpet "
                  "theme over a galloping cello, driving snare and toms, a violin 16th ostinato and horns; a broad "
                  "violin and horn tune in B, and a Bb-C-A7 turnaround with a tom fill back into the theme")

    drums = s.track("drums", DRUMS, vol=98, pan=0, reverb=30)
    timp = s.track("timpani", GM["timpani"], vol=96, pan=-8, reverb=50)
    cb = s.track("contrabass", GM["contrabass"], vol=106, pan=6, reverb=30)
    vc = s.track("cello", GM["cello"], vol=127, pan=26, reverb=38)
    vn = s.track("violins", GM["strings"], vol=110, pan=-28, reverb=54)
    hn = s.track("horns", GM["french_horn"], vol=86, pan=-18, reverb=58)
    tbn = s.track("trombone", GM["trombone"], vol=80, pan=20, reverb=44)
    tp = s.track("trumpet", GM["trumpet"], vol=112, pan=8, reverb=48)
    tp2 = s.track("trumpet2", GM["trumpet"], vol=100, pan=-10, reverb=52)
    gl = s.track("glock", GM["glockenspiel"], vol=100, pan=30, reverb=60)

    # --- contrabass: 3+3+2 in A, driving 8ths with octave kicks in A' / B', long notes opening B, a walk into bar 0
    for b in range(32):
        r = n(BASS[b])
        if b < 8 or 20 <= b < 24:
            v = 88 if b < 8 else 82
            cb.note(b, 0, r, 1.3, v + 10).note(b, 1.5, r, 1.3, v).note(b, 3, r, .85, v - 4)
        elif 16 <= b < 20:
            cb.note(b, 0, r, 3.8, 82)
        elif b == 31:
            for i, (p, d) in enumerate([(r, .5)] * 6 + [(n("B1"), .5), (n("C#2"), .5)]):
                cb.note(b, i * .5, p, d * .8, 86 + 2 * i)
        else:
            v = 86 if b < 16 else 90
            for i in range(8):
                up = i in (3, 7)                       # the octave on the "and" of 2 and 4
                cb.note(b, i * .5, r + (12 if up else 0), .4, v + (10 if i == 0 else 4 if i == 4 else 0) - (6 if up else 0))

    # --- cello gallop (8th + two 16ths per beat) on the bass note's class in C3..B3; beat 4 leaps to a chord tone
    prev = None
    for b in range(32):
        r = near(n(BASS[b]) % 12, prev, n("C3"), n("B3"))
        prev = r
        up = next(r + k for k in (7, 3, 4, 8, 9, 5) if (r + k) % 12 in pcs(PROG[b]))
        if b == 31:
            up = n("C#3")                              # the leading tone under the run, -> D3 at bar 0
        v = (84 if b < 8 else 88 if b < 16 else 72 if b < 20 else 78 if b < 24 else 90 if b < 28 else 94)
        for beat in range(4):
            p = up if beat == 3 else r
            acc = 10 if beat == 0 else 5 if beat == 2 else 0
            vc.note(b, beat, p, .42, v + acc).note(b, beat + .5, p, .2, v - 8).note(b, beat + .75, p, .2, v - 4)

    # --- timpani: chord roots in D2..C#3
    def tk(b, beat, v, d=1.0, ch=None):
        timp.note(b, beat, near(root_pc(ch or PROG[b]), None, n("D2"), n("C#3")), d, v)

    for b in range(32):
        if b < 8:
            tk(b, 0, 104 if b in (0, 4) else 90)
            if b % 2 == 1:
                tk(b, 2.5, 74, .5)
            if b == 7:
                tk(b, 3, 84, .5)
                tk(b, 3.5, 90, .5)
        elif b < 15:
            tk(b, 0, 100 if b in (8, 12, 14) else 92)
            tk(b, 2, 80)
            if b >= 12:
                tk(b, 3.5, 82, .5)
        elif b == 15:
            tk(b, 0, 98)
            for i in range(8):                         # roll from beat 2 into B
                tk(b, 2 + i * .25, 60 + 4 * i, .25)
        elif b < 24:
            if b % 2 == 0 or b >= 20:
                tk(b, 0, 72 if b < 20 else 82)
            if b == 23:
                tk(b, 2, 80, .5)
                tk(b, 3, 88, .5)
                tk(b, 3.5, 96, .5)
        elif b < 30:
            tk(b, 0, 100 if b in (24, 26) else 90)
            tk(b, 2, 84)
        elif b == 30:
            for i in range(4):
                tk(b, i, 88 + 4 * i)
        else:
            for i in range(16):                        # roll on A, crescendo into bar 0
                tk(b, i * .25, 58 + int(2.4 * i), .25)

    # --- horns: chords in A (G3..A4); the theme an octave down in A' 8-11 and B' 24-27; chords at the climax and
    # the turnaround; the B tune an octave under the violins
    prev = None
    for b in list(range(0, 8)) + list(range(12, 16)) + list(range(28, 32)):
        v = voicing(PROG[b], prev, n("G3"), n("A4"))
        prev = v
        vel = 70 if b < 8 else 82 if b < 16 else 80
        hn.chord(b, 0, v, 3.8, vel)
    hn.swell(6, 0, 8, 96, 127).cc(8, 0, 11, 120)
    hn.swell(28, 0, 15.5, 90, 120).cc(0, 0, 11, 110)
    hn.cc(12, 0, 11, 120).cc(16, 0, 11, 112).cc(24, 0, 11, 120)

    # --- trumpets: the theme; trumpet 2 harmonises the climaxes (12-15, 24-31), the glockenspiel doubles them
    for b, items in THEME.items():
        extra = []
        if 8 <= b < 12 or 24 <= b < 28:
            extra.append((hn, -12, THEME_VEL[b] - 14))
        if 12 <= b < 16 or b >= 24:
            extra.append((tp2, "harm", THEME_VEL[b] - 14))
        if 12 <= b < 16 or 24 <= b < 28:
            extra.append((gl, 12, 74))
        play(tp, b, items, THEME_VEL[b], extra=extra)

    # --- the B tune: violins, horns an octave down (the horns skip the run)
    for b, items in TUNE.items():
        vel = 84 if b < 20 else 92
        play(vn, b, items, vel, legato=.97, extra=[(hn, -12, vel - 6)] if b < 23 else [])
    hn.note(23, 0, n("C4"), 1.4, 86).note(23, 1.5, n("G3"), .45, 80).note(23, 2, n("E4"), 1.9, 84)
    vn.swell(16, 0, 16, 96, 120).swell(20, 0, 16, 110, 127)
    vn.cc(24, 0, 11, 120).cc(0, 0, 11, 112)

    # --- violins: the downbeat hit at bar 0, the 16th chord ostinato (D4..E5), the landing D6 at bar 24, the run
    # up the A7 scale into bar 0
    vn.chord(0, 0, [n("D5"), n("F#5"), n("A5")], .45, 92)
    prev = None
    for b in list(range(4, 16)) + list(range(24, 32)):
        v = voicing(PROG[b], prev, n("D4"), n("E5"))
        prev = v
        pat = [0, 2, 1, 2] if b % 2 == 0 else [2, 1, 0, 1]
        vel = 54 if b < 8 else 66 if b < 12 else 72 if b < 16 else 74
        if b == 24:
            vn.note(b, 0, n("D6"), 1.8, 96)
            vn.arp(b, 2, v, pat, .25, 2, vel, gate=.8, accent=10)
        elif b == 31:
            vn.arp(b, 0, v, pat, .25, 2, vel, gate=.8, accent=10)
            run = ["A4", "B4", "C#5", "D5", "E5", "F#5", "G5", "A5"]
            for i, p in enumerate(run):
                vn.note(b, 2 + i * .25, n(p), .23, 76 + 3 * i)
        else:
            vn.arp(b, 0, v, pat, .25, 4, vel, gate=.8, accent=10)

    # --- trombones: open fifths in C3..D4 (stabs in A, held in A', chords in B, stabs then held in B')
    prev = None
    for b in range(32):
        r = near(root_pc(PROG[b]), prev, n("C3"), n("B3"))
        prev = r
        fifth = [r, r + 7]
        if b < 8:
            tbn.chord(b, 0, fifth, .7, 80 if b in (0, 4) else 72)
            if b in (3, 7):
                tbn.chord(b, 2, fifth, .45, 70).chord(b, 3, fifth, .45, 78)
        elif b < 15:
            tbn.chord(b, 0, fifth if b < 12 else voicing(PROG[b], None, n("C3"), n("D4")), 3.8, 70 if b < 12 else 76)
        elif b == 15:
            tbn.chord(b, 0, fifth, .9, 86).chord(b, 1.5, fifth, .45, 80)
        elif b < 24:
            tbn.chord(b, 0, voicing(PROG[b], None, n("D3"), n("E4")), 3.8, 60 if b < 20 else 68)
        elif b < 28:
            tbn.chord(b, 0, fifth, .7, 90).chord(b, 1.5, fifth, .45, 80).chord(b, 3, fifth, .45, 76)
        else:
            tbn.chord(b, 0, voicing(PROG[b], None, n("C3"), n("D4")), 3.8, 80)
    tbn.swell(31, 0, 3.8, 90, 127).cc(0, 0, 11, 118)

    # --- kit
    def pat(b, parts):
        for key, p, v, *acc in parts:
            drums.hits(b, p, key, vel=v, accent=acc[0] if acc else None)

    HAT8 = ("hat", "x.x.x.x.x.x.x.x.", 46, 4)
    HAT16 = ("hat", "xxxxxxxxxxxxxxxx", 34, 2)
    for b in range(32):
        if b < 8:                                      # A: kick 1 + 2&3, backbeat, ghosts, 8th hats
            parts = [("kick", "X.....x.X.....x.", 80), HAT8, ("snare", ".......x......x.", 42)]
            parts.append(("snare", "....x..........." if b == 7 else "....x.......x...", 98))
            pat(b, parts)
            if b == 3:
                drums.note(b, 3.5, DR["high_tom"], .2, 80).note(b, 3.75, DR["mid_tom"], .2, 86)
            if b == 7:
                for i, k in enumerate(["high_tom2", "high_tom", "mid_tom", "tom2"]):
                    drums.note(b, 3 + i * .25, DR[k], .2, 82 + 5 * i)
        elif b < 15 or 24 <= b < 28:                   # A' / B': busier kick, 16th hats, more ghosts
            parts = [("kick", "X.....x.X.x...x.", 86), HAT16, ("snare", "..x....x.x....x.", 44),
                     ("snare", "....x.......x...", 96)]
            if b in (12, 13, 14, 26):
                parts.append(("low_tom", "............x.xx", 80))
            pat(b, parts)
            if b in (11, 27):
                drums.note(b, 3.5, DR["snare"], .2, 74).note(b, 3.75, DR["snare"], .2, 86)
        elif b == 15:                                  # fill into B
            pat(b, [("kick", "X.......X.......", 90), ("snare", "....x...", 100), ("hat", "x.x.x.x.", 44)])
            for i in range(4):
                drums.note(b, 2 + i * .25, DR["snare"], .2, 70 + 6 * i)
            for i, k in enumerate(["high_tom2", "high_tom", "mid_tom", "low_tom"]):
                drums.note(b, 3 + i * .25, DR[k], .2, 88 + 4 * i)
        elif b < 24:                                   # B: floor-tom gallop, ride, the snare creeping back
            parts = [("kick", "X.......X......." if b < 20 else "X.....x.X.......", 80),
                     ("low_tom", "X.xx....X.xx....", 64), ("tom2", "....x.......x...", 70),
                     ("ride", "x.x.x.x.x.x.x.x.", 44, 4)]
            if b in (20, 21):
                parts.append(("snare", "............x...", 88))
            elif b == 22:
                parts.append(("snare", "....x.......x...", 94))
            pat(b, parts)
            if b == 19:
                for i, k in enumerate(["high_tom", "mid_tom", "tom2", "low_tom"]):
                    drums.note(b, 3 + i * .25, DR[k], .2, 70 + 4 * i)
            if b == 23:                                # snare roll into B'
                for i in range(8):
                    drums.note(b, 2 + i * .25, DR["snare"], .2, 60 + 5 * i)
        elif b < 30:                                   # the turnaround: 8th kick pairs, tom gallop on beat 4
            pat(b, [("kick", "X.x...x.X.x...x.", 88), HAT16, ("snare", "..x....x.x......", 46),
                    ("snare", "....x.......x...", 98), ("low_tom", "............x.xx", 84)])
        elif b == 30:                                  # 16th snare crescendo, accents on the backbeat
            pat(b, [("kick", "X...x...X...x...", 90)])
            for i in range(16):
                drums.note(b, i * .25, DR["snare"], .2, 52 + 2 * i + (24 if i in (4, 12) else 0))
        else:                                          # tom fill down into the crash at bar 0
            pat(b, [("kick", "X.......X.......", 90)])
            fill = ["snare", "high_tom2", "snare", "high_tom2", "snare", "high_tom", "snare", "high_tom",
                    "tom3", "tom3", "mid_tom", "mid_tom", "tom2", "tom2", "low_tom", "low_tom"]
            for i, k in enumerate(fill):
                drums.note(b, i * .25, DR[k], .2, 72 + int(1.5 * i) + (6 if i % 4 == 0 else 0))
    # cymbals on the sections
    drums.note(0, 0, DR["crash"], 1, 104)
    drums.note(8, 0, DR["crash"], 1, 96)
    drums.note(12, 0, DR["splash"], 1, 82)
    drums.note(14, 0, DR["crash2"], 1, 92)
    drums.note(16, 0, DR["china"], 1, 76)
    drums.note(20, 0, DR["crash2"], 1, 80)
    drums.note(24, 0, DR["crash"], 1, 104)
    drums.note(26, 0, DR["crash2"], 1, 96)
    drums.note(28, 0, DR["crash"], 1, 92)

    s.humanize(timing=0.01, velocity=5)
    return [s]
