"""
legend_coelacanth — the fight with the coelacanth (실러캔스), the crystal-cave legend (README §3.3): A harmonic
minor, 116 bpm, 32 bars, a seamless loop, one `main` stem.

Ancient and mystic. The engine is a marimba in running sixteenths grouped 3+3+2+3+3+2 (accents on the group
starts), over a cello "tread" (quarter + two eighths) that becomes octave-pumping eighths, contrabass and timpani
on the roots, and a kit of low toms on the backbeat, kick, shaker and tambourine.
The coelacanth's own motif is in the strings: the awakening — the tonic held, a quick scoop through the second and
third into the fifth (A — B C E—) — answered by the harmonic minor's augmented-second sigh G#–F–E over the dominant
(the living fossil turning in the dark). Kalimba doubles the theme the first time; choir pads and a crystal pad
strike each bar; the bass descends by the lament tetrachord A G# F E under the answer.

Form (one chord per bar):
  A   0-7   Am F Dm E | Am C+/G# F E        the theme in violins + kalimba, choir "aah" pad, marimba engine, tom
                                            backbeat; violas join at 4; the sigh an octave down, harp sweep into A'
  A'  8-15  Am F Dm E | F Dm E Am           violas double the theme an octave down, choir fuller, octave-pumping
                                            cellos, tambourine; the climb to D6 (bar 13), a full cadence on Am
  B   16-23 F Am/C Dm E | F G#o7 Am E       contrast: half-time drums, harp arpeggios, crystal on every half bar, a
                                            chant in two choir voices (peak A5, bar 22) answered by kalimba; the
                                            violins rest, then it builds (violas double the chant, high tremolo,
                                            pumping cellos, the marimba back in sixteenths, a snare roll)
  B'  24-31 Dm Am/C G#o7/B E | F Dm E7 E7   the motif turned upside down in a rising sequence, violas in harmony,
                                            the climax F6 (bar 29), then the turnaround on an E pedal (E Phrygian
                                            dominant): rising/falling string runs, marimba sweeps, timpani + snare
                                            rolls, a reverse cymbal and a tom fill into bar 0
"""

import itertools

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("A", "harmonic_minor")
KEY_PCS = {(KEY.pc + s) % 12 for s in KEY.steps}

# chord pitch classes
CH = {"Am": {9, 0, 4}, "F": {5, 9, 0}, "Dm": {2, 5, 9}, "E": {4, 8, 11}, "E7": {4, 8, 11, 2},
      "C+": {0, 4, 8}, "G#o7": {8, 11, 2, 5}}

# (chord, contrabass pitch) per bar
PLAN = [
    ("Am", "A1"), ("F", "F1"), ("Dm", "D2"), ("E", "E2"), ("Am", "A1"), ("C+", "G#1"), ("F", "F1"), ("E", "E1"),
    ("Am", "A1"), ("F", "F1"), ("Dm", "D2"), ("E", "E2"), ("F", "F1"), ("Dm", "D2"), ("E", "E1"), ("Am", "A1"),
    ("F", "F2"), ("Am", "C2"), ("Dm", "D2"), ("E", "E2"), ("F", "F2"), ("G#o7", "G#2"), ("Am", "A2"), ("E", "E2"),
    ("Dm", "D2"), ("Am", "C2"), ("G#o7", "B1"), ("E", "E2"), ("F", "F1"), ("Dm", "D2"), ("E7", "E2"), ("E7", "E1"),
]
BARS = len(PLAN)
PROG = [c for c, _ in PLAN]
BASS = [n(b) for _, b in PLAN]


def tones(ch, lo, hi):
    """All pitches of chord `ch` in lo..hi, ascending."""
    return [p for p in range(lo, hi + 1) if p % 12 in CH[ch]]


def voicing(ch, prev, lo, hi):
    """A close voicing (every chord tone once, within an octave) inside lo..hi, moving least from `prev`."""
    opts = [[p for p in range(lo, hi + 1) if p % 12 == pc] for pc in sorted(CH[ch])]
    best, score = None, None
    for combo in itertools.product(*opts):
        v = sorted(combo)
        if v[-1] - v[0] > 12:
            continue
        if prev:
            s = sum(min(abs(a - b) for b in prev) for a in v)
        else:
            s = abs(sum(v) / len(v) - (lo + hi) / 2)
        if score is None or s < score:
            best, score = v, s
    return best


