"""
stings — the seven one-shot cues (README §3.4): kind="sting", one `main` stem, loudness -17 dBFS RMS.

The three catch fanfares quote MOTIF and grow in grandeur (mallets and guitar in G -> brass and strings in C ->
full orchestra in D); the escape is light and a little comic; omen / hook / fail serve the legend encounter and sit
in D minor, the key of `encounter`.

  sting_catch   ~2.5 s  G major, 148 bpm. Glockenspiel + marimba reel up D E F# G to re, then MOTIF bar 4
                        (re mi re do) over V - I6/4 - V - I; steel-guitar strums, acoustic bass, bell tree, triangle
  sting_rare    ~4 s    C major, 150 bpm. A snare roll and a string/harp run on V7 lead into MOTIF bars 3-4 on
                        trumpets; horns, trombones and tuba harmonise I vi V7/ii ii I6/4 V7 I; strings flourish in
                        the held notes; a harp chord-glissando, a string sweep to C7 and a crash on the final chord
  sting_legend  ~7 s    D major, 126 bpm. Timpani roll + reverse cymbal on V into a tutti (crash, bells, harp);
                        MOTIF bar 1 on trumpets, its head sequenced on IV up to the climax (B5 over Em7), a timpani /
                        snare roll on V7, then MOTIF bar 4 as the cadence (V I6/4 V7 I), final timpani roll + crash
  sting_escape  ~2 s    G minor, 144 bpm. Muted trumpet falls chromatically D C# C B onto a drooping Bb (a sigh,
                        "aww"), pizzicato chords i - iv - i6 and a low pizzicato plunk to close; dry and close
  sting_omen    ~4 s    D minor, 60 bpm. A low choir cluster (root, tritone, fifth) and the contrabass rise by
                        semitones D Eb E F, each step shorter, in a crescendo; a low reverse cymbal swells into a soft
                        boom on D at 2.6 s (timpani, gran cassa, low piano) and the choir is left on a bare fifth
  sting_hook    ~2.5 s  D minor, 138 bpm. Orchestra hit + timpani + crash + brass on i, timpani pick-ups, stabs on
                        bVI and bVII and the tutti again on i (trumpets climb F5 G5 A5)
  sting_fail    ~3 s    D minor, 60 bpm. A dissonant low string chord (D | A E F Bb) sighs Bb -> A, then the whole
                        chord slides down a whole tone (pitch bend) and fades away

Positions are absolute beats from the start of the sting (bar 0); `bars` just covers the music for the piano roll.
Lengths: FluidSynth 2.3 keeps rendering after the MIDI ends until every voice has died, so Song.tail cuts nothing
here (it is still set to the intended release, for renderers that stop at the end of the file). A sting ends where
the raw render falls below -62 dBFS, so the endings are shaped with expression (CC11): the fanfares' last chords
are held until their flourishes have arrived and then die away at an even rate in dB (ring_out), the others fade
(fade). Every sting also keeps its raw mix quiet (low track vols, or quieter(); the build normalizes the level), so
the cut lands about 45-50 dB below the peak instead of 55-60: the game brings the music back at the clip's end
(Music.cs), and a long inaudible reverb tail would only keep it waiting.
Dissonance (omen, fail) lives inside one track (a choir or string cluster); between tracks only consonances,
tritones and intervals wider than two octaves meet, so the lint's clash check stays meaningful.
"""

import math

from fk_music import Song, Key, GM, DRUMS, DR, MOTIF, n

LOUD = -17


def P(s):
    """'D4 F#4 A4' -> [62, 66, 69]."""
    return [n(x) for x in s.split()]


def roll(tr, beat, beats, pitch, v0, v1, step=0.125):
    """A roll (timpani, snare; pitch = a MIDI number): a stroke every `step` beats, velocity ramping v0 -> v1."""
    k = max(1, int(round(beats / step)))
    for i in range(k):
        tr.note(0, beat + i * step, pitch, step * 0.9, v0 + (v1 - v0) * i / max(1, k - 1))


def fade(tracks, beat, beats, v0=127, v1=0):
    """An expression (CC11) ramp on several tracks: shapes a sting's release (rings, held chords, cymbals)."""
    for t in tracks:
        t.swell(0, beat, beats, v0, v1)


