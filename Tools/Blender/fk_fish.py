"""
FishingKing - procedural fish sprites.

Every species is described by a parameter dict (body profile, fins, colours, patterns,
extras). The builder makes a lofted body (UVs: u = tail->head, v = belly->back), flat fins
in the mid-plane, eyes / barbels / bills, renders a side view facing +X and writes
Sprites/Fish/<id>_0.png (tail straight) and <id>_1.png (tail swung) for a 2-frame swim cycle.

Run:  blender -b --python Tools/Blender/fk_fish.py [-- id1 id2 ...]
"""
import sys
import os
import math
import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402

OUT = os.path.join(C.SPRITES, "Fish")

# ----------------------------------------------------------------------------- species visuals
# h: max half height / length, w: half thickness / half height, peak: t of max height,
# nose: nose exponent (big = blunt), ped: peduncle ratio, arch: back hump, mouth: nose tip z offset
F = {}


def fish(id, **kw):
    F[id] = kw


# ---- Lake
fish("crucian_carp", px=22, h=0.21, peak=0.5, nose=1.7, ped=0.33, arch=0.2,
     tail=dict(type="fork", len=0.27, span=0.95),
     dorsal=[dict(t0=0.28, t1=0.62, h=0.5, shape="low", rake=0.1)],
     anal=[dict(t0=0.17, t1=0.3, h=0.5, shape="tri")], pelvic=dict(t=0.5, h=0.4), pect=0.35,
     col=dict(back="#3d4a24", side="#8e8a44", belly="#dccf94", fin="#5c5630"), shine=0.3,
     pat=[dict(type="scales", color="#5e5a2c", scale=26, width=0.09, alpha=0.6)])
fish("bluegill", px=19, h=0.29, peak=0.5, nose=1.6, ped=0.32, arch=0.05,
     tail=dict(type="fork", len=0.22, span=0.9, notch=0.3),
     dorsal=[dict(t0=0.2, t1=0.7, h=0.42, shape="spiny")],
     anal=[dict(t0=0.18, t1=0.42, h=0.45, shape="round")], pelvic=dict(t=0.56, h=0.35), pect=0.4,
     col=dict(back="#27464a", side="#5c7f62", belly="#eda33a", fin="#34504a"),
     pat=[dict(type="bands", color="#233a3c", count=6, width=0.38, t0=0.15, t1=0.8, v0=0.34, v1=1.0),
          dict(type="blotch", color="#0c1018", t=0.77, v=0.6, r=0.055)])
fish("carp", px=34, h=0.18, peak=0.48, nose=1.8, ped=0.34, arch=0.22, mouth=-0.1,
     tail=dict(type="fork", len=0.25, span=1.0),
     dorsal=[dict(t0=0.25, t1=0.62, h=0.45, shape="low", rake=0.1)],
     anal=[dict(t0=0.17, t1=0.27, h=0.55, shape="tri")], pelvic=dict(t=0.5, h=0.4), pect=0.4,
     col=dict(back="#523f1e", side="#b0873a", belly="#e6cf8c", fin="#8c5a2a"), shine=0.3,
     barbels=[dict(z=-0.25, len=0.07, ang=-30)],
     pat=[dict(type="scales", color="#7a5a22", scale=22, width=0.1, alpha=0.7)])
fish("largemouth_bass", px=30, h=0.165, peak=0.5, nose=1.35, ped=0.3, arch=0.08, mouth=0.05,
     tail=dict(type="square", len=0.2, span=0.95),
     dorsal=[dict(t0=0.45, t1=0.66, h=0.55, shape="spiny"), dict(t0=0.24, t1=0.44, h=0.6, shape="round")],
     anal=[dict(t0=0.18, t1=0.33, h=0.55, shape="round")], pelvic=dict(t=0.6, h=0.4), pect=0.4,
     mouth_line=0.16,
     col=dict(back="#34521f", side="#7f9e48", belly="#ecebc9", fin="#58773a"),
     pat=[dict(type="mottle", color="#27391a", scale=9, thresh=0.52, v0=0.36, v1=0.66, t0=0.05, t1=0.9)])
fish("golden_carp", px=36, h=0.18, peak=0.48, nose=1.8, ped=0.34, arch=0.22, mouth=-0.1,
     tail=dict(type="fork", len=0.28, span=1.1),
     dorsal=[dict(t0=0.25, t1=0.62, h=0.55, shape="low", rake=0.1)],
     anal=[dict(t0=0.17, t1=0.27, h=0.6, shape="tri")], pelvic=dict(t=0.5, h=0.45), pect=0.45,
     col=dict(back="#d8901a", side="#ffcf3e", belly="#fff1a6", fin="#ff8f1a"), shine=1.0, emit=1.15,
     barbels=[dict(z=-0.25, len=0.07, ang=-30)],
     pat=[dict(type="scales", color="#c9780f", scale=22, width=0.1, alpha=0.8)])

# ---- Stream
fish("pale_chub", px=17, h=0.13, peak=0.5, nose=1.5, ped=0.34,
     tail=dict(type="fork", len=0.26, span=1.0),
     dorsal=[dict(t0=0.4, t1=0.52, h=0.8, shape="tri")],
     anal=[dict(t0=0.2, t1=0.36, h=0.9, shape="tri", rake=0.2)], pelvic=dict(t=0.5, h=0.4), pect=0.35,
     col=dict(back="#48677a", side="#c7d3d9", belly="#f3f3f3", fin="#e28a68"), shine=0.8,
     pat=[dict(type="bands", color="#4f86b3", count=9, width=0.33, t0=0.12, t1=0.82, v0=0.3, v1=0.82)])
fish("cherry_salmon", px=24, h=0.14, peak=0.52, nose=1.45, ped=0.36,
     tail=dict(type="square", len=0.2, span=0.9),
     dorsal=[dict(t0=0.45, t1=0.6, h=0.7, shape="tri"), dict(t0=0.14, t1=0.19, h=0.3, shape="round")],
     anal=[dict(t0=0.2, t1=0.32, h=0.55, shape="tri")], pelvic=dict(t=0.46, h=0.4), pect=0.4,
     col=dict(back="#3a4a3a", side="#aab2a2", belly="#f2eee2", fin="#8a8a6a"), shine=0.4,
     pat=[dict(type="bands", color="#4c4c6e", count=8, width=0.45, t0=0.12, t1=0.8, v0=0.38, v1=0.66),
          dict(type="spots", color="#1c1c1c", scale=16, size=0.1, v0=0.7, v1=1.0, t0=0.2, t1=0.85)])
fish("rainbow_trout", px=28, h=0.15, peak=0.5, nose=1.5, ped=0.36,
     tail=dict(type="square", len=0.21, span=1.0),
     dorsal=[dict(t0=0.44, t1=0.6, h=0.7, shape="tri"), dict(t0=0.14, t1=0.19, h=0.3, shape="round")],
     anal=[dict(t0=0.2, t1=0.32, h=0.55, shape="tri")], pelvic=dict(t=0.46, h=0.4), pect=0.4,
     col=dict(back="#4a6a3a", side="#c2c8b0", belly="#f4f0e8", fin="#7a8a5a"), shine=0.4,
     pat=[dict(type="stripe", color="#e0677a", center=0.5, width=0.12, t0=0.08, t1=0.92),
          dict(type="spots", color="#1a1a1a", scale=20, size=0.11, v0=0.45, v1=1.0)],
     finspots="#1a1a1a")