def below(p, ch):
    """A second voice: the nearest chord tone a minor third to a major sixth under p."""
    for q in range(p - 3, p - 10, -1):
        if q % 12 in CH[ch]:
            return q
    return p - 3


def roles(bar):
    """Cello pitches: R = the bass an octave up, 5 = the chord tone nearest a fifth above R, 8 = R's octave,
    ln = a scale step next to the next bar's R (an off-beat approach)."""
    ch = PROG[bar]
    r = BASS[bar] + 12
    five = min((p for p in range(r + 3, r + 10) if p % 12 in CH[ch]), key=lambda p: abs(p - r - 7))
    nxt = BASS[(bar + 1) % BARS] + 12
    ln = next(q for q in (nxt - 1, nxt + 1, nxt - 2, nxt + 2) if q % 12 in KEY_PCS)
    return {"R": r, "5": five, "8": r + 12, "ln": ln}


# --- the coelacanth theme (violins): (note | None, beats) per bar
THEME = {
    # A: the awakening (A — B C E—), the sigh down; the awakening on iv, the augmented-second sigh G# F E
    0: [("A4", 1.5), ("B4", .25), ("C5", .25), ("E5", 2)],
    1: [("F5", 1.5), ("E5", .5), ("C5", 1), ("A4", 1)],
    2: [("D5", 1.5), ("E5", .25), ("F5", .25), ("A5", 2)],
    3: [("G#5", 1.5), ("F5", .5), ("E5", 1.5), (None, .5)],
    4: [("A4", 1.5), ("B4", .25), ("C5", .25), ("E5", 1), ("A5", 1)],
    5: [("G#5", 1.5), ("E5", .5), ("C5", 1), ("E5", 1)],                       # over the augmented C+/G#
    6: [("A5", 1), ("F5", .5), ("E5", .5), ("C5", 1), ("A4", 1)],
    7: [("B4", 1), ("G#4", .5), ("F4", .5), ("E4", 2)],                         # the sigh an octave down
    # A': the same call, then the climb B5 C6 D6 and the cadence
    8: [("A4", 1.5), ("B4", .25), ("C5", .25), ("E5", 2)],
    9: [("F5", 1.5), ("E5", .25), ("D5", .25), ("C5", 1), ("F5", 1)],
    10: [("D5", 1.5), ("E5", .25), ("F5", .25), ("A5", 2)],
    11: [("G#5", 1.5), ("A5", .5), ("B5", 2)],                                   # the sigh turned upward
    12: [("C6", 2), ("A5", 1), ("F5", 1)],
    13: [("D6", 1.5), ("C6", .5), ("A5", 1), ("F5", 1)],
    14: [("G#5", 1.5), ("F5", .5), ("E5", 1), ("B4", 1)],
    15: [("A4", 2.5), (None, 1.5)],
    # B': the awakening upside down (a falling fifth) in a rising sequence, the sigh, the climax and the
    # turnaround: Phrygian-dominant runs up and down to the leading tone
    24: [("A5", 1.5), ("F5", .25), ("E5", .25), ("D5", 2)],
    25: [("C6", 1.5), ("B5", .25), ("A5", .25), ("E5", 2)],
    26: [("D6", 1.5), ("C6", .25), ("B5", .25), ("F5", 1), ("G#5", 1)],
    27: [("B5", 1), ("G#5", .5), ("F5", .5), ("E5", 1), ("B5", 1)],
    28: [("C6", 2), ("A5", 1), ("C6", 1)],
    29: [("D6", 1), ("F6", 2), ("D6", .5), ("A5", .5)],                          # climax
    30: [("E5", .25), ("F5", .25), ("G#5", .25), ("F5", .25), ("G#5", .25), ("A5", .25), ("B5", .25), ("A5", .25),
         ("B5", .25), ("C6", .25), ("D6", .25), ("C6", .25), ("D6", 1)],
    31: [("B5", 1), ("G#5", .5), ("F5", .5), ("E5", .5), ("D5", .25), ("C5", .25), ("B4", .5), ("G#4", .5)],
}
THEME_VEL = {**{b: 90 for b in range(0, 8)}, **{b: 96 for b in range(8, 12)}, **{b: 102 for b in range(12, 16)},
             **{b: 98 for b in range(24, 28)}, **{b: 100 for b in range(28, 31)}, 31: 96}

