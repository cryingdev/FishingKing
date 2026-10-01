"""
stage_swamp — 안개 늪지 (misty swamp, murky). D dorian, 72 bpm, 24 bars = 80 s, loop.

Form (8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A  (0-7)    Dm7  Em7  F  G | C  G/B  Am7  Em7       the bass creeps up D-E-F-G, falls back C-B-A, ii -> i
  A' (8-15)   the same, ending Em7 | Am7               v slips down a step into B
  B  (16-23)  G  Am7  F  C/E | Dm6  G/D  F  C          bass G-A-F-E-D, i-IV over a D pedal (the dorian colour),
                                                       climax C6 in bar 18, bVII -> i back to bar 0
day    clarinet melody, low marimba "drips" (answers in the melody's rests), fretless bass with a few slides,
       bowed pad (3-voice upper structures keep the third: Dm7 = A-C-F over the D bass)
night  bassoon (the melody an octave down, thinner), vibraphone glints in its rests, soft tremolo strings
       (bass + a low bed)
fight  low toms + shaker + side stick, a taiko on D / A, upright bass in driving eighths, pizzicato ostinato
The clarinet rests in bars 3, 7, 11, 14, 15, 19, 23 (29 %), the bassoon in ten bars, so the swamp ambience breathes.
No chord holds a semitone (no maj7 / 9th; the dorian colour comes from G major and Dm6), every track puts only chord
tones on the beats, and passing tones / bass approach notes are short off-beat eighths, so no stem rubs on a beat.
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("D", "dorian")

# chord tones as pitch classes (none of these chords contains a semitone)
CH = {c: [n(x + "0") % 12 for x in tones] for c, tones in {
    "Dm7": ["D", "F", "A", "C"], "Dm6": ["D", "F", "A", "B"], "Em7": ["E", "G", "B", "D"],
    "F": ["F", "A", "C"], "G": ["G", "B", "D"], "C": ["C", "E", "G"], "Am7": ["A", "C", "E", "G"],
}.items()}

# one chord per bar: (chord, bass note of the base stems)
A_SEC = [("Dm7", "D2"), ("Em7", "E2"), ("F", "F2"), ("G", "G2"),
         ("C", "C3"), ("G", "B2"), ("Am7", "A2"), ("Em7", "E2")]
A2_SEC = A_SEC[:6] + [("Em7", "E2"), ("Am7", "A2")]
B_SEC = [("G", "G2"), ("Am7", "A2"), ("F", "F2"), ("C", "E2"),
         ("Dm6", "D2"), ("G", "D2"), ("F", "F2"), ("C", "C2")]
PROG = A_SEC + A2_SEC + B_SEC
BARS = len(PROG)


def section(bar):
    return "A" if bar < 8 else ("A2" if bar < 16 else "B")


def chord(bar):
    return PROG[bar % BARS][0]


def bass(bar):
    return n(PROG[bar % BARS][1])


# three-voice upper structures: the seventh chords leave their root to the bass, so a 3-voice pad / ostinato always
# carries the third (a closed Dm7 from A3 would be A-C-D, a tonic without its F); Dm6 keeps plain D-F-A up there
UPPER3 = {c: [n(x + "0") % 12 for x in tones] for c, tones in {
    "Dm7": ["F", "A", "C"], "Em7": ["G", "B", "D"], "Dm6": ["D", "F", "A"],
}.items()}


def voicing(ch, lo, count, upper3=False):
    """The first `count` chord tones at or above `lo` (closed position); upper3: from UPPER3 (above a bass)."""
    tones = UPPER3.get(ch, CH[ch]) if upper3 else CH[ch]
    out, p = [], lo
    while len(out) < count:
        if p % 12 in tones:
            out.append(p)
        p += 1
    return out


def upper(ch, p):
    """A chord tone a fifth (or fourth / sixth / third) above the bass note p."""
    for iv in (7, 5, 8, 3, 9, 4):
        if (p + iv) % 12 in CH[ch]:
            return p + iv
    return p + 12


def upper_near(ch, p, target):
    """The chord tone within a sixth of the bass note p (C2 or higher, not p or target) nearest `target`:
    the bass then walks smoothly into its step to the next bar."""
    cands = [q for q in range(max(n("C2"), p - 9), p + 10) if q % 12 in CH[ch] and q not in (p, target)]
    return min(cands, key=lambda q: (abs(q - target), -q))


def in_key_below(p):
    p -= 1
    while not KEY.in_key(p):
        p -= 1
    return p


def in_key_above(p):
    p += 1
    while not KEY.in_key(p):
        p += 1
    return p


def step_to(cur, nxt):
    """A scale step next to `nxt`, approached from cur's side (from the other side when that is cur itself)."""
    if nxt >= cur:
        p = in_key_below(nxt)
        return p if p != cur else in_key_above(nxt)
    p = in_key_above(nxt)
    return p if p != cur else in_key_below(nxt)


