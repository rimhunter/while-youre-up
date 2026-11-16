using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class TierUp
    {
        [Scenario("opportunity-job-report-still-says-on-the-way-to-after-thousands-of-reads", MapSize = 80)]
        static void ReportSurvivesTierUp(ScenarioContext s) {
            var l = Opportunity.SetupOnRoute(s, pawnX: -15, itemX: -5, itemZ: 3, zoneX: 8, jobX: 25);
            var hauling = s.RunUntil(() => l.Pawn.CurJobDef == JobDefOf.HaulToCell, 2500);
            s.Check(hauling, "the pawn should start hauling the steel on the way; jobs were: " + Opportunity.Sequence(s, l.Pawn));
            if (!hauling) return;

            var first = l.Pawn.GetJobReport();
            s.Check(first.Contains("on the way to"), "the haul's job report should say \"on the way to\" at first, was: " + first);

            var reads = 0;
            var clock = Stopwatch.StartNew();
            string report = null;
            while (clock.ElapsedMilliseconds < 4000) {
                for (var i = 0; i < 500; i++) {
                    report = l.Pawn.GetJobReport();
                    reads++;
                }
                System.Threading.Thread.Sleep(10);
            }
            s.Check(report.Contains("on the way to"), "after " + reads + " reads the haul's job report should still say \"on the way to\", was: " + report);

            Opportunity.RunToJobTarget(s, l);
            Opportunity.CheckHauledThenDidJob(s, l);
            var haul = s.JobsOf(l.Pawn).First(j => j.Def == "HaulToCell");
            s.Check(haul.Reports.Last().Contains("on the way to"), "the haul's last job report should say \"on the way to\", reports were: " + string.Join(" | ", haul.Reports));
        }
    }
}
