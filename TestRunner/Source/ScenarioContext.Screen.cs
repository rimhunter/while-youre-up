using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WyuTestRunner
{
    public sealed partial class ScenarioContext
    {
        internal readonly List<string> ScreenshotFiles = new List<string>();
        bool adaptiveTraining;

        internal void PrepareScreen() {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            if (Current.Game.CurrentMap != Map) Current.Game.CurrentMap = Map;
            CameraJumper.TryHideWorld();
            adaptiveTraining = Prefs.AdaptiveTrainingEnabled;
            Prefs.AdaptiveTrainingEnabled = false;
            Find.Selector.ClearSelection();
            CloseWindows();
        }

        internal void LeaveScreen() {
            Find.Selector.ClearSelection();
            CloseWindows();
            Prefs.AdaptiveTrainingEnabled = adaptiveTraining;
        }

        static void CloseWindows() {
            foreach (var w in Find.WindowStack.Windows.ToList())
                Find.WindowStack.TryRemove(w, doCloseSound: false);
        }

        public void SelectOnly(Thing thing) {
            Find.Selector.ClearSelection();
            Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
        }

        public void LookAt(IntVec3 center, float cellsAcross) {
            var aspect = (float)UnityEngine.Screen.width / UnityEngine.Screen.height;
            var size   = cellsAcross / aspect / 2f;
            Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), size);
            Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), size * cellsAcross / CellsAcross(center));
            Metric("cells_across", CellsAcross(center));
        }

        float CellsAcross(IntVec3 center) => UnityEngine.Screen.width * 10f / Mathf.Abs(ScreenPixelOf(center + IntVec3.East * 10).x - ScreenPixelOf(center).x);

        // Unity allows GUI calls only inside OnGUI, which runs only in a windowed game; Waylith (1.7) allows them anywhere.
        public static IEnumerator InGuiPass(Action body) {
            var go   = new GameObject("WyuTestRunner.GuiPass");
            var pass = go.AddComponent<GuiPass>();
            pass.Body = body;
            var clock = Stopwatch.StartNew();
            try {
                while (!pass.Done) {
                    if (clock.Elapsed.TotalSeconds > 10) throw new ScenarioFailure("the game ran no OnGUI pass within 10 s; GUI code needs a windowed launch on 1.6");
                    yield return null;
                }
            }
            finally {
                UnityEngine.Object.Destroy(go);
            }
            if (pass.Error is not null) throw new ScenarioFailure("the GUI pass threw: " + pass.Error);
        }

        sealed class GuiPass : MonoBehaviour
        {
            public Action Body;
            public bool Done;
            public Exception Error;

            void OnGUI() {
                if (Done) return;
                Done = true;
                var saved = Event.current;
                try { Body(); }
                catch (Exception e) { Error = e; }
                finally { Event.current = saved; }
            }
        }

        public static IEnumerator Frames(int count) {
            for (var i = 0; i < count; i++) yield return null;
        }

        public Vector2 ScreenPixelOf(IntVec3 cell) => ScreenPixelOf(cell.ToVector3Shifted());
        public Vector2 ScreenPixelOf(Vector3 mapPos) => mapPos.MapToUIPosition() * Prefs.UIScale;

        public Shot Screenshot(string name) {
            var dir = Environment.GetEnvironmentVariable("WYU_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("environment variable WYU_SCREENSHOT_DIR is required to take a screenshot and is not set");
            return new Shot(this, Path.Combine(dir, name + ".png"));
        }

        public sealed class Shot : IEnumerator
        {
            const int SettleFrames = 30;
            const int WriteTimeoutSeconds = 20;

            readonly ScenarioContext s;
            readonly IEnumerator body;

            public string Path { get; }
            public Png Image { get; private set; }
            public IReadOnlyList<DrawnText> Texts { get; private set; } = new List<DrawnText>();

            internal Shot(ScenarioContext s, string path) {
                this.s = s;
                Path   = path;
                body   = Take();
            }

            public object Current => body.Current;
            public bool MoveNext() => body.MoveNext();
            public void Reset() => throw new NotSupportedException();

            IEnumerator Take() {
                if (File.Exists(Path)) throw new ScenarioFailure("screenshot " + Path + " already exists");
                CameraJumper.TryHideWorld();
                foreach (var p in s.Map.mapPawns.AllPawnsSpawned)
                    p.Drawer.tweener.ResetTweenedPosToRoot();
                for (var i = 0; i < SettleFrames; i++) yield return null;
                UiRecorder.Begin();
                yield return null;
                Texts = UiRecorder.End();
                s.Check(Find.CurrentMap == s.Map && !WorldRendererUtility.WorldRendered, "the screenshot should show the scenario's map, not the world or another map");
                ScreenCapture.CaptureScreenshot(Path);
                var clock = Stopwatch.StartNew();
                byte[] data = null;
                while (data is null) {
                    yield return null;
                    if (File.Exists(Path)) {
                        var bytes = File.ReadAllBytes(Path);
                        if (Png.IsComplete(bytes)) data = bytes;
                    }
                    if (data is null && clock.Elapsed.TotalSeconds > WriteTimeoutSeconds)
                        throw new ScenarioFailure($"the game did not write the screenshot {Path} within {WriteTimeoutSeconds} s");
                }
                Image = Png.Decode(data);
                s.ScreenshotFiles.Add(Path);
                s.Check(Image.Width == UnityEngine.Screen.width && Image.Height == UnityEngine.Screen.height,
                    $"screenshot {Path} is {Image.Width}x{Image.Height}, the screen is {UnityEngine.Screen.width}x{UnityEngine.Screen.height}");
                var colors = new HashSet<(byte, byte, byte)>();
                for (var y = 0; y < Image.Height; y += Math.Max(1, Image.Height / 24))
                    for (var x = 0; x < Image.Width; x += Math.Max(1, Image.Width / 32))
                        colors.Add(Image.Pixel(x, y));
                s.Check(colors.Count >= 16, $"screenshot {Path} looks blank: {colors.Count} distinct colors in a 32x24 sample");
                s.Check(Texts.Count > 0, $"no text was drawn on screen while {Path} was taken");
            }

            public float FractionAlong(Vector2 from, Vector2 to, Func<(byte r, byte g, byte b), bool> color, float t0 = 0.25f, float t1 = 0.75f, int samples = 21, int radius = 6) {
                var hits = 0;
                for (var i = 0; i < samples; i++) {
                    var p = Vector2.Lerp(from, to, Mathf.Lerp(t0, t1, i / (float)(samples - 1)));
                    if (AnyNear(p, radius, color)) hits++;
                }
                return hits / (float)samples;
            }

            public int CountIn(Rect area, Func<(byte r, byte g, byte b), bool> color) {
                var n = 0;
                for (var y = Mathf.Max(0, (int)area.yMin); y < Mathf.Min(Image.Height, (int)area.yMax); y++)
                    for (var x = Mathf.Max(0, (int)area.xMin); x < Mathf.Min(Image.Width, (int)area.xMax); x++)
                        if (color(Image.Pixel(x, y))) n++;
                return n;
            }

            bool AnyNear(Vector2 p, int radius, Func<(byte r, byte g, byte b), bool> color) {
                for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++) {
                        int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                        if (x >= 0 && y >= 0 && x < Image.Width && y < Image.Height && color(Image.Pixel(x, y))) return true;
                    }
                return false;
            }

            public bool ShowsText(string words) => Texts.Any(t => t.Text.Contains(words));

            public string TextsMatching(string words) {
                var hits = Texts.Where(t => t.Text.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0).Select(t => t.ToString()).ToList();
                return hits.Count == 0 ? "(no drawn text contains \"" + words + "\"; " + Texts.Count + " texts drawn)" : string.Join(" | ", hits);
            }
        }
    }
}
