"""
encounter — 전설어 조우 (the legend encounter, an underwater scene; README §3.3). D minor with Phrygian / chromatic
colour, 60 bpm, 16 bars = 64 s, a seamless loop in three vertical layers the game adds one by one:
lurk (eyes in the dark) -> + approach (the fish comes closer) -> + tease (it noses the lure).

Harmony: one chord map (HARM) for every stem, so any combination of layers lines up bar for bar.
  0-3    Dm   Dm    Eb    Dm          the Phrygian bII slides in and out over the tonic
  4-7    Bb/D Gm    Eb/G  Asus4-A     the Neapolitan (Eb/G) to the dominant: half cadence
  8-11   Dm   Eb    E°    Dm/F        the bass creeps up chromatically, D Eb E F ...
  12-15  Gm   Eb/G  Asus4-A  A7b9     ... to G, the climax (G6 in the tremolo, celesta cascade from G7),
                                      the dominant (b9 on top) leads back to bar 0
Bass: D D Eb D | D G G A | D Eb E F | G G A A.

lurk      warm-pad drone in open fifths (tied common tones), sustained contrabass on the bass line with bowed
          swells, five sparse low piano clusters (chord tones plus a whole step, off the beat), four reverse-cymbal
          swells into bars 4, 8, 13 and 0. No melody.
approach  cello pulse in eighths (3+3+2 accents, light; root / chord tones on the beats, a lead-in on the last
          eighth: the leading tone from below, or the chord tone above, never a semitone against the held chord;
          octave leaps and a rising register in the second half), violas holding the inner thirds.
tease     high tremolo strings (A5..G6) whose top line hovers D6-Eb6, drops to A5 at bar 8 and climbs to G6;
          the lure: the celesta plays the game's MOTIF head in D minor (A D E F— E D, bar 0; the head again at
          bar 8); glassy celesta figures (chromatic upper neighbours off the beat), denser and higher towards
          bar 13; crystal bells on single chord tones.

The game also plays a heartbeat (38-50 Hz thumps at 0.5 / 0.8 Hz) and a drone (55-50 Hz every 1.6 s): nothing here
strikes low notes on a regular quarter (or dotted-quarter) grid. The bass is sustained above D2, the cello pulse
is in even eighths with soft accents, the piano clusters are sparse, irregular and off the beat.
Only chord tones sit on the beats in every track (neighbours and lead-ins on off-beat eighths / sixteenths), and the
only semitone inside a chord (A7b9's A against the tremolo's Bb) is kept more than two octaves apart.
"""

from fk_music import Song, Key, GM, MOTIF, n

KEY = Key("D", "minor")
BARS = 16

# chord tones as pitch classes
CH = {c: [n(x + "0") % 12 for x in tones.split()] for c, tones in {
    "Dm": "D F A", "Eb": "Eb G Bb", "Bb/D": "Bb D F", "Gm": "G Bb D", "Eb/G": "Eb G Bb",
    "Asus4": "A D E", "A": "A C# E", "Edim": "E G Bb", "Dm/F": "D F A", "A7b9": "A C# E G Bb",
}.items()}
# the celesta / crystal leave the root out of A7b9 (A would rub against the tremolo's Bb)
HIGH_CH = dict(CH, A7b9=[n(x + "0") % 12 for x in "C# E G Bb".split()])

# per bar: (beat, chord) changes; a chord lasts until the next change or the bar line
HARM = [
    [(0, "Dm")], [(0, "Dm")], [(0, "Eb")], [(0, "Dm")],
    [(0, "Bb/D")], [(0, "Gm")], [(0, "Eb/G")], [(0, "Asus4"), (2, "A")],
    [(0, "Dm")], [(0, "Eb")], [(0, "Edim")], [(0, "Dm/F")],
    [(0, "Gm")], [(0, "Eb/G")], [(0, "Asus4"), (2, "A")], [(0, "A7b9")],
]
BASS = "D2 D2 Eb2 D2  D2 G2 G2 A2  D2 Eb2 E2 F2  G2 G2 A2 A2".split()