# the B chant (choir, with a second voice): long notes, rising to A5 at bar 22, ending on the sigh
CHANT = {
    16: [("A4", 2), ("C5", 2)],
    17: [("E5", 3), ("D5", 1)],
    18: [("F5", 1.5), ("E5", .5), ("D5", 2)],
    19: [("E5", 1), ("B4", 1), ("G#4", 2)],
    20: [("A4", 1), ("C5", 1), ("F5", 2)],
    21: [("F5", 1.5), ("E5", .5), ("D5", 1), ("B4", 1)],
    22: [("C5", 1), ("E5", 1), ("A5", 2)],
    23: [("G#5", 1.5), ("F5", .5), ("E5", 2)],
}

# kalimba answers to the chant (high chord tones in the held beats)
KALIMBA_B = {
    16: [(None, 2), ("C6", .5), ("A5", .5), ("F5", .5), ("A5", .5)],
    17: [(None, 1), ("A5", .5), ("C6", .5), ("E6", 1.5), (None, .5)],
    18: [(None, 2), ("D6", .5), ("A5", .5), ("F5", .5), ("A5", .5)],
    19: [(None, 1), ("G#5", .5), ("B5", .5), ("E6", 1.5), (None, .5)],
    20: [(None, 2), ("C6", .5), ("A5", .5), ("F5", .5), ("C6", .5)],
    21: [(None, 1), ("G#5", .5), ("B5", .5), ("D6", .5), ("F6", .5), (None, 1)],
    22: [(None, 2), ("E6", .5), ("C6", .5), ("A5", .5), ("E5", .5)],
    23: [(None, 1.5), ("B5", .5), ("G#5", .5), ("F5", .5), ("E5", 1)],
}


def phrase(tr, bar, items, vel, legato=.92, octave=0, harmony=None, hvel=None, hmin=.5):
    """A bar of (note | None, beats): accent on the downbeat, short notes a little softer. harmony = a second
    track that plays below() each note of at least `hmin` beats, held over the shorter notes after it."""
    pos, out = 0.0, []
    for (p, d) in items:
        if p is not None:
            v = vel + (8 if pos == 0 else 0) - (6 if d <= .5 else 0)
            tr.note(bar, pos, n(p) + 12 * octave, d * legato, v)
            out.append((pos, d, n(p) + 12 * octave, v))
        pos += d
    assert abs(pos - 4) < 1e-6, f"bar {bar}: {pos} beats"
    if harmony is not None:
        long_ = [x for x in out if x[1] >= hmin]
        for i, (st, d, p, v) in enumerate(long_):
            end = long_[i + 1][0] if i + 1 < len(long_) else st + d
            harmony.note(bar, st, below(p, PROG[bar]), (end - st) * legato, (hvel or vel) + (v - vel))


def roll(tr, bar, beat, beats, pitch, v0, v1, step=.125):
    """A roll (timpani / snare): repeated notes from v0 to v1."""
    k = int(round(beats / step))
    for i in range(k):
        tr.note(bar, beat + i * step, pitch, step * .95, v0 + (v1 - v0) * i / max(1, k - 1))


def sweep(tr, bar, beat, beats, ch, lo, hi, v0, v1, down=False):
    """A harp / marimba sweep through the chord tones lo..hi, evenly spread over `beats`."""
    ps = tones(ch, lo, hi)
    if down:
        ps = ps[::-1]
    step = beats / len(ps)
    for i, p in enumerate(ps):
        d = min(max(step, .25) * 1.5, beats - i * step)       # let ring, but not into the next chord
        tr.note(bar, beat + i * step, p, d, v0 + (v1 - v0) * i / max(1, len(ps) - 1))


# --- the marimba engine: sixteenths 3+3+2 | 3+3+2 over the chord tones of G3..D#5 (index into them)
RUN = [0, 2, 3, 1, 2, 3, 2, 1, 0, 2, 4, 1, 2, 3, 2, 3]
RUN_ACC = {0: 14, 3: 8, 6: 8, 8: 12, 11: 8, 14: 8}
EIGHTS = [0, 2, 3, 2, 1, 3, 4, 3]                       # B: the same chord tones in eighths
UP = [0, 1, 2, 3, 1, 2, 3, 4, 2, 3, 4, 5, 3, 4, 5, 6]   # the turnaround: climbing four-note cells
DOWN = [6, 5, 4, 3, 5, 4, 3, 2, 4, 3, 2, 1, 3, 2, 1, 0]


