using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HospitalSim
{
    //konsola w samym programie bo w zbudowanej aplikacji nie ma konsoli unity a komunikaty o zdarzeniach i zgonach musza gdzies byc widac
    public static class LogPanel
    {
        struct Entry { public string text; public LogType type; }

        static readonly List<Entry> entries = new List<Entry>();
        static bool installed;
        static Vector2 scroll;
        static bool stick = true;
        const int MaxEntries = 500;

        public static bool Visible;
        public static int Unread { get; private set; }
        public static Rect Area { get; private set; }

        public static void Install()
        {
            if (installed) return;
            installed = true;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            var clock = SimClock.Instance;
            string time = clock != null ? clock.DateString + " " + clock.TimeString + "  " : DateTime.Now.ToString("HH:mm:ss") + "  ";
            entries.Add(new Entry { text = time + msg, type = type });
            if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
            if (!Visible) Unread++;
        }

        public static void Clear() { entries.Clear(); Unread = 0; }

        public static void Draw()
        {
            if (!Visible) { Area = Rect.zero; return; }
            Unread = 0;
            float w = Mathf.Min(620, Screen.width - 20), h = 260;
            Area = new Rect(Screen.width - w - 10, Screen.height - h - 10, w, h);
            GUILayout.BeginArea(Area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Komunikaty</b>");
            GUILayout.FlexibleSpace();
            stick = GUILayout.Toggle(stick, "Przewijaj", GUILayout.Width(95));
            if (GUILayout.Button("Zapisz do pliku", GUILayout.Width(130))) SaveToFile();
            if (GUILayout.Button("Wyczyść", GUILayout.Width(80))) Clear();
            if (GUILayout.Button("X", GUILayout.Width(28))) Visible = false;
            GUILayout.EndHorizontal();
            if (stick) scroll.y = float.MaxValue;
            scroll = GUILayout.BeginScrollView(scroll);
            var prev = GUI.color;
            foreach (var e in entries)
            {
                GUI.color = e.type == LogType.Warning ? new Color(1f, 0.85f, 0.3f) :
                            (e.type == LogType.Error || e.type == LogType.Exception) ? new Color(1f, 0.45f, 0.45f) : Color.white;
                GUILayout.Label(e.text);
            }
            GUI.color = prev;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static void SaveToFile()
        {
            try
            {
                string p = Path.Combine(FileBrowser.ProjectsDir, "komunikaty_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                var lines = new List<string>();
                foreach (var e in entries) lines.Add(e.text);
                File.WriteAllLines(p, lines);
                Debug.Log("Zapisano komunikaty do " + p);
            }
            catch (Exception ex) { Debug.LogWarning("Nie udało się zapisać komunikatów: " + ex.Message); }
        }
    }
}