def ring_out(tracks, beat, beats, db=40):
    """A fanfare's last chord dying away: expression (CC11) falls `db` dB at an even rate, like a natural decay
    (FluidSynth's CC11 gain is about 40*log10(v/127) dB, so a linear CC ramp would hold and then drop), then
    cuts to 0 so that no voice rings on."""
    steps = max(4, int(beats * 16))
    for t in tracks:
        for i in range(steps + 1):
            t.cc(0, beat + beats * i / steps, 11, 127 * 10 ** (-db * i / steps / 40))
        t.cc(0, beat + beats + 1 / 16, 11, 0)


def quieter(song, db):
    """Lower the raw render by `db` dB (every track's CC7; FluidSynth's volume curve is about 40*log10(v/127) dB).
    The build normalizes the level back, so the mix is unchanged; the only effect is that the end-of-sting cut
    (where the raw render falls below -62 dBFS) moves `db` dB up the inaudible reverb tail."""
    k = 10 ** (-db / 40)
    for t in song.tracks:
        t.vol = int(round(t.vol * k))


def glide(tr, beat, beats, semis):
    """A pitch-bend ramp from 0 to `semis` semitones (the toolkit sets a +-2 semitone bend range)."""
    steps = max(2, int(beats * 32))
    for i in range(steps + 1):
        tr.bend(0, beat + beats * i / steps, semis * 4096 * i / steps)


def blocks(tr, table, vel, legato=0.95):
    """Held chords from a table of (beat, beats, voicing) rows."""
    for (beat, beats, voicing) in table:
        tr.chord(0, beat, P(voicing), beats * legato, vel)


def strum_up(tr, beat, pitches, dur, vel, gap=0.02):
    """A guitar up-strum: high string first."""
    for i, p in enumerate(sorted(pitches, reverse=True)):
        tr.note(0, beat + i * gap, p, dur - i * gap, vel)


# ------------------------------------------------------------------------------------------------- sting_catch

def catch():
    key = Key("G", "major")
    s = Song("sting_catch", bpm=148, bars=1, key=key, kind="sting", loudness=LOUD, tail=1.0,
             desc="Catch (common / fine fish): glockenspiel + marimba reel up to MOTIF bar 4 (re mi re do), "
                  "V - I6/4 - V - I in G, steel-guitar strums, bell tree and triangle")
    gl = s.track("glock", GM["glockenspiel"], vol=100, pan=16, reverb=40)
    mb = s.track("marimba", GM["marimba"], vol=106, pan=-14, reverb=34)
    gt = s.track("guitar", GM["steel_guitar"], vol=88, pan=-30, reverb=30, chorus=14)
    bs = s.track("bass", GM["acoustic_bass"], vol=94, pan=0, reverb=20)
    pc = s.track("perc", DRUMS, vol=92, pan=10, reverb=36)

    # the tune: a reel-up run sol la ti do (D5 E5 F#5 G5), then MOTIF bar 4, its last do left to ring
    run = [(-3, .25, 74), (-2, .25, 78), (-1, .25, 82), (0, .25, 86)]
    cadence = [(1, 1.0, 94), (2, .5, 88), (1, .5, 84), (0, 3.5, 96)]     # MOTIF[3], the do held to ring
    assert [x[0] for x in cadence] == [d for d, _ in MOTIF[3]]
    gl.degs(0, 0, key, run + cadence, octave=5, legato=0.95)
    mb.degs(0, 0, key, run + cadence[:-1], octave=4, vel=80, legato=0.9)  # marimba doubles it an octave down
    mb.chord(0, 3, P("G3 B3 D4 G4"), 3.5, 62, strum=0.05)                 # ... and rolls the tonic chord

    # harmony: D (V) for the run and re, G/D (the cadential six-four) under mi, D under re, G under do
    D, G64, G = P("D3 A3 D4 F#4"), P("D3 G3 B3 D4 G4"), P("G2 B2 D3 G3 B3 G4")
    gt.chord(0, 0.0, D, 0.95, 72, strum=0.025)
    gt.chord(0, 1.0, D, 0.45, 76, strum=0.02)
    strum_up(gt, 1.5, D[1:], 0.45, 60)
    gt.chord(0, 2.0, G64, 0.45, 74, strum=0.02)
    strum_up(gt, 2.5, D[1:], 0.45, 62)
    gt.chord(0, 3.0, G, 3.5, 72, strum=0.035)

    bs.seq(0, 0, [("D2", 1, 86), ("F#2", 1, 80), ("D2", 1, 82), ("G2", 3.5, 92)], legato=0.92)

    pc.note(0, 0, DR["bell_tree"], 1.0, 62)        # sparkle as the fish comes out of the water
    pc.note(0, 1.0, DR["shaker"], 0.2, 40)
    pc.note(0, 2.0, DR["shaker"], 0.2, 44)
    pc.note(0, 2.5, DR["shaker"], 0.2, 36)
    pc.note(0, 3.0, DR["triangle"], 3.5, 64)       # ding on the tonic

    ring_out(s.tracks, 4.0, 2.5)                    # the tonic rings a beat, then dies away
    quieter(s, 6)                                   # ... and the inaudible reverb tail is cut sooner
    s.humanize(timing=0.01, velocity=5)
    return s


