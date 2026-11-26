using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HospitalSim.Import;
using UnityEngine;

namespace HospitalSim
{
    //glowny skrypt aplikacji i jedyny ktory trzeba wrzucic do sceny
    //menu nowy budynek wczytaj budynek symulacja oraz przejscia do edytora i symulacji
    public class HospitalApp : MonoBehaviour
    {
        public enum State { Menu, Busy, Editor, Simulation }

        public static HospitalApp Instance { get; private set; }

        [Tooltip("Plik przykładowego szpitala w folderze StreamingAssets (opcjonalny)")]
        public string sampleFile = "przyklad_szpital.hospps";
        [Tooltip("Materiał URP/Lit dla budynku. Opcjonalny, ale zalecany przed zbudowaniem aplikacji (.exe)")]
        public Material litMaterial;
        [Tooltip("Materiał URP/Unlit z zaznaczonym 'Enable GPU Instancing' dla agentów. Opcjonalny, ale zalecany przed zbudowaniem aplikacji")]
        public Material unlitMaterial;

        public State Current { get; private set; } = State.Menu;
        public ProjectData Project { get; private set; }
        public RawModel Model { get; private set; }
        public string ProjectPath { get; private set; }
        public bool Dirty { get; private set; }
        public BuildingModel Building { get; private set; }
        public CameraRig Rig { get; private set; }
        public bool ModalOpen => browser.IsOpen || dialog != null;

        readonly FileBrowser browser = new FileBrowser();
        Dialog dialog;
        EditorScreen editor;
        GameObject simRoot;
        Transform world;
        string busyText = "";
        Rect simBar;
        bool quitConfirmed;
        GUIStyle titleStyle, bigButton, wrapStyle;

        class Dialog
        {
            public string title, text;
            public readonly List<KeyValuePair<string, Action>> buttons = new List<KeyValuePair<string, Action>>();
            public Vector2 scroll;
        }

        class LoadResult { public string json; public RawModel model; }
        class ImportResult { public RawModel model; public FloorDetector.Result floors; }

        void Awake()
        {
            Instance = this;
            MaterialUtil.LitTemplate = litMaterial;
            MaterialUtil.UnlitTemplate = unlitMaterial;
            LogPanel.Install();
            Application.wantsToQuit += OnWantsToQuit;

            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            Rig = cam.GetComponent<CameraRig>();
            if (Rig == null) Rig = cam.gameObject.AddComponent<CameraRig>();
            Rig.overUi = MouseOverUi;

            if (FindFirstObjectByType<Light>() == null)
            {
                var lg = new GameObject("Światło");
                var l = lg.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.1f;
                l.shadows = LightShadows.None;
                lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            }

            world = new GameObject("Świat").transform;
        }

        void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            if (Instance == this) Instance = null;
        }

        bool OnWantsToQuit()
        {
            if (quitConfirmed || !Dirty || Current != State.Editor) return true;
            AskUnsaved(() => { quitConfirmed = true; Application.Quit(); });
            return false;
        }

        bool MouseOverUi()
        {
            if (ModalOpen) return true;
            var m = Input.mousePosition;
            var g = new Vector2(m.x, Screen.height - m.y);
            if (LogPanel.Area.Contains(g)) return true;
            if (Current == State.Editor && editor != null) return editor.OverUi(g);
            if (Current == State.Simulation) return SimHud.IsOverUI(g);
            return Current != State.Simulation;
        }

        public void MarkDirty() { Dirty = true; }

        //okna dialogowe

        public void ShowMessage(string title, string text, params KeyValuePair<string, Action>[] buttons)
        {
            dialog = new Dialog { title = title, text = text };
            if (buttons == null || buttons.Length == 0) dialog.buttons.Add(new KeyValuePair<string, Action>("OK", null));
            else dialog.buttons.AddRange(buttons);
        }

        static KeyValuePair<string, Action> Btn(string t, Action a) { return new KeyValuePair<string, Action>(t, a); }

        void ShowError(string title, Exception e)
        {
            var ex = e is AggregateException && e.InnerException != null ? e.InnerException : e;
            string msg = ex is ImportException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
            Debug.LogWarning(title + ": " + msg);
            ShowMessage(title, msg);
        }

        void AskUnsaved(Action then)
        {
            ShowMessage("Niezapisane zmiany", "Projekt ma niezapisane zmiany. Zapisać przed wyjściem?",
                Btn("Zapisz", () => SaveProject(then)),
                Btn("Nie zapisuj", then),
                Btn("Anuluj", null));
        }

        //ciezka robota idzie w tle zeby wczytywanie duzego pliku nie zamrazalo okna

