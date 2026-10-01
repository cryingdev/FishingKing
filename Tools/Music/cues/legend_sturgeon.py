"""
legend_sturgeon — the fight with the sturgeon (철갑상어), the ice-lake legend (README §3.3): C minor, 112 bpm,
32 bars, a seamless loop, one `main` stem.

Heavy and cold. The engine is a low string ostinato in 3+3+2 eighths (cellos on the bass an octave up, the
contrabass marking the accents), locked to the kick and the timpani; it tightens to sixteenths in the last section.
The sturgeon's own theme is in trombones: a sixteenth pickup into a held rising fifth (C C G—, the armoured fish
rising), answered by a falling sigh on the flat sixth (Ab G F Eb). Choir, a high tubular-bell toll and a high
string tremolo give the ice; the leading tone (B) only appears on the dominant, the Neapolitan Db darkens the
cadences.

Form (one chord per bar):
  A   0-7   Cm Ab Fm G | Cm Ab Db G     the theme in trombone octaves over the 3+3+2 cellos, half-time drums,
                                        a bell toll every two bars, a faint high tremolo; half cadence on G
  A'  8-15  Cm Ab Fm G | Ab Bb Fm G     choir, horn and viola eighths join, the second trombone harmonises,
                                        violins double the climb to F5 (bar 13) an octave up; the drums go full-time
  B   16-23 Ab Eb/G Fm Eb | Db Ab/C Bb G  glacial: the bass sinks stepwise from Ab to G under a broad choir tune
                                        (peak Ab5, bar 21), bells toll each bar, high tremolo; trombones rest, then
                                        return as a soft chorale with the horn doubling the tune (20-23)
  B'  24-31 Cm Ab Fm G | Ab Fm Db G     the theme over sixteenth cellos and driving toms, then Ab Fm Db G with the
                                        climb to F5, timpani + snare rolls and a falling G run into bar 0
"""

import itertools

from fk_music import Song, Key, GM, DRUMS, DR, n, triad

KEY = Key("C", "minor")
KEY_PCS = {(KEY.pc + s) % 12 for s in KEY.steps}
ALLOWED = KEY_PCS | {1}   # plus Db (the Neapolitan, Fm's flat sixth) for the ostinato's neighbour notes

# (chord, contrabass pitch) per bar
PLAN = [
    ("Cm", "C2"), ("Ab", "Ab1"), ("Fm", "F1"), ("G", "G1"), ("Cm", "C2"), ("Ab", "Ab1"), ("Db", "Db2"), ("G", "G1"),
    ("Cm", "C2"), ("Ab", "Ab1"), ("Fm", "F1"), ("G", "G1"), ("Ab", "Ab1"), ("Bb", "Bb1"), ("Fm", "F1"), ("G", "G1"),
    ("Ab", "Ab2"), ("Eb", "G2"), ("Fm", "F2"), ("Eb", "Eb2"), ("Db", "Db2"), ("Ab", "C2"), ("Bb", "Bb1"), ("G", "G1"),
    ("Cm", "C2"), ("Ab", "Ab1"), ("Fm", "F1"), ("G", "G1"), ("Ab", "Ab1"), ("Fm", "F1"), ("Db", "Db2"), ("G", "G1"),
]
BARS = len(PLAN)
PROG = [c for c, _ in PLAN]
BASS = [n(b) for _, b in PLAN]

CHORDS = {"Cm": ("C", "min"), "Ab": ("Ab", "maj"), "Fm": ("F", "min"), "G": ("G", "maj"), "Db": ("Db", "maj"),
          "Bb": ("Bb", "maj"), "Eb": ("Eb", "maj")}


def pcs(ch):
    r, q = CHORDS[ch]
    return {p % 12 for p in triad(n(r + "0"), q)}


def low_root(ch, lo):
    """The chord's root as the lowest pitch >= lo."""
    p = n(CHORDS[ch][0] + "0")
    while p < lo:
        p += 12
    return p


def below(p, ch):
    """A second voice: the nearest chord tone a minor third to a sixth under p."""
    for q in range(p - 3, p - 10, -1):
        if q % 12 in pcs(ch):
            return q
    return p - 3


