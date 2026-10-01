"""
aquarium — the player's aquarium (README §3.1): F major, 72 bpm, 16 bars, a seamless loop, one `main` stem, no drums.
Players stay here a long time (feeding, cleaning), so everything is soft, slow and roomy: long notes, few layers at
once, and only chord tones on the beats.

Form (4-bar phrases):
  A   0-3   F    | Dm7  | Gm7  | C7sus4 C7      music box: an arch to D6, the 4-3 suspension makes a half cadence
  A'  4-7   F    | Am7  | Bb   | Gm7 C7         music box climbs to E6; it breathes in bar 7 while the vibraphone
                                                answers (C7 arpeggio) and hands over to ...
  B   8-11  Dm7  | Am7  | Bb   | C7sus4 C7      ... the celesta (deceptive cadence onto Dm7): the climax F6 in bar 10,
                                                a vibraphone counter-line of held tones with a 4-3 suspension
  A'' 12-15 F    | Dm7  | Bb6 Bbm6 | Gm11 C7   music box quotes MOTIF bar 1 (sol do re mi— re do) on the return, a
                                                borrowed Bbm6: D -> Db -> C in the tune and in the inner voices alike
                                                (Gm11 = G Bb C F keeps the Db falling to C, then F -> E as C7 arrives);
                                                the vibraphone's C7 arpeggio leads back to bar 0
Under it: harp rolls (flowing eighths in B), electric-piano chords (held in A, a lazy "1, 2-and" comp after),
an electric-piano left-hand bass on the roots, and a quiet warm pad that ties common tones and swells in B.
Tonic and subdominant are add9 / 6 colours rather than major sevenths, so no layer rubs a half step on a beat.
"""

from fk_music import Song, Key, GM, MOTIF, n

KEY = Key("F", "major")

# harmony: per bar, (beat, chord) changes; a chord lasts until the next change or the bar line
HARM = [
    [(0, "F")], [(0, "Dm7")], [(0, "Gm7")], [(0, "C7sus4"), (2, "C7")],            # A
    [(0, "F")], [(0, "Am7")], [(0, "Bb")], [(0, "Gm7"), (2, "C7")],                # A'
    [(0, "Dm7")], [(0, "Am7")], [(0, "Bb")], [(0, "C7sus4"), (2, "C7")],           # B
    [(0, "F")], [(0, "Dm7")], [(0, "Bb6"), (2, "Bbm6")], [(0, "Gm11"), (2, "C7")], # A''
]
SECTION = ["A"] * 4 + ["A'"] * 4 + ["B"] * 4 + ["A2"] * 4

# voicings. Electric piano A3..G4 (rootless-ish, the bass has the roots), pad F4..D5 above it, harp arpeggio tones
# D3..D5 (low to high). F = F add9, Bb = Bb add9.
EP = {"F": "A3 C4 F4 G4", "Dm7": "A3 C4 D4 F4", "Gm7": "Bb3 D4 F4 G4", "C7sus4": "Bb3 C4 F4 G4",
      "C7": "Bb3 C4 E4 G4", "Am7": "A3 C4 E4 G4", "Bb": "Bb3 C4 D4 F4", "Bb6": "Bb3 D4 F4 G4",
      "Bbm6": "Bb3 Db4 F4 G4", "Gm11": "Bb3 C4 F4 G4"}
PAD = {"F": "F4 A4 C5", "Dm7": "F4 A4 D5", "Gm7": "F4 Bb4 D5", "C7sus4": "F4 Bb4 C5", "C7": "E4 Bb4 C5",
       "Am7": "E4 A4 C5", "Bb": "F4 Bb4 D5", "Bb6": "G4 Bb4 D5", "Bbm6": "G4 Bb4 Db5", "Gm11": "F4 Bb4 C5"}
HARP = {"F": "F3 C4 F4 A4 C5", "Dm7": "D3 A3 D4 F4 A4", "Gm7": "G3 D4 F4 Bb4 D5", "C7sus4": "G3 C4 F4 G4 Bb4",
        "C7": "G3 C4 E4 G4 Bb4", "Am7": "A3 E4 G4 A4 C5", "Bb": "Bb3 F4 Bb4 C5 D5", "Bb6": "Bb3 F4 G4 Bb4 D5",
        "Bbm6": "Bb3 F4 G4 Bb4 Db5", "Gm11": "G3 C4 F4 Bb4 C5"}