# -------------------------------------------------------------------------------------------------- sting_rare

# brass harmony under MOTIF bars 3-4 (beats from the start): I vi V7/ii ii I6/4 V7 I
RARE_HORNS = [(1.0, 2.0, "E4 G4"), (3.0, 1.0, "E4 A4"), (4.0, 1.0, "E4 G4"), (5.0, 1.0, "D4 F4"),
              (6.0, 0.5, "C4 E4"), (6.5, 0.5, "B3 F4"), (7.0, 3.0, "C4 E4 G4")]
RARE_TBNS = [(1.0, 2.0, "E3 G3"), (3.0, 1.0, "E3 A3"), (4.0, 1.0, "E3 A3"), (5.0, 1.0, "F3 A3"),
             (6.0, 0.5, "E3 G3"), (6.5, 0.5, "D3 G3"), (7.0, 3.0, "C3 G3")]
# bass: G under the pick-up, then C A C# (the V7/ii in first inversion) D G C
RARE_BASS = [("G1", 1), ("C2", 2), ("A1", 1), ("C#2", 1), ("D2", 1), ("G1", 1), ("C2", 3)]


def rare():
    key = Key("C", "major")
    s = Song("sting_rare", bpm=150, bars=2, key=key, kind="sting", loudness=LOUD, tail=1.5,
             desc="Rare / heroic fish: MOTIF bars 3-4 on trumpets over horns, trombones and tuba in C, string and "
                  "harp flourishes, a snare-roll pick-up and a crash on the final major chord")
    tp = s.track("trumpets", GM["trumpet"], vol=108, pan=6, reverb=42)
    hn = s.track("horns", GM["french_horn"], vol=86, pan=-22, reverb=50)        # under the trumpets' MOTIF
    tb = s.track("trombones", GM["trombone"], vol=88, pan=20, reverb=44)
    tu = s.track("tuba", GM["tuba"], vol=92, pan=4, reverb=34)
    st = s.track("strings", GM["strings"], vol=98, pan=-34, reverb=48)
    hp = s.track("harp", GM["harp"], vol=94, pan=34, reverb=50)
    pc = s.track("perc", DRUMS, vol=96, pan=0, reverb=38)

    # trumpets: MOTIF bars 3-4 from beat 1 (sol do re mi- sol mi | re mi re do--), climbing to G5 then home;
    # the last do is held on (3 beats) under the sweeps and the ring-out
    vels = [86, 90, 94, 100, 108, 98, 96, 98, 92, 110]
    tune = [(d, b, v) for (d, b), v in zip(MOTIF[2] + MOTIF[3], vels)]
    tune[-1] = (0, 3.0, vels[-1])
    tp.degs(0, 1.0, key, tune, octave=5, legato=0.9)

    blocks(hn, RARE_HORNS, 78)
    blocks(tb, RARE_TBNS, 74)
    tu.seq(0, 0, [(p, d, 70 if i == 0 else 84) for i, (p, d) in enumerate(RARE_BASS)], legato=0.95)

    # strings: a 32nd-note run up the V7 into the motif, a high C pedal, then flourishes in the held notes
    up = P("G4 A4 B4 C5 D5 E5 F5 G5")
    st.seq(0, 0, [(p, .125, 58 + 4 * i) for i, p in enumerate(up)], legato=1.0)
    st.note(0, 1.0, "C6", 1.45, 66)
    st.seq(0, 2.5, [("E5", .25, 70), ("G5", .25, 74), ("C6", .25, 78), ("E6", .75, 84),       # C -> Am: up
                    ("C#6", .25, 80), ("A5", .25, 74), ("G5", .25, 72), ("E5", .25, 70),     # A7: down
                    ("F5", .25, 72), ("A5", .25, 76), ("D6", .25, 80), ("F6", .25, 86),      # Dm: up again
                    ("E6", .5, 84), ("D6", .25, 78), ("B5", .25, 76),                        # I6/4  V7
                    ("C6", .25, 84), ("E6", .25, 88), ("G6", .25, 92), ("C7", 2.25, 96)],    # I: sweep to C7
           legato=0.95)

    # harp: doubles the run (pluck definition), marks the chord changes, and glisses up the final chord
    hp.seq(0, 0, [(p, .125, 60 + 3 * i) for i, p in enumerate(up)], legato=1.0)
    for (beat, voicing, v) in ((1.0, "C3 G3 C4", 74), (3.0, "A2 E3 A3", 66), (5.0, "D3 A3 D4", 68)):
        hp.chord(0, beat, P(voicing), 1.0, v, strum=0.04)
    gl = P("C4 E4 G4 C5 E5 G5 C6 E6")
    hp.seq(0, 7.0, [(p, .125 if i < 7 else 2.0, 70 + 3 * i) for i, p in enumerate(gl)], legato=1.0)

    # percussion: snare roll into the motif, bass drum + splash on it, crash on the last chord
    roll(pc, 0.0, 1.0, DR["snare"], 30, 74)
    pc.note(0, 1.0, DR["kick2"], 0.5, 84)
    pc.note(0, 1.0, DR["splash"], 0.5, 58)
    pc.note(0, 7.0, DR["kick2"], 0.5, 96)
    pc.note(0, 7.0, DR["crash"], 1.0, 104)

    # the last chord sings until the string sweep and the harp glissando have arrived (C7, E6 at beat 7.9),
    # then dies away with the crash
    ring_out(s.tracks, 8.25, 1.5)
    quieter(s, 7)                                   # the inaudible reverb tail is cut sooner
    s.humanize(timing=0.01, velocity=5)
    return s


