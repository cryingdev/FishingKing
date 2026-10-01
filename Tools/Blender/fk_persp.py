"""
FishingKing - shared behind-the-angler perspective camera.

World (Blender): X right, Y forward (distance from the angler), Z up, water surface at Z = 0.
The angler's feet are at (0, 0, standH). The camera sits behind/above the feet and is identical
in Unity (FishingKing.Persp), so everything Unity places on the water lines up with the renders.
"""
import os
import json
import math
import bpy

CAM_BACK = 10.5    # metres behind the feet
CAM_UP = 4.75      # metres above the feet
PITCH = 12.0       # degrees looking down
F_PX = 520.0       # focal length in pixels for a 400 px tall image
W, H, PPU = 640, 400, 16
DATA = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Data"))


def stage_canvas(stage_id):
    """(widthPx, heightPx) of a stage's layers, from the game's Data/stage_<id>.json: 640 x 400, or wider for a stage
    rendered with OVERSCAN (the sea: 800, the same camera and focal length, so the extra columns are just more of the
    same view, for the game's camera to pan over beyond its 480 px frame). FK_CANVAS_W=640 in the environment forces
    the 640 layout (build_overscan.ps1 renders it too and splices its back layer into the wide one's centre)."""
    force = os.environ.get("FK_CANVAS_W")
    if force:
        return int(force), 400
    try:
        with open(os.path.join(DATA, "stage_%s.json" % stage_id), encoding="utf-8") as f:
            d = json.load(f)
        return int(d.get("widthPx") or 640), int(d.get("heightPx") or 400)
    except (OSError, ValueError):
        return 640, 400


def set_canvas(width, height):
    """The canvas the stage camera renders (W x H); the projection keeps F_PX and the centre, so a wider canvas only
    adds columns on both sides. hyb_core.set_canvas calls this and updates its own copies."""
    global W, H
    W, H = int(width), int(height)


def cam_pos(stand_h):
    return (0.0, -CAM_BACK, stand_h + CAM_UP)


def setup_camera(stand_h, width=None, height=None):
    width = W if width is None else width
    height = H if height is None else height
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None or cam.name != "PerspCam":
        cd = bpy.data.cameras.new("PerspCam")
        cam = bpy.data.objects.new("PerspCam", cd)
        bpy.context.scene.collection.objects.link(cam)
        sc.camera = cam
    cd = cam.data
    cd.type = "PERSP"
    cd.sensor_fit = "VERTICAL"
    cd.sensor_height = 24.0
    cd.lens = F_PX * 24.0 / H
    cd.clip_start = 0.1
    cd.clip_end = 5000
    cam.location = cam_pos(stand_h)
    cam.rotation_euler = (math.radians(90 - PITCH), 0, 0)
    sc.render.resolution_x = width
    sc.render.resolution_y = height
    return cam


def project(p, stand_h):
    """World point -> pixel offset from the image centre (x right, y up). Mirrors Persp.cs."""
    cx, cy, cz = cam_pos(stand_h)
    rx, ry, rz = p[0] - cx, p[1] - cy, p[2] - cz
    s, c = math.sin(math.radians(PITCH)), math.cos(math.radians(PITCH))
    zc = ry * c - rz * s          # forward
    yc = ry * s + rz * c          # up
    return F_PX * rx / zc, F_PX * yc / zc, zc


def reflect_point(x, y, z, stand_h):
    """Where the mirror image of (x, y, z) appears on the water plane (for fake reflections)."""
    cx, cy, cz = cam_pos(stand_h)
    t = cz / (cz + max(z, 0.0))
    return cx + t * (x - cx), cy + t * (y - cy)
