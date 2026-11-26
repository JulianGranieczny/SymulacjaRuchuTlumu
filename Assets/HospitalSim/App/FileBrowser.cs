using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HospitalSim
{
    //proste okno wyboru pliku w imgui bez zadnych bibliotek dziala na windowsie linuksie i macu
    public class FileBrowser
    {
        public bool IsOpen { get; private set; }

        string title, currentDir, fileName = "", error;
        string[] exts;
        bool saveMode;
        Action<string> onPick;
        Vector2 scroll;
        readonly List<string> dirs = new List<string>();
        readonly List<string> files = new List<string>();
        string selected;
        string confirmOverwrite;
        float lastClick;

        public static string ProjectsDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Symulator szpitala");
                try { Directory.CreateDirectory(d); } catch (Exception) { d = Application.persistentDataPath; }
                return d;
            }
        }

        public void Open(string title, string[] extensions, string startDir, Action<string> onPick)
        {
            Show(title, extensions, startDir, "", false, onPick);
        }

        public void Save(string title, string extension, string startDir, string defaultName, Action<string> onPick)
        {
            Show(title, new[] { extension }, startDir, defaultName, true, onPick);
        }

        void Show(string t, string[] e, string dir, string name, bool save, Action<string> cb)
        {
            title = t; exts = e; saveMode = save; onPick = cb; fileName = name ?? "";
            selected = null; confirmOverwrite = null; error = null;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = ProjectsDir;
            IsOpen = true;
            Navigate(dir);
        }

        public void Close() { IsOpen = false; onPick = null; }

        void Navigate(string dir)
        {
            try
            {
                var full = Path.GetFullPath(dir);
                var d = new List<string>(Directory.GetDirectories(full));
                var f = new List<string>();
                foreach (var file in Directory.GetFiles(full))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (exts == null || exts.Length == 0 || Array.IndexOf(exts, ext) >= 0) f.Add(file);
                }
                d.Sort(StringComparer.OrdinalIgnoreCase);
                f.Sort(StringComparer.OrdinalIgnoreCase);
                dirs.Clear(); dirs.AddRange(d);
                files.Clear(); files.AddRange(f);
                currentDir = full;
                scroll = Vector2.zero;
                selected = null;
                error = null;
            }
            catch (Exception ex) { error = "Nie można otworzyć folderu: " + ex.Message; }
        }

        public void Draw()
        {
            if (!IsOpen) return;
            float w = Mathf.Min(820, Screen.width - 40), h = Mathf.Min(600, Screen.height - 40);
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.ModalWindow(9001, r, DrawWindow, title);
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("W górę", GUILayout.Width(80)))
            {
                var parent = Directory.GetParent(currentDir);
                if (parent != null) Navigate(parent.FullName);
            }
            if (GUILayout.Button("Projekty", GUILayout.Width(80))) Navigate(ProjectsDir);
            if (GUILayout.Button("Pulpit", GUILayout.Width(70))) Navigate(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            if (GUILayout.Button("Dokumenty", GUILayout.Width(95))) Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            try
            {
                foreach (var drive in Directory.GetLogicalDrives())
                    if (GUILayout.Button(drive.TrimEnd('\\', '/'), GUILayout.Width(44))) Navigate(drive);
            }
            catch (Exception) { }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Folder:", GUILayout.Width(55));
            currentDir = GUILayout.TextField(currentDir ?? "");
            if (GUILayout.Button("Idź", GUILayout.Width(45)))
            {
                if (Directory.Exists(currentDir)) Navigate(currentDir);
                else error = "Nie ma takiego folderu.";
            }
            GUILayout.EndHorizontal();

            if (error != null) { var c = GUI.color; GUI.color = new Color(1f, 0.5f, 0.5f); GUILayout.Label(error); GUI.color = c; }

            scroll = GUILayout.BeginScrollView(scroll, GUI.skin.box, GUILayout.ExpandHeight(true));
            foreach (var d in dirs)
            {
                if (GUILayout.Button("[folder]  " + Path.GetFileName(d), GUI.skin.label)) { Navigate(d); GUIUtility.ExitGUI(); }
            }
            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                var prev = GUI.backgroundColor;
                bool isSel = selected == f;
                if (isSel) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                string info = "";
                try { info = "   " + (new FileInfo(f).Length / 1024) + " KB   " + File.GetLastWriteTime(f).ToString("dd.MM.yyyy HH:mm"); } catch (Exception) { }
                if (GUILayout.Button(name + info, isSel ? GUI.skin.button : GUI.skin.label))
                {
                    bool dbl = isSel && Time.realtimeSinceStartup - lastClick < 0.4f;
                    selected = f; fileName = name; lastClick = Time.realtimeSinceStartup;
                    if (dbl) { Confirm(); GUIUtility.ExitGUI(); }
                }
                GUI.backgroundColor = prev;
            }
            if (dirs.Count == 0 && files.Count == 0) GUILayout.Label("(pusty folder)");
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Plik:", GUILayout.Width(55));
            fileName = GUILayout.TextField(fileName);
            GUILayout.Label(string.Join(", ", exts), GUILayout.Width(170));
            GUILayout.EndHorizontal();

            if (confirmOverwrite != null)
            {
                GUILayout.Label("Plik '" + Path.GetFileName(confirmOverwrite) + "' już istnieje. Nadpisać?");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Tak, nadpisz")) { var p = confirmOverwrite; confirmOverwrite = null; Finish(p); }
                if (GUILayout.Button("Nie")) confirmOverwrite = null;
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(saveMode ? "Zapisz" : "Otwórz", GUILayout.Width(120), GUILayout.Height(30))) Confirm();
            if (GUILayout.Button("Anuluj", GUILayout.Width(120), GUILayout.Height(30))) Close();
            GUILayout.EndHorizontal();
        }

        void Confirm()
        {
            if (string.IsNullOrEmpty(fileName)) { error = "Wybierz plik."; return; }
            string path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(currentDir, fileName);
            if (saveMode)
            {
                if (!path.ToLowerInvariant().EndsWith(exts[0])) path += exts[0];
                if (File.Exists(path)) { confirmOverwrite = path; return; }
                Finish(path);
            }
            else
            {
                if (Directory.Exists(path)) { Navigate(path); return; }
                if (!File.Exists(path)) { error = "Plik nie istnieje."; return; }
                Finish(path);
            }
        }

        void Finish(string path)
        {
            var cb = onPick;
            IsOpen = false;
            onPick = null;
            cb?.Invoke(path);
        }
    }
}