# ------------------------------------------------------------------------------------------------ sting_legend

# harmony (beats from the start): A (V, the roll) | D | Bm | G | Em7 | A7 | A  D/A  A7 | D
LEG_HORNS = [(1.0, 1.0, "C#4 E4 A4"), (2.0, 2.0, "D4 F#4 A4"), (4.0, 1.0, "D4 F#4 B4"), (5.0, 2.0, "D4 G4 B4"),
             (7.0, 1.0, "E4 G4 B4"), (8.0, 1.0, "E4 G4 C#5"), (9.0, 1.0, "E4 A4 C#5"), (10.0, 0.5, "F#4 A4 D5"),
             (10.5, 0.5, "E4 G4 C#5"), (11.0, 4.0, "F#4 A4 D5")]
LEG_TBNS = [(1.0, 1.0, "A2 E3"), (2.0, 2.0, "D3 F#3 A3"), (4.0, 1.0, "D3 F#3 B3"), (5.0, 2.0, "D3 G3 B3"),
            (7.0, 1.0, "E3 G3 B3"), (8.0, 1.0, "E3 G3 A3"), (9.0, 1.0, "E3 A3 C#4"), (10.0, 0.5, "F#3 A3 D4"),
            (10.5, 0.5, "E3 G3 C#4"), (11.0, 4.0, "F#3 A3 D4")]
# bass: the A pedal, then falling thirds D B G E into A, and home to D
LEG_BASS = [("A1", 2), ("D2", 2), ("B1", 1), ("G1", 2), ("E1", 1), ("A1", 3), ("D2", 4)]


