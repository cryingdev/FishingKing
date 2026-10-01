"""
legend_great_white — the fight with the great white shark (백상아리), the open-ocean legend (README §3.3): F minor,
128 bpm, 32 bars, a seamless loop, one `main` stem.

Menace. The engine circles: 16 sixteenths grouped 3+3+3+3+2+2, cellos accenting root - fifth - octave - fifth -
third - fifth on the group heads (the shark going round), the contrabass and the taiko striking the same group heads
on the root; the kit adds a half-time snare in A and a full backbeat later. No semitone figure anywhere in the low
voices (no E-F, no X-Y-X neighbour): the low figure is built from roots, fifths, octaves and thirds only.
The shark's own motif is in the low brass (trombones): a dotted lunge up two stacked fourths into a held minor
seventh (F Bb Eb—), answered by a pentatonic dive (Db Bb Ab F—); it climbs in sequence through Ab Db Gb and,
at the climaxes, up to Db5. Dissonant "bite" clusters (C Db G Ab in the brass section, off the beat) snap at the
phrase ends; a high C7(b9) string cluster swells at the end of B.

Form (one chord per bar; Gb is the Neapolitan, E dim7 the leading-tone seventh):
  A   0-7   Fm Db Gb/Bb C | Fm Db Bbm C        the motif in trombone octaves over the circling cellos (group heads
                                               only in 0-3, full sixteenths from 4), contrabass + taiko on the group
                                               heads, half-time snare; staccato violas enter at 4; bites at 3 and 7
  A'  8-15  Fm Eb/G Ab Db | Bbm Gb Edim7 C     horns join in unison, the second trombone harmonises, tuba and low
                                               piano weight the bass (F G Ab Db Bb Gb E C), violin stabs, brass
                                               off-beat stabs; the climb peaks on Db5 (13), a dim7 descent (14), bites
  B   16-23 Db Eb Cm Fm | Db Eb Gb C           the open ocean: half time, cellos in eighths, a broad violin tune
                                               (peak Bb5 at 21) over horn guide tones and a high shimmer; trombones
                                               return softly at 20; a C7(b9) cluster swell + snare roll into B'
  B'  24-31 Fm Db Gb/Bb C | Fm Db Bbm C        the motif returns over everything (violins circling high), then
                                               higher: C F Bb— (28), Db5 doubled an octave up by the violins (29);
                                               turnaround: a held C chord crescendo, a violin run, snare + taiko roll
                                               and a last bite into the crash at bar 0
"""

import itertools

from fk_music import Song, Key, GM, DRUMS, DR, n, triad

KEY = Key("F", "minor")

# (chord, contrabass note) per bar
PLAN = [
    ("Fm", "F1"), ("Db", "Db2"), ("Gb", "Bb1"), ("C", "C2"), ("Fm", "F1"), ("Db", "Db2"), ("Bbm", "Bb1"), ("C", "C2"),
    ("Fm", "F1"), ("Eb", "G1"), ("Ab", "Ab1"), ("Db", "Db2"), ("Bbm", "Bb1"), ("Gb", "Gb1"), ("Edim7", "E1"), ("C", "C2"),
    ("Db", "Db2"), ("Eb", "Eb2"), ("Cm", "C2"), ("Fm", "F1"), ("Db", "Db2"), ("Eb", "Eb2"), ("Gb", "Gb1"), ("C", "C2"),
    ("Fm", "F1"), ("Db", "Db2"), ("Gb", "Bb1"), ("C", "C2"), ("Fm", "F1"), ("Db", "Db2"), ("Bbm", "Bb1"), ("C", "C2"),
]
BARS = len(PLAN)
PROG = [c for c, _ in PLAN]
BASS = [n(b) for _, b in PLAN]

CHORDS = {"Fm": ("F", "min"), "Db": ("Db", "maj"), "Gb": ("Gb", "maj"), "C": ("C", "maj"), "Bbm": ("Bb", "min"),
          "Eb": ("Eb", "maj"), "Ab": ("Ab", "maj"), "Edim7": ("E", "dim7"), "Cm": ("C", "min")}


def pcs(ch):
    r, q = CHORDS[ch]
    return {p % 12 for p in triad(n(r + "0"), q)}


def root_in(ch, lo):
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
    """A close voicing of `ch` (all its tones) inside lo..hi, moving as little as possible from `prev`."""
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