        void RunBackground<T>(string text, Func<T> work, Action<T> done, string errorTitle)
        {
            var back = Current;
            Current = State.Busy;
            busyText = text;
            StartCoroutine(WaitTask(Task.Run(work), done, errorTitle, back));
        }

        IEnumerator WaitTask<T>(Task<T> task, Action<T> done, string errorTitle, State back)
        {
            while (!task.IsCompleted) yield return null;
            Current = back == State.Busy ? State.Menu : back;
            if (task.IsFaulted) { ShowError(errorTitle, task.Exception); yield break; }
            try { done(task.Result); }
            catch (Exception e) { Current = back == State.Busy ? State.Menu : back; ShowError(errorTitle, e); }
        }

        //nowy budynek

        public void NewBuilding()
        {
            browser.Open("Wybierz model budynku (FBX, OBJ, GLB, glTF, STL)", ModelImporter.Extensions, LastDir("lastModelDir"), path =>
            {
                PlayerPrefs.SetString("lastModelDir", Path.GetDirectoryName(path));
                RunBackground("Wczytywanie modelu " + Path.GetFileName(path) + "...",
                    () =>
                    {
                        var m = ModelImporter.Load(path);
                        return new ImportResult { model = m, floors = FloorDetector.Detect(m) };
                    },
                    r => CreateProject(r, path), "Nie udało się wczytać modelu");
            });
        }

        void CreateProject(ImportResult r, string modelPath)
        {
            CloseEverything();
            Model = r.model;
            Project = new ProjectData { name = Path.GetFileNameWithoutExtension(modelPath), sourceModel = Path.GetFileName(modelPath) };
            ProjectPath = null;
            var L = Project.layout;

            float x0, y0, z0, x1, y1, z1;
            Model.GetBounds(out x0, out y0, out z0, out x1, out y1, out z1);
            string floorsInfo;
            if (r.floors != null && r.floors.ok)
            {
                L.floors = Mathf.Clamp(r.floors.floors, 1, 50);
                L.floorHeight = Mathf.Max(2f, r.floors.floorHeight);
                L.originY = r.floors.groundY;
                floorsInfo = "Wykryto " + L.floors + " piętra, wysokość piętra " + L.floorHeight.ToString("0.00") + " m.";
            }
            else
            {
                L.originY = y0;
                L.floors = Mathf.Clamp(Mathf.RoundToInt((y1 - y0) / L.floorHeight), 1, 50);
                floorsInfo = "Nie udało się wykryć pięter - sprawdź zakładkę 'Piętra'.";
            }
            L.floorNames = new List<string>();
            for (int f = 0; f < L.floors; f++) L.floorNames.Add(f == 0 ? "Parter" : f + " piętro");
            const float margin = 8f;
            //jak wymiary wygladaja na metry to dobieramy komorke do wielkosci budynku
            float big = Mathf.Max(x1 - x0, z1 - z0);
            float cs = big >= 5f && big <= 700f ? EditorScreen.SuggestCellSize(big) : 1f;
            L.cellSize = cs;
            L.originX = Mathf.Floor((x0 - margin) / cs) * cs;
            L.originZ = Mathf.Floor((z0 - margin) / cs) * cs;
            L.width = Mathf.Clamp(Mathf.CeilToInt((x1 + margin - L.originX) / cs), 1, 2000);
            L.depth = Mathf.Clamp(Mathf.CeilToInt((z1 + margin - L.originZ) / cs), 1, 2000);
            L.cells = null;
            L.EnsureCells();
            AddStarterRooms(L);

            BuildBuilding();
            Dirty = true;
            EnterEditor();
            Rig.Frame(Building.Bounds);

            string warn = Model.warnings.Count > 0 ? "\n\n" + string.Join("\n", Model.warnings) : "";
            ShowMessage("Model wczytany",
                "Trójkąty: " + Model.TriangleCount.ToString("N0") + "\nWymiary: " + (x1 - x0).ToString("0.0") + " x " + (z1 - z0).ToString("0.0") +
                " m, wysokość " + (y1 - y0).ToString("0.0") + " m\n" + floorsInfo +
                "\n\nJeśli wymiary są dziwne (np. kilometry), popraw skalę w zakładce 'Model'. Potem maluj sale w zakładce 'Sale'." + warn);
        }

