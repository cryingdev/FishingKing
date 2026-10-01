"""
stage_ocean — 먼바다 (open ocean, fishing from a boat). Bb major, 88 bpm, 24 bars = 65.5 s, loop.

A barcarolle: every stem rocks in triplets (a 12/8 feel inside 4/4 bars), like a boat on a long swell.
Form (8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A  (0-7)    Bb  Gm  Eb  F | Bb/D  Eb  Cm7  Fsus4-F          bass falls to F2, rises again from D3; half cadence
  A' (8-15)   Bb  F/A  Gm  Eb | Cm7  Dm  Eb-F7  Bb            full cadence, the bass walks Bb2-C3-D3 up into B
  B  (16-23)  Eb  Dm  Cm7  Gm/Bb | Ab  Eb/G  Cm7  Fsus4-F     the bass steps down Eb3..G2 under a rising tune;
                                                              the borrowed Ab (bVII) carries the climax (Eb6, bar 20)
day    French horn sings the tune in A; strings take it an octave up in A' and B while the horn plays a counter-line
       and doubles the climax; harp rocking triplet arpeggios, low strings on the roots, wide string chords
       (left / right), warm pad; all of them breathe in two-bar swells
night  sweep pad, soft low strings on the roots, sparse piano (bass, a thinner tune that skips the first half of A',
       a few high ripples)
fight  kick / toms / snare in 12/8 with rolls into each section, contrabass galloping under the base bass,
       cello triplet ostinato, brass stabs, timpani on the roots
The tune rests in bars 3, 7, 11, 19, 22, 23 (night: also 8-10) so the sea ambience can breathe.
Only chord tones sit on the beats in every track (passing tones on off-beat triplets), so the stems never clash.
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("Bb", "major")
T = 1 / 3          # one triplet eighth: 12 steps per bar

# chord tones as pitch classes
CH = {c: [n(x + "0") % 12 for x in tones] for c, tones in {
    "Bb": ["Bb", "D", "F"], "Gm": ["G", "Bb", "D"], "Eb": ["Eb", "G", "Bb"], "F": ["F", "A", "C"],
    "F7": ["F", "A", "C", "Eb"], "Fsus4": ["F", "Bb", "C"], "Cm7": ["C", "Eb", "G", "Bb"],
    "Dm": ["D", "F", "A"], "Ab": ["Ab", "C", "Eb"],
}.items()}

# one row per bar: (beat, chord, bass)
A_SEC = [
    [(0, "Bb", "Bb2")],
    [(0, "Gm", "G2")],
    [(0, "Eb", "Eb2")],
    [(0, "F", "F2")],
    [(0, "Bb", "D3")],
    [(0, "Eb", "Eb3")],
    [(0, "Cm7", "C3")],
    [(0, "Fsus4", "F2"), (2, "F", "F2")],
]
A2_SEC = [
    [(0, "Bb", "Bb2")],
    [(0, "F", "A2")],
    [(0, "Gm", "G2")],
    [(0, "Eb", "Eb2")],
    [(0, "Cm7", "C3")],
    [(0, "Dm", "D3")],
    [(0, "Eb", "Eb3"), (2, "F7", "F3")],
    [(0, "Bb", "Bb2")],
]
B_SEC = [
    [(0, "Eb", "Eb3")],
    [(0, "Dm", "D3")],
    [(0, "Cm7", "C3")],
    [(0, "Gm", "Bb2")],
    [(0, "Ab", "Ab2")],
    [(0, "Eb", "G2")],
    [(0, "Cm7", "C3")],
    [(0, "Fsus4", "F2"), (2, "F", "F2")],
]
PROG = A_SEC + A2_SEC + B_SEC
BARS = len(PROG)

# passing notes of the base bass: (beat, pitch)
WALK = {15: [(2, "C3"), (3, "D3")],            # Bb2 - C3 - D3 -> Eb3
        23: [(3 + 2 * T, "A2")]}               # leading tone back to Bb2 at bar 0


def section(bar):
    return "A" if bar < 8 else ("A2" if bar < 16 else "B")


def segs(bar):
    """[(start, end, chord, bass)] for one bar (bass as a MIDI number)."""
    row = PROG[bar]
    out = []
    for i, (b, c, bass) in enumerate(row):
        e = row[i + 1][0] if i + 1 < len(row) else 4
        out.append((b, e, c, n(bass)))
    return out


def seg_at(bar, beat):
    for s in segs(bar):
        if s[0] <= beat < s[1]:
            return s
    return segs(bar)[-1]


def bass_line(bar):
    """[(start, end, pitch)]: the base bass of one bar, chord basses plus walking notes, repeated pitches merged."""
    pts = sorted([(st, bass) for (st, en, ch, bass) in segs(bar)] + [(b, n(p)) for (b, p) in WALK.get(bar, [])])
    out = []
    for (b, p) in pts:
        if out and out[-1][2] == p:
            continue
        if out:
            out[-1] = (out[-1][0], b, out[-1][2])
        out.append((b, 4, p))
    return out


def bass_at(bar, beat):
    for (st, en, p) in bass_line(bar):
        if st <= beat + 1e-6 < en:
            return p
    return bass_line(bar)[-1][2]


def voicing(chord, lo, count, triad=False):
    """The first `count` chord tones at or above `lo` (closed position); triad=True leaves out a seventh."""
    pcs = CH[chord][:3] if triad else CH[chord]
    out, p = [], lo
    while len(out) < count:
        if p % 12 in pcs:
            out.append(p)
        p += 1
    return out


def harp_tones(chord, bass):
    """The bass, then the chord tones above it; below D3 only the bass's fifth or octave (no muddy low thirds)."""
    ups = [p for p in voicing(chord, bass + 1, 12) if p >= n("D3") or (p - bass) % 12 in (0, 7)]
    return [bass] + ups