# --- the circling ostinato: pitch roles per bar, rhythms as (beat, beats, role, accent)
def roles(bar, base=None):
    """R = the contrabass an octave up (or `base`), 5 = the chord tone nearest a fifth above R, 3 = the chord tone
    nearest a third above R, 8 = R's octave. Only chord tones: the low figure never moves by a semitone."""
    ch = PROG[bar]
    r = BASS[bar] + 12 if base is None else base
    up = [p for p in range(r + 2, r + 10) if p % 12 in pcs(ch)]
    five = min((p for p in up if p >= r + 3), key=lambda p: abs(p - r - 7))
    third = min(up, key=lambda p: abs(p - r - 3.5))
    return {"R": r, "5": five, "3": third, "8": r + 12}


HEADS = [0, .75, 1.5, 2.25, 3, 3.5]    # the 3+3+3+3+2+2 group heads (beats)
CIRCLE = [(i * .25, .25, r, a) for i, (r, a) in enumerate(
    [("R", 16), ("R", 0), ("R", 3), ("5", 12), ("R", 0), ("R", 3), ("8", 14), ("R", 0),
     ("R", 3), ("5", 12), ("R", 0), ("R", 3), ("3", 14), ("R", 0), ("5", 12), ("R", 2)])]
HEADS_ONLY = [(b, .75 if b < 3 else .5, r, a) for b, r, a in zip(HEADS, ["R", "5", "8", "5", "3", "5"],
                                                                [14, 6, 10, 6, 10, 4])]
EIGHTHS = [(0, .5, "R", 12), (.5, .5, "R", 0), (1, .5, "5", 4), (1.5, .5, "R", 2),
           (2, .5, "8", 8), (2.5, .5, "R", 0), (3, .5, "5", 4), (3.5, .5, "3", 2)]


def ostinato(tr, bar, pattern, vel, gate=.85, base=None, ramp=0.0):
    rl = roles(bar, base)
    for i, (beat, d, role, acc) in enumerate(pattern):
        tr.note(bar, beat, rl[role], d * gate, vel + acc + ramp * i)


# --- the shark motif (trombones): (note | None, beats) per bar
THEME = {
    # A: the lunge up two fourths into a held seventh, the pentatonic dive; in sequence on the Neapolitan
    0: [("F3", .75), ("Bb3", .25), ("Eb4", 3)],
    1: [("Db4", .75), ("Bb3", .25), ("Ab3", 1), ("F3", 2)],
    2: [("Bb3", .75), ("Db4", .25), ("Gb4", 3)],
    3: [("G4", .75), ("E4", .25), ("C4", 1), ("G3", 2)],
    4: [("F3", .75), ("Bb3", .25), ("Eb4", 1), ("Ab4", 2)],                  # three fourths: F Bb Eb Ab
    5: [("F4", .75), ("Eb4", .25), ("Db4", 1), ("Ab3", 2)],
    6: [("Db4", .75), ("F4", .25), ("Bb4", 2), ("Ab4", .5), ("F4", .5)],
    7: [("G4", .75), ("E4", .25), ("C4", 2), (None, 1)],
    # A': the lunge climbs Fm - Ab - Bbm, the peak on Db5 over the Neapolitan, a dim7 descent, the low C
    8: [("F3", .75), ("Bb3", .25), ("Eb4", 3)],
    9: [("Db4", .75), ("Bb3", .25), ("G3", 1), ("Eb3", 2)],
    10: [("Ab3", .75), ("Db4", .25), ("Gb4", 3)],
    11: [("F4", .75), ("Eb4", .25), ("Db4", 1.5), ("Bb3", .5), ("Ab3", 1)],
    12: [("Bb3", .75), ("Eb4", .25), ("Ab4", 2), ("Bb4", 1)],
    13: [("Db5", 1.5), ("Bb4", .5), ("Gb4", 1), ("Db4", 1)],
    14: [("Bb4", .75), ("G4", .25), ("E4", 1), ("Db4", 1), ("Bb3", 1)],
    15: [("C4", 1.5), ("G3", .5), ("C3", 1), (None, 1)],
    # B': the motif returns, then higher (C F Bb—, a 4-3 suspension), the peak again, the held dominant
    24: [("F3", .75), ("Bb3", .25), ("Eb4", 3)],
    25: [("Db4", .75), ("Bb3", .25), ("Ab3", 1), ("F3", 2)],
    26: [("Bb3", .75), ("Db4", .25), ("Gb4", 3)],
    27: [("G4", .75), ("E4", .25), ("C4", 1), ("E4", 1), ("G4", 1)],
    28: [("C4", .75), ("F4", .25), ("Bb4", 2), ("Ab4", 1)],
    29: [("Db5", 1.5), ("Bb4", .5), ("Ab4", 1), ("F4", 1)],
    30: [("Bb4", 1.5), ("Ab4", .5), ("F4", 1), ("Db4", 1)],
    31: [("C4", 3), (None, 1)],
}
THEME_VEL = {**{b: 86 for b in range(0, 8)}, **{b: 94 for b in range(8, 12)}, **{b: 100 for b in range(12, 16)},
             **{b: 98 for b in range(24, 28)}, **{b: 104 for b in range(28, 32)}}

