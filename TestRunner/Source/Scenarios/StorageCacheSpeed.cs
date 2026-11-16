using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class StorageCacheSpeed
    {
        const int Seed          = 14;
        const int Colonists     = 100;
        const int StoolSpacing  = 4;
        const int StockpileCols = 15;
        const int StockpileRows = 10;
        const int LooseItems    = 6000;
        const int WarmUpTicks   = 200;
        const int MeasuredTicks = 1500;

        static readonly string[] StoredDefs   = { "Steel", "WoodLog", "Silver", "Cloth", "ComponentIndustrial" };
        static readonly string[] UnstoredDefs = { "Plasteel", "Gold", "Uranium" };

        [Scenario("storage-cache-speed", MapSize = 200, MaxSeconds = 165)]
        static void Speed(ScenarioContext s) => RunSpeed(s);

        [Scenario("storage-cache-speed-puah", MapSize = 200, MaxSeconds = 165, Mods = "puah-continued")]
        static void SpeedWithPuah(ScenarioContext s) => RunSpeed(s);

        static void RunSpeed(ScenarioContext s) {
            Rand.PushState(Seed);
            try {
                var rng        = new System.Random(Seed);
                var taken      = new HashSet<IntVec3>();
                var storedCells = 0;

                for (var col = 0; col < StockpileCols; col++)
                    for (var row = 0; row < StockpileRows; row++) {
                        var center = s.Cell((col - 7) * 9, (row - 5) * 9);
                        var rect   = CellRect.CenteredOn(center, 2);
                        var zone   = s.Stockpile(rect, StoredDefs[(col + row) % StoredDefs.Length], StoredDefs[(col + row + 1) % StoredDefs.Length]);
                        storedCells += zone.cells.Count;
                        foreach (var c in rect) taken.Add(c);
                    }

                IntVec3 FreeCell(int rx = 66, int rz = 46) {
                    while (true) {
                        var c = s.Cell(rng.Next(-rx, rx + 1), rng.Next(-rz, rz + 1));
                        if (taken.Add(c)) return c;
                    }
                }

                var haulables = 0;
                var stools = 0;
                for (var i = 0; i < LooseItems; i++) {
                    var defs = i % 4 == 0 ? UnstoredDefs : StoredDefs;
                    s.SpawnItem(defs[rng.Next(defs.Length)], 15, FreeCell());
                    haulables++;
                }

                for (var x = -96; x <= 96; x += StoolSpacing)
                    for (var z = -96; z <= 96; z += StoolSpacing) {
                        var cell = s.Cell(x, z);
                        if (!taken.Add(cell)) continue;
                        Opportunity.DesignateDeconstruct(s, cell);
                        stools++;
                    }

                for (var i = 0; i < Colonists; i++)
                    s.SpawnColonist(FreeCell(90, 90));

                s.RecordJobs = false;
                s.Tick(WarmUpTicks);
                var before    = s.TickMilliseconds;
                var cpuBefore = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                s.Tick(MeasuredTicks);
                var elapsed = s.TickMilliseconds - before;
                var cpu     = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds - cpuBefore;

                s.Metric("stools_placed", stools);
                s.Metric("stools_left", s.Map.listerThings.ThingsOfDef(ScenarioContext.Def("Stool")).Count);
                s.Metric("ticks", MeasuredTicks);
                s.Metric("tick_ms", elapsed);
                s.Metric("ticks_per_ms", MeasuredTicks / elapsed);
                s.Metric("process_cpu_ms", cpu);
                s.Metric("colonists", Colonists);
                s.Metric("storage_cells", storedCells);
                s.Metric("loose_items", haulables);
            }
            finally {
                Rand.PopState();
            }
        }
    }
}
