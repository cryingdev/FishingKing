"""
Legend module: 백상아리 GREAT WHITE (ocean, backdrop "ocean"; shares the ocean lurk with the blue marlin). Loaded by
hyb_legend3d.py (model) and hyb_legend_preview.py (review mock). Design row: Docs/legends_rollout.md 3.5 (model,
palette, rig, choreography), 4.5 (the ocean set), 7 (species bones).

The stalker, readable at small size by its silhouette and its two-tone skin:
  * a heavy torpedo (depth 0.24 L, real girth), a conical snout at z +0.50 and an underslung gape (z 0.32 .. 0.44);
  * a SHARP, JAGGED countershading line (slate grey over white): every body ring carries the line as vertices, so the
    line is a hard zig-zag edge between faces, not a blend;
  * a tall rigid first dorsal (limit 0: it never folds), long falcate pectorals (white underneath), small pelvics /
    second dorsal / anal fin (rigid, modelled on the spine segments), a crescent tail (upper lobe a little bigger)
    with caudal keels;
  * five gill slits (gw_gill strips) at z 0.30 .. 0.25;
  * black eyeballs on EyeRoll.L / EyeRoll.R: the front hemisphere is gw_eye_ring (near black) with a small dead-silver
    glint (the gw_eye_glow lens = geo_Eye.*), the back hemisphere is gw_eye_white. Rolling the bone 150 deg about its
    VERTICAL axis turns the pupil back into the head (the lens and the Eye.* empty go with it, so the eyeshine fades
    by its facing test) and brings the white round;
  * UpperJaw: a pink gum band along the upper lip line with the upper tooth row (10 broad teeth per side), hidden in
    the closed head; protruded (+move, +tilt) it slides out under the snout. Jaw: the lower jaw shell with the lower
    row (10 narrow teeth per side). With the jaw hanging 5 deg open the upper teeth show.
"""
import math
import os
import random
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "great_white"
MODEL = "legend_great_white"
ARMATURE = "GreatWhite"             # the FBX root node
PREFIX = "gw"
CM = (300, 500)                     # GameDatabase great_white 300..500 cm (runtime scale = cm / 100)
STAGE = "ocean"
BACKDROP = "ocean"                  # encounter_sets/ocean.py (shared with the blue marlin)
PRESET = "ocean"

# ============================================================================ palette (rollout 3.5)
OUTLINE = "#0b1322"
RAMPS = {
    "gw_back": ["#2a3038", "#3a424c", "#525c68"],
    "gw_side": ["#3a424c", "#525c68", "#707c88"],
    "gw_belly": ["#a0a8b0", "#d2d8de", "#f4f6f8"],
    "gw_fin": ["#22282e", "#333a42", "#4a525c"],
    "gw_gill": ["#14181c", "#1e2428", "#2a3036"],
    "gw_mouth": ["#4a1a22", "#7a2e38", "#a44a52"],
    "gw_teeth": ["#b8b4a8", "#e0dccf", "#f8f6ee"],
    "gw_eye_ring": ["#020304", "#06080a", "#101418"],
    "gw_eye_white": ["#8a8e92", "#c8ccd0", "#eef0f2"],
    "gw_eye_glow": ["#3a4450", "#9aa8b4", "#f4f8fa"],
}
# the ocean set's lure light (rollout 4.1: abyss / fog outline), the eyeshine (3.5: dead silver), the frame line
LURE_LIGHT = {"abyss": "#081e44", "fogOutline": "#2a5a90", "eyeCore": "#f4f8fa", "eyeGlow": "#9aa8b4",
              "frameLine": "#7fd4ff"}
FACE_BONES = ["Head", "UpperJaw", "Jaw"]
# species rig (palette "_rig"). EyeRoll: the eyes look sideways (+-x), so the roll that turns the pupil BACK into the
# head is about the bone's vertical axis; +150 deg about +y turns the right eye's pupil to the rear, the left eye
# mirrors it (axis -y). (Rollout 3.5 / 7 wrote axis (1, 0, 0): about x a sideways eye only spins in place - see the
# integrator notes.) Jaw 50 = the widest gape; Dorsal1 0 = the rigid first dorsal never rises.
RIG = {
    "protrude": [{"bone": "UpperJaw", "move": [0, -0.012, 0.025], "tilt": [8, 0, 0]}],
    "roll": [{"bone": "EyeRoll.L", "axis": [0, -1, 0], "deg": 150},
             {"bone": "EyeRoll.R", "axis": [0, 1, 0], "deg": 150}],
    "limits": [{"bone": "Jaw", "max": 50}, {"bone": "Dorsal1", "max": 0}],
}