def voicing(ch, prev, lo, hi):
    """A close triad of `ch` inside lo..hi, moving as little as possible from `prev`."""
    opts = [[p for p in range(lo, hi + 1) if p % 12 == pc] for pc in sorted(pcs(ch))]
    best, score = None, None
    for combo in itertools.product(*opts):
        v = sorted(combo)
        if v[-1] - v[0] > 12:
            continue
        s = sum(abs(a - b) for a, b in zip(v, prev)) if prev else abs(sum(v) / 3 - (lo + hi) / 2)
        if score is None or s < score:
            best, score = v, s
    return best


# --- the ostinato: pitches per bar by role, rhythms as (beat, beats, role, accent)
def roles(bar):
    """R = the bass an octave up (cellos), 5 = the chord tone nearest a fifth above R, n = the step above 5
    (off-beat neighbour), 8 = R's octave, ln = the step under the next bar's R (off-beat approach)."""
    ch = PROG[bar]
    r = BASS[bar] + 12
    five = min((p for p in range(r + 3, r + 10) if p % 12 in pcs(ch)), key=lambda p: abs(p - r - 7))
    nb = five + 1 if (five + 1) % 12 in ALLOWED else five + 2
    nxt = BASS[(bar + 1) % BARS] + 12
    ln = nxt - 1 if ((nxt - 1) % 12 in pcs(ch) or (nxt - 1) % 12 in KEY_PCS) else nxt - 2
    return {"R": r, "5": five, "n": nb, "8": r + 12, "ln": ln}


OST3 = [(0, .5, "R", 16), (.5, .5, "R", 0), (1, .5, "5", 6), (1.5, .5, "R", 12), (2, .5, "R", 0),
        (2.5, .5, "n", 6), (3, .5, "5", 12), (3.5, .5, "ln", 2)]
DRIVE = [(i * .25, .25, r, a) for i, (r, a) in enumerate(
    [("R", 16), ("R", 0), ("R", 2), ("R", 0), ("5", 6), ("R", 0), ("R", 12), ("R", 0),
     ("R", 4), ("R", 0), ("n", 6), ("5", 0), ("5", 12), ("R", 0), ("R", 2), ("ln", 4)])]
BROAD = [(0, 1.5, "R", 10), (1.5, 1.5, "R", 2), (3, 1, "5", 4)]   # B: the same 3+3+2 in long notes


def ostinato(tr, bar, pattern, vel, gate=.88, ramp=0.0):
    rl = roles(bar)
    for i, (beat, d, role, acc) in enumerate(pattern):
        tr.note(bar, beat, rl[role], d * gate, vel + acc + ramp * i)


