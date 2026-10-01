"""
legend_arapaima — the fight with the arapaima (피라루쿠), the swamp legend (README §3.3): G minor, 120 bpm, 32 bars,
a seamless loop, one `main` stem.

Primal and tribal. The engine is a 3+3+2 groove: low toms in tresillo, congas filling the gaps, a 16th shaker, taiko
on the downbeats, and a low bassoon riff in the same tresillo (16ths: 0 .75 1.5 | 2 2.75 3.5) that sits on a G pedal
and bends to the chord; tuba (and taiko) roots in dotted quarters (3+3+2 in eighths) lock under it, kept in
Bb1..A2 so the root is never above the riff's anchor (Bb1 under its F2, not a Bb2 over it).
The arapaima's own theme is in trombone + horn: a stark rising fourth and minor third (D G Bb) in the riff's rhythm,
answered by a falling tail.

Form (one chord per bar):
  A   0-7   Gm Gm Eb F | Gm Gm Cm D      the theme, half cadence on D
  A'  8-15  Gm Gm Cm Bb | Eb F D D       clarinet joins the riff an octave up, then harmonises the climb to the
                                         climax (F5 over F, bar 13); choir enters; D -> Eb is a deceptive turn into B
  B   16-23 Eb F Bb Gm | Cm F Bb D       lighter: taiko + congas + clave, the riff thins out, a broad clarinet tune
                                         in the relative major (horn second voice from 20), choir pad; trombones rest
  B'  24-31 Gm Gm Eb Eb | Cm Ab D D      the build: the theme returns in sequence, toms thicken to 16ths, the riff
                                         drives in eighths, Cm-Ab-D (bII-V) and a tom / taiko fill into bar 0,
                                         which lands on a crash, a china and one short choir "hah" on Gm
"""

from fk_music import Song, Key, GM, DRUMS, DR, n, triad

KEY = Key("G", "minor")

PROG = ["Gm", "Gm", "Eb", "F", "Gm", "Gm", "Cm", "D",        # A
        "Gm", "Gm", "Cm", "Bb", "Eb", "F", "D", "D",         # A'
        "Eb", "F", "Bb", "Gm", "Cm", "F", "Bb", "D",         # B
        "Gm", "Gm", "Eb", "Eb", "Cm", "Ab", "D", "D"]        # B' (turnaround)

CHORDS = {"Gm": ("G", "min"), "Cm": ("C", "min"), "Eb": ("Eb", "maj"), "F": ("F", "maj"),
          "Bb": ("Bb", "maj"), "Ab": ("Ab", "maj"), "D": ("D", "maj")}


def pcs(ch):
    r, q = CHORDS[ch]
    return {p % 12 for p in triad(n(r + "0"), q)}


def root_in(ch, lo):
    """The chord's root as the lowest pitch >= lo."""
    p = n(CHORDS[ch][0] + "0")
    while p < lo:
        p += 12
    return p


def low_root(ch):
    """Tuba / taiko root in Bb1..A2: never above the riff's anchor (Bb1 under the F2 anchor, not Bb2 over it)."""
    return root_in(ch, n("Bb1"))


def below(p, ch):
    """A second voice: the nearest chord tone a third to a sixth under p."""
    for q in range(p - 3, p - 10, -1):
        if q % 12 in pcs(ch):
            return q
    return p - 3


def voicing(ch, prev, lo=55, hi=67):
    """A close triad of `ch` inside lo..hi, moving as little as possible from `prev`."""
    import itertools
    opts = [[p for p in range(lo, hi + 1) if p % 12 == pc] for pc in sorted(pcs(ch))]
    best, score = None, None
    for combo in itertools.product(*opts):
        v = sorted(combo)
        if v[-1] - v[0] > 12:
            continue
        s = sum(abs(a - b) for a, b in zip(v, prev)) if prev else abs(sum(v) / 3 - 61)
        if score is None or s < score:
            best, score = v, s
    return best