def held(track, voices, vels):
    """Sustained chords, one voicing per bar: a pitch that stays in the next bar is tied over, not re-struck."""
    open_ = {}   # pitch -> (start bar, vel)
    for bar in range(BARS + 1):
        now = set(voices[bar]) if bar < BARS else set()
        for p in sorted(open_):
            if p not in now:
                st, v = open_.pop(p)
                track.note(st, 0, p, (bar - st) * 4 - 0.08, v)
        for p in sorted(now):
            if p not in open_:
                open_[p] = (bar, vels[bar])


def write_melody(track, mel, base, legato=0.95):
    """(pitch | None, beats) per bar; a little louder in B and on higher notes (capped: the climax stays soft)."""
    for bar, items in mel.items():
        pos = 0.0
        for (nm, d) in items:
            if nm is not None:
                p = n(nm)
                v = base + {"A": 0, "A2": 2, "B": 4}[section(bar)] + 0.4 * (p - 72) + (3 if pos % 1 == 0 else -3)
                track.note(bar, pos, p, d * legato, int(min(v, base + 8)))
            pos += d


# ----------------------------------------------------------------------------------------- melody (clarinet)
# chord tones on every beat; the eighths on the "and"s are passing / neighbour tones
MELODY = {
    # A: a slow rise out of the low register and back down; the answer rises to G5 and sighs back to B4
    0: [(None, 1), ("A4", .5), ("C5", .5), ("D5", 1.5), ("C5", .5)],
    1: [("B4", 1.5), ("A4", .5), ("G4", 1), ("E4", 1)],
    2: [("F4", 1), ("A4", 1), ("C5", 2)],
    4: [("G4", .5), ("C5", .5), ("E5", 2.5), ("D5", .5)],
    5: [("B4", 1), ("D5", 1), ("G5", 2)],
    6: [("E5", 2), ("C5", 1.5), ("B4", 2)],      # B4 anticipates Em7 and is held over the bar line (the cadence)
    # A': the opening figure a step higher, then a rising arpeggio that settles on G4
    8: [(None, 1), ("C5", .5), ("D5", .5), ("F5", 1.5), ("E5", .5)],
    9: [("D5", 1), ("E5", 1), ("G5", 1.5), ("E5", .5)],
    10: [("F5", 1.5), ("E5", .5), ("C5", 1), ("A4", 1)],
    12: [(None, .5), ("G4", .5), ("C5", 1), ("E5", 1), ("G5", 1)],
    13: [("D5", 1.5), ("B4", .5), ("G4", 2)],
    # B: up to the climax (C6 over F), the dorian sixth (B5 over Dm6), then down to A4 for the loop
    16: [("G4", 1), ("B4", .5), ("D5", .5), ("G5", 1.5), ("F5", .5)],
    17: [("E5", 1.5), ("G5", .5), ("A5", 2)],
    18: [("C6", 1.5), ("A5", .5), ("F5", 2)],
    20: [("D5", 1), ("F5", 1), ("A5", 1), ("B5", 1)],
    21: [(None, .5), ("A5", .5), ("G5", 1), ("D5", 2)],
    22: [("C5", 1.5), ("D5", .5), ("A4", 2)],
}
PHRASES = [(0, 3), (4, 7), (8, 11), (12, 14), (16, 19), (20, 23)]   # [start, end) bars, for the breath swells

