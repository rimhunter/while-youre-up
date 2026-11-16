using System;
using System.Collections.Generic;
using System.Linq;

namespace WyuTestRunner
{
    sealed class RunConfig
    {
        public string ResultFile { get; private set; }
        public string PlayerLog  { get; private set; }
        public List<string> Selected { get; private set; }
        public bool SelectAll { get; private set; }

        public static RunConfig FromEnvironment() {
            var c = new RunConfig {
                ResultFile = Require("WYU_RESULT_FILE"),
                PlayerLog  = Require("WYU_PLAYER_LOG"),
            };
            var sel = Require("WYU_SCENARIOS");
            c.SelectAll = sel == "all";
            c.Selected  = c.SelectAll ? new List<string>() : sel.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            if (!c.SelectAll && c.Selected.Count == 0) throw new InvalidOperationException("WYU_SCENARIOS selects no scenario");
            return c;
        }

        static string Require(string name) {
            var v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v)) throw new InvalidOperationException($"environment variable {name} is required by the scenario runner and is not set");
            return v;
        }
    }
}