# ============================================================================ body
# profile (Unity model space, metres): z, top y, bottom y, half-width x. Snout tip z +0.50 (a little above the mid
# line), deepest at z ~0.06 (depth 0.238), a depressed keeled peduncle (wider than deep) at z -0.37 .. -0.41.
TABLE = [
    (0.500, 0.010, -0.002, 0.004), (0.490, 0.024, -0.014, 0.016), (0.470, 0.040, -0.028, 0.030),
    (0.440, 0.056, -0.044, 0.044), (0.400, 0.072, -0.060, 0.057), (0.350, 0.087, -0.076, 0.069),
    (0.300, 0.099, -0.090, 0.079), (0.240, 0.109, -0.102, 0.088), (0.170, 0.117, -0.112, 0.095),
    (0.100, 0.121, -0.117, 0.098), (0.030, 0.120, -0.117, 0.097), (-0.050, 0.112, -0.110, 0.091),
    (-0.130, 0.097, -0.094, 0.079), (-0.210, 0.076, -0.072, 0.062), (-0.280, 0.054, -0.050, 0.045),
    (-0.330, 0.038, -0.034, 0.034), (-0.370, 0.028, -0.024, 0.030), (-0.410, 0.022, -0.018, 0.026),
    (-0.430, 0.018, -0.015, 0.018), (-0.445, 0.013, -0.011, 0.010),
]
OVER = 0.015                        # joint extension (inset) of the front segment over the rear one
HEAD_OVER = 0.035                   # the head's longer extension: a 12 deg snout lift opens ~21 mm at the throat
INSET = 0.0015
HINGE = (0.32, -0.045)              # (z, y) gape corner = the Jaw hinge
FRONT = (0.44, -0.028)              # (z, y) front of the gape, under the snout (the snout overhangs to z 0.50)


class SharkBody(K.Body):
    """K.Body with a mouth line that ends at the front of the gape: beyond it the line drops under the snout, so
    the upper shell closes into the snout's flat underside (clip ring clamped)."""

    def mouth_y(self, z):
        (z0, y0), (z1, y1) = self.mouth
        if z <= z1:
            return y0 + (y1 - y0) * (z - z0) / (z1 - z0)
        return y1 - (z - z1) * 2.5


BODY = SharkBody(TABLE, ("gw_back", "gw_side", "gw_belly"), nseg=20, back_pinch=0.08, inset=INSET, over=OVER,
                 mouth=(HINGE, FRONT), n_arc=19, n_chord=2)

# countershading line: angle on the section (degrees; 0 = mid flank, - = lower) along the body, before the jag.
# Low on the snout (white underside), below the eye, high over the gills, dipping behind the pectoral, a white
# flame rising mid-flank, lower again along the peduncle.
CS_BASE = [(0.50, -34.0), (0.46, -26.0), (0.40, -15.0), (0.34, -8.0), (0.28, -5.0), (0.22, -9.0), (0.16, -20.0),
           (0.10, -17.0), (0.04, -7.0), (-0.04, -2.0), (-0.12, -8.0), (-0.22, -12.0), (-0.32, -16.0),
           (-0.46, -20.0)]
NU, NL = 12, 8                      # full body ring: faces over the back (grey) / under the belly (white)
NB, NH, NCH = 3, 12, 2              # head shell: white faces between lip and line per side / grey faces / palate
NJ = 8                              # jaw shell: arc faces (white)


def _interp(z, table):
    if z >= table[0][0]:
        return table[0][1]
    for (za, va), (zb, vb) in zip(table, table[1:]):
        if zb <= z <= za:
            return va + (vb - va) * (z - za) / (zb - za)
    return table[-1][1]


def cs_theta(z):
    """-> (right, left) countershading angles (radians) at z: the base line + an irregular zig-zag (the jag), each
    side its own phase. The jag fades on the snout."""
    base = _interp(z, CS_BASE)
    amp = 1.0 if z < 0.40 else max(0.25, 1.0 - (z - 0.40) / 0.10 * 0.75)
    jr = amp * (6.0 * math.sin(z * 83.0 + 1.3) + 3.0 * math.sin(z * 217.0 + 0.4))
    jl = amp * (6.0 * math.sin(z * 79.0 + 2.1) + 3.0 * math.sin(z * 231.0 + 1.7))
    return math.radians(base + jr), math.radians(base + jl)


def _lin(a, b, n):
    return [a + (b - a) * j / n for j in range(n)]


def full_ring(z, inset):
    """Closed body section: vertex 0 on the right countershading line, NU faces over the back to the left line,
    NL faces under the belly back to vertex 0 (counter-clockwise seen from +z, like K.Body)."""
    tr, tl = cs_theta(z)
    a0, a1 = tr, math.pi - tl
    ths = _lin(a0, a1, NU) + _lin(a1, a0 + 2 * math.pi, NL)
    return [BODY.pt(z, th, inset) for th in ths]


def _grey(k):
    return "gw_back" if 3 <= k <= NU - 4 else "gw_side"


def full_mat(i, k):
    return "gw_belly" if k >= NU else _grey(k)


def _mouth_angle(z, inset):
    yc, ry, hw = BODY.sec(z, inset)
    s = max(-0.97, min(0.9, (BODY.mouth_y(z) - yc) / ry))
    return math.asin(s)