# electric-piano left hand, one bar each: the root on 1, a chord tone on 3 or 4 that steps / falls into the next root
BASS = [
    [("F2", 3), ("C2", 1)],                 # F    -> D
    [("D2", 3), ("F2", 1)],                 # Dm7  -> G
    [("G2", 3), ("Bb2", 1)],                # Gm7  -> C
    [("C3", 2), ("C2", 2)],                 # C7sus4 C7 -> F (the root drops an octave on the change)
    [("F2", 3), ("G2", 1)],                 # F    -> A
    [("A2", 3), ("C3", 1)],                 # Am7  -> Bb
    [("Bb2", 2), ("F2", 2)],                # Bb   -> G
    [("G2", 2), ("C3", 2)],                 # Gm7 C7 -> D (deceptive)
    [("D3", 3), ("C3", 1)],                 # Dm7  -> A
    [("A2", 3), ("C3", 1)],                 # Am7  -> Bb
    [("Bb2", 3), ("D3", 1)],                # Bb   -> C
    [("C3", 2), ("G2", 1), ("E2", 1)],      # C7sus4 C7 -> F (leading tone in the bass)
    [("F2", 3), ("C2", 1)],                 # F    -> D
    [("D2", 2), ("A2", 2)],                 # Dm7  -> Bb
    [("Bb2", 2), ("F2", 2)],                # Bb6 Bbm6 -> G
    [("G2", 2), ("C3", 2)],                 # Gm11 C7 -> F (bar 0)
]

# music box tune (pitch, beats); None = rest. Long notes on chord tones, passing notes only off the beat.
MBOX = {
    0: [("A5", 1.5), ("G5", .5), ("A5", 1), ("C6", 1)],
    1: [("D6", 2), ("C6", 1), ("A5", 1)],
    2: [("Bb5", 1.5), ("A5", .5), ("G5", 2)],
    3: [("F5", 2), ("E5", 2)],                                   # sus4 -> 3 over C7sus4 -> C7
    4: [("A5", 1.5), ("G5", .5), ("A5", 1), ("C6", 1)],
    5: [("E6", 1.5), ("D6", .5), ("C6", 2)],                     # the A' high point
    6: [("D6", 1), ("C6", .5), ("Bb5", .5), ("F5", 2)],
    7: [("G5", 2), (None, 2)],                                   # breath: the vibraphone answers
    # 12: MOTIF bar 1, see songs()
    13: [("A5", 1), ("D6", 2), ("C6", 1)],
    14: [("Bb5", 1), ("D6", 1), ("Db6", 2)],                     # D -> Db as Bb6 turns into Bbm6
    15: [("C6", 1), ("Bb5", 1), (None, 2)],                      # Db -> C -> Bb over Gm11, then the vibraphone
}
# celesta tune in B, rising to the climax F6 in bar 10
CELESTA = {
    8: [("A5", 1.5), ("G5", .5), ("F5", 1), ("A5", 1)],
    9: [("C6", 1.5), ("D6", .5), ("E6", 2)],
    10: [("F6", 2), ("D6", 1), ("C6", 1)],
    11: [("Bb5", 2), ("G5", 2)],
}
# vibraphone: C7 arpeggio answers into the next phrase, and held counter-line tones in B (D C | E C | D F— | E),
# in 6ths / 10ths or contrary motion to the celesta (no octaves with the tune on the beats)
VIBES = [
    (7, 2, [("E5", .5), ("G5", .5), ("Bb5", 1)]),                # -> celesta A5 (bar 8)
    (8, 0, [("D5", 2), ("C5", 2), ("E5", 2), ("C5", 2), ("D5", 2), ("F5", 4), ("E5", 2)]),   # bars 8-11
    (15, 2, [("C5", .5), ("E5", .5), ("G5", 1)]),                # -> music box A5 (bar 0)
]


def voice(s):
    return [n(x) for x in s.split()]


def segments(bars):
    """(bar, beat, length, chord) for every chord of the given bars."""
    for b in bars:
        ch = HARM[b]
        for i, (beat, name) in enumerate(ch):
            end = ch[i + 1][0] if i + 1 < len(ch) else 4
            yield b, beat, end - beat, name