fish("mandarin_fish", px=28, h=0.18, peak=0.55, nose=1.3, ped=0.33, arch=0.1, mouth=0.08,
     tail=dict(type="round", len=0.2, span=0.9),
     dorsal=[dict(t0=0.42, t1=0.72, h=0.5, shape="spiny"), dict(t0=0.2, t1=0.41, h=0.55, shape="round")],
     anal=[dict(t0=0.17, t1=0.34, h=0.55, shape="round")], pelvic=dict(t=0.62, h=0.4), pect=0.45,
     mouth_line=0.18,
     col=dict(back="#6a5a2a", side="#c9a94a", belly="#ead9a2", fin="#8b7030"),
     pat=[dict(type="spots", color="#3a2a14", scale=8, size=0.24, v0=0.2, v1=1.0, t0=0.05, t1=0.95)],
     finspots="#3a2a14")
fish("lenok", px=32, h=0.15, peak=0.5, nose=1.45, ped=0.36,
     tail=dict(type="fork", len=0.22, span=0.95),
     dorsal=[dict(t0=0.46, t1=0.62, h=0.75, shape="tri"), dict(t0=0.14, t1=0.19, h=0.3, shape="round")],
     anal=[dict(t0=0.2, t1=0.32, h=0.55, shape="tri")], pelvic=dict(t=0.46, h=0.4), pect=0.4,
     col=dict(back="#5a3434", side="#b37a6a", belly="#ead2c2", fin="#a45a4a"), shine=0.4,
     eye_col="#e03020",
     pat=[dict(type="spots", color="#1c1010", scale=15, size=0.14, v0=0.4, v1=1.0, t0=0.15, t1=0.9)])

# ---- Sea
fish("horse_mackerel", px=22, h=0.14, peak=0.52, nose=1.5, ped=0.2,
     tail=dict(type="deep", len=0.3, span=1.15),
     dorsal=[dict(t0=0.5, t1=0.62, h=0.6, shape="tri"), dict(t0=0.2, t1=0.48, h=0.35, shape="low")],
     anal=[dict(t0=0.2, t1=0.42, h=0.3, shape="low")], pelvic=dict(t=0.56, h=0.3), pect=0.5,
     col=dict(back="#386a7a", side="#b8c8d0", belly="#f1f4f4", fin="#c8b85a"), shine=0.9,
     pat=[dict(type="stripe", color="#7d8e96", center=0.52, width=0.045, t0=0.02, t1=0.7)])
fish("mackerel", px=26, h=0.13, peak=0.52, nose=1.4, ped=0.18,
     tail=dict(type="deep", len=0.3, span=1.2),
     dorsal=[dict(t0=0.5, t1=0.66, h=0.6, shape="tri"), dict(t0=0.28, t1=0.38, h=0.4, shape="tri")],
     anal=[dict(t0=0.28, t1=0.38, h=0.4, shape="tri")], pelvic=dict(t=0.58, h=0.3), pect=0.4,
     finlets=dict(n=5, t0=0.05, t1=0.25, col="#3a6a7a"),
     col=dict(back="#26687a", side="#9ab8c0", belly="#f4f4f0", fin="#56788a"), shine=0.9,
     pat=[dict(type="bands", color="#16283a", count=15, width=0.32, t0=0.05, t1=0.85, v0=0.62, v1=1.0, wave=0.25)])
fish("rockfish", px=26, h=0.19, peak=0.62, nose=1.5, ped=0.33, arch=0.08, mouth=0.1,
     tail=dict(type="square", len=0.2, span=0.85),
     dorsal=[dict(t0=0.36, t1=0.74, h=0.55, shape="spiny"), dict(t0=0.18, t1=0.35, h=0.5, shape="round")],
     anal=[dict(t0=0.18, t1=0.32, h=0.55, shape="round")], pelvic=dict(t=0.64, h=0.45), pect=0.55,
     mouth_line=0.18,
     col=dict(back="#383838", side="#6a6a62", belly="#b9b1a1", fin="#474742"),
     pat=[dict(type="mottle", color="#262626", scale=7, thresh=0.5, v0=0.3, v1=1.0)])
fish("flounder", px=30, h=0.32, w=0.18, peak=0.52, nose=1.9, ped=0.2,
     tail=dict(type="round", len=0.16, span=0.7),
     dorsal=[dict(t0=0.1, t1=0.86, h=0.18, shape="low")],
     anal=[dict(t0=0.1, t1=0.7, h=0.18, shape="low")], pect=0.3,
     eye_t=0.84, eye_v=0.55, two_eyes=True, eye=0.08,
     col=dict(back="#6a5a38", side="#86754c", belly="#9a8a5c", fin="#6e5e3a"),
     pat=[dict(type="spots", color="#3e3220", scale=11, size=0.14, v0=0.0, v1=1.0),
          dict(type="spots", color="#d8cfb0", scale=17, size=0.07, v0=0.0, v1=1.0, seed=3.0)])
fish("black_porgy", px=28, h=0.21, peak=0.55, nose=1.45, ped=0.28, arch=0.12,
     tail=dict(type="fork", len=0.23, span=0.95),
     dorsal=[dict(t0=0.25, t1=0.68, h=0.45, shape="spiny")],
     anal=[dict(t0=0.18, t1=0.32, h=0.5, shape="tri")], pelvic=dict(t=0.6, h=0.4), pect=0.55,
     col=dict(back="#262a31", side="#666c75", belly="#c7cbd0", fin="#262a31"), shine=0.7,
     pat=[dict(type="bands", color="#474c55", count=6, width=0.3, t0=0.15, t1=0.8, v0=0.3, v1=1.0)])
fish("red_seabream", px=32, h=0.22, peak=0.56, nose=1.4, ped=0.26, arch=0.15,
     tail=dict(type="fork", len=0.25, span=1.0),
     dorsal=[dict(t0=0.25, t1=0.68, h=0.45, shape="spiny")],
     anal=[dict(t0=0.18, t1=0.32, h=0.5, shape="tri")], pelvic=dict(t=0.6, h=0.4), pect=0.6,
     col=dict(back="#c33e4a", side="#f07a7a", belly="#ffd9d0", fin="#e05a5a"), shine=0.9,
     pat=[dict(type="spots", color="#6ac2f2", scale=18, size=0.08, v0=0.45, v1=1.0, t0=0.1, t1=0.85)])

# ---- Swamp
fish("piranha", px=20, h=0.27, peak=0.58, nose=2.3, ped=0.28, arch=0.05, mouth=-0.05,
     tail=dict(type="square", len=0.2, span=0.85),
     dorsal=[dict(t0=0.36, t1=0.5, h=0.45, shape="round"), dict(t0=0.14, t1=0.18, h=0.25, shape="round")],
     anal=[dict(t0=0.14, t1=0.42, h=0.35, shape="low")], pelvic=dict(t=0.55, h=0.3), pect=0.35,
     mouth_line=0.1,
     col=dict(back="#4a5058", side="#9aa0a8", belly="#d8392a", fin="#5a5a5a"), shine=0.5,
     pat=[dict(type="spots", color="#c8d0d8", scale=24, size=0.07, v0=0.45, v1=0.95)])
