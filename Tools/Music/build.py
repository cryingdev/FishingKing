"""
Build the game's BGM: every module in Tools/Music/cues/ defines songs() -> [Song, ...] (fk_music.Song).

    python Tools/Music/build.py                 # all cues: midi + render + ogg + manifest + reports
    python Tools/Music/build.py stage_lake title   # only the cues whose id or module name matches
    python Tools/Music/build.py --lint          # no render: print each cue's lint (fast)
    python Tools/Music/build.py --describe stage_lake   # print the text piano roll

Reports land in Tools/Music/_tmp/reports/<cue>.txt (piano roll + lint + levels).
"""

import importlib.util
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fk_music  # noqa: E402


def modules():
    d = os.path.join(HERE, "cues")
    for fn in sorted(os.listdir(d)):
        if fn.endswith(".py") and not fn.startswith("_"):
            spec = importlib.util.spec_from_file_location("cue_" + fn[:-3], os.path.join(d, fn))
            m = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(m)
            yield fn[:-3], m


def main(argv):
    lint = "--lint" in argv
    describe = "--describe" in argv
    picks = [a for a in argv if not a.startswith("--")]
    if not lint and not describe and not fk_music.tools_ok():
        sys.exit("fluidsynth / oggenc / the GM SoundFont are missing (see Tools/Music/README.md)")
    results, failed = [], []
    for modname, m in modules():
        for song in m.songs():
            if picks and not any(p == song.cue or p == modname for p in picks):
                continue
            if lint:
                print("\n".join(song.lint()))
                continue
            if describe:
                print(song.describe())
                continue
            t0 = time.time()
            try:
                rep = song.build()
            except Exception as e:  # keep going: report every failure at the end
                failed.append((song.cue, repr(e)))
                print(f"FAIL {song.cue}: {e}")
                continue
            rep["build_s"] = round(time.time() - t0, 1)
            results.append(rep)
            stems = "  ".join(f"{k}: {v['rms_db']} dB rms / {v['peak_db']} pk"
                              + (f" seam {v['seam_ratio']}" if "seam_ratio" in v else "")
                              + (f" {v['ogg_kb']} KB" if "ogg_kb" in v else "")
                              for k, v in rep["stems"].items())
            print(f"{song.cue:24s} {rep['kind']:5s} {rep['seconds']:7.2f} s  gain {rep['gain_db']:+.1f} dB  {stems}")
    if results:
        os.makedirs(fk_music.TMP, exist_ok=True)
        with open(os.path.join(fk_music.TMP, "build_report.json"), "w", encoding="utf-8") as f:
            json.dump(results, f, indent=1, ensure_ascii=False)
    if failed:
        print("\nFAILED:\n" + "\n".join(f"  {c}: {e}" for c, e in failed))
        sys.exit(1)


if __name__ == "__main__":
    main(sys.argv[1:])
