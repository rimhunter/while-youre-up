using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WyuTestRunner
{
    public sealed class DrawnText
    {
        public string Text;
        public Rect   Box;
        public Rect   Ink;
        public bool   InScrollView;
        public bool   Overflows;

        public override string ToString() => $"\"{Text}\" at ({Ink.x:0},{Ink.y:0} {Ink.width:0}x{Ink.height:0})";
    }

    static class UiRecorder
    {
        static bool recording;
        static int  scrollDepth;
        static readonly List<DrawnText> texts = new List<DrawnText>();

        public static void Patch(Harmony harmony) {
            harmony.Patch(AccessTools.Method(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }),
                prefix: new HarmonyMethod(typeof(UiRecorder), nameof(OnLabel)));
            harmony.Patch(AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView)),
                prefix: new HarmonyMethod(typeof(UiRecorder), nameof(OnBeginScroll)));
            harmony.Patch(AccessTools.Method(typeof(Widgets), nameof(Widgets.EndScrollView)),
                prefix: new HarmonyMethod(typeof(UiRecorder), nameof(OnEndScroll)));
        }

        public static void Begin() {
            texts.Clear();
            scrollDepth = 0;
            recording   = true;
        }

        public static List<DrawnText> End() {
            recording = false;
            return new List<DrawnText>(texts);
        }

        static void OnBeginScroll() => scrollDepth++;
        static void OnEndScroll() => scrollDepth--;

        static void OnLabel(Rect position, string text, GUIStyle style) {
            if (!recording || string.IsNullOrEmpty(text) || Event.current?.type != EventType.Repaint) return;
            texts.Add(new DrawnText {
                Text         = text,
                Box          = ToScreen(position),
                Ink          = ToScreen(InkRect(position, text, style ?? GUI.skin.label, out var overflows)),
                InScrollView = scrollDepth > 0,
                Overflows    = overflows,
            });
        }

        // Waylith's GUIToScreenPoint adds the group offset after scaling; the backend's device point is what the frame shows.
        static Rect ToScreen(Rect r) {
#if RW_1_7
            var a = UnityStubs.Gui.GuiBackend.Current.GuiToDevicePoint(new Vector2(r.xMin, r.yMin));
            var b = UnityStubs.Gui.GuiBackend.Current.GuiToDevicePoint(new Vector2(r.xMax, r.yMax));
#else
            var a = UnityToDevice(new Vector2(r.xMin, r.yMin));
            var b = UnityToDevice(new Vector2(r.xMax, r.yMax));
#endif
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

#if RW_1_6
        // Unity's GUIToScreenPoint scales the point by GUI.matrix but adds the window and group offsets unscaled (measured on 1.6).
        static Vector2 UnityToDevice(Vector2 p) {
            var m = GUI.matrix;
            if (m.m01 != 0 || m.m10 != 0 || m.m03 != 0 || m.m13 != 0)
                throw new System.InvalidOperationException("the UI recorder handles only a pure scale GUI.matrix, got " + m);
            var u = GUIUtility.GUIToScreenPoint(p);
            return new Vector2(m.m00 * (u.x - (m.m00 - 1) * p.x), m.m11 * (u.y - (m.m11 - 1) * p.y));
        }
#endif

        static Rect InkRect(Rect box, string text, GUIStyle style, out bool overflows) {
            var content = new GUIContent(text);
            var size    = style.CalcSize(content);
            float w = size.x, h = size.y;
            if (style.wordWrap && w > box.width) {
                w = box.width;
                h = style.CalcHeight(content, box.width);
            }
            overflows = w > box.width + 0.5f || h > box.height + 0.5f;
            w = Mathf.Min(w, box.width);
            var a = style.alignment;
            var x = a is TextAnchor.UpperLeft or TextAnchor.MiddleLeft or TextAnchor.LowerLeft ? box.x
                  : a is TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight ? box.xMax - w
                  : box.x + (box.width - w) / 2;
            var y = a is TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight ? box.y
                  : a is TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight ? box.yMax - h
                  : box.y + (box.height - h) / 2;
            return new Rect(x, y, w, h);
        }
    }
}