fish("catfish", px=36, h=0.12, peak=0.8, nose=2.6, ped=0.45, arch=-0.05, taper=0.8,
     tail=dict(type="round", len=0.13, span=0.9),
     dorsal=[dict(t0=0.64, t1=0.7, h=0.5, shape="round")],
     anal=[dict(t0=0.04, t1=0.55, h=0.45, shape="low")], pelvic=dict(t=0.55, h=0.3), pect=0.6,
     head_w=0.8, eye=0.07, mouth_line=0.1,
     barbels=[dict(z=0.25, len=0.35, ang=15, curve=-0.4), dict(z=-0.4, len=0.12, ang=-40)],
     col=dict(back="#383a28", side="#6a6a4a", belly="#cac2a2", fin="#484a34"),
     pat=[dict(type="mottle", color="#2a2c1c", scale=6, thresh=0.5, v0=0.35, v1=1.0)])
fish("snakehead", px=38, h=0.1, peak=0.65, nose=1.9, ped=0.55, taper=0.7,
     tail=dict(type="round", len=0.13, span=1.1),
     dorsal=[dict(t0=0.06, t1=0.7, h=0.6, shape="low")],
     anal=[dict(t0=0.06, t1=0.46, h=0.55, shape="low")], pelvic=dict(t=0.7, h=0.3), pect=0.5,
     mouth_line=0.12, head_w=0.3,
     col=dict(back="#262c20", side="#56603f", belly="#b9b999", fin="#384030"),
     pat=[dict(type="bands", color="#1c2014", count=8, width=0.45, t0=0.05, t1=0.85, v0=0.3, v1=0.8, wave=0.6)])
fish("arowana", px=40, h=0.1, peak=0.55, nose=1.2, ped=0.55, mouth=0.55, arch=-0.1,
     tail=dict(type="round", len=0.15, span=1.1),
     dorsal=[dict(t0=0.05, t1=0.3, h=0.9, shape="low")],
     anal=[dict(t0=0.05, t1=0.42, h=0.9, shape="low")], pelvic=dict(t=0.6, h=0.4), pect=0.6,
     barbels=[dict(z=-0.5, len=0.08, ang=20)],
     col=dict(back="#6b5a4a", side="#e9b8a0", belly="#f8e8d8", fin="#d25a3a"), shine=1.0,
     pat=[dict(type="scales", color="#b3654a", scale=13, width=0.12, alpha=0.9)])
fish("arapaima", px=64, h=0.1, peak=0.62, nose=1.6, ped=0.55, taper=0.8, arch=0.05,
     tail=dict(type="round", len=0.13, span=1.2),
     dorsal=[dict(t0=0.06, t1=0.22, h=1.0, shape="low")],
     anal=[dict(t0=0.06, t1=0.22, h=1.0, shape="low")], pelvic=dict(t=0.55, h=0.3), pect=0.5,
     col=dict(back="#34443a", side="#8a9a80", belly="#cac2aa", fin="#8b3b2a"),
     pat=[dict(type="region", color="#b33a2a", t0=0.0, t1=0.32, v0=0.0, v1=1.0, soft=0.12),
          dict(type="scales", color="#3a2a20", scale=18, width=0.09, alpha=0.5)])

# ---- Ice
fish("smelt", px=15, h=0.1, peak=0.5, nose=1.4, ped=0.4,
     tail=dict(type="fork", len=0.25, span=1.2),
     dorsal=[dict(t0=0.45, t1=0.55, h=1.0, shape="tri"), dict(t0=0.15, t1=0.19, h=0.4, shape="round")],
     anal=[dict(t0=0.18, t1=0.36, h=0.7, shape="low")], pelvic=dict(t=0.48, h=0.4), pect=0.4,
     col=dict(back="#88a6b6", side="#d8e4ea", belly="#f4f8fa", fin="#c9d9e1"), shine=1.0,
     pat=[dict(type="stripe", color="#f5fbff", center=0.52, width=0.07, t0=0.05, t1=0.9)])
fish("burbot", px=34, h=0.1, peak=0.72, nose=2.4, ped=0.45, taper=0.7,
     tail=dict(type="round", len=0.13, span=1.1),
     dorsal=[dict(t0=0.62, t1=0.7, h=0.5, shape="round"), dict(t0=0.05, t1=0.6, h=0.55, shape="low")],
     anal=[dict(t0=0.05, t1=0.5, h=0.5, shape="low")], pelvic=dict(t=0.75, h=0.3), pect=0.5,
     barbels=[dict(z=-0.5, len=0.08, ang=-60)], head_w=0.4,
     col=dict(back="#4a4228", side="#8a7a48", belly="#d9cd99", fin="#6a5a38"),
     pat=[dict(type="mottle", color="#2e2818", scale=8, thresh=0.5, v0=0.3, v1=1.0)])
fish("northern_pike", px=40, h=0.095, peak=0.5, nose=1.1, ped=0.45, mouth=0.1,
     tail=dict(type="fork", len=0.18, span=1.3),
     dorsal=[dict(t0=0.1, t1=0.24, h=1.0, shape="round")],
     anal=[dict(t0=0.1, t1=0.24, h=0.9, shape="round")], pelvic=dict(t=0.42, h=0.4), pect=0.5,
     mouth_line=0.16,
     col=dict(back="#34521f", side="#789a48", belly="#e8e8c8", fin="#8a6a3a"),
     pat=[dict(type="spots", color="#d8e2a2", scale=14, size=0.17, v0=0.28, v1=0.95, t0=0.02, t1=0.82)],
     finspots="#2a2a1a")
fish("arctic_char", px=32, h=0.15, peak=0.5, nose=1.5, ped=0.35,
     tail=dict(type="fork", len=0.22, span=0.95),
     dorsal=[dict(t0=0.45, t1=0.6, h=0.7, shape="tri"), dict(t0=0.14, t1=0.19, h=0.3, shape="round")],
     anal=[dict(t0=0.2, t1=0.32, h=0.55, shape="tri")], pelvic=dict(t=0.46, h=0.45), pect=0.45,
     col=dict(back="#34444a", side="#7a8a7c", belly="#e2582a", fin="#c45a3a"), shine=0.4,
     pat=[dict(type="spots", color="#f2c2a2", scale=15, size=0.11, v0=0.4, v1=1.0)])
fish("sturgeon", px=60, h=0.09, peak=0.55, nose=1.15, ped=0.4, mouth=-0.2, taper=0.8,
     tail=dict(type="shark", len=0.2, span=1.4),
     dorsal=[dict(t0=0.12, t1=0.22, h=0.9, shape="tri", rake=0.4)],
     anal=[dict(t0=0.12, t1=0.2, h=0.8, shape="tri", rake=0.4)], pelvic=dict(t=0.35, h=0.4), pect=0.6,
     barbels=[dict(z=-0.7, len=0.06, ang=-80, at=0.93), dict(z=-0.7, len=0.06, ang=-80, at=0.95)],
     col=dict(back="#4a4a42", side="#7a786a", belly="#d9d5c5", fin="#5a5850"),
     pat=[dict(type="bands", color="#dad2b8", count=16, width=0.35, t0=0.08, t1=0.85, v0=0.84, v1=0.93),
          dict(type="bands", color="#dad2b8", count=16, width=0.35, t0=0.08, t1=0.85, v0=0.47, v1=0.54)])