# the B tune (violins): broad, the open ocean; peak Bb5 at bar 21
TUNE = {
    16: [("Ab4", 1.5), ("Bb4", .5), ("Db5", 1), ("F5", 1)],
    17: [("Eb5", 2), ("G5", 1), ("F5", .5), ("Eb5", .5)],
    18: [("G5", 1.5), ("F5", .5), ("Eb5", 1), ("C5", 1)],
    19: [("Ab4", 1.5), ("Bb4", .5), ("C5", 2)],
    20: [("Db5", 1.5), ("Eb5", .5), ("F5", 1), ("Ab5", 1)],
    21: [("Bb5", 2), ("G5", 1), ("Eb5", .5), ("F5", .5)],
    22: [("Gb5", 2), ("Db5", 1), ("Bb4", 1)],
    23: [("C5", 1.5), (None, 2.5)],
}

# horn guide tones in B (two voices, whole notes)
GUIDE = {16: ("Ab3", "Db4"), 17: ("Bb3", "Eb4"), 18: ("G3", "C4"), 19: ("Ab3", "C4"),
         20: ("Ab3", "F4"), 21: ("G3", "Eb4"), 22: ("Bb3", "Db4"), 23: ("G3", "C4")}

BITE = ["C3", "Db3", "G3", "Ab3"]   # the bite: two minor seconds a fifth apart, always off the beat
CLUSTER = ["Bb4", "C5", "Db5", "E5", "G5"]   # C7(b9) packed close: the end-of-B swell


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
    """A roll: repeated notes from v0 to v1."""
    k = int(round(beats / step))
    for i in range(k):
        tr.note(bar, beat + i * step, pitch, step * .95, v0 + (v1 - v0) * i / max(1, k - 1))


