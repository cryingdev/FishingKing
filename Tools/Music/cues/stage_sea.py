"""
stage_sea — 바다 방파제 (sea breakwater, tetrapods). F major, 100 bpm, 32 bars = 76.8 s, loop.

Form (8 + 8 + 8 + 8 bars). Every stem reads the same chord map (PROG), so day / night / fight line up bar for bar:
  A   (0-7)    F6   Am7  Bb6  C7   | F6   Dm7      Gm7    C7sus-C7      half cadence
  A'  (8-15)   F6   Am7  Bb6  Bbm6 | Am7  Dm7      Gm7-C7 F6-F7/A       borrowed iv, full cadence, F7 leads into B
  B   (16-23)  Bb6  C7   Am7  Dm7  | Gm7  Am7-D7/F# Gm7   C7sus-C7      IV-V-iii-vi, climax D6 over D7 (bar 21)
  A'' (24-31)  F6   Am7  Bb6  C7   | Dm7  Bb6-Bbm6 Gm7    C7sus-C7/E    quieter return, V (bass E) back to bar 0
day    steel guitar strummed like a ukulele (island strum), marimba bass; the tune moves from steel drums (A) to
       whistle (A') to accordion (B) and back to steel drums (A''); accordion pad + steel-drum off-beat pings in A'
night  nylon guitar bossa (thumb bass on 1 and 3, chords on the 3-2 clave), vibraphone playing the tune's long notes
fight  congas, bongos, kick, shaker (claves in B, timbale fills), finger bass in driving eighths (slap bass in B)
       an octave under the base bass, muted-guitar ostinato
The tune rests in bars 3, 7, 11, 19, 23, 25, 27, 30, 31 (the vibraphone in more) so the waves can be heard.
The chords avoid internal semitones (6ths and 7ths, no maj7) and only chord tones sit on the beats in every
track (passing tones on off-beat eighths), so the stems never clash with each other.
"""

from fk_music import Song, Key, GM, DRUMS, DR, TPB, n

KEY = Key("F", "major")


def pcs(*names):
    return [n(x + "0") % 12 for x in names]


# chord tones as pitch classes, root first (the second entry is the third, or the fourth of a sus chord)
CH = {
    "F6": pcs("F", "A", "C", "D"),
    "F7": pcs("F", "A", "C", "Eb"),
    "Am7": pcs("A", "C", "E", "G"),
    "Bb6": pcs("Bb", "D", "F", "G"),
    "Bbm6": pcs("Bb", "Db", "F", "G"),
    "C7": pcs("C", "E", "G", "Bb"),
    "C7sus": pcs("C", "F", "G", "Bb"),
    "Dm7": pcs("D", "F", "A", "C"),
    "Gm7": pcs("G", "Bb", "D", "F"),
    "D7": pcs("D", "F#", "A", "C"),
}

# one row per bar: (beat, chord, bass, alternate bass) — the bass of the day marimba / night guitar thumb
A_SEC = [
    [(0, "F6", "F2", "C3")],
    [(0, "Am7", "A2", "E2")],
    [(0, "Bb6", "Bb2", "F2")],
    [(0, "C7", "C3", "G2")],
    [(0, "F6", "F2", "C3")],
    [(0, "Dm7", "D3", "A2")],
    [(0, "Gm7", "G2", "D3")],
    [(0, "C7sus", "C3", "G2"), (2, "C7", "C3", "E2")],      # E2 on the last eighth leads to F2
]
A2_SEC = A_SEC[:3] + [
    [(0, "Bbm6", "Bb2", "F2")],
    [(0, "Am7", "A2", "E2")],
    [(0, "Dm7", "D3", "A2")],
    [(0, "Gm7", "G2", "D3"), (2, "C7", "C3", "G2")],
    [(0, "F6", "F2", "C3"), (2, "F7", "A2", "C3")],         # F7/A: A2 - (C3) - Bb2 into B
]
B_SEC = [
    [(0, "Bb6", "Bb2", "F2")],
    [(0, "C7", "C3", "G2")],
    [(0, "Am7", "A2", "E2")],
    [(0, "Dm7", "D3", "A2")],
    [(0, "Gm7", "G2", "D3")],
    [(0, "Am7", "A2", "E2"), (2, "D7", "F#2", "A2")],       # D7/F# -> Gm7
    [(0, "Gm7", "G2", "D3")],
    [(0, "C7sus", "C3", "G2"), (2, "C7", "C3", "E2")],
]
A3_SEC = A_SEC[:4] + [
    [(0, "Dm7", "D3", "A2")],
    [(0, "Bb6", "Bb2", "F2"), (2, "Bbm6", "Bb2", "F2")],
    [(0, "Gm7", "G2", "D3")],
    [(0, "C7sus", "C3", "G2"), (2, "C7", "C3", "E2")],      # V with E2 at the end: back to F2 at bar 0
]
PROG = A_SEC + A2_SEC + B_SEC + A3_SEC
BARS = len(PROG)