# --- the riff: anchor (a pedal near G2 that bends to the chord), two chord tones above it
RIFF = {"Gm": ("G2", "Bb2", "D3"), "Eb": ("G2", "Bb2", "Eb3"), "F": ("F2", "A2", "C3"), "Cm": ("G2", "C3", "Eb3"),
        "D": ("F#2", "A2", "D3"), "Ab": ("Ab2", "C3", "Eb3"), "Bb": ("F2", "Bb2", "D3")}
UPPER = {"G2": "A2", "F2": "G2", "F#2": "G2", "Ab2": "Bb2"}    # off-beat upper neighbour of the next anchor
LOWER = {"G2": "F#2", "F2": "Eb2", "F#2": "E2", "Ab2": "G2"}   # ... and the lower one (a turn into it)

# (beat, beats, role, accent); roles: a = anchor, c1 / c2 = chord tones, dn = the scale step under c2,
# up / lo = neighbours of the next anchor
TRES = [(0, .75, "a", 14), (.75, .75, "a", 0), (1.5, .5, "c1", 6), (2, .75, "a", 10), (2.75, .75, "c2", 6),
        (3.5, .5, "up", 0)]
TURN = TRES[:-1] + [(3.5, .25, "up", 0), (3.75, .25, "lo", 4)]                  # on D: A F# -> G
PICK = TRES[:-1] + [(3.5, .25, "c1", 0), (3.75, .25, "up", 4)]                  # stepping down into the next bar
LIGHT = [(0, 1.0, "a", 10), (1.5, .5, "c1", 0), (2, 1.0, "a", 4), (3, .5, "c2", 0), (3.5, .5, "up", 0)]
DRIVE = [(0, .5, "a", 14), (.5, .5, "a", 0), (1, .5, "a", 0), (1.5, .5, "c1", 10), (2, .5, "a", 2), (2.5, .5, "a", 0),
         (3, .5, "c2", 10), (3.5, .5, "up", 0)]
RUN = DRIVE[:-2] + [(3, .25, "c2", 10), (3.25, .25, "dn", 4), (3.5, .25, "c1", 6), (3.75, .25, "lo", 10)]  # D C A F# -> G

# riff pattern and base velocity per bar
RIFF_PLAN = ([TRES] * 7 + [TURN]                                   # A
             + [TRES, PICK, TRES, PICK, TRES, PICK, TRES, TURN]    # A'
             + [LIGHT] * 7 + [TURN]                                # B
             + [TRES] * 4 + [DRIVE] * 3 + [RUN])                   # B'
RIFF_VEL = [84] * 8 + [90] * 8 + [70] * 4 + [76] * 4 + [86] * 4 + [92] * 4


def sounding(tracks, t0, t1):
    return [p for tr in tracks for (st, d, p, _) in tr.notes if st < t1 and st + d > t0]


def riff_bar(tr, bar, pattern, vel, octave=0, bass=()):
    """One bar of the riff. A neighbour note that would rub a semitone or a tritone against a low drum / bass note
    held under it (bass = tracks already written) falls back to the chord tone c1."""
    a, c1, c2 = RIFF[PROG[bar]]
    nxt = RIFF[PROG[(bar + 1) % 32]][0]
    dn = next(p for p in range(n(c2) - 1, n(c2) - 3, -1) if KEY.in_key(p))
    role = {"a": n(a), "c1": n(c1), "c2": n(c2), "dn": dn, "up": n(UPPER[nxt]), "lo": n(LOWER[nxt])}
    for (beat, d, r, acc) in pattern:
        p = role[r]
        if r in ("up", "lo", "dn"):
            t0 = tr.t(bar, beat)
            if any(abs(p - q) % 12 in (1, 6, 11) for q in sounding(bass, t0, t0 + int(d * .9 * 480))):
                p = role["c1"]
        tr.note(bar, beat, p + 12 * octave, d * .9, vel + acc)