# voicings, one string per chord of HARM (so bars 7 and 14 have two)
PAD = [["D3 A3"], ["D3 A3"], ["Eb3 Bb3"], ["D3 A3"],
       ["F3 Bb3"], ["D3 Bb3"], ["Eb3 Bb3"], ["E3 A3", "E3 A3"],
       ["D3 A3"], ["Eb3 Bb3"], ["E3 Bb3"], ["F3 A3"],
       ["D3 G3"], ["Eb3 G3"], ["E3 A3", "E3 A3"], ["E3 A3"]]
VIOLA = [["F3 D4"], ["F3 D4"], ["G3 Eb4"], ["F3 D4"],
         ["F3 D4"], ["G3 D4"], ["G3 Eb4"], ["A3 D4", "A3 C#4"],
         ["A3 D4"], ["G3 Bb3"], ["G3 Bb3"], ["A3 D4"],
         ["G3 Bb3"], ["G3 Eb4"], ["A3 D4", "A3 C#4"], ["G3 C#4"]]
TREM = [["A5 D6"], ["A5 D6"], ["Bb5 Eb6"], ["A5 D6"],
        ["Bb5 D6"], ["Bb5 D6"], ["Bb5 Eb6"], ["A5 D6", "A5 C#6"],
        ["F5 A5"], ["G5 Bb5"], ["G5 Bb5"], ["A5 D6"],
        ["Bb5 D6 G6"], ["Bb5 Eb6 G6"], ["A5 D6 E6", "A5 C#6 E6"], ["Bb5 C#6 E6"]]
# cello pulse per chord: (pulse, alt, high) — the pulse follows the bass, an octave up; it climbs in bars 8-15
CELLO = [[("D3", "A2", "F3")], [("D3", "A2", "F3")], [("Eb3", "Bb2", "G3")], [("D3", "A2", "F3")],
         [("D3", "Bb2", "F3")], [("G2", "D3", "Bb2")], [("G2", "Eb3", "Bb2")],
         [("A2", "E3", "D3"), ("A2", "E3", "C#3")],
         [("D3", "A2", "F3")], [("Eb3", "Bb2", "G3")], [("E3", "Bb2", "G3")], [("F3", "D3", "A3")],
         [("G3", "D3", "Bb3")], [("G3", "Eb3", "Bb3")],
         [("A3", "E3", "D4"), ("A3", "E3", "C#4")], [("A3", "E3", "G3")]]


# FluidR3 sample zones that come out louder / softer than their neighbours (dB at equal velocity, measured);
# even() scales the velocity to compensate (FluidSynth: amplitude ~ velocity squared)
ZONE_DB = {GM["cello"]: {n("E3"): 4.5, n("F3"): 4.5, n("F#3"): 5.0, n("C#4"): -2.0, n("D4"): -2.0},
           GM["contrabass"]: {n("A2"): -7.5}}


def even(tr, p, v):
    return int(round(v * 10 ** (-ZONE_DB.get(tr.program, {}).get(p, 0.0) / 40)))


def voice(s):
    return [n(x) for x in s.split()]


def segs(bar):
    """[(start, end, chord)] for one bar."""
    row = HARM[bar]
    return [(b, row[i + 1][0] if i + 1 < len(row) else 4, c) for i, (b, c) in enumerate(row)]


def seg_at(bar, beat):
    for s in segs(bar):
        if s[0] <= beat < s[1]:
            return s
    return segs(bar)[-1]


def is_beat(pos):
    return abs(pos - round(pos)) < 1e-6