def legend():
    key = Key("D", "major")
    s = Song("sting_legend", bpm=126, bars=4, key=key, kind="sting", loudness=LOUD, tail=2.0,
             desc="Legendary catch: orchestral fanfare in D - timpani roll and reverse cymbal into a tutti, MOTIF "
                  "bar 1 on trumpets, its head sequenced up to the climax, MOTIF bar 4 as the cadence, timpani "
                  "roll, crash and tubular bells")
    # the trumpets carry MOTIF: they sit a little above the horn and trombone chords
    tp = s.track("trumpets", GM["trumpet"], vol=108, pan=8, reverb=52)
    hn = s.track("horns", GM["french_horn"], vol=78, pan=-24, reverb=64)
    tb = s.track("trombones", GM["trombone"], vol=74, pan=22, reverb=56)
    tu = s.track("tuba", GM["tuba"], vol=81, pan=6, reverb=46)
    cb = s.track("basses", GM["contrabass"], vol=83, pan=-6, reverb=50)
    st = s.track("violins", GM["strings"], vol=88, pan=-36, reverb=62)
    tr = s.track("tremolo", GM["tremolo_strings"], vol=77, pan=-14, reverb=60)
    hp = s.track("harp", GM["harp"], vol=83, pan=36, reverb=64)
    bl = s.track("bells", GM["tubular_bells"], vol=81, pan=28, reverb=70)
    rc = s.track("rev_cymbal", GM["reverse_cymbal"], vol=76, pan=-4, reverb=40)
    ti = s.track("timpani", GM["timpani"], vol=94, pan=-10, reverb=55)
    pc = s.track("perc", DRUMS, vol=90, pan=0, reverb=55)

    # --- trumpets: MOTIF bar 1 from the pick-up (its do held as the next pick-up), the head of the motif on IV
    # rising to B5 (the climax over Em7), then MOTIF bar 4 as the cadence, its last do held through the ring-out
    bar1 = MOTIF[0][:-1] + [(0, 1.0)]                                  # its last do lengthened to a beat
    seq_iv = [(3, .5), (4, .5), (5, 1.5), (4, .5)]                     # do re mi re of G (MOTIF head + 3 degrees)
    tune = bar1 + seq_iv + MOTIF[3][:-1] + [(0, 4.0)]
    vels = [84, 104, 96, 104, 92, 96, 98, 102, 114, 100, 102, 104, 98, 118]
    tp.degs(0, 1.5, key, [(d, b, v) for (d, b), v in zip(tune, vels)], octave=5, legato=0.9)

    # --- brass harmony and bass
    blocks(hn, LEG_HORNS, 84)
    blocks(tb, LEG_TBNS, 80)
    for t in (hn, tb):
        t.swell(0, 1.0, 1.0, 50, 127)              # the horns and trombones swell in on the V
    tu.seq(0, 0, [(p, d, 64 if i == 0 else 90) for i, (p, d) in enumerate(LEG_BASS)], legato=0.96)
    tu.note(0, 11.0, "D1", 4.0, 80)                # the final low octave (tuba only)
    cb.seq(0, 0, [(p, d, 70 if i == 0 else 90) for i, (p, d) in enumerate(LEG_BASS)], legato=0.96)
    cb.swell(0, 0, 2.0, 60, 127)

    # --- strings: tremolo on the V in the intro and on the final chord; violins flourish over the trumpets
    tr.chord(0, 0, P("A2 E3 A3"), 2.0, 80)
    tr.swell(0, 0, 2.0, 50, 127)
    tr.chord(0, 11.0, P("D3 A3 D4 F#4"), 4.0, 86)
    st.seq(0, 2.0, [("D5", .25, 78), ("F#5", .25, 82), ("A5", .25, 86), ("D6", .25, 90),       # up into the tutti
                    ("F#6", 1.5, 92), ("E6", .5, 84), ("D6", 1.0, 82),                       # doubling mi re do
                    ("G5", .25, 80), ("B5", .25, 84), ("D6", .25, 88), ("G6", .25, 92),      # up the IV
                    ("B6", 1.5, 100), ("A6", .5, 90),                                        # the climax
                    ("E6", 1.0, 88), ("F#6", .5, 90), ("E6", .5, 86),                        # the cadence
                    ("D6", .25, 98), ("F#6", .25, 100), ("A6", 3.5, 104)], legato=0.95)      # home, up to A6

    # --- harp glisses (chord tones) on the tutti and on the last chord; tubular bells mark the big moments
    gl = P("D4 F#4 A4 D5 F#5 A5 D6 F#6")
    hp.seq(0, 2.0, [(p, .125 if i < 7 else .75, 64 + 3 * i) for i, p in enumerate(gl)], legato=1.0)
    hp.seq(0, 11.0, [(p, .125 if i < 7 else 3.0, 70 + 3 * i) for i, p in enumerate(gl)], legato=1.0)
    bl.note(0, 2.0, "D5", 2.0, 92)
    bl.note(0, 7.0, "B4", 1.0, 86)
    bl.chord(0, 11.0, P("A4 D5"), 4.0, 100)

    # --- intro swell: a reverse cymbal (A5 peaks after ~1 s) over the timpani roll on A
    rc.note(0, 0, "A5", 2.0, 96)

    # --- timpani (tuned D E G A): roll on V, hits on the changes, a roll on V7, a roll under the last chord
    roll(ti, 0.0, 2.0, n("A2"), 40, 104)
    for (beat, p, v) in ((2.0, "D2", 118), (3.5, "A2", 84), (5.0, "G2", 96), (7.0, "E2", 104), (9.0, "A2", 100),
                         (10.5, "A2", 90), (11.0, "D2", 124)):
        ti.note(0, beat, p, 0.9, v)
    roll(ti, 8.0, 1.0, n("A2"), 60, 108)
    roll(ti, 11.25, 3.5, n("D2"), 92, 52)

    # --- percussion: crash + gran cassa on the tutti, crash on the climax, snare roll on V7, two cymbals at the end
    pc.note(0, 2.0, DR["kick2"], 0.5, 104)
    pc.note(0, 2.0, DR["crash"], 1.0, 108)
    pc.note(0, 7.0, DR["kick2"], 0.5, 92)
    pc.note(0, 7.0, DR["crash2"], 1.0, 94)
    roll(pc, 8.0, 1.0, DR["snare"], 30, 84)
    pc.note(0, 9.0, DR["kick2"], 0.5, 88)
    pc.note(0, 11.0, DR["kick2"], 0.5, 112)
    pc.note(0, 11.0, DR["crash"], 1.0, 114)
    pc.note(0, 11.0, DR["crash2"], 1.0, 96)

    # the last chord holds (the harp and the violins arrive on top), then dies away over the timpani roll
    ring_out(s.tracks, 12.75, 2.0)
    quieter(s, 9)                                   # the inaudible reverb tail is cut sooner
    s.humanize(timing=0.01, velocity=5)
    return s


