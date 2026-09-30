using System.Collections;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto legcool (with -fkscene Fishing -fkstage &lt;stage&gt; and the -fksave of an -fkencplay coolsave run, no
    /// -fkencounter): the saved legend cooldown survived the relaunch. Checks the stage's first legend is still away for
    /// no more than its cooldown, then back once it has run out, then that a far-future end (a clock set back) is capped
    /// at the cooldown's length.
    /// </summary>
    public partial class AutoPilot
    {
        IEnumerator LegendCoolTest()
        {
            yield return new WaitForSecondsRealtime(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Watch == null)
            {
                Log("CHECK FAIL no legend watch (use -fkscene Fishing -fkstage <stage with a legend>)");
                Application.Quit();
                yield break;
            }
            var sp = ctl.Watch.Legends[0];
            var rec = Game.I.FindLegend(sp.id);
            long left = LegendWatch.Remaining(sp.id);
            int len = rec?.coolLen ?? 0;
            Log($"legend cooldown test: {sp.id} {left}s left of {len}s (until {rec?.coolUntil ?? 0}, now {SaveSystem.Now})");
            int fails = 0;
            void Check(string what, bool ok)
            {
                if (!ok) fails++;
                Log($"CHECK {(ok ? "PASS" : "FAIL")} {what}");
            }
            Check($"still away after the relaunch ({left}s left, away {ctl.Watch.AwayOf(sp)}, blocked '{ctl.Watch.Blocked}')",
                rec != null && left > 0 && left <= len && ctl.Watch.AwayOf(sp) && ctl.Watch.Away);
            float t = 0f;
            while (ctl.Watch.AwayOf(sp) && t < len + 5f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Check($"back after {t:0.0}s (expected ~{left}s; away {ctl.Watch.AwayOf(sp)})", !ctl.Watch.AwayOf(sp) && Mathf.Abs(t - left) <= 2f);
            if (rec != null)
            {
                // the device clock set back a day: the end a day off, capped at the cooldown's own length
                rec.coolLen = 60;
                rec.coolUntil = SaveSystem.Now + 86400;
                long capped = LegendWatch.Remaining(sp.id);
                Check($"a clock set back is capped ({capped}s left, until {rec.coolUntil - SaveSystem.Now}s from now)", capped > 0 && capped <= 60 && rec.coolUntil - SaveSystem.Now <= 60);
                rec.coolUntil = rec.coolLen = 0;
                Game.I.Save();
            }
            Log($"legend cooldown test done: {fails} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