def head_ring(z, inset):
    """Upper head shell (Head): the arc above the mouth line, white from the lip up to the countershading line,
    grey over the top, + the palate chord."""
    tm = _mouth_angle(z, inset)
    tr, tl = cs_theta(z)
    tr, tl = max(tr, tm + math.radians(6)), max(tl, tm + math.radians(6))
    ths = _lin(tm, tr, NB) + _lin(tr, math.pi - tl, NH) + _lin(math.pi - tl, math.pi - tm, NB) + [math.pi - tm]
    pts = [BODY.pt(z, th, inset) for th in ths]
    a, b = pts[-1], pts[0]
    return pts + [a.lerp(b, (j + 1) / (NCH + 1)) for j in range(NCH)]


def head_mat_for(zs):
    """Face materials of the upper head shell through rings at zs: the chord is the palate (gw_mouth) up to the front
    of the gape, and the snout's white underside beyond it."""
    def fm(i, k):
        if k < NB or NB + NH <= k < 2 * NB + NH:
            return "gw_belly"
        if k < NB + NH:
            return _grey(k - NB)
        return "gw_belly" if zs[i] >= FRONT[0] - 1e-6 else "gw_mouth"
    return fm


def jaw_ring(z, inset):
    """Lower jaw shell (Jaw): the arc below the mouth line (all white) + the mouth-floor chord."""
    tm = _mouth_angle(z, inset)
    ths = [math.pi - tm + (math.pi + 2 * tm) * j / NJ for j in range(NJ + 1)]
    pts = [BODY.pt(z, th, inset) for th in ths]
    a, b = pts[-1], pts[0]
    return pts + [a.lerp(b, (j + 1) / (NCH + 1)) for j in range(NCH)]


def jaw_mat(i, k):
    return "gw_belly" if k < NJ else "gw_mouth"


def half_w_at(z, y, inset=0.0):
    """Half width of the (smooth) section at height y (0 outside)."""
    yc, ry, hw = BODY.sec(z, inset)
    s = (y - yc) / ry
    if abs(s) >= 1.0:
        return 0.0
    return hw * math.sqrt(1 - s * s) * (1 - BODY.back_pinch * max(0.0, s))


# ---------------------------------------------------------------- eyes (black eyeballs on EyeRoll.*)
EYE_Z, EYE_TH = 0.405, math.radians(18)
EYE_R = 0.0115                      # eyeball radius (the visible black disc ~ 0.02 m across)
EYE_OUT = 0.0055                    # how far the eyeball stands out of the skin


def _eye(sg):
    th = EYE_TH if sg > 0 else math.pi - EYE_TH
    skin = BODY.pt(EYE_Z, th)
    n = (BODY.normal(EYE_Z, th) + V((0, 0, 0.22))).normalized()      # sideways, a little forward
    c = skin - n * (EYE_R - EYE_OUT)
    return c, n


EYE_C = {s: _eye(sg)[0] for s, sg in (("L", -1), ("R", 1))}
EYE_N = {s: _eye(sg)[1] for s, sg in (("L", -1), ("R", 1))}

# ---------------------------------------------------------------- the gape (plan view: a broad rounded arch)
ARCH_ZC, ARCH_ZF, ARCH_P = 0.332, 0.436, 3.0


def arch(phi, inward=0.0):
    """Point on the tooth arch at phi (rad, -pi/2 = left corner, 0 = front, +pi/2 = right corner), `inward` m inside
    it, at the lip line height. -> (point, outward plan normal, tangent)."""
    def raw(f):
        s, c = math.sin(f), math.cos(f)
        wc = half_w_at(ARCH_ZC, BODY.mouth_y(ARCH_ZC)) - 0.006
        z = ARCH_ZC + (ARCH_ZF - ARCH_ZC) * abs(c) ** (2.0 / ARCH_P)
        x = math.copysign(wc * abs(s) ** (2.0 / ARCH_P), s)
        lim = max(0.0, half_w_at(z, BODY.mouth_y(z)) - 0.005)
        x = max(-lim, min(lim, x))
        return V((x, BODY.mouth_y(z), z))
    p = raw(phi)
    t = (raw(min(phi + 0.01, math.pi / 2)) - raw(max(phi - 0.01, -math.pi / 2)))
    t.y = 0.0
    t.normalize()
    o = V((t.z, 0.0, -t.x))                       # plan normal, pointing out of the arch
    if o.dot(V((p.x, 0, p.z - ARCH_ZC + 0.05))) < 0:
        o = -o
    return p - o * inward, o, t