# ---- Ocean
fish("yellowtail", px=44, h=0.14, peak=0.52, nose=1.45, ped=0.2,
     tail=dict(type="deep", len=0.28, span=1.2),
     dorsal=[dict(t0=0.2, t1=0.55, h=0.35, shape="low", rake=0.1)],
     anal=[dict(t0=0.2, t1=0.36, h=0.35, shape="low")], pelvic=dict(t=0.6, h=0.35), pect=0.4,
     col=dict(back="#34568a", side="#c1cdd5", belly="#f5f5f5", fin="#e2c230"), shine=0.9,
     pat=[dict(type="stripe", color="#f2d23e", center=0.55, width=0.07, t0=0.02, t1=0.92)])
fish("mahi_mahi", px=48, h=0.15, peak=0.82, nose=3.0, ped=0.25, taper=0.7, arch=0.1,
     tail=dict(type="deep", len=0.3, span=1.4),
     dorsal=[dict(t0=0.08, t1=0.92, h=0.55, shape="low")],
     anal=[dict(t0=0.08, t1=0.5, h=0.45, shape="low")], pelvic=dict(t=0.72, h=0.4), pect=0.5,
     col=dict(back="#1e8c6a", side="#cad83a", belly="#f2ea7a", fin="#2a7aa2"), shine=0.8,
     pat=[dict(type="spots", color="#2a82cc", scale=20, size=0.08, v0=0.3, v1=0.85)])
fish("bluefin_tuna", px=60, h=0.165, peak=0.5, nose=1.45, ped=0.12,
     tail=dict(type="lunate", len=0.28, span=1.5),
     dorsal=[dict(t0=0.5, t1=0.64, h=0.6, shape="tri"), dict(t0=0.3, t1=0.4, h=0.7, shape="falcate", rake=0.5)],
     anal=[dict(t0=0.3, t1=0.4, h=0.7, shape="falcate", rake=0.5)], pelvic=dict(t=0.62, h=0.3), pect=0.45,
     finlets=dict(n=7, t0=0.04, t1=0.28, col="#f2d23e"),
     col=dict(back="#18284a", side="#8a9ab2", belly="#e9edf1", fin="#27375a"), shine=1.0,
     pat=[dict(type="bands", color="#c8d0dc", count=14, width=0.2, t0=0.1, t1=0.7, v0=0.12, v1=0.42)])
fish("ocean_sunfish", px=46, h=0.38, w=0.3, peak=0.55, nose=2.3, ped=0.75, arch=0.0,
     tail=dict(type="clavus", len=0.1, span=1.0),
     dorsal=[dict(t0=0.12, t1=0.3, h=1.1, shape="falcate", rake=0.35)],
     anal=[dict(t0=0.12, t1=0.3, h=1.1, shape="falcate", rake=0.35)], pect=0.25, eye=0.06,
     col=dict(back="#6a7078", side="#9aa0a8", belly="#dadee2", fin="#7a8088"),
     pat=[dict(type="mottle", color="#c2c8ce", scale=5, thresh=0.62, v0=0.0, v1=1.0)])
fish("blue_marlin", px=84, h=0.12, peak=0.55, nose=1.3, ped=0.14,
     tail=dict(type="lunate", len=0.3, span=2.0),
     dorsal=[dict(t0=0.42, t1=0.78, h=1.2, shape="sail", rake=0.2), dict(t0=0.12, t1=0.16, h=0.4, shape="tri")],
     anal=[dict(t0=0.3, t1=0.44, h=0.8, shape="falcate", rake=0.4)], pelvic=dict(t=0.7, h=0.6), pect=0.6,
     bill=0.3,
     col=dict(back="#18285a", side="#6a8ab2", belly="#e9eff5", fin="#27376a"), shine=0.8,
     pat=[dict(type="bands", color="#8abae8", count=13, width=0.18, t0=0.12, t1=0.8, v0=0.3, v1=0.78)])
fish("great_white", px=84, h=0.145, w=0.9, peak=0.52, nose=1.6, ped=0.18, mouth=0.2,
     tail=dict(type="shark", len=0.25, span=1.8),
     dorsal=[dict(t0=0.5, t1=0.64, h=1.35, shape="shark", rake=0.45), dict(t0=0.14, t1=0.17, h=0.25, shape="tri")],
     anal=[dict(t0=0.14, t1=0.17, h=0.25, shape="tri")], pelvic=dict(t=0.36, h=0.5), pect=1.1,
     eye=0.06, eye_col="#0a0a0a", mouth_line=0.14,
     col=dict(back="#56606c", side="#76808c", belly="#f2f2f2", fin="#56606c"),
     belly_line=0.4,
     pat=[dict(type="bands", color="#3a4450", count=40, width=0.3, t0=0.68, t1=0.78, v0=0.3, v1=0.65)])

# ---- Cave
fish("cave_tetra", px=17, h=0.17, peak=0.52, nose=1.8, ped=0.35,
     tail=dict(type="fork", len=0.25, span=1.0),
     dorsal=[dict(t0=0.4, t1=0.52, h=0.8, shape="tri"), dict(t0=0.14, t1=0.18, h=0.3, shape="round")],
     anal=[dict(t0=0.16, t1=0.38, h=0.55, shape="low")], pelvic=dict(t=0.5, h=0.4), pect=0.35,
     eye=0.0, emit=1.1,
     col=dict(back="#e3b1b8", side="#f6d2d4", belly="#fff2f0", fin="#f2c2c2"), shine=0.6)
fish("crystal_koi", px=36, h=0.18, peak=0.48, nose=1.8, ped=0.34, arch=0.2, mouth=-0.1,
     tail=dict(type="fork", len=0.32, span=1.3),
     dorsal=[dict(t0=0.25, t1=0.62, h=0.6, shape="low", rake=0.1)],
     anal=[dict(t0=0.17, t1=0.27, h=0.6, shape="tri")], pelvic=dict(t=0.5, h=0.5), pect=0.55,
     barbels=[dict(z=-0.25, len=0.07, ang=-30)],
     col=dict(back="#7ae0ff", side="#c8f4ff", belly="#ffffff", fin="#b27aff"), shine=1.3, emit=1.25,
     pat=[dict(type="mottle", color="#b85cff", scale=4, thresh=0.56, v0=0.3, v1=1.0),
          dict(type="scales", color="#5ab8e8", scale=20, width=0.08, alpha=0.6)])
fish("anglerfish", px=32, h=0.3, w=0.9, peak=0.66, nose=2.6, ped=0.3, mouth=0.25,
     tail=dict(type="round", len=0.2, span=0.8),
     dorsal=[dict(t0=0.2, t1=0.34, h=0.4, shape="round")],
     anal=[dict(t0=0.18, t1=0.3, h=0.4, shape="round")], pect=0.45, eye=0.07,
     mouth_line=0.35, teeth=True,
     lure=dict(len=0.45, col="#6affea"),
     col=dict(back="#3e3446", side="#5a4c62", belly="#8a7486", fin="#3e3446"),
     pat=[dict(type="mottle", color="#1a1420", scale=7, thresh=0.55, v0=0.0, v1=1.0)])