# ------------------------------------------------------------------------------------------------ sting_escape

def escape():
    key = Key("G", "minor")
    s = Song("sting_escape", bpm=144, bars=1, key=key, kind="sting", loudness=LOUD, tail=1.0,
             desc="Fish got away / line broke: muted trumpet falls chromatically to a drooping Bb over pizzicato "
                  "i - iv - i6 in G minor, a low pizzicato plunk to close")
    # dry, and quiet in the raw render so the tail crosses the -62 dBFS cut sooner (the build normalizes);
    # the muted trumpet leads, the pizzicato chords stay under it (only the closing plunk is big)
    mt = s.track("muted_tpt", GM["muted_trumpet"], vol=78, pan=10, reverb=18)
    pz = s.track("pizz", GM["pizzicato"], vol=68, pan=-16, reverb=16)

    # muted trumpet: D C# C B (chromatic, a little detached) onto Bb, which droops and wobbles ("aww")
    mt.seq(0, 0, [("D5", .5, 86), ("C#5", .5, 78), ("C5", .5, 76), ("B4", .5, 72), ("Bb4", .75, 82)], legato=0.82)
    steps = 24
    for i in range(steps + 1):                     # beats 2.1 .. 2.6: sag ~3/4 semitone with a growing wobble
        x = i / steps
        mt.bend(0, 2.1 + 0.5 * x, -3000 * x * x + 700 * x * math.sin(2 * math.pi * 3 * x))

    # pizzicato: Gm, Cm, Gm/Bb on the beats, then the plunk (G2 + G3)
    for (beat, voicing, v) in ((0.0, "G2 D3 Bb3", 68), (1.0, "C3 Eb3 G3", 62), (2.0, "Bb2 D3 G3", 60),
                               (2.75, "G2 G3", 96)):
        pz.chord(0, beat, P(voicing), 0.4, v)
    fade(s.tracks, 2.9, 0.35)                      # damp the plunk

    s.humanize(timing=0.01, velocity=5)
    return s