# --- the sturgeon theme (trombones): (note | None, beats) per bar
THEME = {
    # A: the call (pickup, rising fifth held) and the flat-six sigh; in sequence on Fm; half cadence on G
    0: [("C4", .75), ("C4", .25), ("G4", 2), ("Eb4", 1)],
    1: [("Ab4", 1.5), ("G4", .5), ("F4", 1), ("Eb4", 1)],
    2: [("F4", .75), ("F4", .25), ("C5", 2), ("Ab4", 1)],
    3: [("G4", 1.5), ("F4", .5), ("D4", 1), ("B3", 1)],
    4: [("C4", .75), ("C4", .25), ("G4", 1), ("C5", 1), ("Bb4", .5), ("G4", .5)],
    5: [("Eb5", 1.5), ("C5", .5), ("Ab4", 1), ("C5", 1)],
    6: [("Db5", 1.5), ("C5", .5), ("Ab4", 1), ("F4", 1)],                   # Neapolitan
    7: [("D4", .75), ("D4", .25), ("G4", 2), ("B3", 1)],                    # the call on the dominant
    # A': the call again, then higher: the climb to F5 over Bb
    8: [("C4", .75), ("C4", .25), ("G4", 2), ("Eb4", .5), ("G4", .5)],
    9: [("C5", 1.5), ("Bb4", .5), ("Ab4", 1), ("Eb4", 1)],
    10: [("F4", .75), ("Ab4", .25), ("C5", 1.5), ("Db5", .5), ("C5", 1)],
    11: [("B4", 1.5), ("C5", .5), ("D5", 1), ("G4", 1)],
    12: [("C5", .75), ("C5", .25), ("Eb5", 2), ("C5", 1)],                  # the call on Ab, a sixth up
    13: [("D5", 1), ("F5", 2.5), ("Eb5", .5)],                              # climax
    14: [("C5", 1), ("Ab4", .5), ("Bb4", .5), ("C5", 1), ("Ab4", 1)],
    15: [("D5", 1.5), ("C5", .5), ("B4", 1), ("G4", 1)],
    # B': the call returns, the answer turned upward, the climb again and the falling G run into bar 0
    24: [("C4", .75), ("C4", .25), ("G4", 2), ("Eb4", 1)],
    25: [("Ab4", 1), ("C5", 1), ("Bb4", .5), ("Ab4", .5), ("Eb4", 1)],
    26: [("F4", .75), ("F4", .25), ("C5", 2), ("Ab4", 1)],
    27: [("G4", 1.5), ("Ab4", .5), ("B4", 1), ("D5", 1)],
    28: [("C5", .75), ("C5", .25), ("Eb5", 2), ("C5", 1)],
    29: [("F5", 1.5), ("Eb5", .5), ("C5", 1), ("Ab4", 1)],
    30: [("Ab4", 1), ("Db5", 1), ("F5", 2)],
    31: [("D5", 2), ("B4", .5), ("G4", .5), ("D4", .5), ("B3", .5)],
}
THEME_VEL = {**{b: 90 for b in range(0, 8)}, **{b: 94 for b in range(8, 12)}, **{b: 100 for b in range(12, 16)},
             **{b: 96 for b in range(24, 28)}, **{b: 96 for b in range(28, 32)}}

# the B tune (choir): broad, rising against the sinking bass, peak Ab5 at bar 21
TUNE = {
    16: [("Eb4", 1), ("Ab4", 1), ("C5", 2)],
    17: [("Bb4", 3), ("G4", 1)],
    18: [("Ab4", 1.5), ("G4", .5), ("F4", 1), ("C5", 1)],
    19: [("Bb4", 2), ("Eb5", 2)],
    20: [("F5", 1.5), ("Eb5", .5), ("Db5", 1), ("Ab4", 1)],
    21: [("C5", 1), ("Eb5", 1), ("Ab5", 2)],
    22: [("F5", 1.5), ("Eb5", .5), ("D5", 1), ("Bb4", 1)],
    23: [("B4", 1.5), ("C5", .5), ("D5", 2)],
}

# tubular bells (C4..F5): a high toll, (beat, note) per bar
BELLS = {
    0: [(0, "C5")], 2: [(0, "C5")], 4: [(0, "Eb5")], 6: [(0, "F5")], 7: [(2, "D5")],
    8: [(0, "Eb5")], 10: [(0, "F5")], 12: [(0, "Eb5")], 14: [(0, "C5")], 15: [(0, "D5"), (2, "B4")],
    16: [(0, "Eb5")], 17: [(0, "Eb5")], 18: [(0, "F5")], 19: [(0, "Eb5")],
    20: [(0, "F5")], 21: [(0, "Eb5")], 22: [(0, "F5")], 23: [(0, "D5")],
    24: [(0, "C5")], 26: [(0, "F5")], 28: [(0, "Eb5")], 29: [(0, "F5")], 30: [(0, "F5")], 31: [(0, "D5"), (2, "B4")],
}


def phrase(tr, bar, items, vel, legato=.92, octave=0, harmony=None, hvel=None):
    """A bar of (note | None, beats): accent on the downbeat, short notes a little softer. harmony = a second
    track that plays below() each note."""
    pos = 0.0
    for (p, d) in items:
        if p is not None:
            v = vel + (8 if pos == 0 else 0) - (6 if d <= .5 else 0)
            tr.note(bar, pos, n(p) + 12 * octave, d * legato, v)
            if harmony is not None:
                harmony.note(bar, pos, below(n(p), PROG[bar]), d * legato, (hvel or vel) + (v - vel))
        pos += d
    assert abs(pos - 4) < 1e-6, f"bar {bar}: {pos} beats"


