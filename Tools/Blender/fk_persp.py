"""
FishingKing - shared behind-the-angler perspective camera.

World (Blender): X right, Y forward (distance from the angler), Z up, water surface at Z = 0.
The angler's feet are at (0, 0, standH). The camera sits behind/above the feet and is identical
in Unity (FishingKing.Persp), so everything Unity places on the water lines up with the renders.
"""
import math
import bpy

CAM_BACK = 10.5    # metres behind the feet
CAM_UP = 4.75      # metres above the feet
PITCH = 12.0       # degrees looking down
F_PX = 520.0       # focal length in pixels for a 400 px tall image
W, H, PPU = 640, 400, 16


def cam_pos(stand_h):
    return (0.0, -CAM_BACK, stand_h + CAM_UP)


def setup_camera(stand_h, width=W, height=H):
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