# ---------------------------------------------------------------- bones
UPPER_JAW_AT = (0.0, BODY.mouth_y(0.335) + 0.004, 0.335)
BONES = [  # (name, parent, head in Unity model space); every rest rotation is identity
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, 0.02)),
    ("Head", "Spine.F", (0, 0, 0.22)),
    ("Jaw", "Head", (0, HINGE[1], HINGE[0])),
    ("UpperJaw", "Head", UPPER_JAW_AT),
    ("EyeRoll.L", "Head", tuple(round(v, 5) for v in EYE_C["L"])),
    ("EyeRoll.R", "Head", tuple(round(v, 5) for v in EYE_C["R"])),
    ("Pec.L", "Spine.F", (-0.072, -0.078, 0.195)),
    ("Pec.R", "Spine.F", (0.072, -0.078, 0.195)),
    ("Dorsal1", "Spine.F", (0, 0.118, 0.09)),
    ("Spine.B1", "Root", (0, 0, -0.02)),
    ("Spine.B2", "Spine.B1", (0, 0, -0.16)),
    ("Spine.B3", "Spine.B2", (0, 0, -0.27)),
    ("Tail", "Spine.B3", (0, 0, -0.36)),
    ("Tail.Upper", "Tail", (0, 0.015, -0.395)),
    ("Tail.Lower", "Tail", (0, -0.015, -0.395)),
]
JOINTS = {"Spine.F|Head": 0.22, "Spine.B1|Spine.F": 0.0, "Spine.B2|Spine.B1": -0.16, "Spine.B3|Spine.B2": -0.27,
          "Tail|Spine.B3": -0.36}


def _fin(part, base, span_dir, chord_hint, shape, th, mat, under=None):
    """Flat fin in the plane of span_dir / chord (shape = [(u along the span, w along the chord)]); `under` = the
    material of the face turned away from +y (the white underside of a pectoral)."""
    d = V(span_dir).normalized()
    c = V(chord_hint) - d * V(chord_hint).dot(d)
    c.normalize()
    n = d.cross(c).normalized()
    if n.y < 0:
        n = -n
    pts = [V(base) + d * u + c * w for u, w in shape]
    faces = K.plate(part, pts, n, th, mat)
    if under:
        for f in faces:
            f.normal_update()
            if f.normal.dot(n) < -0.5:
                f.material_index = part.mi(under)
    return faces


def _tooth(part, base, down, o, t, h, w, lean):
    """A tetrahedral tooth: blade base along the arch tangent t, one base vertex inward (-o), tip `h` along `down`."""
    b1 = part.bm.verts.new(base - t * (w / 2))
    b2 = part.bm.verts.new(base + t * (w / 2))
    b3 = part.bm.verts.new(base - o * 0.003)
    tip = part.bm.verts.new(base + down * h - o * lean)
    fs = [part.face((b1, b2, b3), "gw_teeth", False)]
    for a, b in ((b1, b2), (b2, b3), (b3, b1)):
        fs.append(part.face((a, b, tip), "gw_teeth", False))
    part.closed(fs)


