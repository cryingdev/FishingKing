"""
stage_lake — 고요한 호수 (calm lake, daytime shore). D major, 80 bpm, 24 bars = 72 s, loop.

Form (8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A  (0-7)    D  A/C#  Bm  F#m/A | G  D/F#  Em7  Asus4-A       descending bass, half cadence
  A' (8-15)   the same, ending Em7-A7 | D, bass walk D-A-F# up into B
  B  (16-23)  G  A  F#m  Bm | Em7  F#m7  G  Asus4-A7           IV-V-iii-vi, climax (E6) in bar 17, V back to bar 0
day    nylon guitar fingerpicking (carries the bass), flute melody, light pad, steel-guitar strums from A'
night  electric piano (bass + broken chords), soft strings, music box (a thinner copy of the melody)
fight  folk-rock kit, pick bass in eighths an octave under the base bass, string ostinato in eighths
The flute rests in bars 3, 7, 11, 15, 19, 22, 23 (the music box in more) so the lake ambience can breathe.
Only chord tones sit on the beats in every track (passing tones on off-beat eighths), so the stems never clash.
"""

from fk_music import Song, Key, GM, DRUMS, DR, n

KEY = Key("D", "major")

# chord tones as pitch classes
CH = {c: [n(x + "0") % 12 for x in tones] for c, tones in {
    "D": ["D", "F#", "A"], "A": ["A", "C#", "E"], "A7": ["A", "C#", "E", "G"], "Asus4": ["A", "D", "E"],
    "Bm": ["B", "D", "F#"], "F#m": ["F#", "A", "C#"], "F#m7": ["F#", "A", "C#", "E"],
    "G": ["G", "B", "D"], "Em7": ["E", "G", "B", "D"],
}.items()}

# one row per bar: (beat, chord, bass, alternate bass for the guitar's / piano's second bass note)
A_SEC = [
    [(0, "D", "D3", "A2")],
    [(0, "A", "C#3", "A2")],
    [(0, "Bm", "B2", "F#2")],
    [(0, "F#m", "A2", "F#2")],
    [(0, "G", "G2", "B2")],
    [(0, "D", "F#2", "A2")],
    [(0, "Em7", "E2", "B2")],
    [(0, "Asus4", "A2", None), (2, "A", "A2", "E3")],
]
A2_SEC = A_SEC[:6] + [
    [(0, "Em7", "E2", None), (2, "A7", "A2", "A2")],
    [(0, "D", "D3", "A2")],                      # + F#2 on beat 3 walking up to G2
]
B_SEC = [
    [(0, "G", "G2", "D3")],
    [(0, "A", "A2", "E2")],
    [(0, "F#m", "F#2", "C#3")],
    [(0, "Bm", "B2", "F#2")],
    [(0, "Em7", "E2", "B2")],
    [(0, "F#m7", "F#2", "A2")],
    [(0, "G", "G2", "B2")],
    [(0, "Asus4", "A2", None), (2, "A7", "A2", "E3")],   # + C#3 on beat 3.5 leading to D3
]
PROG = A_SEC + A2_SEC + B_SEC
BARS = len(PROG)


def section(bar):
    return "A" if bar < 8 else ("A2" if bar < 16 else "B")


def segs(bar):
    """[(start, end, chord, bass, alt)] for one bar (pitches as MIDI numbers)."""
    row = PROG[bar]
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


def voicing(chord, lo, count):
    """The first `count` chord tones at or above `lo` (closed position)."""
    out, p = [], lo
    while len(out) < count:
        if p % 12 in CH[chord]:
            out.append(p)
        p += 1
    return out


def clip(bar, beat, dur):
    """Shorten a note so it never rings into the next chord."""
    end = seg_at(bar, beat)[1]
    return max(0.1, min(dur, end - beat - 0.04))


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