# night: the bassoon sings the outline an octave down, long notes only, and leaves A' to the vibraphone
NIGHT_MELODY = {
    0: [(None, 1), ("A3", 1), ("D4", 2)],
    1: [("B3", 2), ("G3", 1), ("E3", 1)],
    2: [("F3", 1), ("A3", 1), ("C4", 2)],
    4: [("G3", 1), ("E4", 3)],
    5: [("B3", 1), ("D4", 1), ("G4", 2)],
    6: [("E4", 2), ("C4", 2)],
    12: [(None, 1), ("C4", 1), ("E4", 1), ("G4", 1)],
    13: [("D4", 2), ("G3", 2)],
    16: [("G3", 1), ("B3", 1), ("G4", 2)],
    17: [("E4", 2), ("A4", 2)],
    18: [("C5", 2), ("F4", 2)],
    20: [("D4", 1), ("F4", 1), ("A4", 1), ("B4", 1)],
    21: [(None, 1), ("G4", 1), ("D4", 2)],
    22: [("C4", 2), ("A3", 2)],
}


# --------------------------------------------------------------------------------------------------- day stem
# marimba: a lazy dotted "drip" under the melody (voice index into voicing(chord, D3, 3); 3 = the lowest an octave up)
DRIP = {
    "A": [(0, 0, 1.4, 52), (1.5, 2, 1.0, 44), (3, 1, 0.9, 46)],
    "A2": [(0, 0, 1.4, 54), (1.5, 2, 0.9, 46), (2.5, 1, 0.9, 42), (3, 3, 0.9, 48)],
    "B": [(0, 0, 0.9, 56), (1, 2, 0.9, 46), (1.5, 1, 0.9, 44), (2.5, 3, 0.9, 50), (3, 2, 0.9, 46)],
}
# ... and short answers in the bars where the clarinet rests: (beat, pitch, beats, vel)
MARIMBA_FILL = {
    3: [(0, "G3", 1.4, 54), (1.5, "B3", .45, 48), (2, "D4", .9, 52), (2.5, "G4", .45, 50), (3, "B4", .9, 54)],
    7: [(0, "E3", 1.4, 54), (1, "B3", .9, 48), (1.5, "D4", .45, 50), (2.5, "G4", .9, 52), (3, "E4", .45, 48),
        (3.5, "D4", .45, 44)],
    11: [(0, "G3", 1.4, 56), (1, "D4", .45, 48), (1.5, "G4", .9, 52), (2.5, "B4", .9, 54), (3, "D5", .9, 56)],
    14: [(0, "E3", 1.4, 50), (1.5, "G3", .9, 44), (2, "B3", .9, 46), (3, "D4", .9, 46)],
    15: [(0, "A3", 1.4, 52), (1, "C4", .45, 46), (1.5, "E4", .9, 50), (2.5, "G4", .45, 52), (3, "A4", .45, 54),
         (3.5, "C5", .45, 50)],
    19: [(0, "E3", 1.4, 56), (1, "G3", .45, 48), (1.5, "C4", .9, 52), (2.5, "E4", .45, 54), (3, "G4", .9, 56)],
    23: [(0, "G3", 1.4, 54), (1, "C4", .45, 48), (1.5, "E4", .9, 52), (2.5, "G4", .45, 52), (3, "E4", .45, 48),
         (3.5, "D4", .45, 46)],
}
# fretless: (beat, "R" root / "U" upper chord tone / "S" step into the next bar, beats)
FRETLESS = {
    "A": [(0, "R", 3.9)],
    "A2": [(0, "R", 2.9), (3, "U", 0.9)],
    "B": [(0, "R", 2.4), (2.5, "U", 0.95), (3.5, "S", 0.45)],
    "turn": [(0, "R", 2.9), (3, "U", 0.45), (3.5, "S", 0.45)],
}
TURN_BARS = {3, 7, 11, 15}
GLIDE_BARS = {4, 12, 17, 20, 22}      # the fretless slides into these roots from the note before


def glide(track, bar, semis=-1, beats=0.2, steps=6):
    """Pitch-bend slide into the note at the bar line, from `semis` away (bend range 2 semitones)."""
    v0 = int(8192 * semis / 2)
    track.bend(bar, -0.03, v0)
    for i in range(1, steps + 1):
        track.bend(bar, beats * i / steps, int(v0 * (1 - i / steps)))