def clip(bar, beat, dur):
    """Shorten a note so it never rings into the next chord."""
    end = seg_at(bar, beat)[1]
    return max(0.1, min(dur, end - beat - 0.04))


def in_key_step(p, up):
    p += 1 if up else -1
    while not KEY.in_key(p):
        p += 1 if up else -1
    return p


# ------------------------------------------------------------------------------------------------------ tunes
# (pitch | None, beats) per bar; bars not listed are rests. The lilt is the barcarolle cell 2/3 + 1/3.
HORN_A = {
    # phrase 1: rises to Eb5 and settles on Bb4
    0: [("F4", 2 * T), ("Bb4", T), ("D5", 2), ("C5", 2 * T), ("Bb4", T)],
    1: [("G4", 1 + 2 * T), ("A4", T), ("Bb4", 1), ("D5", 1)],
    2: [("Eb5", 1 + 2 * T), ("D5", T), ("Bb4", 1 + 2 * T)],
    # phrase 2: the same cell a third higher, peak F5, half cadence on C5
    4: [("Bb4", 2 * T), ("D5", T), ("F5", 2), ("D5", 2 * T), ("C5", T)],
    5: [("Bb4", 1 + 2 * T), ("C5", T), ("Eb5", 1), ("Bb4", 2 * T), ("D5", T)],
    6: [("Eb5", 1 + 2 * T), ("D5", T), ("C5", 2)],
}
# strings: A' an octave up (peak D6), B a new rising tune with the climax Eb6 over Ab
STRINGS_TUNE = {
    8: [("D5", 2 * T), ("F5", T), ("Bb5", 2), ("C6", 2 * T), ("D6", T)],
    9: [("C6", 1 + 2 * T), ("Bb5", T), ("A5", 1), ("F5", 1)],
    10: [("G5", 1 + 2 * T), ("A5", T), ("Bb5", 1 + 2 * T)],
    12: [("Eb5", 2 * T), ("G5", T), ("C6", 2), ("Bb5", 2 * T), ("G5", T)],
    13: [("D6", 1 + 2 * T), ("C6", T), ("A5", 1), ("F5", 1)],
    14: [("G5", 1 + 2 * T), ("Bb5", T), ("A5", 1 + 2 * T), ("C6", T)],
    15: [("Bb5", 2)],
    16: [("Eb5", 2 * T), ("G5", T), ("Bb5", 2), ("G5", 2 * T), ("Bb5", T)],
    17: [("A5", 1), ("D6", 1 + 2 * T), ("C6", T), ("A5", 1)],
    18: [("G5", 1 + 2 * T), ("Bb5", T), ("C6", 2)],
    20: [("Eb6", 2), ("C6", 1), ("Bb5", 2 * T), ("Ab5", T)],
    21: [("G5", 1 + 2 * T), ("F5", T), ("Eb5", 2)],
}
# horn counter-line under the strings (rising against the falling tune), then the climax an octave below
HORN_COUNTER = {
    12: [("G4", 2), ("Eb4", 2)],
    13: [("F4", 2), ("A4", 2)],
    14: [("Bb4", 2), ("C5", 2)],
    15: [("D5", 2)],
    16: [("Bb4", 2), ("G4", 2)],
    17: [("F4", 2), ("A4", 2)],
    18: [("G4", 2), ("Eb4", 2)],
    20: [(n(p) - 12, d) for (p, d) in STRINGS_TUNE[20]],
    21: [(n(p) - 12, d) for (p, d) in STRINGS_TUNE[21]],
}
# night piano: the long notes only, A' and B an octave lower (darker); the first half of A' is left out
NIGHT_TUNE = {
    0: [("F4", 2 * T), ("Bb4", T), ("D5", 3)],
    1: [("G4", 2), ("Bb4", 1), ("D5", 1)],
    2: [("Eb5", 2), ("Bb4", 2)],
    4: [(None, 1), ("F5", 2), ("D5", 1)],
    5: [("Bb4", 2), ("Eb5", 2)],
    6: [("Eb5", 2), ("C5", 2)],
    12: [(None, 1), ("C5", 2), ("Bb4", 1)],
    13: [("D5", 2), ("A4", 1), ("F4", 1)],
    14: [("G4", 2), ("A4", 2)],
    15: [("Bb4", 2)],
    16: [(None, 1), ("Bb4", 2), ("G4", 1)],
    17: [("A4", 1), ("D5", 2), ("A4", 1)],
    18: [("G4", 2), ("C5", 2)],
    20: [("Eb5", 2), ("C5", 2)],
    21: [("G4", 2), ("Eb4", 2)],
}

