using System.Collections.Generic;
using UnityEngine;

namespace WyuTestRunner
{
    static class LogErrorSink
    {
        static readonly List<string> errors = new List<string>();
        static readonly object gate = new object();
        static bool hooked;

        public static void Hook() {
            if (hooked) return;
            hooked = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string condition, string stackTrace, LogType type) {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            lock (gate) errors.Add(condition);
        }

        public static int Count { get { lock (gate) return errors.Count; } }

        public static IReadOnlyList<string> Since(int index) {
            lock (gate) return errors.GetRange(index, errors.Count - index);
        }
    }
}