def marimba_bar(tr, bar, pattern, vel, step=.25, ramp=0.0):
    ps = tones(PROG[bar], n("G3"), n("D#5"))
    for i, k in enumerate(pattern):
        acc = RUN_ACC.get(i, 0) if step == .25 else (8 if i % 2 == 0 else 0)
        tr.note(bar, i * step, ps[k], step * .85, vel + acc + ramp * i)


# --- cellos: "tread" (A), octave-pumping eighths (A', B'), sixteenth pedal (turnaround); (beat, beats, role, acc)
TREAD = [(0, 1, "R", 14), (1, .5, "R", 0), (1.5, .5, "5", 6), (2, 1, "R", 10), (3, .5, "R", 0), (3.5, .5, "ln", 4)]
PUMP = [(i * .5, .5, r, a) for i, (r, a) in enumerate(
    [("R", 14), ("8", 0), ("R", 4), ("8", 8), ("5", 0), ("8", 4), ("R", 10), ("ln", 2)])]
LONG = [(0, 2, "R", 8), (2, 2, "5", 0)]
PEDAL = [(i * .25, .25, "R" if i not in (7, 15) else "8", 14 if i in (0, 8) else (8 if i in (3, 6, 11, 14) else 0))
         for i in range(16)]


def cellos(tr, bar, pattern, vel, gate=.85, ramp=0.0):
    rl = roles(bar)
    for i, (beat, d, role, acc) in enumerate(pattern):
        tr.note(bar, beat, rl[role], d * gate, vel + acc + ramp * i)