SEC_VEL = {"A": 72, "A2": 76, "B": 82}


def write_tune(track, tune, center, shift=0, legato=0.95):
    """Phrase dynamics: louder sections, higher notes a little louder, off-beat triplets lighter, the climax up."""
    for bar, items in tune.items():
        pos = 0.0
        for (nm, d) in items:
            if nm is not None:
                p = n(nm) if isinstance(nm, str) else nm
                v = SEC_VEL[section(bar)] + shift + 0.5 * (p - center)
                v += 3 if abs(pos - round(pos)) < 1e-6 else -6
                if bar == 20:
                    v += 8
                track.note(bar, pos, p, d * legato, int(v))
            pos += d


def swells(track, lo, hi):
    """Two-bar swells (CC11) like a long sea swell: up over a bar, down over the next; B a little fuller.
    Each one sinks to where the next starts (the last to the opening level), so the expression never jumps,
    not even at the loop point."""
    def boost(bar):
        return {"A": 0, "A2": 4, "B": 8}[section(bar % BARS)]
    for bar in range(0, BARS, 2):
        track.swell(bar, 0, 4, lo + boost(bar), hi + boost(bar))
        track.swell(bar + 1, 0, 4, hi + boost(bar), lo + boost(bar + 2))


# ------------------------------------------------------------------------------------------------- day stem
HARP = {
    "A": [0, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 2],
    "A2": [0, 2, 3, 4, 5, 4, 1, 3, 4, 5, 4, 3],
    "B": [0, 2, 4, 5, 6, 5, 1, 3, 5, 6, 5, 4],
}
HARP_FILL = {
    19: [0, 1, 2, 3, 4, 3, 4, 5, 6, 7, 8, 9],     # sweep up into the climax
    23: [7, 6, 5, 4, 3, 2, 5, 4, 3, 2, 1, 0],     # two falling waves back to the soft opening
}