# ------------------------------------------------------------------------------------------- melody (flute)
# (pitch | None, beats) per bar; bars not listed are rests
MELODY = {
    # A: rising arpeggio, falling scale, rising again; then the higher answer ending on E over Em7
    0: [(None, 1), ("A4", .5), ("D5", .5), ("F#5", 1.5), ("E5", .5)],
    1: [("E5", 1.5), ("D5", .5), ("C#5", 1), ("A4", 1)],
    2: [("B4", 1), ("D5", 1), ("F#5", 2)],
    4: [("D5", 1), ("G5", 1), ("B5", 2)],
    5: [("A5", 1.5), ("G5", .5), ("F#5", 1), ("D5", 1)],
    6: [("G5", 1.5), ("F#5", .5), ("E5", 2)],
    # A': the same shapes a third higher, reaching D6; E5 over A7 is left for the guitar to resolve
    8: [(None, 1), ("F#5", .5), ("G5", .5), ("A5", 1.5), ("B5", .5)],
    9: [("A5", 1.5), ("G5", .5), ("E5", 1), ("C#5", 1)],
    10: [("D5", 1.5), ("E5", .5), ("F#5", 2)],
    12: [("B4", .5), ("D5", .5), ("G5", 1), ("B5", 2)],
    13: [("A5", 1), ("D6", 1), ("A5", 1), ("F#5", 1)],
    14: [("G5", 1.5), ("F#5", .5), ("E5", 2)],
    # B: the climax (E6 over A), then a lower answer ending on E over F#m7
    16: [("D5", .5), ("E5", .5), ("G5", 1), ("B5", 2)],
    17: [("A5", 1), ("C#6", 1), ("E6", 1.5), ("D6", .5)],
    18: [("C#6", 1.5), ("B5", .5), ("A5", 1), ("F#5", 1)],
    20: [("B5", 1.5), ("A5", .5), ("G5", 1), ("E5", 1)],
    21: [("F#5", 1.5), ("A5", .5), ("E5", 2)],
}

# night: the music box keeps only the long notes, and skips the first half of A'
NIGHT_MELODY = {
    0: [(None, 2), ("F#5", 2)],
    1: [("E5", 2), ("C#5", 1), ("A4", 1)],
    2: [("D5", 2), ("F#5", 2)],
    4: [(None, 1), ("G5", 1), ("B5", 2)],
    5: [("A5", 2), ("F#5", 2)],
    6: [("G5", 2), ("E5", 2)],
    12: [(None, 1), ("G5", 1), ("B5", 2)],
    13: [("A5", 1), ("D6", 1), ("A5", 2)],
    14: [("G5", 2), ("E5", 2)],
    16: [(None, 1), ("G5", 1), ("B5", 2)],
    17: [("A5", 2), ("C#6", 2)],
    18: [("A5", 2), ("F#5", 2)],
    20: [("B5", 2), ("G5", 2)],
    21: [("F#5", 2), ("E5", 2)],
}

SEC_VEL = {"A": 70, "A2": 74, "B": 80}


def melody_vel(bar, p, base_shift=0):
    """Phrase dynamics: louder sections, higher notes a little louder."""
    return int(SEC_VEL[section(bar)] + base_shift + 0.6 * (p - 76))


def write_melody(track, mel, shift=0, legato=0.95):
    for bar, items in mel.items():
        pos = 0.0
        for (nm, d) in items:
            if nm is not None:
                p = n(nm)
                track.note(bar, pos, p, d * legato, melody_vel(bar, p, shift))
            pos += d


# ------------------------------------------------------------------------------------------------- day stem
# fingerpicking patterns: (beat, voice, beats); voice "B" = bass, "A" = alternate bass, 0-2 = chord voice,
# 3 = the lowest voice an octave up
PICK = {
    "A": [(0, "B", 2), (1, 0, 1), (1.5, 1, 1), (2, "A", 2), (2.5, 2, 1), (3, 1, 1)],
    "A2": [(0, "B", 2), (0.5, 0, 1), (1, 1, 1), (1.5, 2, 1.5), (2, "A", 2), (2.5, 1, 1), (3, 2, 1), (3.5, 1, .5)],
    "B": [(0, "B", 2), (0.5, 0, 1), (1, 1, 1), (1.5, 3, 1.5), (2, "A", 2), (2.5, 2, 1), (3, 1, 1), (3.5, 2, .5)],
}