fish("coelacanth", px=48, h=0.16, peak=0.58, nose=1.8, ped=0.4,
     tail=dict(type="coel", len=0.24, span=1.0),
     dorsal=[dict(t0=0.55, t1=0.66, h=0.9, shape="tri"), dict(t0=0.28, t1=0.36, h=0.8, shape="lobe")],
     anal=[dict(t0=0.28, t1=0.36, h=0.8, shape="lobe")], pelvic=dict(t=0.48, h=0.7, shape="lobe"), pect=0.8,
     col=dict(back="#27375a", side="#35466b", belly="#465a7c", fin="#27375a"), shine=0.3,
     pat=[dict(type="spots", color="#c2cadb", scale=7, size=0.13, v0=0.1, v1=1.0, t0=0.05, t1=0.95)])


# ----------------------------------------------------------------------------- body math
def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


class Body:
    def __init__(self, p):
        self.p = p
        self.H = p["h"]
        self.W = p.get("w", 0.55)

    def f(self, t):
        p = self.p
        tp, ped = p["peak"], p["ped"]
        if t < tp:
            u = t / tp
            return ped + (1 - ped) * math.sin(u * math.pi / 2) ** p.get("taper", 1.0)
        u = (t - tp) / (1 - tp)
        nb = p["nose"]
        return max(0.0, 1 - u ** nb) ** (1 / 1.8)

    def x(self, t):
        return t - 0.5

    def zc(self, t):
        return self.p.get("mouth", 0.0) * self.H * smoothstep(self.p["peak"], 1.0, t)

    def ru(self, t):
        return self.H * self.f(t) * (1 + self.p.get("arch", 0.0))

    def rl(self, t):
        return self.H * self.f(t) * (1 - self.p.get("arch", 0.0) * 0.5)

    def ztop(self, t):
        return self.zc(t) + self.ru(t)

    def zbot(self, t):
        return self.zc(t) - self.rl(t)

    def half_w(self, t):
        hw = 1 + self.p.get("head_w", 0.0) * smoothstep(0.55, 0.95, t)
        return self.W * self.H * self.f(t) * hw


def build_body(b, mat):
    N, M = 44, 24
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    vuv = {}
    for i in range(N + 1):
        t = 0.0 + 0.995 * i / N
        ring = []
        for k in range(M):
            th = 2 * math.pi * k / M
            s, c = math.sin(th), math.cos(th)
            r = b.ru(t) if s >= 0 else b.rl(t)
            v = bm.verts.new((b.x(t), b.half_w(t) * c, b.zc(t) + r * s))
            vuv[v] = (t, 0.5 + 0.5 * s)
            ring.append(v)
        rings.append(ring)
    for i in range(N):
        for k in range(M):
            k2 = (k + 1) % M
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    tail_c = bm.verts.new((b.x(0) - 0.001, 0, b.zc(0)))
    vuv[tail_c] = (0.0, 0.5)
    nose_c = bm.verts.new((b.x(1.0), 0, b.zc(1.0)))
    vuv[nose_c] = (1.0, 0.5)
    for k in range(M):
        k2 = (k + 1) % M
        bm.faces.new((rings[0][k2], rings[0][k], tail_c))
        bm.faces.new((rings[-1][k], rings[-1][k2], nose_c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        for lp in f.loops:
            lp[uv].uv = vuv[lp.vert]
    ob = C.mesh_object("Body", bm, mat)
    C.set_smooth(ob)
    return ob


# ----------------------------------------------------------------------------- fins
FIN_SHAPES = {
    "tri": [(0, 0.15), (0.3, 0.4), (0.7, 0.85), (0.85, 1.0), (1.0, 0.0)],
    "round": [(0, 0.25), (0.15, 0.7), (0.45, 1.0), (0.8, 0.8), (1.0, 0.0)],
    "low": [(0, 0.55), (0.2, 0.8), (0.6, 0.95), (0.9, 1.0), (1.0, 0.0)],
    "sail": [(0, 0.2), (0.3, 0.55), (0.6, 0.8), (0.82, 1.0), (0.92, 0.95), (1.0, 0.0)],
    "falcate": [(0, 0.0), (0.2, 0.35), (0.45, 1.0), (0.55, 0.95), (1.0, 0.0)],
    "shark": [(0, 0.0), (0.12, 0.25), (0.3, 1.0), (0.45, 0.9), (1.0, 0.0)],
    "lobe": [(0, 0.3), (0.3, 0.9), (0.6, 1.0), (0.9, 0.6), (1.0, 0.0)],
}


def fin_outline(b, fin, top=True):
    t0, t1 = fin["t0"], fin["t1"]
    hh = fin["h"] * b.H
    rake = fin.get("rake", 0.15)
    shape = fin.get("shape", "tri")
    if shape == "spiny":
        pts = []
        n = 7
        for i in range(n + 1):
            u = i / n
            pts.append((u, 0.55 if i % 2 == 0 else 1.0))
        pts.append((1.0, 0.0))
        pts[0] = (0, 0.45)
    else:
        pts = FIN_SHAPES[shape]
    out = []
    dip = 0.12 * b.H
    sgn = 1 if top else -1
    zf = b.ztop if top else b.zbot
    # outline (back -> front)
    for u, hf in pts:
        t = t0 + u * (t1 - t0)
        x = b.x(t) - rake * hf * hh
        out.append((x, zf(t) + sgn * hf * hh))
    # base (front -> back), dipping into the body
    for i in range(6):
        t = t1 - (t1 - t0) * i / 5
        out.append((b.x(t), zf(t) - sgn * dip))
    if not top:
        out.reverse()
    return out


def tail_outline(b, tail):
    tl = tail["len"]
    ts = tail["span"] * b.H
    hb = b.p["ped"] * b.H * 0.95
    typ = tail["type"]
    if typ == "fork":
        n = tail.get("notch", 0.45)
        pts = [(0.03, hb), (-tl * 0.6, ts * 0.75), (-tl, ts), (-tl * 0.85, ts * 0.55),
               (-tl * n, 0.0), (-tl * 0.85, -ts * 0.55), (-tl, -ts), (-tl * 0.6, -ts * 0.75), (0.03, -hb)]
    elif typ == "deep":
        pts = [(0.03, hb), (-tl * 0.55, ts * 0.6), (-tl, ts), (-tl * 0.72, ts * 0.45),
               (-tl * 0.32, 0.0), (-tl * 0.72, -ts * 0.45), (-tl, -ts), (-tl * 0.55, -ts * 0.6), (0.03, -hb)]
    elif typ == "lunate":
        pts = [(0.03, hb), (-tl * 0.35, ts * 0.4), (-tl * 0.8, ts * 0.85), (-tl, ts), (-tl * 0.62, ts * 0.45),
               (-tl * 0.36, 0.0), (-tl * 0.62, -ts * 0.45), (-tl, -ts), (-tl * 0.8, -ts * 0.85),
               (-tl * 0.35, -ts * 0.4), (0.03, -hb)]
    elif typ == "square":
        pts = [(0.03, hb), (-tl * 0.8, ts * 0.9), (-tl, ts), (-tl * 0.95, 0.0), (-tl, -ts),
               (-tl * 0.8, -ts * 0.9), (0.03, -hb)]
    elif typ == "round":
        pts = [(0.03, hb)]
        for i in range(9):
            a = math.radians(70 - 140 * i / 8)
            pts.append((-tl * (0.55 + 0.45 * math.cos(a)), ts * math.sin(a)))
        pts.append((0.03, -hb))
    elif typ == "shark":
        pts = [(0.03, hb), (-tl * 0.7, ts * 0.8), (-tl, ts * 1.05), (-tl * 0.8, ts * 0.55),
               (-tl * 0.35, 0.0), (-tl * 0.6, -ts * 0.55), (-tl * 0.5, -ts * 0.7), (0.03, -hb)]
    elif typ == "clavus":
        pts = [(0.05, hb)]
        for i in range(11):
            a = math.radians(85 - 170 * i / 10)
            wob = 1.0 + 0.12 * math.cos(i * math.pi)
            pts.append((-tl * wob * (0.4 + 0.6 * math.cos(a) ** 0.5), hb * 1.05 * math.sin(a)))
        pts.append((0.05, -hb))
    elif typ == "coel":
        pts = [(0.03, hb), (-tl * 0.45, ts * 0.8), (-tl * 0.6, ts * 0.35), (-tl * 1.05, ts * 0.18),
               (-tl * 1.2, 0.0), (-tl * 1.05, -ts * 0.18), (-tl * 0.6, -ts * 0.35), (-tl * 0.45, -ts * 0.8),
               (0.03, -hb)]
    else:
        raise ValueError(typ)
    return pts


# ----------------------------------------------------------------------------- materials
def body_material(p, name="BodyMat"):
    col = p["col"]
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["UV"])
    t, v = s[0], s[1]
    bl = p.get("belly_line", 0.36)
    base = nb.ramp(v, [(0.0, C.lin(col["belly"])), (bl, C.lin(col["side"])), (0.72, C.lin(col["back"]))])
    for i, pat in enumerate(p.get("pat", [])):
        base = apply_pattern(nb, base, t, v, pat, p, i)
    shade = C.toon_shade(nb, p.get("shine", 0.0))
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, p.get("emit", 1.0))
    return m