def section(bar):
    return ("A", "A2", "B", "A3")[bar // 8]


def segs(bar):
    """[(start, end, chord, bass, alt)] for one bar (pitches as MIDI numbers)."""
    row = PROG[bar % BARS]
    out = []
    for i, (b, c, bass, alt) in enumerate(row):
        e = row[i + 1][0] if i + 1 < len(row) else 4
        out.append((b, e, c, n(bass), n(alt)))
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


def place(pc, lo):
    """The pitch of pitch class pc at or above lo (within an octave)."""
    return lo + (pc - lo) % 12


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


# ------------------------------------------------------------------------------------------------- the tune
# (pitch | None, beats) per bar; bars not listed are rests. Chord tones on every beat.
TUNE = {
    # A (steel drums): a rising, syncopated arch on F, answered lower; the second phrase reaches C6
    0: [("C5", .5), ("F5", .5), ("A5", 1.5), ("G5", .5), ("F5", 1)],
    1: [("E5", 1), ("C5", .5), ("D5", .5), ("E5", 2)],
    2: [("D5", .5), ("F5", .5), ("G5", 3)],
    4: [("C5", .5), ("F5", .5), ("A5", 1), ("C6", 1), ("A5", 1)],
    5: [("A5", .5), ("G5", .5), ("F5", 1), ("A5", .5), ("G5", .5), ("F5", .5), ("E5", .5)],
    6: [("D5", 1.5), ("F5", .5), ("G5", 2)],
    # A' (whistle): the arch starts a third higher; closes on F over the cadence
    8: [("F5", .5), ("A5", .5), ("C6", 1.5), ("Bb5", .5), ("A5", 1)],
    9: [("G5", 1), ("E5", .5), ("F5", .5), ("G5", 1), ("A5", 1)],
    10: [("Bb5", 1.5), ("A5", .5), ("G5", 1), ("F5", 1)],
    12: [("C6", 1), ("A5", .5), ("G5", .5), ("E5", 1), ("G5", 1)],
    13: [("F5", .5), ("A5", .5), ("C6", 2), ("A5", 1)],
    14: [("Bb5", .5), ("A5", .5), ("G5", 1), ("E5", 1), ("G5", 1)],
    15: [("F5", 2)],
    # B (accordion): longer notes, then the climb to the climax (D6 over D7) and the fall to F
    16: [("D5", 1), ("F5", 1), ("G5", 1.5), ("F5", .5)],
    17: [("E5", 1.5), ("D5", .5), ("C5", 1), ("G5", 1)],
    18: [("A5", 2), ("G5", 1), ("E5", 1)],
    20: [("D5", .5), ("G5", .5), ("Bb5", 1.5), ("A5", .5), ("G5", 1)],
    21: [("C6", 1), ("A5", .5), ("C6", .5), ("D6", 1.5), ("C6", .5)],
    22: [("Bb5", 1.5), ("A5", .5), ("G5", 1), ("F5", 1)],
    # A'' (steel drums): the arch once more, then single phrases with space; Db (Bbm6) colours the last one
    24: [("C5", .5), ("F5", .5), ("A5", 1.5), ("G5", .5), ("C6", 1)],
    26: [("F5", 1), ("D5", .5), ("F5", .5), ("G5", 2)],
    28: [("A5", 1), ("F5", .5), ("G5", .5), ("A5", 1), ("C6", 1)],
    29: [("D6", .5), ("C6", .5), ("Bb5", 1), ("F5", 1), ("Db5", 1)],
}

# night: the vibraphone plays a thinner copy of the tune in fewer bars (none in bars 11-12 and 15)
NIGHT_BARS = [0, 1, 2, 4, 5, 6, 8, 9, 10, 13, 14, 16, 17, 18, 20, 21, 22, 24, 26, 28, 29]

SEC_VEL = {"A": 76, "A2": 72, "B": 74, "A3": 70}


def tune_vel(bar, p, shift=0):
    """Phrase dynamics: section level, higher notes a little louder."""
    return int(SEC_VEL[section(bar)] + shift + 0.6 * (p - n("A5")))


def tune_notes(bar):
    """[(beat, pitch, dur)] of the tune in one bar."""
    out, pos = [], 0.0
    for (nm, d) in TUNE.get(bar, []):
        if nm is not None:
            out.append((pos, n(nm), d))
        pos += d
    return out


def strum(tr, bar, beat, ps, dur, vel, up=False, gap=0.022):
    """A strummed chord: down = low to high, up = high to low and a little softer."""
    for i, p in enumerate(sorted(ps, reverse=up)):
        tr.note(bar, beat + i * gap, p, max(0.1, dur - i * gap), vel - (2 * i if up else 0))


# ------------------------------------------------------------------------------------------------- day stem
# island strum, (beat, up?, beats, vel): D . D U . U D U
UKE = {
    "A": [(0, False, 1, 50), (1, False, .5, 44), (1.5, True, 1, 36), (2.5, True, .5, 34), (3, False, .5, 44),
          (3.5, True, .5, 34)],
    "A2": [(0, False, 1, 52), (1, False, .5, 46), (1.5, True, 1, 38), (2.5, True, .5, 36), (3, False, .5, 46),
           (3.5, True, .5, 36)],
    "B": [(0, False, 1, 56), (1, False, .5, 48), (1.5, True, .5, 40), (2, False, .5, 48), (2.5, True, .5, 40),
          (3, False, .5, 50), (3.5, True, .5, 40)],
    "A3": [(0, False, 1.5, 46), (1.5, True, 1, 34), (2.5, True, .5, 32), (3, False, 1, 40)],
}

# marimba bass: (beat offset in the chord, voice, beats, vel); B = bass, A = alternate bass, U = the third above
MAR_FULL = {
    "A": [(0, "B", 1.45, 72), (1.5, "A", .45, 60), (2, "B", .95, 66), (3, "U", .9, 50)],
    "A2": [(0, "B", 1.45, 72), (1.5, "A", .45, 60), (2, "B", .95, 66), (3, "U", .45, 50), (3.5, "A", .45, 52)],
    "B": [(0, "B", .95, 74), (1, "U", .45, 50), (1.5, "A", .45, 62), (2, "B", .95, 68), (3, "U", .45, 52),
          (3.5, "A", .45, 56)],
    "A3": [(0, "B", 1.9, 68), (2, "A", .95, 58), (3, "U", .9, 46)],
}
MAR_HALF = [(0, "B", .95, 70), (1, "U", .45, 48), (1.5, "A", .45, 58)]


def day(s):
    uke = s.track("uke", GM["steel_guitar"], stem="day", vol=92, pan=-26, reverb=40)
    mar = s.track("marimba", GM["marimba"], stem="day", vol=112, pan=6, reverb=42)
    pan_ = s.track("steel_drums", GM["steel_drums"], stem="day", vol=106, pan=18, reverb=58)
    wh = s.track("whistle", GM["whistle"], stem="day", vol=76, pan=12, reverb=64)
    acc = s.track("accordion", GM["accordion"], stem="day", vol=101, pan=-12, reverb=54)

    for bar in range(BARS):
        sec = section(bar)
        # ukulele-like strums (close voicings C4..Bb4)
        for (bt, up, d, v) in UKE[sec]:
            vc = voicing(seg_at(bar, bt)[2], n("C4"), 4)
            strum(uke, bar, bt, vc[1:] if up else vc, clip(bar, bt, d), v, up)

        # marimba: bass with a calypso lilt
        for (st, en, ch, bass, alt) in segs(bar):
            pat = MAR_FULL[sec] if en - st == 4 else MAR_HALF
            for (off, which, d, v) in pat:
                p = {"B": bass, "A": alt, "U": place(CH[ch][1], n("E3"))}[which]
                mar.note(bar, st + off, p, clip(bar, st + off, d), v)

        # A': steel-drum off-beat pings and a soft accordion pad under the whistle
        if sec == "A2":
            for bt in (1.5, 3.5):
                pan_.chord(bar, bt, voicing(seg_at(bar, bt)[2], n("G4"), 2), 0.4, 40)
        if sec == "A2" or bar >= 28:
            for (st, en, ch, bass, alt) in segs(bar):
                acc.chord(bar, st, voicing(ch, n("D4"), 3), en - st - 0.08, 40 if sec == "A2" else 36)

    # the tune: steel drums in A and A'', whistle in A', accordion in B
    for bar in TUNE:
        tr, shift = {"A": (pan_, 2), "A2": (wh, -6), "B": (acc, 0), "A3": (pan_, 4)}[section(bar)]
        for (b, p, d) in tune_notes(bar):
            tr.note(bar, b, p, d * (0.9 if tr is pan_ else 0.95), tune_vel(bar, p, shift))

    # accordion: the pad breathes in A', the B melody swells to the climax, the closing pad fades to the loop
    acc.cc(0, 0, 11, 96)
    acc.swell(8, 0, 4, 80, 100)
    acc.swell(14, 0, 8, 100, 112)
    acc.swell(19, 0, 8, 112, 124)
    acc.swell(22, 0, 8, 124, 112)
    acc.swell(28, 0, 15, 92, 74)
    wh.cc(0, 0, 11, 110)


# ----------------------------------------------------------------------------------------------- night stem
# bossa guitar: chords on the 3-2 clave over two bars, (beat, beats); thumb on the bass / alternate bass
CLAVE = {
    "A": ([(0, .9), (1.5, .9), (3, .9)], [(1, .9), (2.5, .9)]),
    "A2": ([(0, .9), (1.5, .9), (3, .9)], [(1, .9), (2.5, .9), (3.5, .4)]),
    "B": ([(0, .9), (1, .4), (1.5, .9), (3, .9)], [(.5, .4), (1, .9), (2.5, .9), (3.5, .4)]),
    "A3": ([(0, 1.4), (3, .9)], [(1, .9), (2.5, 1.4)]),
}
CLAVE_VEL = {"A": 44, "A2": 46, "B": 49, "A3": 42}


def night(s):
    gtr = s.track("nylon", GM["nylon_guitar"], stem="night", vol=94, pan=-14, reverb=54)
    vib = s.track("vibes", GM["vibraphone"], stem="night", vol=105, pan=16, reverb=72, chorus=20)

    for bar in range(BARS):
        sec = section(bar)
        # thumb: root on 1, alternate bass on 3 (a two-chord bar: root, then the alternate on the last eighth)
        for (st, en, ch, bass, alt) in segs(bar):
            if en - st == 4:
                gtr.note(bar, 0, bass, 1.9, 58)
                gtr.note(bar, 2, alt, 1.9, 54)
            else:
                gtr.note(bar, st, bass, 1.45, 58)
                gtr.note(bar, st + 1.5, alt, 0.45, 50)
        # fingers: soft four-note chords G3..F4 on the clave
        for (bt, d) in CLAVE[sec][bar % 2]:
            vc = voicing(seg_at(bar, bt)[2], n("G3"), 4)
            v = CLAVE_VEL[sec] - (4 if bt % 1 else 0)
            gtr.chord(bar, bt, vc, clip(bar, bt, d), v, strum=0.012)

    # vibraphone: a thinner copy of the tune — the notes on the beats and the long ones, each held to the next
    for bar in NIGHT_BARS:
        kept = [(b, p, d) for (b, p, d) in tune_notes(bar) if b % 1 == 0 or d >= 1]
        for i, (b, p, d) in enumerate(kept):
            end = kept[i + 1][0] if i + 1 < len(kept) else b + d
            vib.note(bar, b, p, clip(bar, b, end - b), tune_vel(bar, p, 24))


# ----------------------------------------------------------------------------------------------- fight stem
# bass in eighths: R = root, O = octave, F = fifth, x = a step leading to the next bar's root
FB = {
    "A": [("R", 88), ("R", 60), ("R", 76), ("O", 84), ("R", 88), ("R", 62), ("F", 80), ("x", 70)],
    "A2": [("R", 90), ("R", 62), ("R", 78), ("O", 86), ("R", 90), ("O", 70), ("F", 82), ("x", 72)],
    "B": [("R", 92), ("O", 70), ("R", 84), ("R", 66), ("O", 92), ("R", 70), ("F", 86), ("x", 74)],
}
FB["A3"] = FB["A2"]
# muted-guitar ostinato: (beat, voice, vel) on a three-note voicing F3..C4
MG = {
    "A": [(0, 0, 72), (.5, 2, 52), (1, 1, 64), (1.5, 2, 54), (2, 0, 70), (2.5, 2, 52), (3, 1, 64), (3.5, 2, 56)],
    "B": [(0, 0, 76), (.5, 2, 56), (.75, 1, 48), (1, 2, 64), (1.5, 0, 70), (2, 2, 62), (2.5, 1, 56),
          (2.75, 2, 48), (3, 0, 70), (3.5, 1, 58)],
}
MG["A2"] = MG["A"] + [(3.75, 1, 46)]
MG["A3"] = MG["A2"]


def fight_root(chord, lo):
    return place(CH[chord][0], lo)


def fight(s):
    dr = s.track("drums", DRUMS, stem="fight", vol=85, pan=0, reverb=22)
    fb = s.track("finger_bass", GM["finger_bass"], stem="fight", vol=59, pan=0, reverb=6)
    sb = s.track("slap_bass", GM["slap_bass"], stem="fight", vol=58, pan=0, reverb=8)
    mg = s.track("muted_gtr", GM["muted_guitar"], stem="fight", vol=78, pan=26, reverb=26)

    for bar in range(BARS):
        sec = section(bar)
        fill = bar % 8 == 7

        # --- percussion
        if bar == 16:
            dr.note(bar, 0, DR["crash"], 1.5, 76)       # one crash, into B
        kick = {"A": "x.....x.x.......", "A2": "x.....x.x.....x.", "B": "x...x...x...x...",
                "A3": "x.....x.x.....x."}[sec]
        slap = "....x..........."                     # tumbao: slap on 2, open tones on 4 and 4&
        opens = "............x.x."
        ghost = "x.x...x.x.x....."
        shaker = "x.x.x.x.x.x.x.x." if sec in ("A", "A3") else "xxxxxxxxxxxxxxxx"
        bongo = "x.x.x.x.x.x.x.x."
        if fill:
            kick, opens, bongo = kick[:12] + "....", "................", bongo[:8] + "........"
            shaker = shaker[:12] + "...."
        dr.hits(bar, kick, "kick", vel=70)
        dr.hits(bar, slap, "mute_conga", vel=96)
        dr.hits(bar, ghost, "mute_conga", vel=44)
        dr.hits(bar, opens, "hi_conga" if sec != "B" else "lo_conga", vel=90)
        dr.hits(bar, shaker, "shaker", vel=34 if sec == "B" else 40, accent=4)
        if sec != "A":
            dr.hits(bar, bongo, "hi_bongo", vel=54, accent=4)
            if not fill:
                dr.hits(bar, "..........x.....", "lo_bongo", vel=66)
        if sec == "B":
            dr.hits(bar, "x.....x.....x..." if bar % 2 == 0 else "....x...x.......", "claves", vel=50)
            if not fill:
                dr.hits(bar, "......x.........", "hi_conga", vel=76)

        # fills into the next section (timbales, a conga run before A'')
        if bar in (7, 31):
            for i, (k, v) in enumerate([("hi_timbale", 70), ("hi_timbale", 64), ("lo_timbale", 78), ("lo_timbale", 86)]):
                dr.note(bar, 3 + 0.25 * i, DR[k], 0.22, v)
        if bar == 15:
            seq = ["hi_timbale", "hi_timbale", "lo_timbale", "hi_timbale", "lo_timbale", "lo_timbale", "hi_timbale",
                   "lo_timbale"]
            for i, k in enumerate(seq):
                dr.note(bar, 2 + 0.25 * i, DR[k], 0.22, 62 + 3 * i)
            dr.note(bar, 2, DR["kick"], 0.2, 84)
        if bar == 23:
            seq = ["hi_conga", "hi_conga", "lo_conga", "lo_conga", "hi_conga", "lo_conga", "lo_timbale", "lo_timbale"]
            for i, k in enumerate(seq):
                dr.note(bar, 2 + 0.25 * i, DR[k], 0.22, 58 + 4 * i)

        # --- bass: eighth-note drive on the chord roots, an octave under the base bass (slap bass in B)
        tr, lo = (sb, n("A1")) if sec == "B" else (fb, n("F1"))
        nxt = fight_root(segs(bar + 1)[0][2], lo)
        for i, (which, v) in enumerate(FB[sec]):
            bt = 0.5 * i
            root = fight_root(seg_at(bar, bt)[2], lo)
            if which == "x":
                p = in_key_below(nxt) if nxt >= root else in_key_above(nxt)
            else:
                p = root + {"R": 0, "O": 12, "F": 7}[which]
            tr.note(bar, bt, p, 0.42, v)

        # --- muted-guitar ostinato on the chord tones
        for (bt, k, v) in MG[sec]:
            vc = voicing(seg_at(bar, bt)[2], n("F3"), 3)
            mg.note(bar, bt, vc[k], 0.2 if bt % 0.5 else 0.38, v)


def keep_downbeats(song, slack=8):
    """humanize() can pull a downbeat a few ticks into the previous bar; put such notes back on the bar line so
    the rests stay where they are written (and the piano roll reads bar by bar)."""
    bar_t = song.bpb * TPB
    for t in song.tracks:
        out = []
        for (st, d, p, v) in t.notes:
            r = st % bar_t
            if r > bar_t - slack and st + bar_t - r < song.loop_ticks:
                st, d = st + bar_t - r, max(1, d - (bar_t - r))
            out.append((st, d, p, v))
        t.notes = out


def songs():
    s = Song("stage_sea", bpm=100, bars=BARS, key=KEY, stems=["day", "night", "fight"], kind="loop",
             loudness=-21, mixes=[["day", "fight"], ["night", "fight"]], ref=["day"],
             desc="Sea breakwater: ukulele-like steel-guitar strums and marimba bass under a tune passed from steel "
                  "drums to whistle to accordion (F major); night: bossa nylon guitar and vibraphone; "
                  "fight: congas, bongos and kick, finger/slap bass, muted-guitar ostinato")
    day(s)
    night(s)
    fight(s)
    s.humanize(timing=0.01, velocity=5)
    keep_downbeats(s)
    return [s]