def day(s):
    horn = s.track("horn", GM["french_horn"], stem="day", vol=98, pan=-14, reverb=70)
    vln = s.track("violins", GM["strings"], stem="day", vol=120, pan=10, reverb=74)
    harp = s.track("harp", GM["harp"], stem="day", vol=100, pan=24, reverb=66)
    low = s.track("basses", GM["slow_strings"], stem="day", vol=96, pan=4, reverb=56)
    st_l = s.track("str_left", GM["slow_strings"], stem="day", vol=84, pan=-40, reverb=78)
    st_r = s.track("str_right", GM["slow_strings"], stem="day", vol=86, pan=36, reverb=78)
    pad = s.track("pad", GM["warm_pad"], stem="day", vol=74, pan=0, reverb=82)

    for bar in range(BARS):
        sec = section(bar)
        # harp: rocking triplet waves from the bass up, re-voiced at each chord
        pat = HARP_FILL.get(bar, HARP[sec])
        for i, k in enumerate(pat):
            bt = i * T
            st, en, ch, bass = seg_at(bar, bt)
            tones = harp_tones(ch, bass)
            v = 60 if (i == 0 or bt == st) else (52 if i % 3 == 0 else 45)
            v += {"A": 0, "A2": 3, "B": 6}[sec]
            if bar == 19:
                v += i                                   # the sweep grows into bar 20
            harp.note(bar, bt, tones[k], clip(bar, bt, 1.3), v)

        # low strings: the base bass, sustained
        for (st, en, p) in bass_line(bar):
            v = {"A": 56, "A2": 60, "B": 66}[sec] + (0 if st == 0 else -6)
            low.note(bar, st, p, en - st - 0.06, v)

        # wide string chords: right = middle voicing, left = high voicing (from A'), warm pad underneath
        for (st, en, ch, bass) in segs(bar):
            d = en - st - 0.08
            vr = {"A": voicing(ch, 50, 3), "A2": voicing(ch, 55, 3), "B": voicing(ch, 55, 3)}[sec]
            st_r.chord(bar, st, vr, d, {"A": 46, "A2": 50, "B": 56}[sec])
            if sec != "A":
                vl = voicing(ch, 62, 3) if sec == "A2" else voicing(ch, 65, 3)
                st_l.chord(bar, st, vl, d, {"A2": 44, "B": 52}[sec])
            pad.chord(bar, st, voicing(ch, 58, 2 if sec == "A" else 3), d, {"A": 40, "A2": 44, "B": 48}[sec])

    write_tune(horn, HORN_A, center=n("Bb4"), shift=2)
    write_tune(horn, {b: v for b, v in HORN_COUNTER.items() if b < 20}, center=n("Bb4"), shift=-22, legato=0.97)
    write_tune(horn, {b: v for b, v in HORN_COUNTER.items() if b >= 20}, center=n("Bb4"), shift=-18)
    write_tune(vln, STRINGS_TUNE, center=n("Bb5"), shift=8)

    for tr, lo, hi in ((low, 92, 110), (st_r, 84, 112), (st_l, 84, 112), (pad, 86, 106)):
        tr.cc(0, 0, 11, lo)
        swells(tr, lo, hi)
    # the strings tune leans into each phrase
    vln.cc(0, 0, 11, 104)
    for bar in (8, 12, 16, 20):
        vln.swell(bar, 0, 4, 100, 118)
        vln.swell(bar + 1, 0, 8, 118, 104)


# ----------------------------------------------------------------------------------------------- night stem
# bar -> (beat, direction) of a soft high ripple where the tune rests; in A' each rising one gets a falling answer
RIPPLES = {3: (1, 1), 8: (1, 1), 9: (2, -1), 10: (1, 1), 11: (2, -1), 19: (1, 1)}


def night(s):
    pno = s.track("piano_lh", GM["piano"], stem="night", vol=127, pan=-10, reverb=70)
    rh = s.track("piano_rh", GM["piano"], stem="night", vol=127, pan=4, reverb=76)
    swp = s.track("sweep", GM["sweep_pad"], stem="night", vol=70, pan=10, reverb=80)
    low = s.track("low_strings", GM["slow_strings"], stem="night", vol=127, pan=0, reverb=64)

    for bar in range(BARS):
        sec = section(bar)
        # piano left hand: the bass, and in A'/B a soft fifth (or tenth) on beat 2 of a whole-bar chord
        for (st, en, p) in bass_line(bar):
            pno.note(bar, st, p, en - st - 0.05, 60 if st == 0 else 52)
        for (st, en, ch, bass) in segs(bar):
            if sec != "A" and st == 0 and en == 4 and bar not in WALK:
                up = bass + 7 if (bass + 7) % 12 in CH[ch] else voicing(ch, bass + 12, 1)[0]
                pno.note(bar, 2, up, 1.9, 46)
        # piano ripples: three chord tones, ringing, where the tune rests (rising, or falling as an answer)
        if bar in RIPPLES:
            b0, way = RIPPLES[bar]
            ch = seg_at(bar, b0)[2]
            for i, p in enumerate(voicing(ch, 72, 3)[::way]):
                bt = b0 + i * T
                rh.note(bar, bt, p, clip(bar, bt, 2.2), 48 + 2 * i * way + (4 if way < 0 else 0))

        # sweep pad: the chords, low-mid and wide; soft low strings hold the bass
        for (st, en, ch, bass) in segs(bar):
            swp.chord(bar, st, voicing(ch, 53, 3), en - st - 0.08, {"A": 52, "A2": 53, "B": 54}[sec])
        for (st, en, p) in bass_line(bar):
            low.note(bar, st, p, en - st - 0.06, {"A": 46, "A2": 48, "B": 52}[sec])

    write_tune(rh, NIGHT_TUNE, center=n("Bb4"), shift=6, legato=1.0)

    for tr, lo, hi in ((swp, 80, 110), (low, 86, 104)):
        tr.cc(0, 0, 11, lo)
        swells(tr, lo, hi)