def day(s):
    gtr = s.track("nylon", GM["nylon_guitar"], stem="day", vol=100, pan=-18, reverb=48)
    fl = s.track("flute", GM["flute"], stem="day", vol=88, pan=10, reverb=62)
    pad = s.track("pad", GM["warm_pad"], stem="day", vol=78, pan=0, reverb=72)
    stl = s.track("steel", GM["steel_guitar"], stem="day", vol=104, pan=28, reverb=50)

    for bar in range(BARS):
        sec = section(bar)
        # guitar: bass + arpeggio, louder bass
        for (bt, which, d) in PICK[sec]:
            st, en, ch, bass, alt = seg_at(bar, bt)
            if which == "B":
                p, v = bass, 66
            elif which == "A":
                p, v = alt, 60
            else:
                vc = voicing(ch, 54, 3)
                p = vc[0] + 12 if which == 3 else vc[which]
                v = 52 if bt % 1 else 56
            gtr.note(bar, bt, p, clip(bar, bt, d), v + (4 if sec == "B" else 0))
        if bar == 15:
            gtr.note(bar, 3, n("F#2"), 0.96, 62)          # walk D3 - A2 - F#2 -> G2
        if bar == 23:
            gtr.note(bar, 3.5, n("C#3"), 0.48, 60)        # leading tone back to D3 at bar 0

        # pad: thin in A, fuller later; one note per chord
        for (st, en, ch, bass, alt) in segs(bar):
            vc = {"A": voicing(ch, 62, 2), "A2": voicing(ch, 57, 3), "B": voicing(ch, 57, 4)}[sec]
            pad.chord(bar, st, vc, en - st - 0.08, {"A": 40, "A2": 46, "B": 50}[sec])

        # steel guitar strums: one soft downstroke per chord in A', a gentle pattern in B
        if sec == "A2":
            for (st, en, ch, bass, alt) in segs(bar):
                stl.chord(bar, st, voicing(ch, 55, 4), en - st - 0.1, 46, strum=0.05)
        elif sec == "B":
            for (bt, up, d, v) in [(0, False, 1.5, 54), (1.5, True, .5, 40), (2, False, 1.5, 50), (3.5, True, .5, 38)]:
                ch = seg_at(bar, bt)[2]
                vc = voicing(ch, 55, 4)
                stl.chord(bar, bt, vc[1:] if up else vc, clip(bar, bt, d), v, strum=0.025 if up else 0.04)

    write_melody(fl, MELODY)

    # pad breathes up into B and back down by the loop point
    pad.cc(0, 0, 11, 92)
    pad.swell(16, 0, 8, 92, 118)
    pad.swell(21, 0, 12, 118, 92)


# ----------------------------------------------------------------------------------------------- night stem
def night(s):
    ep = s.track("epiano", GM["epiano"], stem="night", vol=110, pan=-14, reverb=60, chorus=40)
    st_ = s.track("strings", GM["slow_strings"], stem="night", vol=102, pan=16, reverb=75)
    mb = s.track("musicbox", GM["music_box"], stem="night", vol=127, pan=22, reverb=78)

    for bar in range(BARS):
        sec = section(bar)
        for (st, en, ch, bass, alt) in segs(bar):
            vc = voicing(ch, 54, 3)
            # bass on each chord; in B a whole-bar chord splits into bass + alternate bass on beat 2
            if sec == "B" and en - st == 4:
                ep.note(bar, st, bass, 1.9, 58)
                ep.note(bar, 2, alt, 1.9, 52)
            else:
                ep.note(bar, st, bass, en - st - 0.05, 58)
            # right hand: a late chord in A, two softer hits in A', a slow broken chord in B
            if sec == "A":
                hits = [(st + 1, vc, 2.8, 44)] if en - st >= 2 else []
            elif sec == "A2":
                hits = [(st + 1, vc, 1.4, 46), (st + 2.5, vc[1:], 1.4, 40)] if en - st == 4 else [(st + 1, vc, 0.9, 44)]
            else:
                hits = [(st + b, [vc[k]], 1.2, v) for (b, k, v) in [(0.5, 0, 44), (1, 1, 42), (1.5, 2, 46),
                                                                    (2.5, 1, 40), (3, 2, 42)] if st + b < en]
            for (bt, ps, d, v) in hits:
                ep.chord(bar, bt, ps, clip(bar, bt, d), v)

        # strings: two high notes in A, three in A', four in B; long and soft
        for (st, en, ch, bass, alt) in segs(bar):
            vc = {"A": voicing(ch, 64, 2), "A2": voicing(ch, 59, 3), "B": voicing(ch, 57, 4)}[sec]
            st_.chord(bar, st, vc, en - st - 0.06, {"A": 40, "A2": 44, "B": 48}[sec])
        if bar == 15:
            ep.note(bar, 2, n("A2"), 0.95, 50)
            ep.note(bar, 3, n("F#2"), 0.95, 52)
        if bar == 23:
            ep.note(bar, 3.5, n("C#3"), 0.45, 46)

    write_melody(mb, NIGHT_MELODY, shift=-2, legato=0.98)

    # strings breathe in two-bar waves
    for bar in range(0, BARS, 4):
        st_.swell(bar, 0, 8, 80, 104)
        st_.swell(bar + 2, 0, 8, 104, 80)


