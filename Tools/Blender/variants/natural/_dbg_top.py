import sys, os
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_fish as NF
from nat_core import bpy
import fk_fish as FF
NF.N.new_scene()
for fid in ("largemouth_bass", "northern_pike"):
    p = NF.natural_params(fid, FF.F[fid])
    NF._cur.clear(); NF._cur.update(p=p, top=True, tones=NF.top_tones(fid, p))
    NF.clear_meshes()
    objs = NF.build_top(fid, p, 0, 20.0)
    bpy.context.view_layer.update()
    body = objs[0]
    ys = [v.co.y for v in body.data.vertices]
    print("DBG", fid, "W", p.get("w"), "h", p["h"], "body y range", min(ys), max(ys), "bounds", FF.bounds_xy(objs))