def apply_pattern(nb, base, t, v, pat, p, idx):
    typ = pat["type"]
    col = C.lin(pat["color"])
    H = p["h"]
    seed = pat.get("seed", 0.0) + idx * 1.7 + (sum(map(ord, p.get("_id", ""))) % 97) * 0.13
    mask = None
    if typ == "bands":
        tt = t
        if pat.get("wave"):
            wv = nb.math("SINE", nb.math("MULTIPLY", v, 14.0))
            tt = nb.math("MULTIPLY_ADD", wv, pat["wave"] / pat["count"], t)
        fr = nb.math("FRACT", nb.math("MULTIPLY_ADD", tt, pat["count"], 0.37))
        mask = nb.math("LESS_THAN", fr, pat["width"])
    elif typ == "stripe":
        d = nb.math("ABSOLUTE", nb.math("SUBTRACT", v, pat["center"]))
        mask = nb.math("LESS_THAN", d, pat["width"])
    elif typ in ("spots", "scales"):
        vec = nb.combine(nb.math("MULTIPLY", t, 1.0), nb.math("MULTIPLY", v, 2.2 * H), seed)
        vo = nb.node("ShaderNodeTexVoronoi")
        vo.voronoi_dimensions = "3D"
        if typ == "scales":
            vo.feature = "DISTANCE_TO_EDGE"
        nb.link(vec, vo.inputs["Vector"])
        # keep cells at least ~3px wide so patterns survive the pixel grid
        cell_px = 3.0 if typ == "spots" else 3.5
        vo.inputs["Scale"].default_value = min(pat["scale"], p["px"] / cell_px)
        if "Randomness" in vo.inputs:
            vo.inputs["Randomness"].default_value = 0.8 if typ == "spots" else 0.25
        if typ == "spots":
            mask = nb.math("LESS_THAN", vo.outputs["Distance"], max(pat["size"], 0.24))
        else:
            mask = nb.math("LESS_THAN", vo.outputs["Distance"], max(pat["width"], 0.12))
            mask = nb.math("MULTIPLY", mask, pat.get("alpha", 1.0))
    elif typ == "mottle":
        vec = nb.combine(t, nb.math("MULTIPLY", v, 2.2 * H), seed)
        no = nb.node("ShaderNodeTexNoise")
        no.noise_dimensions = "3D"
        nb.link(vec, no.inputs["Vector"])
        no.inputs["Scale"].default_value = min(pat["scale"], p["px"] / 3.0)
        no.inputs["Detail"].default_value = 3.0
        mask = nb.math("GREATER_THAN", no.outputs["Fac"], pat["thresh"])
    elif typ == "blotch":
        dx = nb.math("SUBTRACT", t, pat["t"])
        dz = nb.math("MULTIPLY", nb.math("SUBTRACT", v, pat["v"]), 2.2 * H)
        d = nb.vmath("LENGTH", nb.combine(dx, dz, 0.0))
        mask = nb.math("LESS_THAN", d, pat["r"])
    elif typ == "region":
        soft = pat.get("soft", 0.0)
        if soft > 0:
            # dithered soft edge: compare t against a jittered threshold from a fine voronoi
            vo = nb.node("ShaderNodeTexWhiteNoise")
            vo.noise_dimensions = "2D"
            nb.link(nb.combine(nb.math("MULTIPLY", t, 60.0), nb.math("MULTIPLY", v, 20.0), 0), vo.inputs["Vector"])
            thr = nb.math("MULTIPLY_ADD", vo.outputs["Value"], soft, pat["t1"] - soft * 0.5)
            mask = nb.math("LESS_THAN", t, thr)
        else:
            mask = nb.in_range(t, pat["t0"], pat["t1"])
    else:
        raise ValueError(typ)
    if typ != "region":
        if "t0" in pat or "t1" in pat:
            mask = nb.math("MULTIPLY", mask, nb.in_range(t, pat.get("t0", -1), pat.get("t1", 2)))
    if "v0" in pat or "v1" in pat:
        mask = nb.math("MULTIPLY", mask, nb.in_range(v, pat.get("v0", -1), pat.get("v1", 2)))
    return nb.mix(mask, base, col)


def fin_material(p, name="FinMat"):
    col = p["col"]["fin"]
    spots = p.get("finspots")
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    base = nb.rgb(C.lin(col))
    if spots:
        tc = nb.node("ShaderNodeTexCoord")
        vo = nb.node("ShaderNodeTexVoronoi")
        nb.link(tc.outputs["Object"], vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = 26.0
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], 0.2)
        base = nb.mix(mask, base, C.lin(spots))
    # fins are lit a bit flatter and slightly translucent-looking (lighter)
    shade = C.toon_shade(nb, 0.0, stops=[(0.0, C.lin("#8a8aa8")), (0.4, C.lin("#e8e8f0"))])
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, p.get("emit", 1.0))
    return m