def songs():
    s = Song("legend_coelacanth", bpm=116, bars=BARS, key=KEY, stems=["main"], loudness=-17.0,
             desc="Coelacanth fight: ancient and mystic. A 3+3+2 marimba sixteenth engine over cellos, timpani and a "
                  "low-tom backbeat; the coelacanth's string theme (the tonic held, a scoop up to the fifth, then the "
                  "harmonic minor's augmented-second sigh G#-F-E) with kalimba, choir and crystal pads; a hushed B "
                  "section with a two-voice choir chant over harp arpeggios, and an E Phrygian-dominant turnaround "
                  "with string runs, rolls and a reverse cymbal back into the theme")

    drums = s.track("drums", DRUMS, vol=94, pan=0, reverb=44)
    timp = s.track("timpani", GM["timpani"], vol=86, pan=-10, reverb=56)
    cb = s.track("contrabass", GM["contrabass"], vol=80, pan=6, reverb=36)
    vc = s.track("cellos", GM["cello"], vol=88, pan=-18, reverb=40)
    mar = s.track("marimba", GM["marimba"], vol=80, pan=24, reverb=58)
    vln = s.track("violins", GM["strings"], vol=124, pan=-24, reverb=60)
    vla = s.track("violas", GM["strings"], vol=98, pan=28, reverb=60)
    kal = s.track("kalimba", GM["kalimba"], vol=96, pan=-34, reverb=72)
    choir = s.track("choir", GM["choir"], vol=96, pan=4, reverb=84)
    crys = s.track("crystal", GM["crystal"], vol=86, pan=36, reverb=92)
    harp = s.track("harp", GM["harp"], vol=96, pan=-40, reverb=72)
    trem = s.track("tremolo", GM["tremolo_strings"], vol=84, pan=34, reverb=76)
    rc = s.track("revcym", GM["reverse_cymbal"], vol=76, pan=-6, reverb=60)

    # --- violins: the theme; kalimba doubles it in A, violas an octave down in A' and in harmony in B'
    for b, items in THEME.items():
        v = THEME_VEL[b]
        if 24 <= b < 30:
            phrase(vln, b, items, v, harmony=vla, hvel=v - 14)
        else:
            phrase(vln, b, items, v)
        if b < 8:
            phrase(kal, b, items, 72, legato=1.0)
        if 8 <= b < 16:
            phrase(vla, b, items, v - 6, octave=-1)
    vln.cc(0, 0, 11, 100).swell(2, 0, 6, 100, 112).swell(4, 0, 12, 104, 114)
    vln.cc(8, 0, 11, 106).swell(11, 0, 8, 106, 124).swell(14, 0, 8, 124, 104)
    vln.cc(24, 0, 11, 108).swell(27, 0, 8, 108, 120)
    vla.cc(0, 0, 11, 96).swell(28, 0, 14, 104, 124)

    # --- violas: soft inner chord (A, from bar 4); the chant an octave down (20-23); measured G#/B tremolo in the
    # turnaround
    prev = None
    for b in range(4, 8):
        v_ = voicing(PROG[b], prev, n("G3"), n("G#4"))
        vla.chord(b, 0, v_[1:], 3.9, 62)
        prev = v_
    for b in range(20, 24):
        phrase(vla, b, CHANT[b], 66 + 3 * (b - 20), legato=.97, octave=-1)
    for b, p in ((30, "G#4"), (31, "B4")):
        for i in range(16):
            vla.note(b, i * .25, n(p), .22, 64 + (b - 30) * 10 + i + (10 if i % 4 == 0 else 0))

    # --- kalimba: the answers to the chant
    for b, items in KALIMBA_B.items():
        phrase(kal, b, items, 70 + (4 if b >= 20 else 0), legato=1.0)

    # --- choir: "aah" pads (A, A', B'), the chant in two voices (B)
    prev = None
    for b in list(range(0, 16)) + list(range(24, BARS)):
        lo, hi = (n("G3"), n("G#4")) if b < 8 else ((n("A3"), n("A#4")) if b < 16 else (n("B3"), n("C5")))
        v_ = voicing(PROG[b], prev, lo, hi)
        choir.chord(b, 0, v_, 3.92, 66 if b < 8 else (70 if b < 16 else 74))
        prev = v_
    for b, items in CHANT.items():
        phrase(choir, b, items, 84 if b < 20 else 90, legato=.97, harmony=choir, hvel=(74 if b < 20 else 80))
    choir.cc(0, 0, 11, 84)
    for b in (0, 2, 4, 6):
        choir.swell(b, 0, 4, 84, 104).swell(b + 1, 0, 3.9, 104, 86)
    choir.cc(8, 0, 11, 92).swell(8, 0, 20, 92, 118).swell(14, 0, 8, 118, 92)
    choir.cc(16, 0, 11, 96).swell(16, 0, 24, 96, 120).swell(22, 0, 8, 120, 104)
    choir.cc(24, 0, 11, 96).swell(24, 0, 28, 96, 112)

    # --- crystal pad: a struck chord each bar, every half bar in B
    prev = None
    for b in range(BARS):
        v_ = voicing(PROG[b], prev, n("A4"), n("A#5"))
        if 16 <= b < 24:
            crys.chord(b, 0, v_, 1.9, 64, strum=.05).chord(b, 2, v_[1:], 1.9, 52, strum=.05)
        else:
            crys.chord(b, 0, v_, 3.9, 54 if b < 8 else 58, strum=.05)
        prev = v_

    # --- marimba: the engine
    for b in range(BARS):
        if b < 8:
            marimba_bar(mar, b, RUN, 60)
        elif b < 16:
            marimba_bar(mar, b, RUN, 66 if b != 15 else 60)
        elif b < 20:
            marimba_bar(mar, b, EIGHTS, 56, step=.5)
        elif b < 22:
            marimba_bar(mar, b, EIGHTS, 62, step=.5)
        elif b < 30:
            marimba_bar(mar, b, RUN, 60 + (b - 22) if b < 24 else 66)
        elif b == 30:
            marimba_bar(mar, b, UP, 64, ramp=1.0)
        else:
            marimba_bar(mar, b, DOWN, 74, ramp=.5)

    # --- harp: sweeps at the section joins, eighth-note arpeggios through B
    sweep(harp, 7, 2, 2, "E", n("E3"), n("E5"), 54, 84)
    sweep(harp, 15, 2, 2, "Am", n("A3"), n("C6"), 70, 46, down=True)
    for b in range(16, 24):   # (in bar 23 the sweep takes beats 2-4)
        ps = tones(PROG[b], BASS[b] + 12, BASS[b] + 34)
        harp.arp(b, 0, ps, [0, 1, 2, 3, 4, 3, 2, 1], .5, 2 if b == 23 else 4, vel=52 + (4 if b >= 20 else 0),
                 gate=1.2, accent=6)
    sweep(harp, 23, 2, 2, "E", n("E3"), n("G#5"), 54, 84)
    sweep(harp, 31, 2, 2, "E7", n("E3"), n("E6"), 52, 84)

    # --- tremolo strings: a high shimmer while B builds, then the dominant in the turnaround
    prev = None
    for b in range(20, 24):
        v_ = voicing(PROG[b], prev, n("E5"), n("F6"))
        trem.chord(b, 0, v_[-2:], 3.9, 54 + 4 * (b - 20))
        prev = v_
    trem.chord(30, 0, [n("B5"), n("D6")], 7.8, 66)
    trem.cc(0, 0, 11, 80).swell(20, 0, 16, 80, 112).cc(24, 0, 11, 90).swell(30, 0, 8, 90, 127)

    # --- cellos and contrabass
    CB_A = [(0, 2.4, 12), (2.5, 1.4, 4)]
    CB_8 = [(i * .5, .45, 12 if i in (0, 3, 6) else 0) for i in range(8)]
    for b in range(BARS):
        r = BASS[b]
        if b < 8:
            cellos(vc, b, TREAD, 82)
            for (beat, d, acc) in CB_A:
                cb.note(b, beat, r, d, 80 + acc)
        elif b < 15:
            cellos(vc, b, PUMP, 84)
            for (beat, d, acc) in CB_8:
                cb.note(b, beat, r, d, 78 + acc)
        elif b == 15:
            cellos(vc, b, TREAD[:4], 80)
            cb.note(b, 0, r, 2.4, 88)
        elif b < 20:
            cellos(vc, b, LONG, 58, gate=.97)
            cb.note(b, 0, r, 3.9, 66)
        elif b < 24:
            cellos(vc, b, PUMP, 64 + 3 * (b - 20))
            cb.note(b, 0, r, 1.9, 72).note(b, 2, r, 1.9, 66)
        elif b < 30:
            cellos(vc, b, PUMP, 86)
            for (beat, d, acc) in CB_8:
                cb.note(b, beat, r, d, 82 + acc)
        else:
            cellos(vc, b, PEDAL, 78 + 6 * (b - 30), gate=.8, ramp=.6)
            for i in range(8):   # octave eighths on E
                cb.note(b, i * .5, n("E1") if i % 2 == 0 else n("E2"), .45, 82 + 2 * i + 4 * (b - 30))

    # --- timpani: roots (D2..C#3), the downbeat and the and-of-3; rolls into each section
    def troot(b):
        p = BASS[b] % 12 + n("C2")
        return p + 12 if p < n("D2") else p

    def tp(b, beat, v, d=.9):
        timp.note(b, beat, troot(b), d, v)
    for b in range(BARS):
        if b < 8:
            tp(b, 0, 100 if b % 2 == 0 else 94)
            tp(b, 2.5, 82)
        elif b < 15:
            tp(b, 0, 110)
            tp(b, .75, 74, .4)
            tp(b, 2.5, 92)
            tp(b, 3.5, 80, .5)
        elif b == 15:
            tp(b, 0, 104, 2)
        elif b < 20:
            tp(b, 0, 70, 2)
        elif b < 23:
            tp(b, 0, 82)
            tp(b, 2.5, 72)
        elif b < 30:
            tp(b, 0, 112)
            tp(b, .75, 76, .4)
            tp(b, 2.5, 94)
            tp(b, 3.5, 84, .5)
    e2 = n("E2")
    roll(timp, 3, 3, 1, e2, 62, 88)
    roll(timp, 7, 2, 2, e2, 60, 100)
    roll(timp, 15, 2, 2, n("A2"), 84, 50)          # dying away into B
    tp(23, 0, 86)
    roll(timp, 23, 1, 3, e2, 50, 98)
    for i in range(8):
        tp(30, i * .5, 76 + 2 * i, .45)
    roll(timp, 31, 0, 4, e2, 58, 90)

    # --- reverse cymbals: ~2.5 beats at E4 (peaks after ~1.27 s), landing on bars 8, 24 and 0; E4 is a chord tone
    # of the E it swells over
    for b in (7, 23, 31):
        rc.note(b, 1.5, n("E4"), 2.5, 70 if b == 7 else 84)

    # --- kit
    def kit(b, parts):
        for key, pat, v in parts:
            drums.hits(b, pat, key, vel=v)

    def fill(b, beat, keys, v0, step=.25, dv=5):
        for i, key in enumerate(keys):
            drums.note(b, beat + i * step, DR[key], step * .9, v0 + dv * i)

    for b in range(BARS):
        if b < 8:
            kit(b, [("kick", "X.....x...x.....", 82), ("low_tom", "....x.......x...", 78),
                    ("shaker", "x.x.x.x.x.x.x.x.", 34)])
            if b % 4 == 1:
                kit(b, [("tom2", "..............x.", 62)])
        elif b < 15:
            kit(b, [("kick", "X.....x.X.x...x.", 84), ("low_tom", "....x.......x...", 74),
                    ("snare", "....x.......x...", 78), ("tom2", "..x.......x.....", 58),
                    ("tambourine", "..x...x...x...x.", 50)])
            drums.hits(b, "xxxxxxxxxxxxxxxx", "shaker", vel=26, accent=4)
        elif b == 15:
            kit(b, [("kick", "X.......", 86), ("low_tom", "....x...........", 66)])
        elif b < 20:
            kit(b, [("kick", "X.........x.....", 64), ("low_tom", "........x.......", 54),
                    ("shaker", "x.x.x.x.x.x.x.x.", 22)])
        elif b < 23:
            kit(b, [("kick", "X.....x...x.....", 76), ("low_tom", "....x.......x...", 66),
                    ("tom2", "..............x.", 56), ("shaker", "x.x.x.x.x.x.x.x.", 30)])
        elif b == 23:
            kit(b, [("kick", "X.....x.", 78), ("shaker", "xxxxxxxx........", 30)])
            roll(drums, b, 2, 2, DR["snare"], 40, 96, step=.25)
        elif b < 30:
            kit(b, [("kick", "X.....x.X.x...x.", 88), ("low_tom", "....x.......x...", 78),
                    ("snare", "....x.......x...", 84), ("tom2", "..x..x....x..x..", 60),
                    ("tambourine", "..x...x...x...x.", 54)])
            drums.hits(b, "xxxxxxxxxxxxxxxx", "shaker", vel=28, accent=4)
        elif b == 30:
            kit(b, [("kick", "X.x.x.x.X.x.x.x.", 84), ("snare", "....x.......x...", 86),
                    ("low_tom", "x..x..x.x..x..x.", 72), ("tom2", ".x..x..x.x..x..x", 58)])
            drums.hits(b, "xxxxxxxxxxxxxxxx", "shaker", vel=30, accent=4)
        else:
            kit(b, [("kick", "X...x...x...x...", 90)])
            roll(drums, b, 0, 3, DR["snare"], 44, 88, step=.25)
            fill(b, 3, ["high_tom", "tom3", "mid_tom", "low_tom"], 74, dv=4)
    fill(3, 3, ["mid_tom", "mid_tom", "low_tom", "low_tom"], 66, dv=4)
    fill(7, 2, ["high_tom", "high_tom", "tom3", "tom3", "mid_tom", "mid_tom", "low_tom", "low_tom"], 58, dv=4)
    fill(22, 3, ["mid_tom", "low_tom", "mid_tom", "low_tom"], 62, dv=3)
    # cymbals and the finger cymbal (triangle) of the chant
    drums.note(0, 0, DR["crash"], 1, 84).note(0, 0, DR["china"], 1, 52)
    drums.note(8, 0, DR["crash"], 1, 90)
    drums.note(12, 0, DR["crash2"], 1, 84)
    drums.note(16, 0, DR["splash"], 1, 50)
    for b in range(16, 24):
        drums.note(b, 0, DR["triangle"], 1, 46 if b < 20 else 54)
    drums.note(20, 0, DR["splash"], 1, 58)
    drums.note(24, 0, DR["crash"], 1, 92).note(24, 0, DR["china"], 1, 58)
    drums.note(28, 0, DR["crash2"], 1, 90)

    s.humanize(timing=0.01, velocity=5)
    return [s]