# ----------------------------------------------------------------------------------------------- fight stem
# drum patterns: 12 triplet steps per bar (beat b = step 3b)
GROOVE = {
    "A": {"kick": "x.....x.....", "snare": "...x.....x..", "low_tom": "..x.....x..x"},
    "A2": {"kick": "x....xx.....", "snare": "...x.....x.x", "low_tom": "..x..x..x...", "mid_tom": "..........x."},
    "B": {"kick": "x..x..x..x..", "snare": "...x.....x..", "low_tom": "..x..x..x..x", "mid_tom": ".x.....x....",
          "ride": "x..x..x..x.."},
}
CELLO = {
    "A": [0, 2, 1, 0, 2, 1, 0, 2, 1, 0, 2, 1],
    "A2": [0, 2, 1, 0, 3, 1, 0, 2, 1, 0, 3, 2],
    "B": [0, 2, 3, 1, 2, 3, 0, 2, 3, 1, 3, 2],
}
CELLO_FILL = {19: [0, 1, 2, 1, 2, 3, 2, 3, 4, 3, 4, 4],      # climbing into the climax
              23: [3, 2, 1, 2, 1, 0, 3, 2, 1, 2, 1, 0]}      # settling back to the opening register
# brass stabs: (step, beats)
STABS = {
    "A": [(0, .6)],
    "A2": [(0, .6), (8, .25), (9, .6)],
    "B": [(0, .9), (5, .25), (6, .6), (8, .25), (9, .6)],
}
BASS_GALLOP = {   # steps of the contrabass: "r" root, "o" octave up, "a" approach to the next bar
    "A": "r.rr.rr.rr.a",
    "A2": "r.rr.or.rr.a",
    "B": "r.rr.or.rrra",
}


def roll(tr, bar, beat, beats, pitch, v0, v1, step=T / 2):
    """A crescendo roll of repeated notes (sixteenth triplets by default)."""
    k = int(round(beats / step))
    for i in range(k):
        tr.note(bar, beat + i * step, pitch, step * 0.9, v0 + (v1 - v0) * i / max(1, k - 1))


BASS_FLOOR = n("G1")


def approach(nxt, root, chord):
    """A scale step into the next bar's bass, from the side we come from if possible; never a semitone off a tone
    of the chord still sounding (the base bass holds it), else the current root."""
    if nxt == root:
        return root
    for up in (nxt < root, nxt > root):
        p = in_key_step(nxt, up)
        if abs(p - root) <= 5 and BASS_FLOOR <= p and all((p - c) % 12 not in (1, 11) for c in CH[chord]):
            return p
    return root


def fbass(p):
    """The fight bass sits an octave under the base bass, in unison where that would drop below G1 (so the line
    moves by step or fourth instead of leaping a seventh, and stays above the mud of the lowest octave)."""
    return p - 12 if p - 12 >= BASS_FLOOR else p


def timp_pitch(p):
    """The bass pitch class on the timpani, F2..E3."""
    q = n("F2") + (p - n("F2")) % 12
    return q