# --- the arapaima theme (trombone + horn): (note | None, beats) per bar
THEME = {
    # A: the call D G Bb in the riff's rhythm, a falling answer
    0: [("D4", .75), ("D4", .75), ("G4", .5), ("Bb4", 1.5), ("A4", .5)],
    1: [("Bb4", 1), ("G4", .5), ("A4", .5), ("D4", 1.5), (None, .5)],
    2: [("Eb4", .75), ("Eb4", .75), ("G4", .5), ("Bb4", 1.5), ("C5", .5)],
    3: [("C5", 1.5), ("A4", .5), ("F4", 2)],
    4: [("D4", .75), ("D4", .75), ("G4", .5), ("Bb4", 1), ("D5", 1)],
    5: [("D5", 1.5), ("C5", .5), ("Bb4", 1), ("G4", 1)],
    6: [("G4", .75), ("G4", .75), ("C5", .5), ("Eb5", 1.5), ("D5", .5)],
    7: [("D5", 1), ("C5", .5), ("A4", .5), ("F#4", 2)],                         # D7: half cadence
    # A': the call again, then the climb to F5
    8: [("D4", .75), ("D4", .75), ("G4", .5), ("Bb4", 1.5), ("C5", .5)],
    9: [("D5", 1.5), ("C5", .5), ("Bb4", .5), ("A4", .5), ("G4", 1)],
    10: [("Eb4", .75), ("Eb4", .75), ("G4", .5), ("C5", 1.5), ("D5", .5)],
    11: [("D5", 1.5), ("C5", .5), ("Bb4", 1), ("F4", 1)],
    12: [("Bb4", .75), ("Bb4", .75), ("C5", .5), ("Eb5", .5), ("D5", .5), ("Eb5", 1)],
    13: [("F5", 1.5), ("Eb5", .5), ("C5", 1), ("A4", 1)],                       # climax
    14: [("D5", 1.5), ("Eb5", .5), ("D5", 1), ("A4", 1)],                       # Eb: the b9 sigh over D
    15: [("C5", 1), ("A4", .5), ("F#4", 1.5), (None, 1)],
    # B': the call in sequence (Gm, Eb), rising to the turnaround
    24: [("D4", .75), ("D4", .75), ("G4", .5), ("Bb4", 1), ("D5", 1)],
    25: [("D5", .75), ("D5", .75), ("F5", .5), ("D5", 1), ("Bb4", 1)],
    26: [("Eb4", .75), ("Eb4", .75), ("G4", .5), ("Bb4", 1), ("Eb5", 1)],
    27: [("Eb5", .75), ("Eb5", .75), ("F5", .5), ("Eb5", 1), ("Bb4", 1)],
    28: [("Eb4", .75), ("G4", .75), ("C5", .5), ("Eb5", 1.5), ("D5", .5)],
    29: [("Eb5", 1.5), ("F5", .5), ("Eb5", 1), ("C5", 1)],                      # Ab: bII
    30: [("A4", .75), ("A4", .75), ("C5", .5), ("D5", 1.5), ("Eb5", .5)],
    31: [("D5", 3), ("C5", .5), ("A4", .5)],                                    # held, swelling into bar 0
}
# base velocity per bar: each 4-bar phrase leans toward its high point (Eb5 at 6, F5 at 13, the held D5 at 31)
THEME_VEL = dict(zip(range(0, 16), [88, 90, 93, 90, 90, 93, 97, 92, 94, 96, 99, 96, 100, 106, 103, 98]))
THEME_VEL.update(zip(range(24, 32), [90, 93, 94, 97, 98, 101, 102, 100]))   # 31: the swell does the rest