        static void AddStarterRooms(LayoutData L)
        {
            L.rooms.Clear();
            var start = new[]
            {
                RoomType.Corridor, RoomType.Door, RoomType.Stairs, RoomType.Entrance, RoomType.Exit,
                RoomType.WaitingRoom, RoomType.Ward, RoomType.StaffRoom, RoomType.AssemblyPoint
            };
            foreach (var t in start)
                L.rooms.Add(new RoomDef
                {
                    name = EditorScreen.DefaultName(t, 1), type = t, color = EditorScreen.DefaultColor(t, 0),
                    capacity = t == RoomType.Ward ? 4 : -1
                });
        }

        //wczytywanie projektu

        public void OpenProject(bool thenSimulate)
        {
            browser.Open(thenSimulate ? "Wybierz budynek do symulacji (.hospps)" : "Wczytaj budynek (.hospps)", new[] { HospppsFile.Extension },
                LastDir("lastProjectDir"), path => LoadProject(path, thenSimulate));
        }

        void LoadProject(string path, bool thenSimulate)
        {
            PlayerPrefs.SetString("lastProjectDir", Path.GetDirectoryName(path));
            RunBackground("Wczytywanie " + Path.GetFileName(path) + "...",
                () =>
                {
                    RawModel m;
                    string json = HospppsFile.ReadRaw(path, out m);
                    return new LoadResult { json = json, model = m };
                },
                r =>
                {
                    var proj = HospppsFile.FromJson(r.json);
                    CloseEverything();
                    Project = proj;
                    Model = r.model;
                    bool fromStreaming = path.StartsWith(Application.streamingAssetsPath, StringComparison.OrdinalIgnoreCase);
                    ProjectPath = fromStreaming ? null : path;
                    Dirty = fromStreaming;
                    if (!fromStreaming) AddRecent(path);
                    BuildBuilding();
                    if (thenSimulate) StartCoroutine(StartSimulation());
                    else { EnterEditor(); Rig.Frame(Building.Bounds); }
                }, "Nie udało się wczytać projektu");
        }

        //zapis

        public void SaveProject(Action after)
        {
            if (Project == null) return;
            if (string.IsNullOrEmpty(ProjectPath)) { SaveProjectAs(after); return; }
            try
            {
                HospppsFile.Save(ProjectPath, Project, Model);
                Dirty = false;
                AddRecent(ProjectPath);
                Debug.Log("Zapisano projekt: " + ProjectPath);
                after?.Invoke();
            }
            catch (Exception e) { ShowError("Nie udało się zapisać", e); }
        }

        public void SaveProjectAs(Action after)
        {
            if (Project == null) return;
            string dir = string.IsNullOrEmpty(ProjectPath) ? LastDir("lastProjectDir") : Path.GetDirectoryName(ProjectPath);
            browser.Save("Zapisz budynek jako", HospppsFile.Extension, dir, (Project.name ?? "szpital") + HospppsFile.Extension, path =>
            {
                ProjectPath = path;
                PlayerPrefs.SetString("lastProjectDir", Path.GetDirectoryName(path));
                SaveProject(after);
            });
        }

        //przed symulacja zawsze zapisujemy na wszelki wypadek
        public void SaveAndSimulate()
        {
            SaveProject(() => StartCoroutine(StartSimulation()));
        }

        //przejscia miedzy ekranami

        void BuildBuilding()
        {
            if (Building != null) Destroy(Building.gameObject);
            Building = BuildingModel.Create(Model, world);
            Building.Rebuild(Project.layout);
        }

        public void RebuildBuilding()
        {
            if (Building == null) { BuildBuilding(); return; }
            Building.Rebuild(Project.layout);
        }

        void EnterEditor()
        {
            StopSimulation();
            if (editor == null)
            {
                editor = new GameObject("Edytor").AddComponent<EditorScreen>();
                editor.transform.SetParent(world, false);
                editor.Init(this);
            }
            Rig.SetMode(CameraRig.View.Plan);
            Current = State.Editor;
        }

        void CloseEditor()
        {
            if (editor != null) Destroy(editor.gameObject);
            editor = null;
        }

        void CloseEverything()
        {
            StopSimulation();
            CloseEditor();
            if (Building != null) Destroy(Building.gameObject);
            Building = null;
        }

        public void GoToMenu()
        {
            if (Current == State.Editor && Dirty) { AskUnsaved(() => { Dirty = false; GoToMenu(); }); return; }
            CloseEverything();
            Project = null; Model = null; ProjectPath = null; Dirty = false;
            Current = State.Menu;
        }

        public void BackToEditor()
        {
            StopSimulation();
            EnterEditor();
            if (editor != null) editor.SetFloor(FloorViewController.Instance != null ? FloorViewController.Instance.floor : 0);
        }

        //start symulacji