def day(s):
    cl = s.track("clarinet", GM["clarinet"], stem="day", vol=92, pan=12, reverb=58)
    mar = s.track("marimba", GM["marimba"], stem="day", vol=122, pan=-24, reverb=62)
    fb = s.track("fretless", GM["fretless_bass"], stem="day", vol=104, pan=-4, reverb=28)
    pad = s.track("pad", GM["bowed_pad"], stem="day", vol=86, pan=8, reverb=78)

    for bar in range(BARS):
        sec, ch, root = section(bar), chord(bar), bass(bar)
        # fretless bass
        pat = FRETLESS["turn"] if bar in TURN_BARS or bar == 23 else FRETLESS[sec]
        step = step_to(root, bass(bar + 1))
        prev = fb.notes[-1][2] if fb.notes else root
        for (bt, what, d) in pat:
            if what == "R":
                p, v = root, 66
            elif what == "U":
                # A2 has no step note: its upper tone itself leans into the next root (D-F-E, E-G-F, B-D-E ...)
                p, v = upper_near(ch, root, bass(bar + 1) if pat is FRETLESS["A2"] else step), 58
            else:
                p, v = step, 54
            fb.note(bar, bt, p, d, v)
        if bar in GLIDE_BARS:                     # B2-C3, C2-D2, E2-F2 up; B2-A2 down (bend range 2)
            glide(fb, bar, max(-2, min(2, prev - root)))
        # marimba
        if bar in MARIMBA_FILL:
            for (bt, nm, d, v) in MARIMBA_FILL[bar]:
                mar.note(bar, bt, n(nm), d, v)
        else:
            vc = voicing(ch, n("D3"), 3)
            for (bt, k, d, v) in DRIP[sec]:
                mar.note(bar, bt, vc[0] + 12 if k == 3 else vc[k], d, v)

    # bowed pad: three voices from A3 (four in B), common tones tied over
    voices = [voicing(chord(b), n("A3") if section(b) == "A" else n("G3"), 4 if section(b) == "B" else 3,
                      upper3=section(b) != "B") for b in range(BARS)]
    held(pad, voices, [{"A": 42, "A2": 45, "B": 47}[section(b)] for b in range(BARS)])
    for bar in range(0, BARS, 4):                 # slow fog waves
        pad.swell(bar, 0, 8, 84, 104)
        pad.swell(bar + 2, 0, 8, 104, 84)

    write_melody(cl, MELODY, 70)
    for (a, b) in PHRASES:                        # breath: a swell over each phrase
        mid = (a + b) / 2
        cl.swell(a, 0, (mid - a) * 4, 96, 118)
        cl.swell(int(mid), (mid % 1) * 4, (b - mid) * 4, 118, 98)


# ------------------------------------------------------------------------------------------------- night stem
# vibraphone: (beat, pitches, beats, vel): soft dyads above the bassoon, short figures in its rests
VIBES = {
    0: [(0, ["A4", "D5"], 3.6, 36)],
    2: [(0, ["A4", "C5"], 3.6, 34)],
    3: [(0.5, ["D5"], 1.3, 42), (1.5, ["B4"], 1.3, 38), (2.5, ["G4"], 1.4, 36)],
    4: [(0, ["G4", "C5"], 3.6, 34)],
    6: [(0, ["A4", "E5"], 3.6, 34)],
    7: [(0.5, ["G4"], 1.3, 38), (1.5, ["B4"], 1.3, 40), (2.5, ["D5"], 1.4, 42)],
    8: [(1, ["C5"], 1.4, 44), (2, ["F5"], 1.9, 46)],
    9: [(0, ["D5"], 1.9, 42), (2, ["G5"], 1.4, 46), (3, ["E5"], 0.9, 40)],
    10: [(0, ["F5"], 1.9, 44), (2, ["C5"], 0.9, 40), (3, ["A4"], 0.9, 38)],
    11: [(1, ["B4"], 2.8, 36)],
    13: [(2, ["B4", "D5"], 1.9, 34)],
    14: [(0.5, ["E5"], 1.3, 40), (1.5, ["D5"], 1.3, 38), (2.5, ["B4"], 1.4, 36)],
    15: [(0.5, ["C5"], 1.3, 38), (1.5, ["E5"], 1.3, 40), (2.5, ["G5"], 1.4, 42)],
    16: [(0, ["B4", "D5"], 3.6, 36)],
    18: [(0, ["A4", "C5"], 3.6, 36)],
    19: [(0.5, ["G5"], 1.3, 42), (1.5, ["E5"], 1.3, 40), (2.5, ["C5"], 1.4, 38)],
    20: [(0, ["D5", "F5"], 3.6, 36)],
    22: [(2, ["A4", "C5"], 1.9, 34)],
    23: [(0.5, ["G4"], 1.3, 38), (1.5, ["C5"], 1.3, 40), (2.5, ["E5"], 1.4, 42)],
}


