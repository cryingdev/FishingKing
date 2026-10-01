"""
map — the island map (README §3.1): a light journey in C major, 104 bpm, 16 bars, one `main` stem.

Form (4-bar phrases):
  A1  0-3   C  | Am | F  | G          accordion: MOTIF head, sequenced up to F; half cadence on ti
  A2  4-7   C  | Am | Dm7 G7 | C      accordion: head rises to C6, MOTIF bar 2 shape, MOTIF bar 4 cadence (re mi re do)
  B1  8-11  F  | G  | Em7 | Am        whistle: the head in augmentation, then in its own rhythm on D
  B2  12-15 Dm7 | Em7 | F | G7        whistle: climb to the climax (E6, MOTIF bar 2 shape), G7 leads back to bar 0
Under it: marimba broken chords (one breath at the bar-7 cadence), xylophone echoes in the melody's gaps,
a walking acoustic bass, accordion off-beat chords in B, tambourine + shaker.
"""

from fk_music import Song, Key, GM, DRUMS, MOTIF, n

# marimba voicings (mid register, G3..F4) and accordion off-beat chord voicings (B3..A4)
MARIMBA = {
    "C": ["G3", "C4", "E4"], "Am": ["A3", "C4", "E4"], "F": ["A3", "C4", "F4"], "G": ["G3", "B3", "D4"],
    "G7": ["G3", "B3", "F4"], "Dm7": ["A3", "D4", "F4"], "Em7": ["G3", "B3", "E4"],
}
ACC = {
    "F": ["C4", "F4", "A4"], "G": ["B3", "D4", "G4"], "Em7": ["B3", "E4", "G4"], "Am": ["C4", "E4", "A4"],
    "Dm7": ["D4", "F4", "A4"], "G7": ["B3", "D4", "F4"],
}

# harmony: per bar, (beat, chord) changes
CHORDS = [
    [(0, "C")], [(0, "Am")], [(0, "F")], [(0, "G")],
    [(0, "C")], [(0, "Am")], [(0, "Dm7"), (2, "G7")], [(0, "C")],
    [(0, "F")], [(0, "G")], [(0, "Em7")], [(0, "Am")],
    [(0, "Dm7")], [(0, "Em7")], [(0, "F")], [(0, "G7")],
]

# walking bass, one bar of quarter notes each: chord tones on 1, mostly a step into the next root on 4.
# Where the melody runs through the same chord (bars 0, 5, 11, 12, 15) the bass takes another path or moves
# against it, so the outer voices never move in octaves / fifths (no bass shadowing the MOTIF head).
BASS = [
    ["C2", "E2", "G2", "C3"], ["A2", "C3", "G2", "E2"], ["F2", "A2", "C3", "A2"], ["G2", "B2", "D3", "B2"],
    ["C3", "G2", "E2", "G2"], ["A2", "G2", "E2", "C2"], ["D2", "F2", "G2", "B2"], ["C3", "A2", "G2", "E2"],
    ["F2", "A2", "C3", "A2"], ["G2", "B2", "D3", "F3"], ["E3", "D3", "B2", "G2"], ["A2", "E2", "G2", "C3"],
    ["D3", "C3", "A2", "F2"], ["E2", "B2", "G2", "E2"], ["F2", "A2", "C3", "A2"], ["G2", "A2", "B2", "G2"],
]   # bar 12 falls while the whistle climbs; bar 15 climbs G-A-B under the falling G7 line, then V -> I

# accordion melody, A section (scale degrees of C, octave 5: 0 = C5, -3 = G4)
ACCORDION = {
    0: [(-3, .5), (0, .5), (1, .5), (2, 1.5), (4, .5), (2, .5)],   # MOTIF bar 1, tail turned upward
    1: [(5, 1), (4, .5), (2, .5), (0, 1), (None, 1)],             # la sol mi (MOTIF bar 2) closing on do
    2: [(0, .5), (3, .5), (4, .5), (5, 1.5), (4, .5), (3, .5)],    # the head sequenced onto F
    3: [(1, 1), (2, .5), (1, .5), (-1, 1.5), (None, .5)],         # re mi re ti: half cadence
    4: [(-3, .5), (0, .5), (1, .5), (2, .5), (4, 1), (5, 1)],      # head in straight eighths, rising on
    5: [(7, 1.5), (6, .5), (5, .5), (4, .5), (2, 1)],              # the A climax C6, stepping down
    6: [(d + 7, b) for d, b in MOTIF[1][:3]] + [(4, 1.5), (None, .5)],   # MOTIF bar 2 up an octave, a breath
    7: MOTIF[3],                                                  # the motif's own cadence: re mi re do
}

# whistle melody, B section (octave 5)
WHISTLE = {
    8: [(0, 1), (3, 1), (4, 1), (5, 1)],                          # head in augmentation on F
    9: [(6, 2), (5, .5), (4, .5), (1, 1)],
    10: [(1, .5), (4, .5), (5, .5), (6, 1.5), (5, .5), (4, .5)],  # head in its own rhythm on D
    11: [(7, 1), (5, .5), (4, .5), (2, 1.5), (None, .5)],
    12: [(3, .5), (5, .5), (7, .5), (8, 1.5), (7, .5), (5, .5)],  # head rhythm on a Dm7 arpeggio
    13: [(d + 11, b) for d, b in MOTIF[1]],                       # climax: MOTIF bar 2 from E6
    14: [(7, 1), (8, .5), (7, .5), (5, 1.5), (None, .5)],         # do re do la (MOTIF bar 4 turned)
    15: [(4, 1.5), (3, .5), (1, 1), (None, 1)],                   # down the G7 chord, rest for the pickup
}