        IEnumerator StartSimulation()
        {
            Current = State.Busy;
            busyText = "Sprawdzanie projektu...";
            yield return null;
            yield return null;

            var L = Project.layout;
            var res = LayoutBaker.Compute(L, Project.waypoints);
            if (!res.Ok)
            {
                EnterEditor();
                ShowMessage("Nie można uruchomić symulacji", "Popraw te rzeczy w edytorze:\n\n• " + string.Join("\n• ", res.errors));
                yield break;
            }

            CloseEditor();
            busyText = "Budowanie kolizji ścian...";
            yield return null;
            Building.ShowUpTo(-1);
            Building.EnsureColliders();
            Physics.SyncTransforms();

            //polaczenia ktore przechodza przez sciany modelu wywalamy bo agent i tak by tamtedy nie przeszedl
            //sprawdzamy ok 1 m nad podloga zeby lozka i stoly nie psuly polaczen
            var nav = Project.settings.navigation;
            var lift = Vector3.up * (nav.wallCheckHeight - L.waypointHeight);
            var ext = new Vector3(nav.wallCellSize * 0.5f + nav.wallClearance + 0.15f, nav.wallCheckHalfHeight, nav.wallCellSize * 0.5f + nav.wallClearance + 0.15f);
            int moved = LayoutBaker.NudgeFromWalls(res, L, p => Physics.CheckBox(p + lift, ext, Quaternion.identity, 1, QueryTriggerInteraction.Ignore));
            if (moved > 0) Debug.Log("Odsunięto od ścian " + moved + " waypointów.");
            int pruned = LayoutBaker.PruneBlocked(res, L, (a, b) =>
            {
                Vector3 d = b - a;
                float len = d.magnitude;
                return Physics.SphereCast(a + lift, 0.05f, d / len, out RaycastHit _, len, 1, QueryTriggerInteraction.Ignore);
            });
            if (pruned > 0) Debug.Log("Usunięto " + pruned + " połączeń, które przechodziły przez ściany modelu.");
            if (!res.Ok)
            {
                EnterEditor();
                ShowMessage("Nie można uruchomić symulacji", "Popraw te rzeczy w edytorze:\n\n• " + string.Join("\n• ", res.errors));
                yield break;
            }

            busyText = "Liczenie tras (" + res.nodes.Count + " waypointów)...";
            yield return null;
            yield return null;

            bool failed = false;
            try
            {
                simRoot = new GameObject("Symulacja");
                simRoot.transform.SetParent(world, false);
                SimLauncher.Launch(Project, res, simRoot.transform, Rig.Cam, true);
                SimHud.ExtraUiHit = g => simBar.Contains(g) || LogPanel.Area.Contains(g) || ModalOpen;
            }
            catch (Exception e)
            {
                failed = true;
                Debug.LogException(e);
                StopSimulation();
                EnterEditor();
                ShowError("Błąd przy starcie symulacji", e);
            }
            if (failed) yield break;

            Current = State.Simulation;
            Rig.SetMode(CameraRig.View.Plan);
            Rig.Frame(Building.Bounds);
            Debug.Log("Symulacja uruchomiona: " + (Project.name ?? ""));
        }

        void StopSimulation()
        {
            if (simRoot != null)
            {
                //najpierw czekamy az joby agentow skoncza a dopiero potem wszystko niszczymy
                simRoot.SetActive(false);
                Destroy(simRoot);
            }
            simRoot = null;
            SimHud.ExtraUiHit = null;
            if (Current == State.Simulation) Current = State.Menu;
        }

        void Update()
        {
            if (Current == State.Simulation && Building != null)
            {
                var fv = FloorViewController.Instance;
                if (fv != null)
                {
                    bool cut = Rig.Mode == CameraRig.View.Plan || (fv.mode != FloorViewController.Mode.All && fv.hideUpperGeometry);
                    Building.ShowUpTo(cut ? fv.floor : -1);
                }
            }
        }

        //ostatnie projekty

        List<string> Recent()
        {
            var l = new List<string>();
            foreach (var s in PlayerPrefs.GetString("recent", "").Split('|'))
                if (!string.IsNullOrEmpty(s) && File.Exists(s)) l.Add(s);
            return l;
        }

        void AddRecent(string path)
        {
            var l = Recent();
            l.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            l.Insert(0, path);
            if (l.Count > 8) l.RemoveRange(8, l.Count - 8);
            PlayerPrefs.SetString("recent", string.Join("|", l));
            PlayerPrefs.Save();
        }

        static string LastDir(string key)
        {
            string d = PlayerPrefs.GetString(key, "");
            return Directory.Exists(d) ? d : FileBrowser.ProjectsDir;
        }

        //interfejs