def build():
    """-> (parts {bone or "Eye.L"/"Eye.R": Part}, empties {name: (parent bone, pos, facing or None)}, info)."""
    B = BODY
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    def rings(z0, z1, ext_lo, over=OVER, step=0.045):
        # the joint extension TAPERS from inset (z0 - over) to the nominal skin at z0: no flat step ring (a 1.5 mm
        # annulus facing the tail catches the lure light as a bright seam on the dark silhouette - the white belly
        # ramp makes it pop)
        rs = [full_ring(z0 - over, INSET)] if ext_lo else []
        return rs + [full_ring(z, 0.0) for z in B.stations(z0, z1, step, 0.012)]

    # ------------------------------------------------ body segments, rear -> front (the front one carries the inset
    # extension back over each joint; the rear one ends in a cap on the joint)
    for bone, (z0, z1), ext in (("Tail", (-0.445, -0.36), False), ("Spine.B3", (-0.36, -0.27), True),
                                ("Spine.B2", (-0.27, -0.16), True), ("Spine.B1", (-0.16, 0.0), True),
                                ("Spine.F", (0.0, 0.22), True)):
        K.loft(P(bone), rings(z0, z1, ext), full_mat, cap0="gw_side", cap1="gw_side")
    # head: gill region to the gape corner + a throat plug (inset, to z 0.35) that fills the throat when the jaw drops
    rs = rings(0.22, HINGE[0], True, over=HEAD_OVER, step=0.03)
    rs += [full_ring(HINGE[0] + 0.001, INSET), full_ring(0.35, INSET)]
    K.loft(P("Head"), rs, full_mat, cap0="gw_side", cap1="gw_mouth")
    # upper head shell (on Head) from the gape corner to the snout tip; the palate is its chord
    zs = sorted(set(B.stations(HINGE[0], 0.50, 0.025, 0.006)) | {FRONT[0]})
    K.loft(P("Head"), [head_ring(z, 0.0) for z in zs], head_mat_for(zs), cap0="gw_side", cap1="gw_side")
    # lower jaw shell (on Jaw) from just behind the hinge (inset) to the chin under the snout
    zs = B.stations(HINGE[0], FRONT[0], 0.03, 0.008)
    K.loft(P("Jaw"), [jaw_ring(HINGE[0] - OVER, INSET), jaw_ring(HINGE[0] - 0.001, INSET)] + [jaw_ring(z, 0.0) for z in zs],
           jaw_mat, cap0="gw_mouth", cap1="gw_belly")
    head_bm = P("Head").bm
    head_bm.normal_update()
    head_bvh = BVHTree.FromBMesh(head_bm)
    # ------------------------------------------------ gill slits: 5 dark strips per side, the first the longest
    for sg in (1, -1):
        for j in range(5):
            zj = 0.300 - 0.012 * j
            t_top, t_bot = math.radians(30 - 2 * j), math.radians(-40 + 4 * j)
            pts, ns = [], []
            for q in range(5):
                u = q / 4
                th = t_top + (t_bot - t_top) * u
                th = th if sg > 0 else math.pi - th
                z = zj + 0.004 * math.sin(math.pi * u) - 0.002 * u
                pts.append(B.pt(z, th))
                ns.append(B.normal(z, th))
            K.strip_decal(P("Head"), head_bvh, pts, ns, 0.0036, "gw_gill", lift=0.0008)
    # ------------------------------------------------ eyes: black eyeball (white back) on EyeRoll.*, glint = geo_Eye.*
    empties = {}
    for s, sg in (("L", -1), ("R", 1)):
        c, n = EYE_C[s], EYE_N[s]
        a, b, n = K.frame_from(n, (0, 1, 0))
        ball = P("EyeRoll." + s)
        faces = K.ellipsoid(ball, c, (EYE_R, EYE_R, EYE_R), (a, b, n), "gw_eye_ring", 10, 6, smooth=True)
        white = ball.mi("gw_eye_white")
        for f in faces:
            if (f.calc_center_median() - c).normalized().dot(n) < math.cos(math.radians(88)):
                f.material_index = white
        # the glint: a small dead-silver lens up-front on the pupil (a catchlight in the light, the eyeshine source)
        up = (V((0, 1, 0)) - n * n.y).normalized()
        fw = (V((0, 0, 1)) - n * n.z).normalized()
        gd = (n * math.cos(math.radians(24)) + up * math.sin(math.radians(20)) + fw * math.sin(math.radians(9))).normalized()
        ga, gb, gn = K.frame_from(gd, (0, 1, 0))
        K.ellipsoid(P("Eye." + s), c + gd * (EYE_R + 0.0003), (0.0032, 0.0032, 0.0011), (ga, gb, gn), "gw_eye_glow",
                    8, 3, smooth=True)
        empties["Eye." + s] = ("EyeRoll." + s, c + n * EYE_R, n)
    empties["Mouth"] = ("Head", V((0, B.mouth_y(0.42) - 0.004, 0.42)), None)
    # ------------------------------------------------ UpperJaw: gum band along the upper lip, inside the closed head
    band = P("UpperJaw")
    th_b, top, bot = 0.004, 0.020, 0.003          # tall: protruded 12 mm down it still rises into the palate
    brs = []
    for i in range(13):
        phi = -math.pi / 2 + math.pi * i / 12
        p, o, t = arch(phi, 0.0)
        up = V((0, 1, 0))
        brs.append([p + o * (th_b / 2) + up * top, p + o * (th_b / 2) - up * bot,
                    p - o * (th_b / 2) - up * bot, p - o * (th_b / 2) + up * top])
    K.loft(band, brs, lambda i, k: "gw_mouth", smooth=False, cap0="gw_mouth", cap1="gw_mouth")
    # upper teeth: broad triangles hanging from the band's lower outer edge (tips inside the closed jaw)
    teeth = {"upper": 0, "lower": 0}
    for sg in (1, -1):
        for j in range(10):
            phi = sg * math.radians(4.0 + 8.6 * j)
            p, o, t = arch(phi, 0.0)
            h = 0.013 - 0.0065 * j / 9
            base = p + o * (th_b / 2 - 0.0008) - V((0, 0.0015, 0))
            lean = 0.0012
            yt = base.y - h
            lim = half_w_at(base.z, yt) - 0.0025             # keep the tip inside the lower jaw when closed
            xt = abs(base.x - o.x * lean)
            if xt > lim:
                lean += (xt - lim) / max(0.2, abs(o.x))
            _tooth(band, base, V((0, -1, 0)), o, t, h, 0.8 * h, lean)
            teeth["upper"] += 1
    # lower teeth: narrower spikes standing on the jaw just inside the upper row (tips inside the closed head)
    jaw = P("Jaw")
    for sg in (1, -1):
        for j in range(10):
            phi = sg * math.radians(5.0 + 8.4 * j)
            p, o, t = arch(phi, 0.0045)
            h = 0.011 - 0.006 * j / 9
            base = p - V((0, 0.0012, 0))
            _tooth(jaw, base, V((0, 1, 0)), o, t, h, 0.55 * h, 0.0008)
            teeth["lower"] += 1
    # ------------------------------------------------ first dorsal: tall, rigid (limit 0), modelled standing
    d1 = [(0.150, 0.100), (0.140, 0.126), (0.122, 0.165), (0.098, 0.205), (0.074, 0.234), (0.054, 0.248),
          (0.046, 0.232), (0.042, 0.200), (0.036, 0.164), (0.026, 0.138), (0.004, 0.122), (0.010, 0.104)]
    K.plate(P("Dorsal1"), [V((0, y, z)) for z, y in d1], (1, 0, 0), 0.006, "gw_fin")
    # ------------------------------------------------ pectorals: long, falcate, rigid; white underneath
    pec = [(-0.020, 0.036), (0.030, 0.034), (0.080, 0.026), (0.130, 0.012), (0.170, -0.006), (0.195, -0.022),
           (0.210, -0.034), (0.195, -0.037), (0.160, -0.031), (0.120, -0.027), (0.080, -0.028), (0.040, -0.032),
           (-0.020, -0.036)]
    for s, sg in (("L", -1), ("R", 1)):
        _fin(P("Pec." + s), (sg * 0.072, -0.078, 0.195), (sg * 1.0, -0.42, -0.55), (0, 0, 1), pec, 0.005, "gw_fin",
             under="gw_belly")
        # pelvics (rigid, on Spine.B1)
        _fin(P("Spine.B1"), (sg * 0.045, -0.100, -0.095), (sg * 0.7, -0.6, -0.6), (0, 0, 1),
             [(-0.012, 0.022), (0.035, 0.013), (0.066, -0.012), (0.050, -0.019), (0.020, -0.017), (-0.012, -0.022)],
             0.004, "gw_fin")
    # second dorsal + anal: small rigid fins on Spine.B3
    K.plate(P("Spine.B3"), [V((0, y, z)) for z, y in ((-0.268, 0.040), (-0.277, 0.061), (-0.291, 0.075),
                                                         (-0.296, 0.060), (-0.306, 0.046), (-0.300, 0.030))],
            (1, 0, 0), 0.004, "gw_fin")
    K.plate(P("Spine.B3"), [V((0, y, z)) for z, y in ((-0.274, -0.034), (-0.283, -0.056), (-0.297, -0.067),
                                                         (-0.301, -0.053), (-0.310, -0.041), (-0.304, -0.027))],
            (1, 0, 0), 0.004, "gw_fin")
    # ------------------------------------------------ caudal fin: crescent, the upper lobe a little bigger
    upper = [(-0.378, 0.022), (-0.400, 0.045), (-0.425, 0.082), (-0.450, 0.125), (-0.472, 0.165), (-0.490, 0.192),
             (-0.500, 0.205), (-0.496, 0.188), (-0.482, 0.150), (-0.470, 0.108), (-0.462, 0.068), (-0.458, 0.035),
             (-0.452, 0.004), (-0.420, -0.002), (-0.395, 0.004)]
    lower = [(-0.378, -0.020), (-0.398, -0.040), (-0.420, -0.070), (-0.440, -0.103), (-0.458, -0.135),
             (-0.470, -0.156), (-0.466, -0.140), (-0.458, -0.105), (-0.455, -0.070), (-0.453, -0.035),
             (-0.450, -0.004), (-0.420, 0.002), (-0.395, -0.004)]
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in upper], (1, 0, 0), 0.004, "gw_fin")
    K.plate(P("Tail.Lower"), [V((0, y, z)) for z, y in lower], (1, 0, 0), 0.004, "gw_fin")
    # caudal keels: a horizontal ridge on each side of the peduncle
    for sg in (1, -1):
        K.plate(P("Tail"), [V((sg * x, 0.001, z)) for x, z in ((0.022, -0.360), (0.036, -0.380), (0.034, -0.425),
                                                               (0.012, -0.445))], (0, 1, 0), 0.004, "gw_side")
    info = {"teeth": teeth, "jointsZ": JOINTS,
            "mouthLine": {"hinge_zy": HINGE, "front_zy": FRONT, "snoutOverhang_m": round(0.50 - FRONT[0], 3)},
            "eyes": {s: {"centre": K.r3(EYE_C[s]), "facing": K.r3(EYE_N[s]), "radius": EYE_R} for s in ("L", "R")},
            "countershading": "hard jagged line: ring vertices on the line (full rings %d grey + %d white faces)" % (NU, NL)}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