def fight(s):
    dr = s.track("drums", DRUMS, stem="fight", vol=90, pan=0, reverb=34)
    bs = s.track("bass", GM["contrabass"], stem="fight", vol=76, pan=0, reverb=20)
    vc = s.track("cello", GM["cello"], stem="fight", vol=116, pan=-24, reverb=42)
    br = s.track("brass", GM["brass"], stem="fight", vol=62, pan=22, reverb=52)
    tp = s.track("timpani", GM["timpani"], stem="fight", vol=104, pan=10, reverb=50)

    for bar in range(BARS):
        sec = section(bar)
        # --- drums
        if bar in (0, 8, 16):
            dr.note(bar, 0, DR["crash"], 2, 94)
        if bar == 20:
            dr.note(bar, 0, DR["crash2"], 2, 100)
        g = dict(GROOVE[sec])
        if bar in (7, 15, 23):                         # leave beats 2-3 for the fill
            g = {k: v[:6] + "......" for k, v in g.items()}
        if bar == 19:                                  # one long snare roll instead of the groove
            g = {"kick": "x...........", "ride": "x..x..x..x.."}
        for k, pat in g.items():
            vel = {"kick": 90, "snare": 76, "low_tom": 70, "mid_tom": 64, "ride": 56}[k]
            dr.hits(bar, pat, k, step=T, vel=vel + (4 if sec == "B" else 0), accent=3 if k == "low_tom" else None)
        if bar == 7:
            roll(dr, bar, 2, 2, DR["snare"], 50, 92)
        if bar == 15:
            toms = ["high_tom"] * 3 + ["mid_tom"] * 3 + ["tom2"] * 3 + ["low_tom"] * 3
            for i, k in enumerate(toms):                 # sixteenth triplets down the toms
                dr.note(bar, 2 + i * T / 2, DR[k], 0.15, 72 + 2 * i)
            dr.note(bar, 2, DR["kick"], 0.3, 86)
        if bar == 19:
            roll(dr, bar, 0, 4, DR["snare"], 30, 96)
        if bar == 23:
            roll(dr, bar, 2, 2, DR["snare"], 46, 98)
            dr.note(bar, 2, DR["kick"], 0.3, 84)

        # --- contrabass gallop under the base bass, stepping into the next bar
        pat = BASS_GALLOP[sec]
        nxt = fbass(bass_line((bar + 1) % BARS)[0][2])
        for i, c in enumerate(pat):
            if c == ".":
                continue
            bt = i * T
            root = fbass(bass_at(bar, bt))
            if c == "o":
                p = root + 12
            elif c == "a":
                walk = [w for (b, w) in WALK.get(bar, []) if abs(b - bt) < 1e-6]
                if walk:
                    p = fbass(n(walk[0]))
                else:
                    p = approach(nxt, root, seg_at(bar, bt)[2])
            else:
                p = root
            on_beat = i % 3 == 0
            bs.note(bar, bt, p, clip(bar, bt, 0.6 if on_beat else 0.3), (92 if on_beat else 72) + (4 if sec == "B" else 0))

        # --- cello triplet ostinato on the chord tones
        fig = CELLO_FILL.get(bar, CELLO[sec])
        for i, k in enumerate(fig):
            bt = i * T
            tones = voicing(seg_at(bar, bt)[2], 48, 5)
            v = (78 if i % 3 == 0 else 62) + (5 if sec == "B" else 0)
            if bar == 19:
                v += i
            vc.note(bar, bt, tones[k], clip(bar, bt, 0.3), v)

        # --- brass stabs
        stabs = STABS[sec]
        if bar in (7, 23):
            stabs = [(0, .6), (6, .6), (8, .25), (9, .6)]
        elif bar == 15:
            stabs = [(0, .6), (6, .6)]
        elif bar == 19:
            stabs = [(0, .6)]
        elif bar == 20:
            stabs = [(0, 1.8), (8, .25), (9, .9)]
        for (step, d) in stabs:
            bt = step * T
            ch = seg_at(bar, bt)[2]
            v = {"A": 70, "A2": 76, "B": 80}[sec] + (6 if step == 0 else 0) + (4 if bar == 20 else 0)
            br.chord(bar, bt, voicing(ch, 53, 3, triad=True), clip(bar, bt, d), v)     # always with its third

        # --- timpani on the roots: A every downbeat, A'/B also beat 2, rolls into bars 20 and 0
        p = timp_pitch(bass_at(bar, 0))
        if bar == 19:
            roll(tp, bar, 2, 2, p, 40, 90)
            tp.note(bar, 0, p, 1.5, 80)
        elif bar == 23:
            tp.note(bar, 0, p, 1.5, 80)
            roll(tp, bar, 2, 2, timp_pitch(bass_at(bar, 2)), 40, 88)
        else:
            tp.note(bar, 0, p, 1.5, {"A": 74, "A2": 80, "B": 86}[sec])
            if sec != "A" and bar not in (7, 15):
                tp.note(bar, 2, timp_pitch(bass_at(bar, 2)), 1.2, {"A2": 66, "B": 74}[sec])


def songs():
    s = Song("stage_ocean", bpm=88, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Open ocean from a boat: a Bb-major barcarolle rocking in triplets, French horn then strings "
                  "over harp waves and wide strings; night: sweep pad, low strings and sparse piano; "
                  "fight: toms and snare rolls, galloping contrabass, cello ostinato, brass stabs, timpani")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