def roll(tr, bar, beat, beats, pitch, v0, v1, step=.125):
    """A roll (timpani / snare): repeated notes from v0 to v1."""
    k = int(round(beats / step))
    for i in range(k):
        tr.note(bar, beat + i * step, pitch, step * .95, v0 + (v1 - v0) * i / max(1, k - 1))


def songs():
    s = Song("legend_sturgeon", bpm=112, bars=BARS, key=KEY, stems=["main"], loudness=-17.0,
             desc="Sturgeon fight: heavy and cold. A 3+3+2 low-string ostinato locked to kick and timpani under the "
                  "sturgeon's trombone theme (a pickup into a held rising fifth, a flat-six sigh); choir, high "
                  "tubular-bell tolls and string tremolo for the ice; a glacial B section with a broad choir tune "
                  "over a sinking bass, and an Ab-Fm-Db-G turnaround with timpani and snare rolls back into the theme")

    drums = s.track("drums", DRUMS, vol=104, pan=0, reverb=40)
    timp = s.track("timpani", GM["timpani"], vol=100, pan=-8, reverb=50)
    cb = s.track("contrabass", GM["contrabass"], vol=90, pan=8, reverb=34)
    vc = s.track("cellos", GM["cello"], vol=114, pan=-20, reverb=38)
    vla = s.track("violas", GM["strings"], vol=96, pan=26, reverb=50)
    vln = s.track("violins", GM["strings"], vol=96, pan=-30, reverb=60)
    trem = s.track("tremolo", GM["tremolo_strings"], vol=90, pan=30, reverb=70)
    tbn = s.track("trombone", GM["trombone"], vol=106, pan=-10, reverb=52)
    tbn2 = s.track("trombone2", GM["trombone"], vol=104, pan=8, reverb=52)
    hn = s.track("horn", GM["french_horn"], vol=96, pan=18, reverb=58)
    choir = s.track("choir", GM["choir"], vol=98, pan=0, reverb=80)
    bells = s.track("bells", GM["tubular_bells"], vol=96, pan=22, reverb=78)

    # --- cellos: the ostinato; contrabass: the accents (A), long notes (B), driving eighths (end of B')
    CB_A = [(0, 1.4, 14), (1.5, 1.4, 4), (3, .9, 8)]
    CB_8 = [(i * .5, .45, 12 if i in (0, 3, 6) else 0) for i in range(8)]
    for b in range(BARS):
        if b < 8:
            ostinato(vc, b, OST3, 84)
        elif b < 16:
            ostinato(vc, b, OST3, 88)
        elif b < 20:
            ostinato(vc, b, BROAD, 74, gate=.96)
        elif b < 24:
            ostinato(vc, b, OST3, 70 + 3 * (b - 20))
        elif b < 31:
            ostinato(vc, b, DRIVE, 82 if b < 28 else 86, gate=.82)
        else:
            ostinato(vc, b, DRIVE, 80, gate=.82, ramp=1.6)   # crescendo into bar 0

        r = BASS[b]
        if 16 <= b < 24:
            cb.note(b, 0, r, 3.9, 78 + (4 if b >= 20 else 0))
        elif b >= 28:
            for (beat, d, acc) in CB_8:
                cb.note(b, beat, r, d, 74 + acc * 2 // 3 + (2 * int(beat * 2) if b == 31 else 0))
        else:
            for (beat, d, acc) in CB_A:
                cb.note(b, beat, r, d, (80 if b < 8 else 84) + acc)

    # --- trombone theme: octaves in A and at the start of B', harmony in A' and the climb; horn in unison in
    # A' and B'; violins an octave up on the climaxes
    for b, items in THEME.items():
        v = THEME_VEL[b]
        if b < 8 or 24 <= b < 28:
            phrase(tbn, b, items, v)
            phrase(tbn2, b, items, v - 10, octave=-1)
        else:
            phrase(tbn, b, items, v, harmony=tbn2, hvel=v - 12)
        if b >= 8:
            phrase(hn, b, items, v - 10)
        if 12 <= b < 16 or b >= 28:
            phrase(vln, b, items, v - 18, legato=.96, octave=1)
    for tr, base in ((tbn, 112), (tbn2, 112), (hn, 108)):
        tr.cc(0, 0, 11, base).swell(31, 0, 2, base - 16, 127).cc(31, 2.25, 11, 120)

    # --- B: trombones rest, then a soft chorale (bass an octave up + one inner chord tone); horn doubles the
    # choir tune an octave down
    for b in range(20, 24):
        lo = BASS[b] + 12
        inner = min((p for p in range(50, 63) if p % 12 in pcs(PROG[b]) and p % 12 != lo % 12),
                    key=lambda p: abs(p - 57))
        v = 62 + 4 * (b - 20)
        tbn2.note(b, 0, lo, 3.9, v)
        tbn.note(b, 0, inner, 3.9, v)
        phrase(hn, b, TUNE[b], 70 + 3 * (b - 20), legato=.96, octave=-1)

    # --- choir: pads in A' and B', the tune (with a second voice) in B
    prev = None
    for b in list(range(8, 16)) + list(range(24, BARS)):
        lo, hi = (60, 72) if b < 16 else (62, 74)
        v_ = voicing(PROG[b], prev, lo, hi)
        choir.chord(b, 0, v_, 3.9, 70 if b < 16 else 74)
        prev = v_
    for b, items in TUNE.items():
        phrase(choir, b, items, 88 if b < 20 else 92, legato=.97, harmony=choir, hvel=(78 if b < 20 else 82))
    choir.cc(0, 0, 11, 80)
    choir.cc(8, 0, 11, 76).swell(8, 0, 28, 76, 110)
    choir.cc(16, 0, 11, 88).swell(16, 0, 20, 88, 116).swell(21, 0, 12, 116, 98)
    choir.cc(24, 0, 11, 92).swell(28, 0, 15.5, 92, 127)

    # --- violas: 3+3+2 chord-tone eighths in A' and B' (Eb3..Eb4, staccato)
    prev = None
    for b in list(range(8, 16)) + list(range(24, BARS)):
        v_ = voicing(PROG[b], prev, 51, 63)
        vla.arp(b, 0, v_, [0, 1, 2, 0, 1, 2, 0, 2], .5, 4, vel=70 + (6 if b >= 28 else 0), gate=.8, accent=6)
        prev = v_

    # --- tremolo: a faint ice shimmer over A, then above the choir tune in B (two high chord tones)
    prev = None
    for b in list(range(0, 8)) + list(range(16, 24)):
        v_ = voicing(PROG[b], prev, 72, 84)
        trem.chord(b, 0, v_[1:], 3.9, 46 if b < 8 else 62 + 2 * (b - 16))
        prev = v_
    trem.cc(0, 0, 11, 72)
    trem.cc(16, 0, 11, 80).swell(16, 0, 16, 80, 110).swell(20, 0, 16, 110, 96)

    # --- tubular bells
    for b, hits in BELLS.items():
        for (beat, p) in hits:
            bells.note(b, beat, p, 2.5 if beat == 0 else 1.8, 84 if b < 16 else (80 if b < 24 else 86))

    # --- timpani: roots (D2..C#3), 3+3+2 accents; rolls into A', B, B' and bar 0
    def tp(b, beat, v, d=.9):
        timp.note(b, beat, low_root(PROG[b], n("D2")), d, v)
    for b in range(BARS):
        if b < 8:
            tp(b, 0, 108 if b % 2 == 0 else 98)
            tp(b, 3, 84)
            if b % 2 == 1:
                tp(b, 1.5, 74, .5)
        elif b < 16:
            tp(b, 0, 110)
            tp(b, 1.5, 80, .5)
            tp(b, 3, 90)
        elif b < 24:
            tp(b, 0, 72 + (6 if b >= 20 else 0), 2)
        elif b < 28:
            tp(b, 0, 112)
            tp(b, 1.5, 82, .5)
            tp(b, 2.5, 78, .5)
            tp(b, 3, 92)
        elif b < 31:
            for beat, v in [(0, 96), (1, 74), (1.5, 72), (2.5, 76), (3, 86), (3.5, 80)]:
                tp(b, beat, v, .45)
    g2 = n("G2")
    roll(timp, 7, 3, 1, g2, 70, 100)
    roll(timp, 15, 2, 2, g2, 64, 100)
    roll(timp, 23, 1, 3, g2, 56, 104)
    tp(31, 0, 108, .45)
    roll(timp, 31, .5, 3.5, g2, 64, 120)

    # --- kit: kick on the 3+3+2 accents, backbeat half-time (A, B) / full-time (A', B'), toms, rolls and fills
    def kit(b, parts):
        for key, pat, v in parts:
            drums.hits(b, pat, key, vel=v)

    def fill(b, beat, keys, v0, step=.25, dv=5):
        for i, key in enumerate(keys):
            drums.note(b, beat + i * step, DR[key], step * .9, v0 + dv * i)

    for b in range(BARS):
        if b < 8:
            parts = [("kick", "X.....x.....x...", 84), ("snare", "........X.......", 80)]
            if b % 2 == 1 and b != 7:
                parts.append(("low_tom", "..........x...x.", 64))
            kit(b, parts)
        elif b < 16:
            kit(b, [("kick", "X.....x.x...x...", 86), ("snare", "....x.......x...", 90),
                    ("hat", "x.x.x.x.x.x.x.x.", 36), ("low_tom", "..........x.....", 62)])
        elif b < 20:
            kit(b, [("kick", "X.........x.....", 78), ("ride", "x...x...x...x...", 38),
                    ("low_tom", "........x.......", 56)])
        elif b < 24:
            parts = [("kick", "X.....x...x.....", 78), ("ride", "x.x.x.x.x.x.x.x.", 32)]
            if b < 23:
                parts += [("snare", "........x.......", 66), ("low_tom", "..............x.", 58)]
            kit(b, parts)
        elif b < 28:
            kit(b, [("kick", "X.....x.x...x...", 88), ("snare", "....x.......x...", 92),
                    ("hat", "x.x.x.x.x.x.x.x.", 40), ("low_tom", "..........x...x.", 66)])
        elif b < 31:
            kit(b, [("kick", "X.....x.x...x.x.", 84), ("snare", "....x.......x...", 88),
                    ("tom2", "..x..x....x..x..", 66), ("low_tom", "...x......x....x", 70)])
            drums.hits(b, "xxxxxxxxxxxxxxxx", "hat", vel=30, accent=4)
        else:
            kit(b, [("kick", "X.....x.....x...", 92)])
            roll(drums, b, 0, 3, DR["snare"], 48, 100, step=.25)
            fill(b, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 90, dv=6)
    fill(7, 3, ["high_tom", "tom3", "mid_tom", "tom2"], 72, dv=6)
    fill(15, 2, ["high_tom", "high_tom", "tom3", "tom3", "mid_tom", "mid_tom", "tom2", "low_tom"], 64, dv=4)
    roll(drums, 23, 2, 2, DR["snare"], 44, 92, step=.25)
    # cymbals: the sections
    drums.note(0, 0, DR["crash"], 1, 96).note(0, 0, DR["china"], 1, 70)
    drums.note(8, 0, DR["crash"], 1, 88)
    drums.note(12, 0, DR["crash2"], 1, 84)
    drums.note(16, 0, DR["china"], 1, 70).note(16, 0, DR["crash2"], 1, 60)
    drums.note(20, 0, DR["splash"], 1, 62)
    drums.note(24, 0, DR["crash"], 1, 98)
    drums.note(28, 0, DR["crash"], 1, 88).note(28, 0, DR["china"], 1, 74)

    s.humanize(timing=0.01, velocity=5)
    return [s]