# the bite (rollout 3.5): snout lifts 12, upper jaw protruded, jaw 50, eyes rolled back, pectorals flared a little
BITE = {"Head": (-12, 0, 0), "Jaw": (50, 0, 0), "UpperJaw": {"rot": (8, 0, 0), "move": (0, -0.012, 0.025)},
        "EyeRoll.L": (0, -150, 0), "EyeRoll.R": (0, 150, 0), "Pec.L": (0, -12, 0), "Pec.R": (0, 12, 0)}
CHECK_OPEN = BITE
CHECK_BEND = {"Spine.F": (0, -4, 0), "Head": (0, -10, 0), "Spine.B1": (0, 8, 0), "Spine.B2": (0, 13, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 10, 0), "Tail.Lower": (0, 10, 0)}

# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_LURE = "kona"               # lure billboard (the ocean set builds lure_kona_0/1; jig as the fallback)
PREVIEW_CM = 420
LURE_R = 6.0                        # ocean visibility (rollout 3.5 light R)
CAM_SCALE = 1.4                     # EncounterDef.camScale: the rest camera 1.4x further from the lure
TURNTABLE = dict(
    open=BITE,
    bend={"Spine.F": (0, -2, 0), "Head": (0, -6, 0), "Spine.B1": (0, 5, 0), "Spine.B2": (0, 9, 0),
          "Spine.B3": (0, 12, 0), "Tail": (0, 14, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0)},
    lit={"Jaw": (5, 0, 0)},
    game={"Jaw": (5, 0, 0)},
)