def harp_part(hp):
    """A sections: an upward roll over the chord's first beats, each string left ringing to the change.
    B: flowing up-and-down eighths. The very last chord rolls downwards into bar 0's low F."""
    for b, beat, ln, ch in segments(range(16)):
        ps = voice(HARP[ch])
        end = beat + ln
        if SECTION[b] == "B":
            if ln >= 4:
                hp.arp(b, beat, ps, [0, 1, 2, 3, 4, 3, 2, 1], 0.5, ln, vel=44, gate=0.95, accent=6)
            else:
                hp.arp(b, beat, ps, [0, 1, 2, 3], 0.5, ln, vel=44, gate=0.95, accent=6)
            continue
        if b == 15 and beat == 2:
            order, steps = [4, 3, 2, 1], [0, .5, 1, 1.5]               # Bb G E C, falling to F3
        elif ln >= 4:
            order, steps = [0, 1, 2, 3, 4], [0, .5, 1, 1.5, 2.5]
        else:
            order, steps = [0, 1, 2, 3], [0, .5, 1, 1.5]
        for k, (i, x) in enumerate(zip(order, steps)):
            hp.note(b, beat + x, ps[i], end - beat - x - 0.04, (52 if k == 0 else 44) + (2 if i == 4 else 0))


def ep_part(ep):
    """A: one soft held chord per change. After that a lazy comp: the chord on 1 and again, softer, on 2-and."""
    for b, beat, ln, ch in segments(range(16)):
        ps = voice(EP[ch])
        if SECTION[b] == "A":
            ep.chord(b, beat, ps, ln - 0.05, 40, strum=0.02)
            continue
        v = 46 if SECTION[b] == "B" else 43
        ep.chord(b, beat, ps, 1.45, v, strum=0.02)
        ep.chord(b, beat + 1.5, ps, ln - 1.55, v - 8, strum=0.015)


def held_chords(tr, voicings, vel):
    """Sustain every chord of HARM, tying the tones the next chord keeps (a smooth pad)."""
    held = {}  # pitch -> start (absolute beats)

    def close(p, end):
        st = held.pop(p)
        tr.note(int(st // 4), st % 4, p, end - st - 0.04, vel)

    for b, beat, ln, ch in segments(range(16)):
        t0 = b * 4 + beat
        ps = set(voice(voicings[ch]))
        for p in [p for p in held if p not in ps]:
            close(p, t0)
        for p in ps:
            held.setdefault(p, t0)
    for p in list(held):
        close(p, 64)


def songs():
    s = Song("aquarium", bpm=72, bars=16, key=KEY, loudness=-22,
             desc="Aquarium: a slow, cosy F-major lullaby; music box and celesta tunes (MOTIF bar 1 on the return), "
                  "vibraphone answers, harp rolls, electric piano chords and bass, a quiet warm pad; no drums")

    mb = s.track("music_box", GM["music_box"], vol=96, pan=14, reverb=70)
    ce = s.track("celesta", GM["celesta"], vol=98, pan=-12, reverb=68)
    vb = s.track("vibes", GM["vibraphone"], vol=96, pan=24, reverb=62)
    hp = s.track("harp", GM["harp"], vol=84, pan=-24, reverb=58)
    ep = s.track("epiano", GM["epiano"], vol=78, pan=-6, reverb=48, chorus=40)
    bs = s.track("ep_bass", GM["epiano"], vol=90, pan=0, reverb=30, chorus=10)
    pad = s.track("pad", GM["warm_pad"], vol=74, pan=0, reverb=78, chorus=40)

    # --- tunes
    for b, line in MBOX.items():
        mb.seq(b, 0, [(p, d, 72 - (4 if i else 0)) for i, (p, d) in enumerate(line)], legato=0.92)
    mb.degs(12, 0, KEY, MOTIF[0], octave=5, vel=68, legato=0.92)          # MOTIF bar 1: C F G A— G F
    for b, line in CELESTA.items():
        v = 74 if b == 10 else 68                                         # lean into the climax
        ce.seq(b, 0, [(p, d, v - (5 if i else 0)) for i, (p, d) in enumerate(line)], legato=0.94)
    for b, beat, line in VIBES:  # the answers sit over the comp, the counter-line (bar 8) under the celesta
        vb.seq(b, beat, line, vel=64 if b == 8 else 76, legato=0.95)

    # --- accompaniment
    harp_part(hp)
    ep_part(ep)
    for b, line in enumerate(BASS):
        bs.seq(b, 0, [(p, d, 70 if i == 0 else 60) for i, (p, d) in enumerate(line)], legato=0.94)

    held_chords(pad, PAD, 46)
    pad.cc(0, 0, 11, 80)
    pad.swell(7, 0, 4, 80, 104)           # opens up under the celesta
    pad.swell(11, 0, 4, 104, 92)
    pad.swell(14, 0, 7.9, 92, 80)         # back to the opening level for the loop

    s.humanize(timing=0.01, velocity=5)
    return [s]