# ----------------------------------------------------------------------------------------------- fight stem
def fight(s):
    dr = s.track("drums", DRUMS, stem="fight", vol=94, pan=0, reverb=24)
    bs = s.track("bass", GM["pick_bass"], stem="fight", vol=74, pan=0, reverb=8)
    os_ = s.track("ostinato", GM["strings"], stem="fight", vol=81, pan=-24, reverb=38)

    def low(p):
        return p - 12

    for bar in range(BARS):
        sec = section(bar)
        # --- drums
        if bar in (0, 8, 16):
            dr.note(bar, 0, DR["crash"], 1.5, 92)
        if bar == 20:
            dr.note(bar, 0, DR["crash2"], 1.5, 78)     # lighter crash halfway through B
        kick = {"A": "x.....x.x.......", "A2": "x.....x.x.....x.", "B": "x.....x.x.x...x."}[sec]
        snare = "....x.......x..."
        hat = {"A": "x.x.x.x.x.x.x.x.", "A2": "x.x.x.xxx.x.x.xx", "B": "x.x.x.x.x.x.x.x."}[sec]
        if bar in (7, 23):
            hat = hat[:12] + "...."
            snare = "....x......."
        if bar == 15:
            hat = hat[:8] + "........"
            snare = "....x..."
            kick = "x.....x........."
        dr.hits(bar, kick, "kick", vel=88, accent=8)
        dr.hits(bar, snare, "snare", vel=84)
        if sec == "B":
            dr.hits(bar, hat, "hat", vel=60, accent=4)
            dr.hits(bar, "....x.......x...", "tambourine", vel=58)
        else:
            dr.hits(bar, hat, "hat", vel=56, accent=4)
        # fills
        if bar == 7:
            for i, v in enumerate([62, 70, 78, 88]):
                dr.note(bar, 3 + 0.25 * i, DR["snare"], 0.2, v)
        if bar == 15:
            toms = ["high_tom", "high_tom", "mid_tom", "mid_tom", "tom2", "tom2", "low_tom", "low_tom"]
            for i, (k, v) in enumerate(zip(toms, [74, 70, 78, 74, 82, 78, 88, 84])):
                dr.note(bar, 2 + 0.25 * i, DR[k], 0.22, v)
            dr.note(bar, 2, DR["kick"], 0.2, 84)
        if bar == 23:
            for i in range(8):
                dr.note(bar, 2 + 0.25 * i, DR["snare"], 0.2, 58 + 5 * i)     # roll into the crash at bar 0

        # --- pick bass: eighths an octave under the base bass, octave pops in A'/B, a step into the next bar
        pat = {"A": [0, 0, 0, 0, 0, 0, 0, "x"], "A2": [0, 0, 0, 12, 0, 0, 0, "x"],
               "B": [0, 0, 12, 0, 0, 0, 12, "x"]}[sec]
        vels = [94, 72, 84, 74, 90, 72, 84, 76]
        nxt = low(segs((bar + 1) % BARS)[0][3])
        for i, off in enumerate(pat):
            bt = 0.5 * i
            root = low(seg_at(bar, bt)[3])
            if off == "x":
                if nxt > root:
                    p = in_key_below(nxt)
                elif nxt < root:
                    p = in_key_above(nxt)
                else:
                    p = root
                if abs(p - root) > 5:
                    p = root
            else:
                p = root + off
            if bar == 15 and bt >= 2:
                p = n("A1") if bt < 3 else n("F#1")         # the walk into B, under the guitar's
            bs.note(bar, bt, p, 0.44, vels[i])

        # --- string ostinato on the chord tones, eighths
        figure = {"A": [0, 2, 1, 2, 0, 2, 1, 2], "A2": [0, 2, 1, 3, 0, 2, 1, 2], "B": [0, 2, 3, 2, 1, 2, 3, 2]}[sec]
        for i, k in enumerate(figure):
            bt = 0.5 * i
            vc = voicing(seg_at(bar, bt)[2], 61, 3)
            p = vc[0] + 12 if k == 3 else vc[k]
            os_.note(bar, bt, p, 0.42, (74 if i % 2 == 0 else 62) + (4 if sec == "B" else 0))


def songs():
    s = Song("stage_lake", bpm=80, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Calm lake shore: fingerpicked nylon guitar and flute over a descending D-major bass; "
                  "night: electric piano, soft strings and a music box; fight: folk-rock kit, pick bass, string ostinato")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    return [s]