# ----------------------------------------------------------------------------- assembly
def build_fish(fid, p, frame, ppu=None):
    p = dict(p)
    p["_id"] = fid
    b = Body(p)
    bmat = body_material(p)
    fmat = fin_material(p)
    objs = [build_body(b, bmat)]
    if frame == 1:
        # swim frame: bend rear body slightly towards the camera
        me = objs[0].data
        for v in me.vertices:
            t = v.co.x + 0.5
            if t < 0.45:
                k = (0.45 - t) / 0.45
                v.co.y -= 0.06 * k * k
    # tail (pivot at peduncle so it can swing)
    tail = C.poly_object("Tail", tail_outline(b, p["tail"]), fmat, thickness=0.012)
    tail.location = (b.x(0.0), 0, b.zc(0.0))
    if frame == 1:
        tail.rotation_euler = (0, math.radians(-7), math.radians(58))
        tail.location.y -= 0.02
    objs.append(tail)
    for fin in p.get("dorsal", []):
        objs.append(C.poly_object("Dorsal", fin_outline(b, fin, True), fmat))
    for fin in p.get("anal", []):
        objs.append(C.poly_object("Anal", fin_outline(b, fin, False), fmat))
    pel = p.get("pelvic")
    if pel:
        t = pel["t"]
        fin = dict(t0=t - 0.07, t1=t + 0.02, h=pel["h"], shape=pel.get("shape", "tri"), rake=0.6)
        objs.append(C.poly_object("Pelvic", fin_outline(b, fin, False), fmat, y=-0.004))
    pect = p.get("pect", 0.0)
    if pect > 0:
        t = p.get("pect_t", min(0.78, p["peak"] + 0.12))
        L = pect * b.H * 1.3
        zc = b.zc(t) - 0.25 * b.rl(t)
        pts = [(0.0, 0.03 * L), (-L * 0.35, 0.05 * L), (-L * 0.95, -L * 0.25), (-L, -L * 0.42), (-L * 0.4, -L * 0.3),
               (0.0, -0.08 * L)]
        y = -b.half_w(t) * 0.92 - 0.004
        ob = C.poly_object("Pect", pts, fmat, thickness=0.006, y=0)
        ob.location = (b.x(t), y, zc)
        objs.append(ob)
    # eyes
    eye = p.get("eye", 0.11)
    if eye > 0:
        et = p.get("eye_t", 1 - (1 - p["peak"]) * 0.28)
        ez_list = [b.zc(et) + p.get("eye_v", 0.3) * b.ru(et)]
        if p.get("two_eyes"):
            ez_list.append(ez_list[0] + eye * b.H * 2.6)
        r = max(eye * b.H, 0.012)
        px = 1.0 / ppu if ppu else 0.0
        big = ppu is None or r * ppu >= 1.7
        for ez in ez_list:
            ey = -b.half_w(et) * 0.95 - r * 0.2
            if big:
                white = C.add_prim("sphere", "Eye", C.toon_material("EyeW", p.get("eye_col", "#f2e6c0"), flat=True),
                                   radius=r, location=(b.x(et), ey, ez), segments=12, ring_count=8)
                white.scale = (1, 0.3, 1)
                objs.append(white)
                pr = max(r * 0.62, px * 0.8)
            else:
                # tiny fish: a single dark pupil that is guaranteed to cover ~1-2 pixels
                pr = max(r * 0.8, px * 0.75)
            pupil = C.add_prim("sphere", "Pupil", C.toon_material("EyeP", "#0a0a12", flat=True),
                               radius=pr, location=(b.x(et) + r * 0.1, ey - r * 0.3, ez), segments=12, ring_count=8)
            pupil.scale = (1, 0.3, 1)
            objs.append(pupil)
    ml = p.get("mouth_line", 0.0)
    if ml > 0:
        tn = 0.995
        x1 = b.x(tn)
        zz = b.zc(tn) - 0.03 * b.H
        pts = [(x1 - ml * 0.7, zz - 0.01), (x1 + 0.005, zz + 0.02 * b.H), (x1 + 0.005, zz - 0.05 * b.H),
               (x1 - ml * 0.7, zz - 0.03)]
        mm = C.toon_material("Mouth", "#1a1014", flat=True)
        tmid = 1 - ml * 0.35
        objs.append(C.poly_object("Mouth", pts, mm, thickness=0.004, y=-b.half_w(tmid) * 0.9 - 0.006))
        if p.get("teeth"):
            tm = C.toon_material("Teeth", "#f4f0e0", flat=True)
            for k in range(4):
                tx = x1 - ml * 0.6 * (k + 0.5) / 4
                objs.append(C.poly_object("Tooth", [(tx - 0.015, zz), (tx + 0.015, zz), (tx, zz - 0.045)], tm,
                                          thickness=0.004, y=-b.half_w(tmid) * 0.9 - 0.01))
    for bar in p.get("barbels", []):
        at = bar.get("at", 0.985)
        x0, z0 = b.x(at), b.zc(at) + bar["z"] * b.H * b.f(at)
        ang = math.radians(bar["ang"])
        L = bar["len"]
        cur = bar.get("curve", 0.0)
        pts = []
        for k in range(6):
            s = k / 5
            a = ang + cur * s
            pts.append((x0 + math.cos(a) * L * s - 0.0 * s, -b.half_w(at) - 0.01, z0 + math.sin(a) * L * s))
        objs.append(C.tube_along("Barbel", pts, [0.009 * (1 - 0.5 * k / 5) for k in range(6)],
                                 C.toon_material("BarbelM", p["col"]["side"])))
    if p.get("bill"):
        L = p["bill"]
        tip = (b.x(1.0) + L, 0, b.zc(1.0))
        pts = [(b.x(0.96), 0, b.zc(0.96)), (b.x(1.0) + L * 0.5, 0, b.zc(1.0)), tip]
        objs.append(C.tube_along("Bill", pts, [b.H * 0.12, b.H * 0.06, 0.004],
                                 C.toon_material("BillM", p["col"]["back"])))
    if p.get("lure"):
        lu = p["lure"]
        t = 0.93
        base = (b.x(t), 0, b.ztop(t) - 0.01)
        L = lu["len"]
        pts = [base, (base[0] + L * 0.3, 0, base[2] + L * 0.45), (base[0] + L * 0.75, 0, base[2] + L * 0.5),
               (base[0] + L * 0.95, 0, base[2] + L * 0.3)]
        objs.append(C.tube_along("Stalk", pts, 0.012, C.toon_material("StalkM", p["col"]["side"])))
        objs.append(C.add_prim("sphere", "Bulb", C.glow_material("Glow", lu["col"], 1.4), radius=0.05,
                               location=(pts[-1][0], -0.02, pts[-1][2] - 0.02), segments=10, ring_count=6))
    fl = p.get("finlets")
    if fl:
        fm = C.toon_material("Finlet", fl["col"], flat=True)
        for k in range(fl["n"]):
            t = fl["t0"] + (fl["t1"] - fl["t0"]) * (k + 0.5) / fl["n"]
            w = (fl["t1"] - fl["t0"]) / fl["n"] * 0.8
            for top in (True, False):
                fin = dict(t0=t - w / 2, t1=t + w / 2, h=0.35, shape="tri", rake=0.4)
                objs.append(C.poly_object("Finlet", fin_outline(b, fin, top), fm, y=-0.003))
    return objs


