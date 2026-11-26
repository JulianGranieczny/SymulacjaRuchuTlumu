using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace HospitalSim
{
    //rysuje edytor dowolnych ustawien przez refleksje kazde pole dostaje swoja kontrolke
    //nowe pole w klasie ustawien pojawia sie tu samo wystarczy dopisac polska nazwe w labels
    public class SettingsGui
    {
        readonly HashSet<string> open = new HashSet<string>();
        readonly Dictionary<string, string> textBuf = new Dictionary<string, string>();
        public bool Changed;

        const float LabelW = 230f;

        //wszystkie pola obiektu jako zwijane sekcje
        public void DrawSections(object root)
        {
            foreach (var f in Fields(root.GetType()))
            {
                var v = f.GetValue(root);
                if (v == null) continue;
                string path = f.Name;
                bool isOpen = open.Contains(path);
                if (GUILayout.Button((isOpen ? "▼ " : "► ") + Labels.Field(f.Name), GUI.skin.box, GUILayout.ExpandWidth(true)))
                {
                    if (isOpen) open.Remove(path); else open.Add(path);
                }
                if (!open.Contains(path)) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                object nv = DrawFields(v, path, 1);
                if (f.FieldType.IsValueType) f.SetValue(root, nv);
                GUILayout.EndVertical();
            }
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var c = GUI.color; GUI.color = new Color(0.8f, 0.9f, 1f);
                GUILayout.Label(GUI.tooltip, GUI.skin.box);
                GUI.color = c;
            }
        }

        //pola jednego obiektu bez sekcji
        public object DrawObject(object obj, string path) { return DrawFields(obj, path, 1); }

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                if (f.IsDefined(typeof(HideInInspector), false)) continue;
                yield return f;
            }
        }

        object DrawFields(object obj, string path, int depth)
        {
            foreach (var f in Fields(obj.GetType()))
            {
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                var range = f.GetCustomAttribute<RangeAttribute>();
                var min = f.GetCustomAttribute<MinAttribute>();
                var label = new GUIContent(Labels.Field(f.Name), tip != null ? tip.tooltip : "");
                object v = f.GetValue(obj);
                object nv = DrawValue(label, v, f.FieldType, path + "." + f.Name, range, min, depth);
                f.SetValue(obj, nv);
            }
            return obj;
        }

        object DrawValue(GUIContent label, object v, Type t, string path, RangeAttribute range, MinAttribute min, int depth)
        {
            if (t == typeof(bool))
            {
                bool b = (bool)v;
                bool nb = GUILayout.Toggle(b, label);
                if (nb != b) Changed = true;
                return nb;
            }
            if (t == typeof(int))
            {
                int i = (int)v;
                int ni = range != null ? IntRange(label, i, (int)range.min, (int)range.max, path) : (int)NumberRow(label, i, path, true);
                if (min != null) ni = Mathf.Max((int)min.min, ni);
                if (ni != i) Changed = true;
                return ni;
            }
            if (t == typeof(uint))
            {
                uint u = (uint)v;
                double d = NumberRow(label, u, path, true);
                uint nu = (uint)Math.Max(0, Math.Min(uint.MaxValue, d));
                if (nu != u) Changed = true;
                return nu;
            }
            if (t == typeof(float))
            {
                float x = (float)v;
                float nx = range != null ? FloatRange(label, x, range.min, range.max, path) : (float)NumberRow(label, x, path, false);
                if (min != null) nx = Mathf.Max(min.min, nx);
                if (!Mathf.Approximately(nx, x)) Changed = true;
                return nx;
            }
            if (t == typeof(string))
            {
                string s = (string)v ?? "";
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(LabelW));
                string ns = GUILayout.TextField(s);
                GUILayout.EndHorizontal();
                if (ns != s) Changed = true;
                return ns;
            }
            if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                int cur = Array.IndexOf(values, v);
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(LabelW));
                if (GUILayout.Button("◄", GUILayout.Width(28))) { cur = (cur - 1 + values.Length) % values.Length; Changed = true; }
                GUILayout.Label(Labels.Enum(values.GetValue(cur)), GUI.skin.box, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("►", GUILayout.Width(28))) { cur = (cur + 1) % values.Length; Changed = true; }
                GUILayout.EndHorizontal();
                return values.GetValue(cur);
            }
            if (t == typeof(Vector2))
            {
                var p = (Vector2)v;
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(LabelW));
                GUILayout.Label("od", GUILayout.Width(22));
                float a = (float)NumberField(p.x, path + ".x", false, 60);
                GUILayout.Label("do", GUILayout.Width(22));
                float b = (float)NumberField(p.y, path + ".y", false, 60);
                GUILayout.EndHorizontal();
                var np = new Vector2(a, Mathf.Max(a, b));
                if (np != p) Changed = true;
                return np;
            }
            if (t == typeof(Color))
            {
                var c = (Color)v;
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(LabelW - 40));
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = c;
                GUILayout.Box("", GUILayout.Width(32), GUILayout.Height(20));
                GUI.backgroundColor = prev;
                float r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
                float g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
                float b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
                GUILayout.EndHorizontal();
                var nc = new Color(r, g, b, 1f);
                if (nc != c) Changed = true;
                return nc;
            }
            if (typeof(IList).IsAssignableFrom(t)) return DrawList(label, (IList)v, t, path, depth);
            if (v == null) return v;

            //zagniezdzona klasa albo struktura
            bool isOpen = open.Contains(path);
            if (GUILayout.Button((isOpen ? "▼ " : "► ") + label.text, GUI.skin.label)) { if (isOpen) open.Remove(path); else open.Add(path); }
            if (!open.Contains(path)) return v;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * depth);
            GUILayout.BeginVertical();
            v = DrawFields(v, path, depth + 1);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return v;
        }

        object DrawList(GUIContent label, IList list, Type t, string path, int depth)
        {
            if (list == null) return null;
            Type et = t.IsArray ? t.GetElementType() : (t.IsGenericType ? t.GetGenericArguments()[0] : typeof(object));
            bool fixedSize = t.IsArray;
            bool isOpen = open.Contains(path);
            if (GUILayout.Button((isOpen ? "▼ " : "► ") + label.text + "  (" + list.Count + ")", GUI.skin.label)) { if (isOpen) open.Remove(path); else open.Add(path); }
            if (!open.Contains(path)) return list;

            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * depth);
            GUILayout.BeginVertical();
            int remove = -1;
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                var item = list[i];
                bool simple = et.IsPrimitive || et == typeof(string) || et.IsEnum || et == typeof(Color) || et == typeof(Vector2);
                object ni;
                if (simple) ni = DrawValue(new GUIContent((i + 1) + "."), item, et, path + "[" + i + "]", null, null, depth + 1);
                else ni = DrawFields(item, path + "[" + i + "]", depth + 1);
                list[i] = ni;
                GUILayout.EndVertical();
                if (!fixedSize && GUILayout.Button("Usuń", GUILayout.Width(52))) remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0) { list.RemoveAt(remove); Changed = true; }
            if (!fixedSize && GUILayout.Button("+ Dodaj"))
            {
                object nItem = et == typeof(string) ? "" : Activator.CreateInstance(et);
                list.Add(nItem);
                Changed = true;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return list;
        }

        double NumberRow(GUIContent label, double v, string path, bool integer)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            double r = NumberField(v, path, integer, 90);
            GUILayout.EndHorizontal();
            return r;
        }

        int IntRange(GUIContent label, int v, int a, int b, string path)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            int s = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, a, b, GUILayout.MinWidth(80)));
            if (s != v) { v = s; textBuf.Remove(path); }
            v = Mathf.Clamp((int)NumberField(v, path, true, 50), a, b);
            GUILayout.EndHorizontal();
            return v;
        }

        float FloatRange(GUIContent label, float v, float a, float b, string path)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            float s = GUILayout.HorizontalSlider(v, a, b, GUILayout.MinWidth(80));
            if (!Mathf.Approximately(s, v)) { v = Mathf.Round(s * 1000f) / 1000f; textBuf.Remove(path); }
            v = Mathf.Clamp((float)NumberField(v, path, false, 60), a, b);
            GUILayout.EndHorizontal();
            return v;
        }

        //pole liczbowe w ktorym da sie wpisac np 0 i nie znika przy tym kropka
        double NumberField(double v, string path, bool integer, float width)
        {
            string shown;
            bool focused = GUI.GetNameOfFocusedControl() == path;
            if (!focused || !textBuf.TryGetValue(path, out shown))
                shown = integer ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture);
            GUI.SetNextControlName(path);
            string s = GUILayout.TextField(shown, GUILayout.Width(width));
            if (s == shown) return v;
            textBuf[path] = s;
            double parsed;
            if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                Changed = true;
                return integer ? Math.Round(parsed) : parsed;
            }
            return v;
        }
    }
}