def songs():
    s = Song("legend_great_white", bpm=128, bars=BARS, key=KEY, stems=["main"], loudness=-17.0,
             desc="Great white fight: menace. A circling 3+3+3+3+2+2 sixteenth engine (cellos, contrabass, taiko) "
                  "under the shark's low-brass motif (a lunge up two fourths into a held seventh, a pentatonic "
                  "dive), Neapolitan and dim7 colour, off-beat brass 'bite' clusters; a broad open-ocean B section "
                  "with a violin tune, a C7(b9) cluster swell, and a crescendo turnaround with taiko and snare rolls")

    drums = s.track("drums", DRUMS, vol=98, pan=0, reverb=36)
    taiko = s.track("taiko", GM["taiko"], vol=88, pan=-6, reverb=48)
    piano = s.track("piano", GM["piano"], vol=92, pan=4, reverb=40)
    cb = s.track("contrabass", GM["contrabass"], vol=94, pan=10, reverb=30)
    vc = s.track("cellos", GM["cello"], vol=127, pan=-20, reverb=36)
    vla = s.track("violas", GM["strings"], vol=88, pan=24, reverb=48)
    vln = s.track("violins", GM["strings"], vol=100, pan=-30, reverb=58)
    trem = s.track("tremolo", GM["tremolo_strings"], vol=84, pan=32, reverb=70)
    tbn = s.track("trombone", GM["trombone"], vol=112, pan=-8, reverb=50)
    tbn2 = s.track("trombone2", GM["trombone"], vol=104, pan=10, reverb=50)
    tuba = s.track("tuba", GM["tuba"], vol=92, pan=2, reverb=34)
    hn = s.track("horns", GM["french_horn"], vol=98, pan=18, reverb=58)
    brass = s.track("brass", GM["brass"], vol=86, pan=-14, reverb=44)

    # --- cellos: group heads (0-3), the full circle (4-15, 24-31), eighths in B, a crescendo of roots at 23 and 31
    for b in range(BARS):
        if b < 4:
            ostinato(vc, b, HEADS_ONLY, 86, gate=.9)
        elif b < 8:
            ostinato(vc, b, CIRCLE, 80)
        elif b < 16:
            ostinato(vc, b, CIRCLE, 84)
        elif b < 23:
            ostinato(vc, b, EIGHTHS, 74 + (4 if b >= 20 else 0), gate=.85)
        elif b == 23:
            r = roles(b)
            for i in range(16):
                vc.note(b, i * .25, r["R"] if i % 3 else r["5"], .21, 70 + 2 * i)
        elif b < 31:
            ostinato(vc, b, CIRCLE, 90)
        else:
            ostinato(vc, b, CIRCLE, 84, ramp=1.2)
    vc.cc(0, 0, 11, 98).cc(4, 0, 11, 102).cc(8, 0, 11, 108).cc(16, 0, 11, 92)
    vc.swell(20, 0, 16, 92, 112).cc(24, 0, 11, 116).swell(31, 0, 3.5, 112, 122)

    # --- contrabass: the root on the group heads (A, A', B'), long notes in B
    for b in range(BARS):
        r = BASS[b]
        if 16 <= b < 23:
            cb.note(b, 0, r, 3.9, 74 + (4 if b >= 20 else 0))
        elif b == 23:
            cb.note(b, 0, r, 3.9, 84)
        else:
            base = 80 if b < 8 else (84 if b < 16 else 88)
            for beat, acc in zip(HEADS, [14, 0, 8, 0, 10, 2]):
                cb.note(b, beat, r, .7 if beat < 3 else .45, base + acc)

    # --- trombones: octaves in A and at the start of B', harmony in A' and the climb; horns in unison from A'
    for b, items in THEME.items():
        v = THEME_VEL[b]
        if b < 8 or 24 <= b < 28:
            phrase(tbn, b, items, v)
            phrase(tbn2, b, items, v - 12, octave=-1)
        else:
            phrase(tbn, b, items, v, harmony=tbn2, hvel=v - 10)
        if b >= 8:
            phrase(hn, b, items, v - 8)
        if 28 <= b < 31:
            phrase(vln, b, items, v - 14, legato=.96, octave=1)
    # the held dominant at 31: tbn C4 + tbn2 G3 (its harmony) + horns C4 E4 + tuba C2, a breath on beat 3
    hn.note(31, 0, n("E4"), 2.76, 92)
    for tr, base in ((tbn, 106), (tbn2, 106), (hn, 104)):
        tr.cc(0, 0, 11, base).cc(8, 0, 11, base + 6).cc(24, 0, 11, base + 10).swell(31, 0, 2.75, base - 14, 120)

    # --- B: trombones rest, then return softly as root + fifth (20-22) and swell on C (23); tuba under them
    for b in range(20, 24):
        lo = root_in(PROG[b], n("C3")) if PROG[b] != "Gb" else n("Gb2")
        fifth = lo + 7
        v = 60 + 6 * (b - 20)
        tbn2.note(b, 0, lo, 3.9, v)
        tbn.note(b, 0, fifth, 3.9, v)
        tuba.note(b, 0, BASS[b] + 12 if BASS[b] < n("C2") else BASS[b], 3.9, v + 4)
    tbn.cc(20, 0, 11, 96)
    tbn2.cc(20, 0, 11, 96)
    tbn.swell(23, 0, 4, 90, 127)
    tbn2.swell(23, 0, 4, 90, 127)
    tbn.cc(24, 0, 11, 116)
    tbn2.cc(24, 0, 11, 116)

    # --- tuba: sustained roots under the theme in A' and B' (D1..F4: the bass an octave up below C2)
    for b in list(range(8, 16)) + list(range(24, BARS)):
        p = BASS[b] + 12 if BASS[b] < n("C2") else BASS[b]
        tuba.note(b, 0, p, 2.9 if b == 31 else 3.9, 74 + (6 if b >= 24 else 0))

    # --- horns in B: two-voice guide tones (the tune's harmony), resting under the cluster at 23
    for b, (lo, hi) in GUIDE.items():
        d = 1.4 if b == 23 else 3.9
        v = 70 + (6 if b >= 20 else 0)
        hn.note(b, 0, n(lo), d, v)
        hn.note(b, 0, n(hi), d, v + 4)
    hn.cc(16, 0, 11, 90).swell(20, 0, 8, 90, 108)

    # --- violas: staccato eighth chords (two upper tones of a close triad, C4..C5), accents on the group heads
    prev = None
    for b in list(range(4, 16)) + list(range(24, BARS)):
        v_ = voicing(PROG[b], prev, 60, 72)
        prev = v_
        top = v_[-2:]
        base = 58 if b < 8 else (64 if b < 16 else 68)
        for i, acc in enumerate([12, 0, 2, 10, 0, 2, 10, 6]):
            vla.chord(b, i * .5, top, .3, base + acc + (2 * i if b == 31 else 0))

    # --- violins: stabs on the group heads in A' (G4..G5 close chords), the B tune, the high circle in B'
    prev = None
    for b in range(8, 16):
        v_ = voicing(PROG[b], prev, 67, 79)
        prev = v_
        hits = [(0, 14), (1.5, 6), (3, 10)] if b != 15 else [(0, 14)]
        for beat, acc in hits:
            vln.chord(b, beat, v_, .35, 66 + acc + (4 if b >= 12 else 0))
    for b, items in TUNE.items():
        phrase(vln, b, items, 90 if b < 20 else 96, legato=.97)
    for b in range(24, 28):
        ostinato(vln, b, CIRCLE, 56, gate=.7, base=root_in(PROG[b], n("F4")))
    # the run into bar 0: G4 .. C6 over C7, chord tones on the beats, the b9 (Ab) passing (bar 30 ended on Db5,
    # so the run starts away from it: no semitone turn back)
    for i, p in enumerate(["G4", "Bb4", "C5", "E5", "G5", "Ab5", "Bb5", "C6"]):
        vln.note(31, 2 + i * .25, n(p), .24, 70 + 5 * i)
    vln.cc(0, 0, 11, 100).cc(16, 0, 11, 92).swell(16, 0, 14, 92, 110).swell(20, 0, 6, 104, 120)
    vln.swell(22, 0, 6, 120, 100).cc(24, 0, 11, 104).swell(28, 0, 8, 104, 122).cc(31, 0, 11, 110)

    # --- tremolo: a high sustain in A' 12-15, the open-ocean shimmer in B, the C7(b9) cluster swell at 23, B'
    prev = None
    for b in list(range(12, 16)) + list(range(16, 23)) + list(range(24, BARS)):
        lo, hi = (82, 94) if 16 <= b < 23 else (77, 89)
        v_ = voicing(PROG[b], prev, lo, hi)
        prev = v_
        trem.chord(b, 0, v_[-2:], 3.9, 52 if b < 16 else (48 if b < 23 else 56))
    trem.chord(23, 1.5, [n(p) for p in CLUSTER], 2.45, 76)
    trem.cc(0, 0, 11, 90).cc(23, 0, 11, 40).swell(23, 1.5, 2.4, 40, 127).cc(24, 0, 11, 92)

    # --- brass section: off-beat power-chord stabs (A', B') and the bite clusters at the phrase ends
    for b in list(range(8, 12)) + list(range(24, 31)):
        r = root_in(PROG[b], n("C3"))
        stab = [r, r + 7, r + 12] if PROG[b] != "Edim7" else [r, r + 6, r + 12]
        for beat in (1.5, 3.5):
            if beat == 3.5 and b == 27:
                continue
            brass.chord(b, beat, stab, .3, 72)
    for b, beat, v in [(3, 3.5, 92), (7, 3.5, 98), (15, 2.5, 104), (15, 3.5, 110), (27, 3.5, 104), (31, 3.5, 112)]:
        brass.chord(b, beat, [n(p) for p in BITE], .3, v)

    # --- piano: low octaves on the downbeat and beat 3 in A' and B' (the hammer under the strings)
    for b in list(range(8, 16)) + list(range(24, BARS)):
        r = BASS[b]
        piano.chord(b, 0, [r, r + 12], 1.4, 74)
        if b != 31:
            piano.chord(b, 3, [r, r + 12], .45, 62)

    # --- taiko: the group heads on the root (C2..B2); half time in B; rolls into B' and into bar 0
    def tk(b, beat, v, d=.5):
        taiko.note(b, beat, root_in(PROG[b], n("C2")), d, v)

    for b in range(BARS):
        if b < 8:
            for beat, v in [(0, 104), (1.5, 82), (3, 94), (3.5, 80)]:
                tk(b, beat, v)
        elif b < 16:
            for beat, v in zip(HEADS, [108, 78, 88, 78, 98, 84]):
                tk(b, beat, v)
        elif b < 20:
            tk(b, 0, 104, 1)
            tk(b, 2.5, 76)
        elif b < 23:
            for beat, v in [(0, 106), (1.5, 78), (2.5, 84), (3, 80)]:
                tk(b, beat, v)
        elif b == 23:
            tk(b, 0, 100)
            for i in range(8):
                tk(b, 2 + i * .25, 66 + 6 * i, .24)
        elif b < 31:
            for beat, v in zip(HEADS, [108, 80, 90, 80, 92, 84]):
                tk(b, beat, v)
            if b == 30:
                tk(b, 3.75, 90, .24)
        else:
            for i in range(16):
                tk(b, i * .25, 64 + 2.4 * i, .24)

    # --- kit: kick on the group heads, half-time snare (A), backbeat (A', B'), toms, rolls and fills
    def kit(b, parts):
        for key, pat, v in parts:
            drums.hits(b, pat, key, vel=v)

    def fill(b, beat, keys, v0, step=.25, dv=5):
        for i, key in enumerate(keys):
            drums.note(b, beat + i * step, DR[key], step * .9, v0 + dv * i)

    for b in range(BARS):
        if b < 8:
            parts = [("kick", "X.....x.....x...", 80), ("snare", "........X.......", 72)]
            if b % 4 == 1:
                parts.append(("low_tom", "..........x...x.", 62))
            if b >= 4:
                parts.append(("pedal_hat", "x...x...x...x...", 40))
            kit(b, parts)
        elif b < 16:
            parts = [("kick", "X.....x.....x.x.", 78), ("snare", "....X.......X...", 80),
                     ("hat", "x.x.x.x.x.x.x.x.", 34)]
            if b % 4 != 3:
                parts.append(("low_tom", "..........x.....", 62))
            kit(b, parts)
        elif b < 20:
            kit(b, [("kick", "X...............", 82), ("low_tom", "........x.......", 58),
                    ("ride", "x...x...x...x...", 36)])
        elif b < 23:
            kit(b, [("kick", "X.........x.....", 82), ("snare", "........x.......", 70),
                    ("ride", "x.x.x.x.x.x.x.x.", 32), ("low_tom", "..............x.", 60)])
        elif b == 23:
            kit(b, [("kick", "X...............", 84)])
            roll(drums, b, 1, 3, DR["snare"], 40, 100, step=.125)
        elif b < 31:
            parts = [("kick", "X.....x.....x.x.", 80), ("snare", "....X.......X...", 78)]
            if b >= 28:
                parts.append(("tom2", "..x..x....x..x..", 62))
            kit(b, parts)
            drums.hits(b, "xxxxxxxxxxxxxxxx", "hat", vel=26, accent=3)
        else:
            kit(b, [("kick", "X.....x.....x...", 84)])
            roll(drums, b, 0, 3, DR["snare"], 44, 96, step=.125)
            fill(b, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 90, dv=6)
    fill(3, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 66, dv=5)
    fill(7, 2, ["high_tom", "high_tom", "tom3", "tom3", "mid_tom", "mid_tom", "low_tom", "low_tom"], 62, dv=4)
    fill(11, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 72, dv=5)
    fill(15, 2, ["snare", "high_tom", "tom3", "snare", "mid_tom", "tom2", "low_tom", "low_tom"], 70, dv=5)
    fill(27, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 78, dv=5)
    # cymbals: the sections
    drums.note(0, 0, DR["crash"], 1, 100).note(0, 0, DR["china"], 1, 72)
    drums.note(4, 0, DR["splash"], 1, 64)
    drums.note(8, 0, DR["crash"], 1, 92)
    drums.note(12, 0, DR["crash2"], 1, 86)
    drums.note(16, 0, DR["china"], 1, 66).note(16, 0, DR["crash2"], 1, 56)
    drums.note(20, 0, DR["splash"], 1, 60)
    drums.note(24, 0, DR["crash"], 1, 100).note(24, 0, DR["china"], 1, 76)
    drums.note(28, 0, DR["crash"], 1, 92)

    s.humanize(timing=0.01, velocity=5)
    return [s]