def render_species(fid, frames=(0, 1)):
    p = F[fid]
    # measure pass: pixels-per-unit so that the whole fish (incl. tail/bill) is ~px wide
    C.clear_objects()
    objs = build_fish(fid, p, 0)
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    for fr in frames:
        C.clear_objects()
        objs = build_fish(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            render_species.frame0 = C.world_bounds(objs)
        # both frames share the frame-0 canvas so the sprite does not jump between frames
        fx0, fx1, fz0, fz1 = render_species.frame0
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        C.ortho_camera((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        C.render_sprite(os.path.join(OUT, f"{fid}_{fr}.png"))
    print("FISH", fid, "ok")


# ----------------------------------------------------------------------------- top view (underwater shadows)
TOP_USES_SIDE = {"flounder", "ocean_sunfish"}  # flat/tall fish read better with their broad side up


def build_fish_top(fid, p, frame, ppu):
    """Fish seen from above, head towards +X; frame 0/1 swing the tail to opposite sides."""
    p = dict(p)
    p["_id"] = fid
    p["w"] = p.get("w", 0.55) * 1.45  # chunkier than life so the underwater shadow reads at small sizes
    b = Body(p)
    bmat = body_material(p)
    fmat = fin_material(p)
    body = build_body(b, bmat)
    objs = [body]
    sgn = 1 if frame == 0 else -1
    for v in body.data.vertices:
        t = v.co.x + 0.5
        if t < 0.5:
            k = (0.5 - t) / 0.5
            v.co.y += sgn * 0.075 * k * k
    tail = C.poly_object("Tail", tail_outline(b, p["tail"]), fmat, thickness=0.012)
    tail.location = (b.x(0.0), sgn * 0.075, b.zc(0.0))
    tail.rotation_euler = (math.radians(90), 0, math.radians(sgn * 24))
    objs.append(tail)
    pect = max(p.get("pect", 0.0), 0.3)
    t = p.get("pect_t", min(0.78, p["peak"] + 0.12))
    L = pect * b.H * 1.5
    pts = [(0.0, 0.03 * L), (-L * 0.35, 0.05 * L), (-L * 0.95, -L * 0.25), (-L, -L * 0.42), (-L * 0.4, -L * 0.3),
           (0.0, -0.08 * L)]
    for side in (1, -1):
        ob = C.poly_object("Pect", pts, fmat, thickness=0.006)
        ob.location = (b.x(t), side * b.half_w(t) * 0.85, b.zc(t))
        ob.rotation_euler = (math.radians(90 if side > 0 else -90), 0, math.radians(side * 12))
        objs.append(ob)
    eye = p.get("eye", 0.11)
    if eye > 0:
        et = p.get("eye_t", 1 - (1 - p["peak"]) * 0.28)
        r = max(eye * b.H * 0.8, 0.8 / ppu)
        for side in (1, -1):
            e = C.add_prim("sphere", "Eye", C.toon_material("EyeP", "#0a0a12", flat=True), radius=r,
                           location=(b.x(et), side * b.half_w(et) * 0.8, b.zc(et) + b.ru(et) * 0.5), segments=10, ring_count=6)
            objs.append(e)
    for bar in p.get("barbels", []):
        at = bar.get("at", 0.985)
        x0, z0 = b.x(at), b.zc(at)
        L = bar["len"]
        for side in (1, -1):
            pts3 = []
            for k in range(6):
                s = k / 5
                a = math.radians(35 + 25 * s)
                pts3.append((x0 - math.cos(a) * L * s * 0.4 + L * 0.2 * s, side * (b.half_w(at) + math.sin(a) * L * s), z0))
            objs.append(C.tube_along("Barbel", pts3, [0.012 * (1 - 0.5 * k / 5) for k in range(6)],
                                     C.toon_material("BarbelM", p["col"]["side"])))
    if p.get("bill"):
        Lb = p["bill"]
        pts3 = [(b.x(0.96), 0, b.zc(0.96)), (b.x(1.0) + Lb * 0.5, 0, b.zc(1.0)), (b.x(1.0) + Lb, 0, b.zc(1.0))]
        objs.append(C.tube_along("Bill", pts3, [b.H * 0.12, b.H * 0.06, 0.006], C.toon_material("BillM", p["col"]["back"])))
    if p.get("lure"):
        lu = p["lure"]
        base = (b.x(0.93), 0, b.ztop(0.93))
        Ll = lu["len"]
        pts3 = [base, (base[0] + Ll * 0.4, 0, base[2] + 0.1), (base[0] + Ll * 0.9, 0, base[2])]
        objs.append(C.tube_along("Stalk", pts3, 0.014, C.toon_material("StalkM", p["col"]["side"])))
        objs.append(C.add_prim("sphere", "Bulb", C.glow_material("Glow", lu["col"], 1.4), radius=0.06,
                               location=pts3[-1], segments=10, ring_count=6))
    return objs


def bounds_xy(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    xs, ys = [], []
    for ob in objs:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        for v in me.vertices:
            w = ev.matrix_world @ v.co
            xs.append(w.x)
            ys.append(w.y)
        ev.to_mesh_clear()
    return min(xs), max(xs), min(ys), max(ys)


def top_camera(cx, cy, w, h, ppu):
    sc = bpy.context.scene
    cam = sc.camera
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(w, h) / ppu
    cam.location = (cx, cy, 30)
    cam.rotation_euler = (0, 0, 0)
    sc.render.resolution_x, sc.render.resolution_y = w, h


def render_species_top(fid):
    import shutil
    if fid in TOP_USES_SIDE:
        for fr in (0, 1):
            shutil.copyfile(os.path.join(OUT, f"{fid}_{fr}.png"), os.path.join(OUT, f"{fid}_t{fr}.png"))
        return
    p = F[fid]
    C.clear_objects()
    objs = build_fish_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    boxes = []
    for fr in (0, 1):
        C.clear_objects()
        objs = build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        boxes.append(bounds_xy(objs))
    bx0 = min(b[0] for b in boxes)
    bx1 = max(b[1] for b in boxes)
    by0 = min(b[2] for b in boxes)
    by1 = max(b[3] for b in boxes)
    pad = 2
    w = int(math.ceil((bx1 - bx0) * ppu)) + pad * 2
    h = int(math.ceil(2 * max(abs(by0), abs(by1)) * ppu)) + pad * 2
    h += h % 2  # spine exactly on the sprite centre so rotation pivots on the body axis
    for fr in (0, 1):
        C.clear_objects()
        build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        top_camera((bx0 + bx1) / 2, 0.0, w, h, ppu)
        C.render_sprite(os.path.join(OUT, f"{fid}_t{fr}.png"))
    C.ortho_camera(0, 0, 32, 32, 16)  # restore side camera orientation for later species


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    top_only = "--top" in argv
    argv = [a for a in argv if not a.startswith("--")]
    ids = argv or list(F.keys())
    C.reset_scene()
    if top_only:
        C.ortho_camera(0, 0, 32, 32, 16)
        for fid in ids:
            render_species_top(fid)
        C.contact_sheet([os.path.join(OUT, f"{i}_t{f}.png") for i in ids for f in (0, 1)],
                        os.path.join(C.TMP, "fish_top.png"), scale=4, cols=8)
        print("TOP done")
        return
    for fid in ids:
        render_species(fid)
        render_species_top(fid)
    small = [i for i in ids if F[i]["px"] <= 30]
    large = [i for i in ids if F[i]["px"] > 30]
    for name, group, sc, cols in (("fish_small.png", small, 7, 6), ("fish_large.png", large, 4, 5)):
        if group:
            paths = [os.path.join(OUT, f"{i}_{f}.png") for i in group for f in (0, 1)]
            C.contact_sheet(paths, os.path.join(C.TMP, name), scale=sc, cols=cols)
    print("SHEETS done")


if __name__ == "__main__":
    main()