# ---------------------------------------------------------------- mock helpers (local)
def _place(M, sc, pos, heading, pitch=0.0, bank=0.0):
    """Scene.place with a pitch (+ = nose up)."""
    s = sc.cm / 100.0
    Mx = (Matrix.Translation(V(pos)) @ Matrix.Rotation(math.radians(heading), 4, "Y")
          @ Matrix.Rotation(math.radians(-pitch), 4, "X") @ Matrix.Rotation(math.radians(bank), 4, "Z")
          @ Matrix.Scale(s, 4))
    Cm = M.LG.C_UW.to_4x4()
    sc.mount.matrix_world = Cm @ Mx @ Cm.inverted()
    M.bpy.context.view_layer.update()


def _fwd(heading, pitch=0.0):
    h, p = math.radians(heading), math.radians(pitch)
    return V((math.sin(h) * math.cos(p), math.sin(p), math.cos(h) * math.cos(p)))


def _heading_to(frm, to):
    d = V(to) - V(frm)
    return math.degrees(math.atan2(d.x, d.z)), math.degrees(math.atan2(d.y, math.hypot(d.x, d.z)))


def preview_beats(M, sc, cam, lure, surface):
    """Six beats of the great white's encounter (rollout 3.5) in the ocean set -> [(name, 480x270 frame)]:
    eyes (dead-silver glints far below) | approach (the huge silhouette rising out of the blue gloom) | circle (the
    wary slow circle, flank lit) | pass (the far point of the circle: a grey wall sweeping between camera and lure) |
    tell (head-on 1.5 m out, the eyes rolled back, the jaw starting to drop) | bite_full (full screen: snout up,
    upper jaw out, jaw 50)."""
    rng = random.Random(11)
    frames = []
    key = PREVIEW_LURE if os.path.isfile(os.path.join(M.ENC, "lure_%s_0.png" % PREVIEW_LURE)) else "jig"
    surf_y = M.PROF.get("surface_y", 1.2)
    veil_col = M.PROF.get("veil", ("#020a1a", 0.45))
    veil_col = veil_col[0] if isinstance(veil_col, (tuple, list)) else veil_col
    s = sc.cm / 100.0
    cam0 = M.CAM                                               # camScale 1.4: the rest camera further back
    M.CAM = V(cam0) * CAM_SCALE
    cam = M.setcam()
    M.CAM = cam0
    lure = V(lure)

    def rows(c):
        cp = V(c.matrix_world.translation)
        cu = M.LG.u_of_w(cp)
        hy = M.project(c, V((cu.x, 0.0, cu.z + 30.0)))[1]            # the far horizon at the lure's depth
        cr = M.project(c, V((cu.x, surf_y, cu.z + 30.0)))[1]         # the surface's far edge (the ceiling layer)
        return hy, cr

    def frame(tag, c, lure_u, lure_frame=0, eyes_mult=1.0, eyes_only=False, rim=True, fish_over_lure=True,
              line=True, bubbles=0, snow=12, lure_scale=1, win=True, veil=0.0):
        lpx = M.project(c, lure_u)[:2]
        upx = M.project(c, lure_u + V(M.PROF.get("line_up", (-1.0, 1.2, -1.4))))[:2]
        hy, cr = rows(c)
        fish = M.render_fish(sc, c, lure_u, tag, rim=rim and not eyes_only, eyes_only=eyes_only)
        under = M.back_layers(lpx, hy, ceil_row=cr)
        if veil > 0:                                           # the Eyes beat darkens the back layers
            under[..., :3] = under[..., :3] * (1 - veil) + M.R.hexrgb(veil_col) * veil
        if fish_over_lure:
            M.put_lure(under, lpx, key, lure_frame, scale=lure_scale)
        under = M.front_fx(under, lpx, upx if line else None, rng, snow=snow, bubbles=bubbles)
        M.R.over(under, fish, 0, 0)
        if not fish_over_lure:
            M.put_lure(under, lpx, key, lure_frame, scale=lure_scale)
        M.put_eyeshine(under, sc.eyes_px(c), eyes_mult)
        if win:
            return M.compose_window(*surface, under, M.WIN)
        return M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)

    swim = dict(Spine_F=(0, -1, 0), Head=(0, -3, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 5, 0), Spine_B3=(0, 7, 0),
                Tail=(0, 10, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Jaw=(5, 0, 0))
    # 1) eyes: dead-silver glints far below in the blue gloom, rising towards the lure (rollout eyes0)
    pos = V((1.6, -2.9, 9.5))                                        # (rollout eyes0 sits on the window's bottom edge)
    h, p = _heading_to(pos, lure)
    _place(M, sc, pos, h, pitch=p * 0.6)
    sc.pose(**swim)
    frames.append(("eyes", frame("g1", cam, lure, eyes_only=True, snow=10, veil=0.45)))
    # 2) approach: the huge silhouette rises slowly out of the gloom (outside the light: abyss body, fog outline)
    pos = V((1.4, -2.1, 5.6))
    h, p = _heading_to(pos, lure + V((0.6, -0.6, 0)))
    _place(M, sc, pos, h + 14, pitch=p * 0.9, bank=-6)
    sc.pose(**swim)
    frames.append(("approach", frame("g2", cam, lure, lure_frame=1, eyes_mult=0.7, veil=0.2)))
    # 3) circle (wary): the slow CCW circle 3.4 m out, 0.6 m below the lure, flank to the lure (lit), far side
    ang = math.radians(35)
    pos = V((3.4 * math.sin(ang), -0.6, 3.4 * math.cos(ang)))
    rad = V((pos.x, 0, pos.z)).normalized()
    hdg = math.degrees(math.atan2(-rad.z, rad.x))                     # CCW tangent (seen from above)
    _place(M, sc, pos, hdg - 8, bank=8)
    sc.pose(Spine_F=(0, -2, 0), Head=(0, -6, 0), Spine_B1=(0, -2, 0), Spine_B2=(0, -3, 0), Spine_B3=(0, -2, 0),
            Tail=(0, 6, 0), Tail_Upper=(0, 4, 0), Tail_Lower=(0, 4, 0), Jaw=(5, 0, 0), Pec_L=(0, 6, 0),
            Pec_R=(0, -6, 0))
    frames.append(("circle", frame("g3", cam, lure, lure_frame=0, bubbles=3)))
    # 4) pass: the circle's far point sweeps between the camera and the lure - a grey wall, backlit by the lure
    cp = M.LG.u_of_w(cam.matrix_world.translation)
    mid = V(cp).lerp(lure, 0.55) + V((0.25, -0.35, 0))
    to = (lure - V(cp))
    side = V((to.z, 0, -to.x)).normalized()                            # across the view, to the right
    hdg = math.degrees(math.atan2(-side.x, -side.z))                   # swimming right-to-left across the view
    _place(M, sc, mid, hdg, bank=-5)
    sc.pose(Spine_F=(0, 2, 0), Head=(0, 4, 0), Spine_B1=(0, -3, 0), Spine_B2=(0, -5, 0), Spine_B3=(0, -6, 0),
            Tail=(0, -8, 0), Tail_Upper=(0, -4, 0), Tail_Lower=(0, -4, 0), Jaw=(5, 0, 0))
    frames.append(("pass", frame("g4", cam, lure, line=False, fish_over_lure=True)))
    # 5) tell: stopped head-on 1.5 m out (a little below / beyond the lure), closing slowly: the eyes roll back, the
    #    mouth starts to drop
    hdg = -150.0
    fwd = _fwd(hdg, 8.0)
    root = lure - fwd * (1.5 + 0.5 * s) + V((0, -0.05 * s, 0))
    _place(M, sc, root, hdg, pitch=8.0)
    sc.pose(Jaw=(22, 0, 0), Head=(-4, 0, 0), EyeRoll_L=(0, -150, 0), EyeRoll_R=(0, 150, 0), Pec_L=(0, -10, 0),
            Pec_R=(0, 10, 0), Tail=(0, 4, 0))
    frames.append(("tell", frame("g5", cam, lure, lure_frame=0, eyes_mult=1.0)))
    # 6) bite (full screen): from below-right onto the lure: snout up 12, upper jaw out, jaw 50, eyes rolled
    cpos = V((-0.75, -0.15, -1.75))
    c6 = M.setcam(pos=cpos, target=V((0.3, 0.16, 0.05)), f=M.FPX * 1.4)
    mouth = V((0, 0.0, 0))
    hdg = -118.0
    fwd = _fwd(hdg, 10.0)
    root = mouth - fwd * (0.42 * s) + V((0, 0.036 * s, 0))
    _place(M, sc, root, hdg, pitch=10.0, bank=-6)
    bite = {k.replace(".", "_"): v for k, v in BITE.items()}
    bite.update(Spine_B1=(0, 4, 0), Spine_B2=(0, 7, 0), Spine_B3=(0, 9, 0), Tail=(0, 12, 0), Tail_Upper=(0, 6, 0),
                Tail_Lower=(0, 6, 0), Pec_L=(0, -20, 0), Pec_R=(0, 20, 0))
    sc.pose(**bite)
    frames.append(("bite_full", frame("g6", c6, mouth + V((0.0, 0.0, 0.0)), line=False, lure_scale=2, snow=16,
                                      bubbles=6, win=False)))
    return frames