# the B tune (clarinet): broad, in the relative major, peak F5 at bar 21
TUNE = {
    16: [("G4", 1.5), ("Bb4", .5), ("Eb5", 2)],
    17: [("C5", 1.5), ("D5", .5), ("C5", 1), ("A4", 1)],
    18: [("Bb4", 1.5), ("C5", .5), ("D5", 2)],
    19: [("D5", 1), ("C5", .5), ("Bb4", .5), ("G4", 2)],                       # the theme's tail, broadened
    20: [("G4", 1.5), ("C5", .5), ("Eb5", 2)],
    21: [("F5", 1.5), ("Eb5", .5), ("C5", 1), ("A4", 1)],
    22: [("Bb4", 1), ("D5", 1), ("F5", 1.5), ("Eb5", .5)],
    23: [("D5", 1.5), ("C5", .5), ("A4", 1), ("F#4", 1)],
}


def play(tr, bar, items, vel, legato=.92, harmony=None, hvel=None):
    """A bar of (note | None, beats): accent on the downbeat, short notes a little softer. harmony = a second
    track that plays below() each note."""
    pos = 0.0
    for (p, d) in items:
        if p is not None:
            v = vel + (8 if pos == 0 else 0) - (6 if d <= .5 else 0)
            tr.note(bar, pos, p, d * legato, v)
            if harmony is not None:
                harmony.note(bar, pos, below(n(p), PROG[bar]), d * legato, (hvel or vel) + (v - vel))
        pos += d