# -------------------------------------------------------------------------------------------------- sting_omen

# the rising mass: (beat, beats, root); each step is shorter than the last
OMEN_STEPS = [(0.0, 0.9, "D"), (0.9, 0.65, "Eb"), (1.55, 0.55, "E"), (2.1, 0.5, "F")]
BOOM = 2.6


def omen():
    key = Key("D", "minor")
    s = Song("sting_omen", bpm=60, bars=1, key=key, kind="sting", loudness=LOUD, tail=1.5,
             desc="Legend omen: a low choir cluster and contrabass rise by semitones in a crescendo, a reverse "
                  "cymbal swells into a soft low boom on D")
    ch = s.track("choir", GM["choir"], vol=100, pan=-8, reverb=58)
    cb = s.track("basses", GM["contrabass"], vol=100, pan=8, reverb=46)
    tr = s.track("cellos", GM["tremolo_strings"], vol=84, pan=20, reverb=50)
    rc = s.track("rev_cymbal", GM["reverse_cymbal"], vol=96, pan=0, reverb=40)
    ti = s.track("timpani", GM["timpani"], vol=100, pan=-12, reverb=46)
    pn = s.track("piano", GM["piano"], vol=92, pan=-4, reverb=50)
    pc = s.track("perc", DRUMS, vol=96, pan=0, reverb=44)

    # each step: contrabass on the root, choir root + tritone + fifth an octave up (the semitone rub sits inside
    # the choir), tremolo strings on the root with the choir
    for (beat, beats, root) in OMEN_STEPS:
        x2, x3 = n(root + "2"), n(root + "3")
        cb.note(0, beat, x2, beats, 84)
        ch.chord(0, beat, [x3, x3 + 6, x3 + 7], beats, 80)
        tr.note(0, beat, x3, beats, 72)
    fade([ch, cb, tr], 0.0, BOOM, 36, 127)

    # the reverse cymbal pitched down to G2 (the one pitch class that rubs with no step of the mass) peaks
    # after ~1.7 s: start it so it lands on the boom
    rc.note(0, BOOM - 1.7, "G2", 1.7, 104)

    # the boom on D (soft enough not to be a jump scare), and the choir left on a bare fifth
    ti.note(0, BOOM, "D2", 1.0, 90)
    pn.chord(0, BOOM, P("D1 A1 D2"), 1.0, 80)
    pc.note(0, BOOM, DR["kick2"], 0.5, 86)
    cb.note(0, BOOM, "D2", 1.0, 92)
    ch.chord(0, BOOM, P("D3 A3"), 1.0, 66)

    fade(s.tracks, BOOM + 0.25, 0.6)
    s.humanize(timing=0.01, velocity=5)
    return s


# -------------------------------------------------------------------------------------------------- sting_hook

# the stabs (beat, beats, trumpets, horns, trombones, tuba, strings): i | bVI bVII | i
HOOK = [(0.0, 0.75, "D5 A5", "D4 F4 A4", "D3 A3", "D2", "D4 F4 A4 D5"),
        (1.0, 0.4, "D5 F5", "Bb3 D4 F4", "Bb2 F3", "Bb1", "D4 F4 Bb4 D5"),
        (1.5, 0.4, "E5 G5", "C4 E4 G4", "C3 G3", "C2", "E4 G4 C5 E5"),
        (2.0, 1.5, "F5 A5", "D4 F4 A4", "D3 A3", "D2", "F4 A4 D5 F5")]


