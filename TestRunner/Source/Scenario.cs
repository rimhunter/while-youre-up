using System;

namespace WyuTestRunner
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ScenarioAttribute : Attribute
    {
        public ScenarioAttribute(string name) { Name = name; }

        public string Name { get; }
        public int    MapSize    { get; set; } = 50;
        public int    MaxSeconds { get; set; } = 90;
        public bool   SelfTest   { get; set; }
        public string Mods       { get; set; }
        public bool   KeepMapNatural { get; set; }
        public bool   SeesPlayerLog  { get; set; }
        public bool   Windowed       { get; set; }
        public bool   WindowedOnUnity { get; set; }
        public string OnlyOn         { get; set; }
#if RW_1_6
        public bool   NeedsWindow    => Windowed || WindowedOnUnity;
#else
        public bool   NeedsWindow    => Windowed;
#endif
        public string StageMods      { get; set; }
    }

    public sealed class ScenarioFailure : Exception
    {
        public ScenarioFailure(string message) : base(message) { }
    }
}