def songs():
    s = Song("legend_arapaima", bpm=120, bars=32, key=KEY, stems=["main"], loudness=-17.0,
             desc="Arapaima fight: primal 3+3+2 engine of taiko, low toms, congas and shaker under a low bassoon "
                  "riff and tuba; the arapaima's rising-fourth theme in trombone and horn, a choir from the A' climax, "
                  "a broad clarinet tune in the lighter B section, a Cm-Ab-D turnaround back into the theme")

    drums = s.track("drums", DRUMS, vol=112, pan=0, reverb=32)
    taiko = s.track("taiko", GM["taiko"], vol=96, pan=-6, reverb=50)
    tuba = s.track("tuba", GM["tuba"], vol=94, pan=4, reverb=28)
    bsn = s.track("bassoon", GM["bassoon"], vol=94, pan=-22, reverb=32)
    cl = s.track("clarinet", GM["clarinet"], vol=94, pan=24, reverb=42)
    tbn = s.track("trombone", GM["trombone"], vol=112, pan=-10, reverb=50)
    hn = s.track("horn", GM["french_horn"], vol=106, pan=14, reverb=56)
    choir = s.track("choir", GM["choir"], vol=96, pan=0, reverb=72)

    # --- tuba: roots (Bb1..A2) in dotted quarters; long notes in B; the riff's 16th tresillo in the turnaround
    for b in range(32):
        r = low_root(PROG[b])
        if 16 <= b < 24:
            v = 70 if b < 20 else 76
            tuba.note(b, 0, r, 1.9, v + 8).note(b, 2, r, 1.9, v)
        elif b >= 28:
            for (beat, d, acc) in [(0, .75, 14), (.75, .75, 0), (1.5, .5, 4), (2, .75, 10), (2.75, .75, 0), (3.5, .5, 4)]:
                tuba.note(b, beat, r, d * .9, 82 + acc)
        else:
            v = 82 if b < 8 or b >= 24 else 86
            tuba.note(b, 0, r, 1.4, v + 12).note(b, 1.5, r, 1.4, v)
            if b in (9, 11, 13):   # A': the fifth (below, or above Bb1) on beat 4, short, room for the riff's pickup
                tuba.note(b, 3, r - 5 if r - 5 >= n("C2") else r + 7, .45, v - 4)
            else:
                tuba.note(b, 3, r, .9, v - 2)

    # --- theme: trombone + horn in unison (A, A', B'); the clarinet harmonises the climaxes (12-15, 28-31)
    for b, items in THEME.items():
        harm = cl if (12 <= b < 16 or b >= 28) else None
        play(tbn, b, items, THEME_VEL[b], harmony=harm, hvel=THEME_VEL[b] - 14)
        play(hn, b, items, THEME_VEL[b] - 8)
    tbn.swell(31, 0, 3, 90, 127).cc(31, 3.5, 11, 110).cc(0, 0, 11, 110)
    hn.swell(31, 0, 3, 90, 127).cc(31, 3.5, 11, 110).cc(0, 0, 11, 110)
    # the deceptive Eb at bar 16: one low-brass stab, then the trombones rest through B
    tbn.chord(16, 0, ["Bb3", "Eb4", "G4"], .9, 104)
    hn.chord(16, 0, ["Bb3", "Eb4"], .9, 90)

    # --- B tune: clarinet, the horn as a second voice below from bar 20
    for b, items in TUNE.items():
        play(cl, b, items, 84 if b < 20 else 90, legato=.96, harmony=hn if b >= 20 else None, hvel=78)

    # --- choir: close triads (G3..G4) from the climax of A' to the end, same chords tied
    prev, b = None, 12
    while b < 32:
        e = b + 1
        while e < 32 and PROG[e] == PROG[b]:
            e += 1
        v = voicing(PROG[b], prev)
        choir.chord(b, 0, v, 4 * (e - b) - .1, 74 if 16 <= b < 24 else 80)
        prev, b = v, e
    choir.swell(12, 0, 8, 55, 108).cc(16, 0, 11, 92).swell(20, 0, 8, 92, 104).cc(24, 0, 11, 104)
    choir.swell(28, 0, 15.5, 104, 127)
    # the loop lands: one short Gm "hah" on bar 0 (expression still 127 from the turnaround), then A stays bare
    choir.chord(0, 0, voicing("Gm", prev), 1.5, 86)

    # --- taiko: chord roots (Bb1..A2, with the tuba)
    def tk(b, beat, v, d=None):   # on-beat hits ring a beat, off-beat ones are cut before the next beat
        taiko.note(b, beat, low_root(PROG[b]), d or (1.0 if beat % 1 == 0 else .5), v)
    for b in range(32):
        if b < 8:
            tk(b, 0, 114 if b % 2 == 0 else 100)
            if b % 2 == 1:
                tk(b, 2.5, 84)
        elif b < 16:
            tk(b, 0, 116)
            tk(b, 1.5, 78)
            tk(b, 2.5, 90)
            if b >= 14:
                tk(b, 3.5, 86)
        elif b < 24:
            tk(b, 0, 120 if b == 16 else (96 if b % 2 == 0 else 86), 2.0)
            if b >= 20:
                tk(b, 2.5, 78 + 2 * (b - 20))
        elif b < 28:
            tk(b, 0, 118 if b == 24 else 104)
            tk(b, 2.5, 88)
        elif b < 30:
            for beat, v in [(0, 112), (1.5, 86), (2.5, 92), (3.5, 86)]:
                tk(b, beat, v)
        elif b == 30:
            for i in range(4):
                tk(b, i, 94 + 4 * i)
        else:
            for i in range(8):
                tk(b, i * .5, 82 + 4 * i, .5)

    # --- riff: bassoon throughout; the clarinet doubles it an octave up in A' 8-11 (written after the tuba and
    # the taiko so its neighbour notes can dodge them)
    for b in range(32):
        riff_bar(bsn, b, RIFF_PLAN[b], RIFF_VEL[b], bass=(tuba, taiko))
    for b in range(8, 12):
        riff_bar(cl, b, RIFF_PLAN[b], RIFF_VEL[b] - 16, octave=1, bass=(tuba, taiko))

    # --- kit (one channel): kick, low toms, congas, shaker, clave, cymbals on the sections
    def bar_pat(b, parts):
        for key, pat, v in parts:
            drums.hits(b, pat, key, vel=v)

    CONGA_A = [("mute_conga", "..x.......x.....", 56), ("hi_conga", "....x.......xx..", 68),
               ("lo_conga", ".......x.......x", 62)]
    CONGA_A2 = [("mute_conga", "..x.......x.....", 58), ("hi_conga", "....x..x....x.x.", 70),
                ("lo_conga", "...........x...x", 64)]
    CONGA_B = [("lo_conga", "X.......x.......", 62), ("mute_conga", "..x.......x.....", 54),
               ("hi_conga", "...x..x....x..x.", 66)]
    TOMS_A = [("low_tom", "X.....x.X.......", 80), ("tom2", "...x.......x....", 72), ("mid_tom", "..............x.", 66)]
    TOMS_A2 = [("low_tom", "X.....x.X.....x.", 84), ("tom2", "..xx......xx....", 70), ("mid_tom", "...............x", 64)]

    for b in range(32):
        sec = "A" if b < 8 else "A2" if b < 16 else "B" if b < 24 else "B2"
        # shaker: 16ths, the off-beat eighth leaning
        sh = [50, 32, 62, 34] if sec != "B" else [44, 28, 54, 30]
        for i in range(16):
            drums.note(b, i * .25, DR["shaker"], .2, sh[i % 4] + (4 if b >= 28 else 0))
        if sec == "A" or (sec == "B2" and b < 28):
            bar_pat(b, [("kick", "X.......x.......", 92)] + (TOMS_A if b not in (7, 23) else TOMS_A[:2]) + CONGA_A)
        elif sec == "A2":
            parts = [("kick", "X.....x.x.......", 92)] + CONGA_A2
            parts += TOMS_A2 if b != 15 else [("low_tom", "X.....x.........", 84)]
            bar_pat(b, parts)
        elif sec == "B":
            parts = [("kick", "X...............", 88), ("claves", "x..x..x...x.x...", 56)] + CONGA_B
            parts += [("low_tom", "X.....x.X......." if b >= 20 else "X.....x.........", 74)]
            if b >= 20:
                parts += [("tom2", "...........x....", 64)]
            bar_pat(b, parts)
        elif b < 30:
            bar_pat(b, [("kick", "X.....x.x.....x.", 94), ("low_tom", "X..x..x.X..x..x.", 86),
                        ("tom2", ".x...x...x...x..", 64), ("mid_tom", "..x.......x.....", 60)] + CONGA_A2)
        elif b == 30:
            bar_pat(b, [("kick", "X.......x.......", 94)] + CONGA_A2[:2])
            for i in range(16):   # 16th toms, rising
                drums.note(b, i * .25, DR["low_tom" if i % 2 == 0 else "tom2"], .2, 62 + 2 * i + (10 if i % 4 == 0 else 0))
        else:
            bar_pat(b, [("kick", "X.......x...x.x.", 90)])
            fill = ["high_tom"] * 4 + ["tom3"] * 4 + ["mid_tom"] * 4 + ["tom2"] * 2 + ["low_tom"] * 2
            for i, key in enumerate(fill):
                drums.note(b, i * .25, DR[key], .2, 76 + 2 * i + (6 if i % 4 == 0 else 0))
        # fills into the next phrase / section
        if b in (7, 23):
            for i, key in enumerate(["high_tom", "tom3", "mid_tom", "tom2"]):
                drums.note(b, 3 + i * .25, DR[key], .2, 74 + 6 * i)
        if b == 15:
            for i, key in enumerate(["high_tom", "high_tom", "tom3", "tom3", "mid_tom", "mid_tom", "tom2", "low_tom"]):
                drums.note(b, 2 + i * .25, DR[key], .2, 72 + 5 * i)
    # cymbals: the sections
    drums.note(0, 0, DR["crash"], 1, 100).note(0, 0, DR["china"], 1, 70)
    drums.note(8, 0, DR["crash"], 1, 82)
    drums.note(12, 0, DR["splash"], 1, 80)
    drums.note(16, 0, DR["china"], 1, 96).note(16, 0, DR["crash2"], 1, 84)
    drums.note(24, 0, DR["crash"], 1, 96)
    drums.note(28, 0, DR["crash2"], 1, 86)

    s.humanize(timing=0.01, velocity=5)
    return [s]