# xylophone echoes in the melody's gaps: (bar, octave, items)
XYLO = [
    (1, 6, [(None, 2.5), (4, .5), (2, .5), (0, .5)]),             # echoes "sol mi do" an octave up
    (3, 5, [(None, 2.5), (4, .5), (6, .5), (8, .5)]),             # G arpeggio up into bar 4
    (7, 6, [(None, 2), (4, .5), (2, .5), (1, .5), (0, .5)]),      # sol mi re do answers the cadence
    (9, 6, [(None, .5), (1, .5), (4, .5), (5, .5), (6, .5)]),     # the head in diminution under the long ti
    (11, 6, [(None, 2.5), (0, .5), (2, .5), (5, .5)]),            # lifts into B2
]


def songs():
    s = Song("map", bpm=104, bars=16, key=Key("C", "major"), stems=["main"], kind="loop", loudness=-19.0,
             desc="지도: 가벼운 여정 (마림바·실로폰, 아코디언/휘슬의 MOTIF 변주, 걷는 베이스, 탬버린·셰이커)")
    k = s.key

    mar = s.track("marimba", GM["marimba"], vol=97, pan=-24, reverb=45)
    xyl = s.track("xylophone", GM["xylophone"], vol=112, pan=28, reverb=55)
    acc = s.track("accordion", GM["accordion"], vol=111, pan=6, reverb=40)
    pah = s.track("accordion_chords", GM["accordion"], vol=76, pan=26, reverb=35)
    whi = s.track("whistle", GM["whistle"], vol=75, pan=-8, reverb=55)
    bas = s.track("bass", GM["acoustic_bass"], vol=90, pan=0, reverb=18)
    drm = s.track("perc", DRUMS, vol=92, pan=10, reverb=30)

    # --- marimba: Alberti eighths in A, rolling eighths in B
    for bar, changes in enumerate(CHORDS):
        pattern = [0, 2, 1, 2] if bar < 8 or bar == 15 else [0, 1, 2, 1]
        vel = 60 if bar < 12 else 64
        if bar == 7:   # the A2 cadence breathes: one struck C chord under the xylophone's answer, then B
            mar.arp(bar, 0, [n(p) for p in MARIMBA["C"]], pattern, 0.5, 2, vel=vel, gate=0.9, accent=8)
            mar.chord(bar, 2, [n(p) for p in MARIMBA["C"]], 1.5, vel + 2, strum=0.03)
            continue
        for i, (beat, ch) in enumerate(changes):
            end = changes[i + 1][0] if i + 1 < len(changes) else s.bpb
            mar.arp(bar, beat, [n(p) for p in MARIMBA[ch]], pattern, 0.5, end - beat, vel=vel, gate=0.9, accent=8)

    # --- walking bass: beat 1 strongest, a lighter pickup on beat 4
    for bar, line in enumerate(BASS):
        for beat, p in enumerate(line):
            bas.note(bar, beat, p, 0.85, (88, 74, 80, 70)[beat])

    # --- accordion melody (A) + the pickup at the end of bar 15 that leads back into bar 0
    acc.cc(0, 0, 11, 110)
    for bar, items in ACCORDION.items():
        vel = 84 if bar in (4, 5) else 82
        acc.degs(bar, 0, k, items, octave=5, vel=vel, legato=0.92)
    acc.swell(4, 0, 4, 110, 120)          # lift towards the C6 in bar 5
    acc.swell(7, 2, 2, 120, 102)          # let the cadence settle
    acc.cc(15, 2, 11, 110)
    acc.degs(15, 3, k, [(-1, .5), (-2, .5)], octave=5, vel=72, legato=0.92)   # ti la -> sol (bar 0)

    # --- accordion off-beat chords under the whistle (B)
    for bar in range(8, 16):
        ch = CHORDS[bar][0][1]
        beats = (1,) if bar == 15 else (1, 3)
        for beat in beats:
            pah.chord(bar, beat, [n(p) for p in ACC[ch]], 0.4, 50 if beat == 1 else 46)

    # --- whistle melody (B), a crescendo into the climax at bar 13
    whi.cc(8, 0, 11, 106)
    for bar, items in WHISTLE.items():
        vel = {12: 80, 13: 82, 14: 78, 15: 72}.get(bar, 78)
        whi.degs(bar, 0, k, items, octave=5, vel=vel, legato=0.9)
    whi.swell(12, 0, 4, 106, 118)          # a moderate lift: the E6 climax should glow, not pierce
    whi.swell(14, 0, 8, 118, 106)

    # --- xylophone echoes; the two climaxes (bar 5, bar 13) doubled an octave above the melody
    for bar, octv, items in XYLO:
        xyl.degs(bar, 0, k, items, octave=octv, vel=68, legato=0.9)
    xyl.degs(5, 0, k, ACCORDION[5], octave=6, vel=50, legato=0.9)
    xyl.degs(13, 0, k, WHISTLE[13], octave=6, vel=54, legato=0.9)

    # --- tambourine on 2 and 4, shaker eighths leaning on the off-beats
    for bar in range(s.bars):
        drm.hits(bar, "xXxXxXxX", "shaker", step=0.5, vel=32)
        if bar == 15:
            tam = "..x..xX."                  # a small push back into bar 0
        elif 12 <= bar <= 14:
            tam = "..x...xx"
        else:
            tam = "..x...x."
        drm.hits(bar, tam, "tambourine", step=0.5, vel=48)

    s.humanize(timing=0.01, velocity=5)
    return [s]