        void OnGUI()
        {
            GuiSkinUtil.Setup();
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                bigButton = new GUIStyle(GUI.skin.button) { fontSize = 19, fixedHeight = 50 };
                wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            }

            bool modal = ModalOpen;
            GUI.enabled = !modal;
            switch (Current)
            {
                case State.Menu: DrawMenu(); break;
                case State.Busy: DrawBusy(); break;
                case State.Editor: if (editor != null) editor.DrawGUI(); break;
                case State.Simulation: DrawSimBar(); break;
            }
            LogPanel.Draw();
            GUI.enabled = true;

            browser.Draw();
            if (dialog != null) DrawDialog();
        }

        void DrawMenu()
        {
            float w = 520, h = 600;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Space(16);
            GUILayout.Label("Symulator ruchu w szpitalu", titleStyle);
            GUILayout.Label("Projekty w formacie .hospps (Hospital Politechnika Śląska)", new GUIStyle(wrapStyle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(20);
            if (GUILayout.Button("Nowy budynek", bigButton)) NewBuilding();
            GUILayout.Label("Wczytaj model 3D budynku (FBX, OBJ, GLB, glTF, STL) i oznacz w nim sale.", wrapStyle);
            GUILayout.Space(8);
            if (GUILayout.Button("Wczytaj budynek", bigButton)) OpenProject(false);
            GUILayout.Label("Otwórz zapisany projekt .hospps w edytorze.", wrapStyle);
            GUILayout.Space(8);
            if (GUILayout.Button("Symulacja", bigButton)) OpenProject(true);
            GUILayout.Label("Wybierz zapisany budynek i od razu uruchom symulację.", wrapStyle);

            string sample = Path.Combine(Application.streamingAssetsPath, sampleFile);
            if (File.Exists(sample))
            {
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Przykładowy szpital - edytuj")) LoadProject(sample, false);
                if (GUILayout.Button("Przykładowy szpital - symuluj")) LoadProject(sample, true);
                GUILayout.EndHorizontal();
            }

            var recent = Recent();
            if (recent.Count > 0)
            {
                GUILayout.Space(12);
                GUILayout.Label("<b>Ostatnie projekty</b>", wrapStyle);
                foreach (var p in recent)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Path.GetFileNameWithoutExtension(p), GUILayout.Width(250));
                    if (GUILayout.Button("Edytuj", GUILayout.Width(90))) LoadProject(p, false);
                    if (GUILayout.Button("Symuluj", GUILayout.Width(90))) LoadProject(p, true);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Komunikaty", GUILayout.Width(120))) LogPanel.Visible = !LogPanel.Visible;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Wyjście", GUILayout.Width(120))) Application.Quit();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawBusy()
        {
            var r = new Rect((Screen.width - 460) * 0.5f, (Screen.height - 90) * 0.5f, 460, 90);
            GUI.Box(r, "");
            int dots = (int)(Time.realtimeSinceStartup * 3f) % 4;
            GUI.Label(new Rect(r.x + 20, r.y + 30, r.width - 40, 30), busyText + new string('.', dots), wrapStyle);
        }

        void DrawSimBar()
        {
            float w = 520;
            simBar = new Rect(Screen.width - w - 10, SimHud.BarBottom + 6f, w, 40);
            GUILayout.BeginArea(simBar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄ Edytor")) BackToEditor();
            if (GUILayout.Button("Menu")) GoToMenu();
            if (GUILayout.Button(Rig.Mode == CameraRig.View.Plan ? "Widok: rzut" : "Widok: 3D"))
            {
                Rig.Toggle();
                var fv = FloorViewController.Instance;
                if (fv != null && Rig.Mode == CameraRig.View.Plan) fv.Set(FloorViewController.Mode.OnlyFloor, fv.floor);
            }
            if (GUILayout.Button("Komunikaty" + (LogPanel.Unread > 0 ? " (" + LogPanel.Unread + ")" : ""))) LogPanel.Visible = !LogPanel.Visible;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawDialog()
        {
            float w = Mathf.Min(560, Screen.width - 40), h = Mathf.Min(420, Screen.height - 40);
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.ModalWindow(9002, r, id =>
            {
                var d = dialog;
                if (d == null) return;
                d.scroll = GUILayout.BeginScrollView(d.scroll);
                GUILayout.Label(d.text, wrapStyle);
                GUILayout.EndScrollView();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                foreach (var b in d.buttons)
                {
                    if (GUILayout.Button(b.Key, GUILayout.MinWidth(110), GUILayout.Height(30)))
                    {
                        dialog = null;
                        b.Value?.Invoke();
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.EndHorizontal();
            }, dialog.title);
        }
    }
}