def night(s):
    bn = s.track("bassoon", GM["bassoon"], stem="night", vol=127, pan=-10, reverb=64)
    vb = s.track("vibes", GM["vibraphone"], stem="night", vol=127, pan=24, reverb=76)
    tr = s.track("tremolo", GM["tremolo_strings"], stem="night", vol=127, pan=-2, reverb=72)

    # tremolo strings: the bass note + two (three in B) low chord tones from E3, tied over
    voices = [[bass(b)] + voicing(chord(b), n("E3"), 3 if section(b) == "B" else 2) for b in range(BARS)]
    held(tr, voices, [{"A": 42, "A2": 44, "B": 48}[section(b)] for b in range(BARS)])
    for bar in range(0, BARS, 4):
        tr.swell(bar, 0, 8, 76, 100)
        tr.swell(bar + 2, 0, 8, 100, 76)

    for bar, items in VIBES.items():
        for (bt, ps, d, v) in items:
            vb.chord(bar, bt, [n(x) for x in ps], d, v + 24)

    write_melody(bn, NIGHT_MELODY, 66, legato=0.97)
    for (a, b) in PHRASES:
        mid = (a + b) / 2
        bn.swell(a, 0, (mid - a) * 4, 94, 114)
        bn.swell(int(mid), (mid % 1) * 4, (b - mid) * 4, 114, 94)


# ------------------------------------------------------------------------------------------------- fight stem
GROOVE = {   # 16 steps per bar (drum kit; the taiko is a separate pitched track)
    "A": {"low_tom": "......x.......x.", "tom2": "............x...", "shaker": "x.x.x.x.x.x.x.x.",
          "stick": "................"},
    "A2": {"low_tom": "......x.....x.x.", "tom2": "...x............", "shaker": "xxxxxxxxxxxxxxxx",
           "stick": "....x.......x..."},
    "B": {"low_tom": "...x..x.....x.x.", "tom2": ".......x........", "shaker": "xxxxxxxxxxxxxxxx",
          "stick": "....x.......x..x", "kick2": "......x...x....."},
}
TAIKO = {   # (beat, pitch, vel): tuned to D and A, which never rub against a D-dorian chord
    "A": [(0, "D2", 100), (2.5, "A1", 84)],
    "A2": [(0, "D2", 102), (1.5, "A1", 82), (2.5, "D2", 90)],
    "B": [(0, "D2", 98), (1.5, "A1", 86), (2.5, "D2", 92), (3.5, "A1", 78)],
}
# upright bass eighths: R root, P root an octave away, U upper chord tone, S step into the next bar
BASS8 = {"A": "RRPRRRUS", "A2": "RRPRRUPS", "B": "RPRURPUS"}
BASS_VEL = [96, 70, 84, 72, 90, 70, 84, 76]
# pizzicato: (step, voice indices, lowest note); index 3 / 4 = the first / second voice an octave up
PIZZ = {
    "A": (0.5, [0, 2, 1, 2, 0, 2, 1, 3], "A3"),
    "A2": (0.25, [0, 1, 2, 1, 3, 1, 2, 1, 0, 1, 2, 1, 3, 2, 1, 2], "A3"),
    "B": (0.25, [0, 2, 1, 2, 3, 2, 1, 2, 0, 2, 1, 2, 3, 4, 3, 2], "A3"),
}