def sustain(tr, table, vel, phrase=4, release=0.06):
    """Hold every voicing of `table` for its chord; a pitch the next chord keeps is tied over, except at a phrase
    line (every `phrase` bars), where everything is re-struck. vel(bar) gives the attack. Returns the notes as
    (start, end, pitch) in absolute beats."""
    held, out = {}, []
    for bar in range(BARS):
        for (st, en, ch), voc in zip(segs(bar), table[bar]):
            t0 = bar * 4 + st
            ps = set(voice(voc))
            fresh = st == 0 and bar % phrase == 0
            for p in [p for p in held if fresh or p not in ps]:
                out.append((held.pop(p), t0, p))
            for p in ps:
                held.setdefault(p, t0)
    out += [(t0, BARS * 4, p) for p, t0 in held.items()]
    for (t0, t1, p) in sorted(out):
        b = int(t0 // 4)
        tr.note(b, t0 - 4 * b, p, t1 - t0 - release, even(tr, p, vel(b)))
    return sorted(out)


def bowed(tr, t0, t1, lo, hi, end):
    """A bowed swell (CC11) over one held note: in from lo, up to hi at 40 %, down to end."""
    b = int(t0 // 4)
    ln = t1 - t0
    tr.swell(b, t0 - 4 * b, ln * 0.4, lo, hi)
    tr.swell(b, t0 - 4 * b + ln * 0.4, ln * 0.6 - 0.05, hi, end)


# ------------------------------------------------------------------------------------------------ lurk stem
# sparse low piano clusters: (bar, beat, pitches, beats, vel) — chord tones plus a whole step, never a semitone
# against the other layers, off the beat and at irregular distances (bars 1, 5, 9, 12, 14)
CLUSTERS = [
    (1, 2.5, "D2 A2 C3 D3", 1.4, 52),       # Dm7 with C-D on top
    (5, 0.5, "G2 D3 F3 G3", 3.0, 48),       # Gm7
    (9, 1.5, "Eb2 Bb2 F3 G3", 2.3, 54),     # Eb add9
    (12, 2.5, "G2 Bb2 C3 D3", 1.4, 50),     # Gm add11
    (14, 0.5, "A2 D3 E3", 1.4, 56),         # Asus4 (clear of the C# on beat 3)
]
# reverse cymbal swells: (bar, beat, pitch, beats). The FluidR3 sample peaks ~1.8 s (D2) / 1.7 s (G2) / 1.65 s (A2)
# after the note-on, so each one lands on the next bar line; the pitch is a chord tone of both bars
SWELLS = [(3, 2.2, "D2", 1.9), (7, 2.35, "A2", 1.75), (12, 2.3, "G2", 1.8), (15, 2.35, "A2", 1.75)]


def lurk(s):
    # bowed pad, not warm_pad: FluidR3's warm / sweep / soundtrack pads render with a large DC offset
    pad = s.track("drone", GM["bowed_pad"], stem="lurk", vol=92, pan=0, reverb=92, chorus=30)
    cb = s.track("contrabass", GM["contrabass"], stem="lurk", vol=104, pan=-6, reverb=60)
    pno = s.track("piano", GM["piano"], stem="lurk", vol=92, pan=-22, reverb=96)
    rc = s.track("revcym", GM["reverse_cymbal"], stem="lurk", vol=70, pan=22, reverb=80)

    # drone: open fifths, common tones tied; breathes in two-bar waves, a little fuller in the second half
    sustain(pad, PAD, lambda b: 58 if b < 8 else 62)
    for bar in range(0, BARS, 2):
        top = 104 if bar < 8 else 112
        pad.swell(bar, 0, 4, 84, top)
        pad.swell(bar + 1, 0, 3.95, top, 84)

    # contrabass: the bass line held, re-bowed on each new pitch and at every phrase
    table = [[BASS[b]] * len(HARM[b]) for b in range(BARS)]
    for (t0, t1, p) in sustain(cb, table, lambda b: 64 if b < 8 else 70, release=0.1):
        bowed(cb, t0, t1, 78, 112 if t0 < 32 else 118, 86)

    for (bar, beat, ps, d, v) in CLUSTERS:
        pno.chord(bar, beat, voice(ps), d, v, strum=0.02)
    for (bar, beat, p, d) in SWELLS:
        rc.note(bar, beat, p, d, 74)


# -------------------------------------------------------------------------------------------- approach stem
# cello eighths: P = pulse, A = alt, H = high, L = lead into the next bar's pulse (index into the 8 eighths)
PULSE_A = ["P", "P", "A", "P", "P", "A", "P", "L"]
PULSE_B = ["P", "P", "H", "P", "A", "P", "H", "L"]
ACCENTS = (0, 3, 6)          # 3+3+2, soft: the even eighths stay in front, not a dotted-quarter thump


def lead(bar, here):
    """The last eighth of a bar: a step into the next bar's pulse. Rising, the leading tone from below when the
    chord holds it (C# into D); otherwise the chord tone just above the target (Bb into A, A into G). A chromatic
    note from below would rub a semitone against the root the bass, pad and violas are still holding."""
    nxt = n(CELLO[(bar + 1) % BARS][0][0])
    if nxt == here:
        return n(CELLO[bar][-1][1])
    if abs(nxt - here) <= 1:
        return here
    ch = CH[HARM[bar][-1][1]]
    if nxt > here and (nxt - 1) % 12 in ch:
        return nxt - 1
    for p in (nxt + 1, nxt + 2, nxt + 3):
        if p % 12 in ch:
            return p
    return here


def approach(s):
    vc = s.track("cello", GM["cello"], stem="approach", vol=124, pan=-18, reverb=50)
    va = s.track("violas", GM["viola"], stem="approach", vol=96, pan=20, reverb=70)

    for bar in range(BARS):
        pat = PULSE_A if bar < 8 else PULSE_B
        for i, role in enumerate(pat):
            bt = 0.5 * i
            st, en, ch = seg_at(bar, bt)
            pulse, alt, high = (n(x) for x in CELLO[bar][[x[2] for x in segs(bar)].index(ch)])
            p = {"P": pulse, "A": alt, "H": high}.get(role) or lead(bar, pulse)
            v = (76 if i in ACCENTS else 66) + (6 if bar >= 8 else 0)
            vc.note(bar, bt, p, 0.42, even(vc, p, v))
    # the pulse grows through the second half and settles back for the loop
    vc.cc(0, 0, 11, 92)
    vc.swell(7, 0, 4, 92, 100)
    vc.swell(8, 0, 4, 100, 94)
    vc.swell(9, 0, 20, 94, 120)
    vc.swell(14, 0, 8, 120, 92)

    # violas: the inner voices (thirds, the sus4-3, the leading tone), each chord a slow swell
    sustain(va, VIOLA, lambda b: 56 if b < 8 else 60, release=0.08)
    for bar in range(BARS):
        for (st, en, ch) in segs(bar):
            hi = 100 if bar < 8 else (106 if bar < 12 else 114)
            va.swell(bar, st, (en - st) * 0.5, 76, hi)
            va.swell(bar, st + (en - st) * 0.5, (en - st) * 0.5 - 0.05, hi, 78)


# ----------------------------------------------------------------------------------------------- tease stem
# celesta figures per bar: (beat, kind, from, notes) in sixteenths. up / down = chord tones from `from`;
# turn = a chord tone, its chromatic upper neighbour, the tone again, then two chord tones below
FIGS = {
    0: [],                                                       # the lure (LURES)
    1: [(2.25, "turn", "A5", 4)],
    2: [(0.5, "up", "G5", 4)],
    3: [(2.5, "down", "A6", 4)],
    4: [(1.25, "up", "F5", 4)],
    5: [(0.5, "turn", "D6", 4)],
    6: [(2.25, "up", "G5", 4)],
    7: [(0.25, "down", "E6", 4), (2.5, "turn", "C#6", 4)],
    8: [(2.75, "down", "D6", 4)],                               # after the lure's head
    9: [(0.25, "up", "Eb5", 5), (2.5, "turn", "Bb5", 4)],
    10: [(0.5, "turn", "G5", 4), (1.75, "up", "E5", 5)],
    11: [(0.25, "up", "D5", 4), (2.25, "up", "A5", 5)],
    12: [(0.25, "up", "D5", 7), (2.5, "up", "Bb5", 5)],          # climbing ... (a breath before the top)
    13: [(0.0, "down", "G7", 8), (2.25, "turn", "G6", 4)],        # ... the cascade from G7 (climax)
    14: [(0.5, "down", "E6", 4), (2.25, "turn", "E6", 4)],
    15: [(0.75, "down", "Bb6", 4), (2.5, "down", "E6", 3)],
}
# the lure: the game's motif (MOTIF bar 1, sol do re mi— re do) turned to D minor, A D E F— E D, high in the
# celesta, as if the legend were toying with the player's own theme: (bar, beat, notes, last note's beats).
# Displaced by an off-beat so D and F (chord tones of Dm) land on the beats and E falls between them;
# bar 8 has only the head (A D E F—), answered by a falling arpeggio
LURES = [(0, 1.5, 6, 0.9), (8, 0.5, 4, 0.7)]
# crystal bells: (bar, beat, pitch, beats, vel), one chord tone each
BELLS = [(1, 2.5, "A6", 1.4, 62), (4, 1.5, "F6", 2.4, 58), (6, 2.5, "G6", 1.4, 62), (9, 1.5, "Bb6", 2.4, 62),
         (11, 2.5, "A6", 1.4, 66), (13, 0.5, "G6", 3.3, 72), (15, 1.5, "E6", 2.4, 64)]


def tones(chord, start, count, up=True):
    """`count` chord tones from `start` (inclusive) upwards or downwards."""
    out, p = [], start
    while len(out) < count:
        if p % 12 in HIGH_CH[chord]:
            out.append(p)
        p += 1 if up else -1
    return out


def figure(tr, bar, beat, kind, start, count, vel):
    ch = seg_at(bar, beat)[2]
    p0 = n(start)
    if kind == "turn":
        t = tones(ch, p0, 3, up=False)
        ps = [t[0], t[0] + 1, t[0], t[1], t[2]][:count]
    else:
        ps = tones(ch, p0, count, up=(kind == "up"))
    for i, p in enumerate(ps):
        pos = beat + 0.25 * i
        here = seg_at(bar, pos)
        if is_beat(pos) and p % 12 not in HIGH_CH[here[2]]:
            raise ValueError(f"encounter: celesta {p} on beat {pos} of bar {bar} is not a chord tone")
        last = i == len(ps) - 1
        d = min(1.0, here[1] - pos - 0.05) if last else 0.22
        # a little louder towards the figure's top note, the beat notes leaning in
        v = vel + int(0.25 * (p - 84)) + (4 if is_beat(pos) else 0)
        tr.note(bar, pos, p, d, v)


def lure(tr, bar, beat, count, last, vel):
    """The first `count` notes of MOTIF bar 1 in D minor from bar / beat (may cross the bar line)."""
    pos = bar * 4 + beat
    for i, (d, ln) in enumerate(MOTIF[0][:count]):
        b, bt = divmod(pos, 4)
        b = int(b)
        p = KEY.deg(d, 6)
        if is_beat(bt) and p % 12 not in HIGH_CH[seg_at(b, bt)[2]]:
            raise ValueError(f"encounter: lure {p} on beat {bt} of bar {b} is not a chord tone")
        dur = last if i == count - 1 else ln * 0.9
        tr.note(b, bt, p, dur, vel + (4 if is_beat(bt) else 0) + (6 if ln > 1 else 0))
        pos += ln


def tease(s):
    tr = s.track("tremolo", GM["tremolo_strings"], stem="tease", vol=80, pan=10, reverb=86)
    ce = s.track("celesta", GM["celesta"], stem="tease", vol=100, pan=-26, reverb=96)
    cr = s.track("crystal", GM["crystal"], stem="tease", vol=112, pan=30, reverb=100)

    sustain(tr, TREM, lambda b: 58 if b < 8 else 64, release=0.08)
    # hovering waves in the first half, one long crescendo to the climax (bar 13), back down for the loop
    for bar in range(0, 8, 2):
        tr.swell(bar, 0, 4, 78, 96)
        tr.swell(bar + 1, 0, 4, 96, 78)
    tr.swell(8, 0, 24, 74, 122)
    tr.swell(14, 0, 8, 122, 78)

    for bar, figs in FIGS.items():
        for (beat, kind, start, count) in figs:
            figure(ce, bar, beat, kind, start, count, 52 if bar < 8 else (58 if bar < 12 else 64))
    for (bar, beat, count, last) in LURES:
        lure(ce, bar, beat, count, last, 56 if bar < 8 else 60)
    for (bar, beat, p, d, v) in BELLS:
        cr.note(bar, beat, p, d, v)


def songs():
    s = Song("encounter", bpm=60, bars=BARS, key=KEY, stems=["lurk", "approach", "tease"], kind="loop",
             loudness=-22, mixes=[["lurk"], ["lurk", "approach"], ["lurk", "approach", "tease"]],
             ref=["lurk", "approach"],
             desc="Legend encounter (underwater): lurk = drone pad, contrabass, sparse low piano clusters and "
                  "reverse cymbals; approach = cello eighth pulse and violas; tease = high tremolo strings, "
                  "celesta figures (the MOTIF head in D minor) and crystal bells, rising to a climax in bar 13. "
                  "D minor with a Phrygian bII")
    lurk(s)
    approach(s)
    tease(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