def hook():
    key = Key("D", "minor")
    s = Song("sting_hook", bpm=138, bars=1, key=key, kind="sting", loudness=LOUD, tail=1.5,
             desc="Legend hooked: orchestra hit + timpani + crash + brass on Dm, stabs on Bb and C, the tutti "
                  "again on Dm (leads into the legend fight)")
    # track volumes leave ~4 dB of headroom in the raw (16-bit) render; the build normalizes the level
    oh = s.track("orch_hit", GM["orchestra_hit"], vol=80, pan=0, reverb=38)
    tp = s.track("trumpets", GM["trumpet"], vol=86, pan=10, reverb=38)
    hn = s.track("horns", GM["french_horn"], vol=78, pan=-22, reverb=46)
    tb = s.track("trombones", GM["trombone"], vol=77, pan=22, reverb=40)
    tu = s.track("tuba", GM["tuba"], vol=75, pan=4, reverb=32)
    st = s.track("strings", GM["strings"], vol=75, pan=-34, reverb=44)
    ti = s.track("timpani", GM["timpani"], vol=85, pan=-8, reverb=40)
    pc = s.track("perc", DRUMS, vol=80, pan=0, reverb=40)

    for i, (beat, beats, t_, h_, b_, u_, s_) in enumerate(HOOK):
        v = 116 if i in (0, 3) else 104
        tp.chord(0, beat, P(t_), beats, v)
        hn.chord(0, beat, P(h_), beats, v - 10)
        tb.chord(0, beat, P(b_), beats, v - 6)
        tu.note(0, beat, u_, beats, v - 8)
        st.chord(0, beat, P(s_), beats, v - 14)
    for beat in (0.0, 2.0):
        oh.chord(0, beat, P("D4 D5"), 0.75, 116)

    # timpani: the hit, two pick-ups on D, the stabs (Bb C), the tutti and a short dying roll
    for (beat, p, v) in ((0.0, "D2", 124), (0.5, "D2", 84), (0.75, "D2", 96), (1.0, "Bb2", 104), (1.5, "C3", 108),
                         (2.0, "D2", 124)):
        ti.note(0, beat, p, 0.45, v)
    roll(ti, 2.25, 1.25, n("D2"), 86, 56)

    pc.note(0, 0.0, DR["kick2"], 0.5, 118)
    pc.note(0, 0.0, DR["crash"], 1.0, 112)
    pc.note(0, 1.0, DR["kick2"], 0.4, 96)
    pc.note(0, 1.5, DR["kick2"], 0.4, 100)
    pc.note(0, 2.0, DR["kick2"], 0.5, 120)
    pc.note(0, 2.0, DR["crash2"], 1.0, 112)
    pc.note(0, 2.0, DR["crash"], 1.0, 100)

    fade(s.tracks, 2.4, 0.6)
    quieter(s, 8)                                   # the legend's cue follows the clip's end: no dead tail
    s.humanize(timing=0.01, velocity=5)
    return s


# -------------------------------------------------------------------------------------------------- sting_fail

def fail():
    key = Key("D", "minor")
    s = Song("sting_fail", bpm=60, bars=1, key=key, kind="sting", loudness=LOUD, tail=1.0,
             desc="Encounter failed: a dissonant low string chord sighs, slides down a whole tone and fades away")
    st = s.track("low_strings", GM["strings"], vol=104, pan=-14, reverb=66)
    cb = s.track("basses", GM["contrabass"], vol=104, pan=12, reverb=58)

    # D | A E F Bb: the E-F rub and the Bb (b6) sit inside the string section; the basses hold the D
    cb.note(0, 0, "D2", 2.8, 100)
    st.chord(0, 0, P("A2 E3 F3"), 2.8, 92)
    st.note(0, 0, "Bb3", 0.6, 96)
    st.note(0, 0.6, "A3", 2.2, 70)                 # the sigh: b6 -> 5
    for t in (st, cb):
        glide(t, 1.0, 1.6, -2)                     # everything sinks a whole tone ...
    fade([st, cb], 0.0, 0.9, 127, 112)
    fade([st, cb], 0.9, 1.7, 112, 0)               # ... and fades away
    quieter(s, 5)                                   # the stage comes back at the clip's end: no dead tail

    s.humanize(timing=0.01, velocity=5)
    return s


def songs():
    return [catch(), rare(), legend(), escape(), omen(), hook(), fail()]