def fold(p):
    """The fight bass sits in A1..A2 (an octave under the base bass when that climbs to B2 / C3)."""
    while p > n("A2"):
        p -= 12
    return p


def fight(s):
    dr = s.track("drums", DRUMS, stem="fight", vol=85, pan=0, reverb=30)
    tk = s.track("taiko", GM["taiko"], stem="fight", vol=67, pan=0, reverb=44)
    bs = s.track("bass", GM["acoustic_bass"], stem="fight", vol=62, pan=-4, reverb=14)
    pz = s.track("pizz", GM["pizzicato"], stem="fight", vol=70, pan=-22, reverb=40)

    for bar in range(BARS):
        sec, ch = section(bar), chord(bar)
        g = GROOVE[sec]
        # --- drum kit
        if bar in (0, 16):
            dr.note(bar, 0, DR["crash"], 1.5, 70)
        if bar == 8:
            dr.note(bar, 0, DR["splash"], 1, 62)
        tom2, low = g["tom2"], g["low_tom"]
        if bar in (7, 15, 23):                        # fills replace the last beats
            low, tom2 = low[:12] + "....", tom2[:12] + "...."
        dr.hits(bar, low, "low_tom", vel=74, accent=8)
        dr.hits(bar, tom2, "tom2", vel=68)
        dr.hits(bar, g["shaker"], "shaker", vel=38 if sec == "A" else 34, accent=4 if sec == "A" else 2)
        dr.hits(bar, g["stick"], "stick", vel=58)
        if "kick2" in g:
            dr.hits(bar, g["kick2"], "kick2", vel=72)
        if bar == 7:
            for i, (k, v) in enumerate([("tom2", 62), ("tom2", 68), ("low_tom", 74), ("low_tom", 82)]):
                dr.note(bar, 3 + 0.25 * i, DR[k], 0.2, v)
        if bar == 15:
            toms = ["mid_tom", "mid_tom", "tom2", "tom2", "low_tom", "low_tom", "low_tom", "low_tom"]
            for i, (k, v) in enumerate(zip(toms, [64, 60, 70, 66, 76, 72, 82, 88])):
                dr.note(bar, 2 + 0.25 * i, DR[k], 0.2, v)
        if bar == 23:
            for i in range(8):
                dr.note(bar, 2 + 0.25 * i, DR["low_tom" if i % 2 else "tom2"], 0.2, 60 + 4 * i)

        # --- taiko
        hits = TAIKO[sec]
        if bar == 23:                                 # a swelling roll into the loop point
            hits = [(0, "D2", 104), (1.5, "A1", 84)] + [(2 + 0.5 * i, "D2", 74 + 8 * i) for i in range(4)]
        for (bt, nm, v) in hits:
            tk.note(bar, bt, n(nm), 0.9 if bt % 1 == 0 else 0.45, v)

        # --- upright bass: eighths on the bass note
        root, nxt = fold(bass(bar)), fold(bass(bar + 1))
        for i, what in enumerate(BASS8[sec]):
            if what == "R":
                p = root
            elif what == "P":
                p = root + 12 if root + 12 <= n("G3") else root - 12
            elif what == "U":
                p = upper(ch, root)
            else:
                p = step_to(root, nxt)
            bs.note(bar, 0.5 * i, p, 0.44, BASS_VEL[i])

        # --- pizzicato ostinato on the chord tones
        step, fig, lo = PIZZ[sec]
        vc = voicing(ch, n(lo), 3, upper3=True)          # Dm7 = A3 C4 F4, so the layer alone spells D minor
        for i, k in enumerate(fig):
            bt = step * i
            p = vc[k] if k < 3 else vc[k - 3] + 12
            v = 70 if bt % 1 == 0 else (60 if bt % 0.5 == 0 else 52)
            pz.note(bar, bt, p, min(0.3, step * 0.8), v + (2 if sec == "B" else 0))


def songs():
    s = Song("stage_swamp", bpm=72, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Misty swamp in D dorian: clarinet over low marimba drips, fretless bass and a bowed pad; "
                  "night: bassoon, vibraphone glints and soft tremolo strings; "
                  "fight: low toms, taiko, shaker, upright bass eighths and a pizzicato ostinato")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
