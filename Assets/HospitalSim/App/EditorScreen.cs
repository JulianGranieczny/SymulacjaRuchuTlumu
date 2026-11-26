using System;
using System.Collections.Generic;
using HospitalSim.Import;
using UnityEngine;
using UnityEngine.Rendering;

namespace HospitalSim
{
    [Serializable]
    public class GridEdit
    {
        [Min(1)] public int floors = 3;
        [Tooltip("Odległość między podłogami kolejnych pięter")]
        [Min(1f)] public float floorHeight = 4f;
        [Tooltip("Na jakiej wysokości jest podłoga parteru w modelu")]
        public float groundY;
        [Tooltip("Wszystko powyżej tej wysokości nad podłogą jest ukryte w widoku piętra, dzięki temu widać ściany jak na planie")]
        [Range(0.3f, 3f)] public float cutHeight = 1.6f;
        [Tooltip("Mniejsza komórka = dokładniejsze malowanie, ale więcej pracy i wolniejsze sprawdzanie")]
        [Range(0.1f, 5f)] public float cellSize = 1f;
    }

    //polozenie i rozmiar siatki w metrach dzieki temu da sie ja dopasowac do budynku o dowolnych proporcjach
    [Serializable]
    public class GridArea
    {
        [Tooltip("Lewa krawędź siatki na planie (X, m)")]
        public float startX;
        [Tooltip("Dolna krawędź siatki na planie (Z, m)")]
        public float startZ;
        [Tooltip("Szerokość siatki w metrach (oś X)")]
        [Min(1f)] public float sizeX = 60f;
        [Tooltip("Głębokość siatki w metrach (oś Z)")]
        [Min(1f)] public float sizeZ = 40f;
        [Tooltip("Ile metrów wolnego miejsca zostawić wokół budynku przy dopasowaniu")]
        [Range(0f, 50f)] public float fitMargin = 8f;
    }

    [Serializable]
    public class WaypointEdit
    {
        [Tooltip("Co ile metrów stawiany jest waypoint w salach i korytarzach. Większy odstęp = szybszy start symulacji")]
        [Range(1f, 10f)] public float waypointSpacing = 3f;
        [Tooltip("Na oddziale waypoint to łóżko, dlatego jest gęściej")]
        [Range(0.5f, 6f)] public float wardWaypointSpacing = 2f;
        [Range(0.1f, 1.5f)] public float waypointHeight = 0.5f;
    }

    //ekran edytora malowanie sal waypointy ustawienia i sprawdzanie projektu
    public class EditorScreen : MonoBehaviour
    {
        enum Tab { Model, Grid, Rooms, Waypoints, Sim, Check }
        enum Tool { Brush, Eraser, Rect, Fill, Pick }

        static readonly string[] TabNames = { "Model", "Piętra", "Sale", "Waypointy", "Symulacja", "Sprawdź" };
        static readonly string[] ToolNames = { "Pędzel (B)", "Gumka (E)", "Prostokąt (R)", "Wypełnij (F)", "Pipeta (I)" };
        static readonly int[] BrushSizes = { 1, 2, 3, 5 };

        HospitalApp app;
        ProjectData P => app.Project;
        LayoutData L => app.Project.layout;

        Tab tab = Tab.Rooms;
        Tool tool = Tool.Brush;
        int brush, brushSize = 1;
        public int Floor { get; private set; }
        bool showGrid = true, showLabels = true, showPreview, showWholeBuilding;

        //nakladka na siatke
        GameObject overlay;
        Mesh cellsMesh, linesMesh, cursorMesh, previewMesh;
        Material overlayMat;
        bool cellsDirty = true, gridDirty = true, previewDirty = true;

        //stan malowania
        bool painting, hoverValid;
        int startX, startZ, lastX, lastZ, hoverX, hoverZ;
        Vector3 hoverWorld;

        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();
        const int MaxUndo = 40;

        struct Label3 { public Vector3 pos; public string text; }
        readonly List<Label3> labels = new List<Label3>();

        LayoutBaker.Result check, preview;
        readonly SettingsGui settingsGui = new SettingsGui();
        readonly SettingsGui smallGui = new SettingsGui();
        GridEdit gridEdit;
        GridArea gridArea;
        readonly SettingsGui areaGui = new SettingsGui();
        WaypointEdit wpEdit;
        FloorDetector.Result detected;
        int deleteConfirm = -1;
        Vector2 leftScroll;
        Rect topBar, leftPanel, statusBar;
        GUIStyle labelStyle, hintStyle;

        [Serializable]
        class UndoState
        {
            public LayoutData layout;
            public List<ManualWaypoint> waypoints;
        }

        public void Init(HospitalApp a)
        {
            app = a;
            overlay = new GameObject("Nakładka edytora");
            overlay.transform.SetParent(transform, false);
            overlayMat = MaterialUtil.VertexColor();
            cellsMesh = MakeLayer("Sale", 0.03f);
            linesMesh = MakeLayer("Linie", 0.04f);
            cursorMesh = MakeLayer("Kursor", 0.05f);
            previewMesh = MakeLayer("Waypointy", 0.06f);
            LoadEdits();
            SetFloor(0);
            check = LayoutBaker.Compute(L, P.waypoints);
        }

        Mesh MakeLayer(string name, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(overlay.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = overlayMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return m;
        }

        void LoadEdits()
        {
            gridEdit = new GridEdit { floors = L.floors, floorHeight = L.floorHeight, groundY = L.originY, cutHeight = L.cutHeight, cellSize = L.cellSize };
            LoadArea();
            wpEdit = new WaypointEdit { waypointSpacing = L.waypointSpacing, wardWaypointSpacing = L.wardWaypointSpacing, waypointHeight = L.waypointHeight };
        }

        void OnDestroy()
        {
            foreach (var m in new[] { cellsMesh, linesMesh, cursorMesh, previewMesh }) if (m != null) Destroy(m);
            if (overlayMat != null) Destroy(overlayMat);
        }

        public void SetFloor(int f)
        {
            Floor = Mathf.Clamp(f, 0, L.floors - 1);
            cellsDirty = gridDirty = true;
            previewDirty = true;
        }

        public void MarkAllDirty()
        {
            cellsDirty = gridDirty = previewDirty = true;
            LoadEdits();
        }

        //przed kazda zmiana zapisujemy stan siatki i waypointow zeby mozna bylo cofnac
        void PushUndo()
        {
            L.Pack();
            undo.Add(JsonUtility.ToJson(new UndoState { layout = L, waypoints = P.waypoints }));
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            redo.Clear();
        }

        void Restore(List<string> from, List<string> to)
        {
            if (from.Count == 0) return;
            L.Pack();
            to.Add(JsonUtility.ToJson(new UndoState { layout = L, waypoints = P.waypoints }));
            var s = JsonUtility.FromJson<UndoState>(from[from.Count - 1]);
            from.RemoveAt(from.Count - 1);
            s.layout.Unpack();
            bool sizeChanged = s.layout.floors != L.floors || s.layout.cutHeight != L.cutHeight || s.layout.originY != L.originY || s.layout.floorHeight != L.floorHeight;
            P.layout = s.layout;
            P.waypoints = s.waypoints ?? new List<ManualWaypoint>();
            if (sizeChanged) app.RebuildBuilding();
            SetFloor(Floor);
            MarkAllDirty();
            app.MarkDirty();
        }

        public void Undo() { Restore(undo, redo); }
        public void Redo() { Restore(redo, undo); }

        public bool OverUi(Vector2 g)
        {
            return topBar.Contains(g) || leftPanel.Contains(g) || statusBar.Contains(g) || app.ModalOpen || LogPanel.Area.Contains(g);
        }

        bool MouseOverUi()
        {
            var m = Input.mousePosition;
            return OverUi(new Vector2(m.x, Screen.height - m.y));
        }

        void Update()
        {
            if (app == null || app.ModalOpen || app.Project == null || app.Building == null) return;
            bool typing = GUIUtility.keyboardControl != 0;
            HandleKeys(typing);

            app.Building.ShowUpTo(showWholeBuilding && app.Rig.Mode == CameraRig.View.Free ? -1 : Floor);

            hoverValid = false;
            Vector3 hit;
            if (app.Rig.MouseOnPlane(L.FloorY(Floor), out hit))
            {
                hoverWorld = hit;
                hoverValid = L.WorldToCell(hit.x, hit.z, out hoverX, out hoverZ);
            }

            bool overUi = MouseOverUi();
            if (tab == Tab.Waypoints) HandleWaypointTool(overUi);
            else HandlePaint(overUi);

            if (cellsDirty) { BuildCells(); cellsDirty = false; }
            if (gridDirty || previewDirty) { BuildLines(); gridDirty = false; previewDirty = false; }
            BuildCursor();
        }

        void HandleKeys(bool typing)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand);
            if (ctrl && Input.GetKeyDown(KeyCode.S)) { app.SaveProject(null); return; }
            if (typing) return;
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) { if (Input.GetKey(KeyCode.LeftShift)) Redo(); else Undo(); }
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (ctrl) return;
            if (Input.GetKeyDown(KeyCode.B)) tool = Tool.Brush;
            if (Input.GetKeyDown(KeyCode.E)) tool = Tool.Eraser;
            if (Input.GetKeyDown(KeyCode.R)) tool = Tool.Rect;
            if (Input.GetKeyDown(KeyCode.F)) tool = Tool.Fill;
            if (Input.GetKeyDown(KeyCode.I)) tool = Tool.Pick;
            if (Input.GetKeyDown(KeyCode.PageUp)) SetFloor(Floor + 1);
            if (Input.GetKeyDown(KeyCode.PageDown)) SetFloor(Floor - 1);
            if (Input.GetKeyDown(KeyCode.Tab)) app.Rig.Toggle();
        }

        //malowanie sal

        int PaintValue()
        {
            bool erase = tool == Tool.Eraser || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (erase || L.rooms.Count == 0) return 0;
            return Mathf.Clamp(brush, 0, L.rooms.Count - 1) + 1;
        }

        void HandlePaint(bool overUi)
        {
            if (Input.GetMouseButtonDown(0) && !overUi && hoverValid)
            {
                if (tool == Tool.Pick)
                {
                    int v = L.Get(Floor, hoverX, hoverZ);
                    if (v > 0) { brush = v - 1; tool = Tool.Brush; tab = Tab.Rooms; }
                    return;
                }
                if (L.rooms.Count == 0 && PaintValue() != 0) return;
                PushUndo();
                painting = true;
                startX = lastX = hoverX; startZ = lastZ = hoverZ;
                if (tool == Tool.Fill) { Fill(hoverX, hoverZ, PaintValue()); painting = false; EndStroke(); }
                else if (tool != Tool.Rect) Stamp(hoverX, hoverZ, PaintValue());
            }
            else if (painting && Input.GetMouseButton(0))
            {
                if (hoverValid && (tool == Tool.Brush || tool == Tool.Eraser) && (hoverX != lastX || hoverZ != lastZ))
                {
                    //rysujemy linie od poprzedniej do obecnej komorki bo przy szybkim ruchu myszy zostawaly dziury
                    int v = PaintValue();
                    int x0 = lastX, z0 = lastZ, x1 = hoverX, z1 = hoverZ;
                    int dx = Mathf.Abs(x1 - x0), dz = -Mathf.Abs(z1 - z0), sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1, err = dx + dz;
                    int guard = 0;
                    while (guard++ < 10000)
                    {
                        Stamp(x0, z0, v);
                        if (x0 == x1 && z0 == z1) break;
                        int e2 = 2 * err;
                        if (e2 >= dz) { err += dz; x0 += sx; }
                        if (e2 <= dx) { err += dx; z0 += sz; }
                    }
                    lastX = hoverX; lastZ = hoverZ;
                }
            }
            else if (painting && !Input.GetMouseButton(0))
            {
                painting = false;
                if (tool == Tool.Rect && hoverValid)
                {
                    int v = PaintValue();
                    int x0 = Mathf.Min(startX, hoverX), x1 = Mathf.Max(startX, hoverX), z0 = Mathf.Min(startZ, hoverZ), z1 = Mathf.Max(startZ, hoverZ);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) L.Set(Floor, x, z, v);
                }
                EndStroke();
            }
        }

        void Stamp(int cx, int cz, int v)
        {
            int s = tool == Tool.Rect ? 1 : brushSize;
            int x0 = cx - (s - 1) / 2, z0 = cz - (s - 1) / 2;
            for (int z = z0; z < z0 + s; z++) for (int x = x0; x < x0 + s; x++) L.Set(Floor, x, z, v);
            cellsDirty = true;
        }

        void EndStroke()
        {
            cellsDirty = true;
            previewDirty = true;
            app.MarkDirty();
        }

        void Fill(int sx, int sz, int value)
        {
            int target = L.Get(Floor, sx, sz);
            if (target == value) return;
            var q = new Queue<int>();
            q.Enqueue(sz * L.width + sx);
            L.Set(Floor, sx, sz, value);
            int guard = 0;
            while (q.Count > 0 && guard++ < 4000000)
            {
                int c = q.Dequeue();
                int x = c % L.width, z = c / L.width;
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + (n == 0 ? 1 : n == 1 ? -1 : 0), nz = z + (n == 2 ? 1 : n == 3 ? -1 : 0);
                    if (!L.InRange(Floor, nx, nz) || L.Get(Floor, nx, nz) != target) continue;
                    L.Set(Floor, nx, nz, value);
                    q.Enqueue(nz * L.width + nx);
                }
            }
        }

        //reczne waypointy lpm dodaje a shift z lpm usuwa najblizszy
        void HandleWaypointTool(bool overUi)
        {
            if (!Input.GetMouseButtonDown(0) || overUi || !hoverValid) return;
            bool remove = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (remove)
            {
                int best = -1; float bd = 1.5f * 1.5f;
                for (int i = 0; i < P.waypoints.Count; i++)
                {
                    var w = P.waypoints[i];
                    if (w.floor != Floor) continue;
                    float d = (w.x - hoverWorld.x) * (w.x - hoverWorld.x) + (w.z - hoverWorld.z) * (w.z - hoverWorld.z);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) return;
                PushUndo();
                P.waypoints.RemoveAt(best);
            }
            else
            {
                PushUndo();
                P.waypoints.Add(new ManualWaypoint { floor = Floor, x = hoverWorld.x, z = hoverWorld.z });
            }
            previewDirty = true;
            app.MarkDirty();
        }

        //budowanie siatek nakladki

        void BuildCells()
        {
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            float y = L.FloorY(Floor), cs = L.cellSize;
            int R = L.rooms.Count;
            var sumX = new float[R]; var sumZ = new float[R]; var cnt = new int[R];
            for (int z = 0; z < L.depth; z++)
            {
                int x = 0;
                while (x < L.width)
                {
                    int val = L.Get(Floor, x, z);
                    if (val == 0 || val > R) { x++; continue; }
                    int x0 = x;
                    while (x < L.width && L.Get(Floor, x, z) == val) x++;
                    var def = L.rooms[val - 1];
                    var col = def.color;
                    col.a = def.type.IsPassage() ? 0.8f : 0.5f;
                    Quad(v, c, t, L.originX + x0 * cs, L.originZ + z * cs, L.originX + x * cs, L.originZ + (z + 1) * cs, y, col);
                    int n = x - x0;
                    sumX[val - 1] += (x0 + n * 0.5f) * n; sumZ[val - 1] += (z + 0.5f) * n; cnt[val - 1] += n;
                }
            }
            //reczne waypointy rysujemy jako male romby
            foreach (var w in P.waypoints)
            {
                if (w.floor != Floor) continue;
                Diamond(v, c, t, w.x, w.z, y + 0.01f, 0.35f, new Color(1f, 0.95f, 0.2f, 1f));
            }
            Apply(cellsMesh, v, c, t, MeshTopology.Triangles);

            labels.Clear();
            for (int d = 0; d < R; d++)
            {
                if (cnt[d] == 0 || L.rooms[d].type.IsPassage()) continue;
                labels.Add(new Label3
                {
                    pos = new Vector3(L.originX + sumX[d] / cnt[d] * cs, y, L.originZ + sumZ[d] / cnt[d] * cs),
                    text = L.rooms[d].name
                });
            }
        }

        void BuildLines()
        {
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            float y = L.FloorY(Floor), cs = L.cellSize;
            float x0 = L.originX, z0 = L.originZ, x1 = L.originX + L.width * cs, z1 = L.originZ + L.depth * cs;

            if (showGrid)
            {
                //przy duzej siatce rysujemy co piata linie bo inaczej robi sie szaro
                int step = Mathf.Max(L.width, L.depth) > 250 ? 5 : 1;
                var gc = new Color(1f, 1f, 1f, 0.13f);
                var gc5 = new Color(1f, 1f, 1f, 0.28f);
                for (int x = 0; x <= L.width; x += step) Line(v, c, t, new Vector3(x0 + x * cs, y, z0), new Vector3(x0 + x * cs, y, z1), x % 10 == 0 ? gc5 : gc);
                for (int z = 0; z <= L.depth; z += step) Line(v, c, t, new Vector3(x0, y, z0 + z * cs), new Vector3(x1, y, z0 + z * cs), z % 10 == 0 ? gc5 : gc);
            }
            var bc = new Color(1f, 0.85f, 0.2f, 0.9f);
            Line(v, c, t, new Vector3(x0, y, z0), new Vector3(x1, y, z0), bc);
            Line(v, c, t, new Vector3(x1, y, z0), new Vector3(x1, y, z1), bc);
            Line(v, c, t, new Vector3(x1, y, z1), new Vector3(x0, y, z1), bc);
            Line(v, c, t, new Vector3(x0, y, z1), new Vector3(x0, y, z0), bc);

            //podglad wygenerowanych waypointow i polaczen
            if (showPreview || tab == Tab.Waypoints)
            {
                if (previewDirty || preview == null) preview = LayoutBaker.Compute(L, P.waypoints);
                var lc = new Color(0.2f, 0.9f, 1f, 0.85f);
                var nc = new Color(1f, 1f, 1f, 0.95f);
                for (int i = 0; i < preview.nodes.Count; i++)
                {
                    var n = preview.nodes[i];
                    if (n.f != Floor || n.manual) continue;
                    var a = new Vector3(n.pos.x, y + 0.02f, n.pos.z);
                    foreach (int j in n.links)
                    {
                        if (j < i) continue;
                        var b = preview.nodes[j];
                        Line(v, c, t, a, new Vector3(b.pos.x, y + 0.02f, b.pos.z), lc);
                    }
                    float r = n.passage ? 0.15f : 0.25f;
                    Line(v, c, t, a + new Vector3(-r, 0, 0), a + new Vector3(r, 0, 0), nc);
                    Line(v, c, t, a + new Vector3(0, 0, -r), a + new Vector3(0, 0, r), nc);
                }
                foreach (var con in preview.connectors)
                {
                    var na = preview.nodes[con.a];
                    var nb = preview.nodes[con.b];
                    if (na.f != Floor && nb.f != Floor) continue;
                    var cc = new Color(1f, 0.3f, 1f, 1f);
                    var p = new Vector3(na.pos.x, y + 0.02f, na.pos.z);
                    var q = new Vector3(nb.pos.x, y + 0.02f, nb.pos.z);
                    //bieg schodow linia od dolu do gory ze strzalka w strone gory
                    if ((q - p).sqrMagnitude > 0.01f)
                    {
                        Line(v, c, t, p, q, cc);
                        var dir = (q - p).normalized;
                        var side = new Vector3(-dir.z, 0f, dir.x);
                        Line(v, c, t, q, q - dir * 0.5f + side * 0.35f, cc);
                        Line(v, c, t, q, q - dir * 0.5f - side * 0.35f, cc);
                    }
                    Line(v, c, t, p + new Vector3(-0.3f, 0, -0.3f), p + new Vector3(0.3f, 0, 0.3f), cc);
                    Line(v, c, t, p + new Vector3(-0.3f, 0, 0.3f), p + new Vector3(0.3f, 0, -0.3f), cc);
                }
            }
            Apply(linesMesh, v, c, t, MeshTopology.Lines);
        }

        void BuildCursor()
        {
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            if (hoverValid && !MouseOverUi() && tab != Tab.Waypoints)
            {
                float y = L.FloorY(Floor), cs = L.cellSize;
                int x0, x1, z0, z1;
                if (tool == Tool.Rect && painting)
                {
                    x0 = Mathf.Min(startX, hoverX); x1 = Mathf.Max(startX, hoverX);
                    z0 = Mathf.Min(startZ, hoverZ); z1 = Mathf.Max(startZ, hoverZ);
                }
                else
                {
                    int s = (tool == Tool.Brush || tool == Tool.Eraser) ? brushSize : 1;
                    x0 = hoverX - (s - 1) / 2; z0 = hoverZ - (s - 1) / 2; x1 = x0 + s - 1; z1 = z0 + s - 1;
                }
                int val = PaintValue();
                Color col = val > 0 ? L.rooms[val - 1].color : new Color(1f, 0.2f, 0.2f);
                col.a = 0.45f;
                Quad(v, c, t, L.originX + x0 * cs, L.originZ + z0 * cs, L.originX + (x1 + 1) * cs, L.originZ + (z1 + 1) * cs, y, col);
            }
            Apply(cursorMesh, v, c, t, MeshTopology.Triangles);
        }

        static void Quad(List<Vector3> v, List<Color32> c, List<int> t, float x0, float z0, float x1, float z1, float y, Color col)
        {
            int b = v.Count;
            v.Add(new Vector3(x0, y, z0)); v.Add(new Vector3(x0, y, z1)); v.Add(new Vector3(x1, y, z1)); v.Add(new Vector3(x1, y, z0));
            Color32 cc = col;
            c.Add(cc); c.Add(cc); c.Add(cc); c.Add(cc);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        static void Diamond(List<Vector3> v, List<Color32> c, List<int> t, float x, float z, float y, float r, Color col)
        {
            int b = v.Count;
            v.Add(new Vector3(x - r, y, z)); v.Add(new Vector3(x, y, z + r)); v.Add(new Vector3(x + r, y, z)); v.Add(new Vector3(x, y, z - r));
            Color32 cc = col;
            c.Add(cc); c.Add(cc); c.Add(cc); c.Add(cc);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        static void Line(List<Vector3> v, List<Color32> c, List<int> t, Vector3 a, Vector3 b, Color col)
        {
            int i = v.Count;
            v.Add(a); v.Add(b);
            Color32 cc = col;
            c.Add(cc); c.Add(cc);
            t.Add(i); t.Add(i + 1);
        }

        static void Apply(Mesh m, List<Vector3> v, List<Color32> c, List<int> t, MeshTopology topo)
        {
            m.Clear();
            m.SetVertices(v);
            m.SetColors(c);
            m.SetIndices(t, topo, 0);
            m.RecalculateBounds();
        }

        //interfejs

        public void DrawGUI()
        {
            if (app == null || app.Project == null || app.Building == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
                hintStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, richText = true };
            }

            if (showLabels) DrawRoomLabels();
            DrawTopBar();
            DrawLeftPanel();
            DrawStatusBar();
        }

        void DrawRoomLabels()
        {
            var cam = app.Rig.Cam;
            foreach (var l in labels)
            {
                Vector3 sp = cam.WorldToScreenPoint(l.pos);
                if (sp.z <= 0f) continue;
                var r = new Rect(sp.x - 100, Screen.height - sp.y - 12, 200, 24);
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), l.text, labelStyle);
                GUI.color = Color.white;
                GUI.Label(r, l.text, labelStyle);
                GUI.color = prev;
            }
        }

        void DrawTopBar()
        {
            topBar = new Rect(0, 0, Screen.width, 44);
            GUILayout.BeginArea(topBar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            string title = (string.IsNullOrEmpty(P.name) ? "Bez nazwy" : P.name) + (app.Dirty ? " *" : "");
            GUILayout.Label("<b>" + title + "</b>", GUILayout.MaxWidth(260));
            if (GUILayout.Button("Zapisz", GUILayout.Width(80))) app.SaveProject(null);
            if (GUILayout.Button("Zapisz jako", GUILayout.Width(105))) app.SaveProjectAs(null);
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
            if (GUILayout.Button("► Symulacja", GUILayout.Width(120))) app.SaveAndSimulate();
            GUI.backgroundColor = prev;
            if (GUILayout.Button("Menu", GUILayout.Width(70))) app.GoToMenu();
            GUILayout.Space(16);

            GUILayout.Label("Piętro:", GUILayout.Width(55));
            for (int f = 0; f < L.floors; f++)
            {
                if (f == Floor) GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button(L.FloorLabel(f), GUILayout.MinWidth(70))) SetFloor(f);
                GUI.backgroundColor = prev;
            }
            GUILayout.Space(16);
            bool plan = app.Rig.Mode == CameraRig.View.Plan;
            if (plan) GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Rzut", GUILayout.Width(60))) app.Rig.SetMode(CameraRig.View.Plan);
            GUI.backgroundColor = !plan ? Color.green : prev;
            if (GUILayout.Button("3D", GUILayout.Width(50))) app.Rig.SetMode(CameraRig.View.Free);
            GUI.backgroundColor = prev;
            if (!plan) showWholeBuilding = GUILayout.Toggle(showWholeBuilding, "Cały budynek", GUILayout.Width(120));
            if (GUILayout.Button("Pokaż całość", GUILayout.Width(115))) app.Rig.Frame(app.Building.Bounds);
            GUILayout.FlexibleSpace();
            GUI.enabled = undo.Count > 0;
            if (GUILayout.Button("Cofnij", GUILayout.Width(70))) Undo();
            GUI.enabled = redo.Count > 0;
            if (GUILayout.Button("Ponów", GUILayout.Width(70))) Redo();
            GUI.enabled = true;
            if (GUILayout.Button("Komunikaty" + (LogPanel.Unread > 0 ? " (" + LogPanel.Unread + ")" : ""), GUILayout.Width(130))) LogPanel.Visible = !LogPanel.Visible;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawStatusBar()
        {
            statusBar = new Rect(0, Screen.height - 28, Screen.width, 28);
            GUILayout.BeginArea(statusBar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (hoverValid)
            {
                var def = L.DefAt(Floor, hoverX, hoverZ);
                GUILayout.Label("Komórka " + hoverX + ", " + hoverZ + "   " + (def != null ? def.name + " (" + Labels.Room(def.type) + ")" : "puste"), GUILayout.Width(380));
            }
            else GUILayout.Label("Poza siatką", GUILayout.Width(380));
            string help = tab == Tab.Waypoints
                ? "LPM: dodaj waypoint   Shift+LPM: usuń   PPM / środkowy: przesuwanie   kółko: zoom"
                : "LPM: maluj   Shift+LPM: gumka   PPM / środkowy: przesuwanie   kółko: zoom   Ctrl+kółko: prędkość   Ctrl+Z: cofnij   PgUp/PgDn: piętro   Tab: rzut/3D";
            GUILayout.Label(help);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawLeftPanel()
        {
            leftPanel = new Rect(0, topBar.height, 400, Screen.height - topBar.height - 28);
            GUILayout.BeginArea(leftPanel, GUI.skin.box);
            var newTab = (Tab)GUILayout.SelectionGrid((int)tab, TabNames, 3);
            if (newTab != tab)
            {
                tab = newTab;
                if (tab == Tab.Check) check = LayoutBaker.Compute(L, P.waypoints);
                previewDirty = true;
                GUIUtility.keyboardControl = 0;
            }
            GUILayout.Space(4);
            leftScroll = GUILayout.BeginScrollView(leftScroll);
            switch (tab)
            {
                case Tab.Model: DrawModelTab(); break;
                case Tab.Grid: DrawGridTab(); break;
                case Tab.Rooms: DrawRoomsTab(); break;
                case Tab.Waypoints: DrawWaypointsTab(); break;
                case Tab.Sim: DrawSimTab(); break;
                case Tab.Check: DrawCheckTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Hint(string text)
        {
            GUILayout.Label(text, hintStyle);
        }

        void DrawModelTab()
        {
            GUILayout.Label("<b>Projekt</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Nazwa", GUILayout.Width(60));
            string n = GUILayout.TextField(P.name ?? "");
            if (n != P.name) { P.name = n; app.MarkDirty(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Plik: " + (app.ProjectPath ?? "jeszcze nie zapisany"), hintStyle);
            if (!string.IsNullOrEmpty(P.sourceModel)) GUILayout.Label("Model źródłowy: " + P.sourceModel, hintStyle);

            GUILayout.Space(8);
            GUILayout.Label("<b>Model</b>");
            var b = app.Building.Bounds;
            GUILayout.Label("Trójkąty: " + app.Model.TriangleCount.ToString("N0") + "   części: " + app.Model.meshes.Count, hintStyle);
            GUILayout.Label("Wymiary: " + b.size.x.ToString("0.0") + " x " + b.size.z.ToString("0.0") + " m, wysokość " + b.size.y.ToString("0.0") + " m", hintStyle);
            float big = Mathf.Max(b.size.x, b.size.z);
            if (big > 1000f) Hint("<color=#ffcc55>Budynek ma ponad kilometr. Model jest pewnie w milimetrach lub centymetrach - użyj przycisków skali poniżej.</color>");
            else if (big < 5f) Hint("<color=#ffcc55>Budynek jest bardzo mały. Model może być w kilometrach albo źle przeskalowany.</color>");

            GUILayout.Space(6);
            Hint("Skala i obrót zmieniają cały model. Rób to na początku, przed malowaniem sal, bo namalowana siatka się nie przesuwa.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("mm → m")) ModelOp(() => app.Model.Scale(0.001f));
            if (GUILayout.Button("cm → m")) ModelOp(() => app.Model.Scale(0.01f));
            if (GUILayout.Button("cale → m")) ModelOp(() => app.Model.Scale(0.0254f));
            if (GUILayout.Button("x10")) ModelOp(() => app.Model.Scale(10f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Obrót X +90")) ModelOp(() => app.Model.RotateX90(1));
            if (GUILayout.Button("Obrót X -90")) ModelOp(() => app.Model.RotateX90(-1));
            if (GUILayout.Button("Obrót Y 90")) ModelOp(() => app.Model.RotateY90(1));
            GUILayout.EndHorizontal();
            Hint("Jeśli budynek leży na boku, to był zapisany z osią Z w górę - użyj 'Obrót X'.");
            if (GUILayout.Button("Wyśrodkuj i postaw na ziemi")) ModelOp(() => app.Model.CenterOnGround());
        }

        void ModelOp(Action op)
        {
            op();
            app.RebuildBuilding();
            FitGridToModel();
            app.Rig.Frame(app.Building.Bounds);
            app.MarkDirty();
        }

        void LoadArea()
        {
            float margin = gridArea != null ? gridArea.fitMargin : 8f;
            gridArea = new GridArea { startX = L.originX, startZ = L.originZ, sizeX = L.width * L.cellSize, sizeZ = L.depth * L.cellSize, fitMargin = margin };
        }

        //proponowany rozmiar komorki zeby budynek nie mial ani za malo ani za duzo komorek
        public static float SuggestCellSize(float buildingSize)
        {
            if (buildingSize < 60f) return 0.5f;
            if (buildingSize < 300f) return 1f;
            if (buildingSize < 700f) return 2f;
            return 3f;
        }

        //zmienia obszar siatki a namalowane sale zostaja tam gdzie byly w budynku
        void SetArea(float ox, float oz, float sx, float sz, float cs)
        {
            cs = Mathf.Clamp(cs, 0.1f, 5f);
            int w = Mathf.Clamp(Mathf.CeilToInt(sx / cs - 1e-3f), 1, 2000), d = Mathf.Clamp(Mathf.CeilToInt(sz / cs - 1e-3f), 1, 2000);
            PushUndo();
            L.Resample(ox, oz, cs, w, d);
            gridEdit.cellSize = cs;
            LoadArea();
            MarkAllDirty();
            app.MarkDirty();
        }

        //dodaje albo zabiera pas komorek z jednej strony
        void GrowEdge(int dx0, int dz0, int dx1, int dz1)
        {
            float cs = L.cellSize;
            float ox = L.originX - dx0 * cs, oz = L.originZ - dz0 * cs;
            float sx = (L.width + dx0 + dx1) * cs, sz = (L.depth + dz0 + dz1) * cs;
            if (sx < cs || sz < cs) return;
            SetArea(ox, oz, sx, sz, cs);
        }

        void FitGridToModel()
        {
            var b = app.Building.Bounds;
            float margin = gridArea != null ? gridArea.fitMargin : 8f;
            float cs = L.cellSize;
            float ox = Mathf.Floor((b.min.x - margin) / cs) * cs, oz = Mathf.Floor((b.min.z - margin) / cs) * cs;
            int w = Mathf.CeilToInt((b.max.x + margin - ox) / cs), d = Mathf.CeilToInt((b.max.z + margin - oz) / cs);
            L.Resample(ox, oz, cs, w, d);
            if (gridArea != null) LoadArea();
            MarkAllDirty();
        }

        void DrawGridTab()
        {
            GUILayout.Label("<b>Piętra i siatka</b>");
            Hint("Ustaw tu, na jakich wysokościach są podłogi. Widok piętra pokazuje model przecięty na wysokości przekroju, jak rzut architektoniczny.");
            if (GUILayout.Button("Wykryj piętra automatycznie")) detected = FloorDetector.Detect(app.Model);
            if (detected != null)
            {
                if (!detected.ok) Hint("<color=#ffcc55>Nie udało się wykryć pięter - wpisz wartości ręcznie.</color>");
                else
                {
                    var lv = new List<string>();
                    foreach (var l in detected.levels) lv.Add(l.ToString("0.00"));
                    Hint("Wykryto " + detected.floors + " piętra, wysokość piętra " + detected.floorHeight.ToString("0.00") + " m, poziomy podłóg: " + string.Join(", ", lv) + " m");
                    if (GUILayout.Button("Użyj wykrytych wartości"))
                    {
                        gridEdit.floors = detected.floors; gridEdit.floorHeight = detected.floorHeight; gridEdit.groundY = detected.groundY;
                        ApplyGrid();
                        detected = null;
                    }
                }
            }
            GUILayout.Space(6);
            smallGui.DrawObject(gridEdit, "grid");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zastosuj", GUILayout.Height(28))) ApplyGrid();
            if (GUILayout.Button("Przywróć", GUILayout.Height(28))) LoadEdits();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>Nazwy pięter</b>");
            for (int f = 0; f < L.floors; f++)
            {
                while (L.floorNames.Count <= f) L.floorNames.Add("");
                GUILayout.BeginHorizontal();
                GUILayout.Label(f.ToString(), GUILayout.Width(24));
                string s = GUILayout.TextField(L.floorNames[f] ?? "");
                if (s != L.floorNames[f]) { L.floorNames[f] = s; app.MarkDirty(); }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            DrawGridArea();
        }

        void DrawGridArea()
        {
            GUILayout.Label("<b>Obszar siatki</b>");
            var b = app.Building.Bounds;
            GUILayout.Label("Siatka " + L.width + " x " + L.depth + " komórek po " + L.cellSize.ToString("0.##") + " m = " +
                            (L.width * L.cellSize).ToString("0.#") + " x " + (L.depth * L.cellSize).ToString("0.#") + " m", hintStyle);
            GUILayout.Label("Budynek: " + b.size.x.ToString("0.#") + " x " + b.size.z.ToString("0.#") + " m (X od " + b.min.x.ToString("0.#") +
                            ", Z od " + b.min.z.ToString("0.#") + ")", hintStyle);
            Hint("Siatkę można dopasować do budynku o dowolnych proporcjach. Namalowane sale zostają w tym samym miejscu budynku. Komórki, które wypadną poza nowy obszar, są czyszczone.");

            if (GUILayout.Button("Dopasuj siatkę do modelu (zapas " + gridArea.fitMargin.ToString("0.#") + " m)")) { PushUndo(); FitGridToModel(); app.MarkDirty(); }
            float suggested = SuggestCellSize(Mathf.Max(b.size.x, b.size.z));
            if (!Mathf.Approximately(suggested, L.cellSize) && GUILayout.Button("Dopasuj też komórkę do wielkości budynku (" + suggested.ToString("0.##") + " m)"))
            {
                gridEdit.cellSize = suggested;
                float margin = gridArea.fitMargin;
                float ox = Mathf.Floor((b.min.x - margin) / suggested) * suggested, oz = Mathf.Floor((b.min.z - margin) / suggested) * suggested;
                SetArea(ox, oz, b.max.x + margin - ox, b.max.z + margin - oz, suggested);
            }

            GUILayout.Space(4);
            areaGui.DrawObject(gridArea, "area");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zastosuj obszar", GUILayout.Height(26))) SetArea(gridArea.startX, gridArea.startZ, gridArea.sizeX, gridArea.sizeZ, L.cellSize);
            if (GUILayout.Button("Przywróć", GUILayout.Height(26))) LoadArea();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Krawędzie (o jedną komórkę):", hintStyle);
            EdgeRow("Góra (Z+)", 0, 0, 0, 1);
            EdgeRow("Dół (Z-)", 0, 1, 0, 0);
            EdgeRow("Lewo (X-)", 1, 0, 0, 0);
            EdgeRow("Prawo (X+)", 0, 0, 1, 0);

            GUILayout.Space(4);
            GUILayout.Label("Przesuń całą siatkę (wyrównanie do ścian):", hintStyle);
            GUILayout.BeginHorizontal();
            float q = L.cellSize * 0.25f;
            if (GUILayout.Button("◄ ¼")) SetArea(L.originX - q, L.originZ, L.width * L.cellSize, L.depth * L.cellSize, L.cellSize);
            if (GUILayout.Button("► ¼")) SetArea(L.originX + q, L.originZ, L.width * L.cellSize, L.depth * L.cellSize, L.cellSize);
            if (GUILayout.Button("▼ ¼")) SetArea(L.originX, L.originZ - q, L.width * L.cellSize, L.depth * L.cellSize, L.cellSize);
            if (GUILayout.Button("▲ ¼")) SetArea(L.originX, L.originZ + q, L.width * L.cellSize, L.depth * L.cellSize, L.cellSize);
            GUILayout.EndHorizontal();
            Hint("Przesuwanie po namalowaniu sal przelicza je na nowe komórki, więc najlepiej wyrównaj siatkę przed malowaniem.");
        }

        void EdgeRow(string label, int x0, int z0, int x1, int z1)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            if (GUILayout.Button("+1")) GrowEdge(x0, z0, x1, z1);
            if (GUILayout.Button("+5")) GrowEdge(x0 * 5, z0 * 5, x1 * 5, z1 * 5);
            if (GUILayout.Button("-1")) GrowEdge(-x0, -z0, -x1, -z1);
            if (GUILayout.Button("-5")) GrowEdge(-x0 * 5, -z0 * 5, -x1 * 5, -z1 * 5);
            GUILayout.EndHorizontal();
        }

        void ApplyGrid()
        {
            PushUndo();
            var g = gridEdit;
            if (!Mathf.Approximately(g.cellSize, L.cellSize))
            {
                float sx = L.width * L.cellSize, sz = L.depth * L.cellSize;
                L.Resample(L.originX, L.originZ, g.cellSize, Mathf.CeilToInt(sx / g.cellSize), Mathf.CeilToInt(sz / g.cellSize));
            }
            if (g.floors != L.floors) L.Resize(L.width, L.depth, g.floors);
            L.floorHeight = g.floorHeight;
            L.originY = g.groundY;
            L.cutHeight = g.cutHeight;
            LoadArea();
            app.RebuildBuilding();
            SetFloor(Floor);
            MarkAllDirty();
            app.MarkDirty();
        }

        void DrawRoomsTab()
        {
            GUILayout.Label("<b>Narzędzie</b>");
            tool = (Tool)GUILayout.SelectionGrid((int)tool, ToolNames, 3);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rozmiar pędzla", GUILayout.Width(110));
            foreach (int s in BrushSizes)
            {
                var prev = GUI.backgroundColor;
                if (brushSize == s) GUI.backgroundColor = Color.green;
                if (GUILayout.Button(s + "x" + s)) brushSize = s;
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            showGrid = Toggle(showGrid, "Linie siatki", ref gridDirty);
            showLabels = GUILayout.Toggle(showLabels, "Nazwy sal");
            showPreview = Toggle(showPreview, "Waypointy", ref previewDirty);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Dodaj salę</b>");
            var types = (RoomType[])Enum.GetValues(typeof(RoomType));
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var t in types)
            {
                if (GUILayout.Button(new GUIContent(Labels.Room(t), Labels.RoomHint(t)), GUILayout.Width(120))) AddRoom(t);
                if (++col % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Sale</b>  (kliknij kolor, żeby malować tą salą)");
            var counts = L.CountCells();
            for (int i = 0; i < L.rooms.Count; i++)
            {
                var r = L.rooms[i];
                bool sel = i == brush;
                GUILayout.BeginHorizontal(sel ? GUI.skin.button : GUI.skin.box);
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = r.color;
                if (GUILayout.Button(sel ? "●" : "", GUILayout.Width(28), GUILayout.Height(22))) { brush = i; if (tool == Tool.Eraser || tool == Tool.Pick) tool = Tool.Brush; }
                GUI.backgroundColor = prev;
                string nn = GUILayout.TextField(r.name, GUILayout.Width(150));
                if (nn != r.name) { r.name = nn; app.MarkDirty(); cellsDirty = true; }
                GUILayout.Label(Labels.Room(r.type), GUILayout.Width(100));
                GUILayout.Label((counts.Length > i + 1 ? counts[i + 1] : 0).ToString(), GUILayout.Width(40));
                GUILayout.EndHorizontal();
                if (sel) DrawRoomDetails(i, r);
            }
            if (L.rooms.Count == 0) Hint("Nie ma jeszcze sal. Dodaj je przyciskami powyżej.");

            GUILayout.Space(8);
            GUILayout.Label("<b>Piętro</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Kopiuj to piętro na:", GUILayout.Width(150));
            for (int f = 0; f < L.floors; f++)
            {
                if (f == Floor) continue;
                if (GUILayout.Button(L.FloorLabel(f))) CopyFloor(Floor, f);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Wyczyść to piętro")) { PushUndo(); for (int z = 0; z < L.depth; z++) for (int x = 0; x < L.width; x++) L.Set(Floor, x, z, 0); EndStroke(); }

            GUILayout.Space(8);
            Hint("<b>Zasady:</b>\n• Sale łączą się ze sobą <b>tylko przez drzwi</b>. Między korytarzem a salą namaluj komórki 'Drzwi' w otworze ściany.\n" +
                 "• Schody i windę namaluj w tym samym miejscu na sąsiednich piętrach.\n• Oddział: liczba łóżek to pojemność.\n" +
                 "• Wejście i miejsce zbiórki mogą być na zewnątrz budynku. Też muszą łączyć się drzwiami.");
            if (!string.IsNullOrEmpty(GUI.tooltip)) Hint("<color=#aaddff>" + GUI.tooltip + "</color>");
        }

        bool Toggle(bool v, string text, ref bool dirtyFlag)
        {
            bool n = GUILayout.Toggle(v, text);
            if (n != v) dirtyFlag = true;
            return n;
        }

        void DrawRoomDetails(int i, RoomDef r)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            Hint(Labels.RoomHint(r.type));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Typ", GUILayout.Width(110));
            var types = (RoomType[])Enum.GetValues(typeof(RoomType));
            int ti = Array.IndexOf(types, r.type);
            if (GUILayout.Button("◄", GUILayout.Width(28))) { PushUndo(); r.type = types[(ti - 1 + types.Length) % types.Length]; RoomChanged(); }
            GUILayout.Label(Labels.Room(r.type), GUI.skin.box);
            if (GUILayout.Button("►", GUILayout.Width(28))) { PushUndo(); r.type = types[(ti + 1) % types.Length]; RoomChanged(); }
            GUILayout.EndHorizontal();

            if (r.type == RoomType.Ward)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Specjalizacja", GUILayout.Width(110));
                var specs = (Specialty[])Enum.GetValues(typeof(Specialty));
                int si = Array.IndexOf(specs, r.specialty);
                if (GUILayout.Button("◄", GUILayout.Width(28))) { r.specialty = specs[(si - 1 + specs.Length) % specs.Length]; app.MarkDirty(); }
                GUILayout.Label(Labels.Spec(r.specialty), GUI.skin.box);
                if (GUILayout.Button("►", GUILayout.Width(28))) { r.specialty = specs[(si + 1) % specs.Length]; app.MarkDirty(); }
                GUILayout.EndHorizontal();
                r.minDoctors = Stepper("Min. lekarzy", r.minDoctors, 0, 50);
                int beds = r.capacity < 0 ? 3 : r.capacity;
                int nb = Stepper("Łóżka (0 = bez limitu)", beds, 0, 500);
                if (nb != beds) { r.capacity = nb; previewDirty = true; }
            }
            else if (r.type == RoomType.Stairs)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("W górę prowadzi", GUILayout.Width(110));
                var dirs = (StairsDir[])Enum.GetValues(typeof(StairsDir));
                int di = Array.IndexOf(dirs, r.stairsUp);
                if (GUILayout.Button("◄", GUILayout.Width(28))) { PushUndo(); r.stairsUp = dirs[(di - 1 + dirs.Length) % dirs.Length]; RoomChanged(); }
                GUILayout.Label(Labels.Stairs(r.stairsUp), GUI.skin.box);
                if (GUILayout.Button("►", GUILayout.Width(28))) { PushUndo(); r.stairsUp = dirs[(di + 1) % dirs.Length]; RoomChanged(); }
                GUILayout.EndHorizontal();
                Hint(r.stairsUp == StairsDir.SameCell
                    ? "Stary sposób: schody namalowane w tym samym miejscu na obu piętrach, agent przenosi się pionowo."
                    : "Bieg schodów: pomaluj go tylko na niższym piętrze, w kierunku wejścia w górę. Za dolnym końcem musi być korytarz lub klatka schodowa, a na wyższym piętrze za górnym końcem podest. Agenci idą po biegu po skosie.");
            }
            else if (r.type == RoomType.WaitingRoom)
            {
                int cap = r.capacity < 0 ? 0 : r.capacity;
                int nc = Stepper("Miejsca (0 = bez limitu)", cap, 0, 5000);
                if (nc != cap) r.capacity = nc;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Kolor", GUILayout.Width(110));
            var c = r.color;
            float cr = GUILayout.HorizontalSlider(c.r, 0f, 1f), cg = GUILayout.HorizontalSlider(c.g, 0f, 1f), cb = GUILayout.HorizontalSlider(c.b, 0f, 1f);
            GUILayout.EndHorizontal();
            var nc2 = new Color(cr, cg, cb, 1f);
            if (nc2 != c) { r.color = nc2; cellsDirty = true; app.MarkDirty(); }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("W górę listy") && i > 0) { PushUndo(); SwapRooms(i, i - 1); brush = i - 1; }
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button(deleteConfirm == i ? "Na pewno usunąć?" : "Usuń salę"))
            {
                if (deleteConfirm == i)
                {
                    PushUndo();
                    L.RemoveRoom(i);
                    brush = Mathf.Clamp(brush, 0, Mathf.Max(0, L.rooms.Count - 1));
                    deleteConfirm = -1;
                    EndStroke();
                }
                else deleteConfirm = i;
            }
            GUI.backgroundColor = prev;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void RoomChanged()
        {
            cellsDirty = previewDirty = true;
            app.MarkDirty();
        }

        int Stepper(string label, int v, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            int n = v;
            if (GUILayout.Button("-", GUILayout.Width(28))) n--;
            GUILayout.Label(v.ToString(), GUI.skin.box, GUILayout.Width(50));
            if (GUILayout.Button("+", GUILayout.Width(28))) n++;
            GUILayout.EndHorizontal();
            n = Mathf.Clamp(n, min, max);
            if (n != v) app.MarkDirty();
            return n;
        }

        void SwapRooms(int a, int b)
        {
            var t = L.rooms[a]; L.rooms[a] = L.rooms[b]; L.rooms[b] = t;
            for (int i = 0; i < L.cells.Length; i++)
            {
                if (L.cells[i] == a + 1) L.cells[i] = (ushort)(b + 1);
                else if (L.cells[i] == b + 1) L.cells[i] = (ushort)(a + 1);
            }
            EndStroke();
        }

        public void AddRoom(RoomType t)
        {
            PushUndo();
            int same = 0;
            foreach (var d in L.rooms) if (d.type == t) same++;
            var r = new RoomDef
            {
                name = DefaultName(t, same + 1),
                type = t,
                color = DefaultColor(t, same),
                capacity = t == RoomType.Ward ? 4 : -1
            };
            L.rooms.Add(r);
            brush = L.rooms.Count - 1;
            tool = Tool.Brush;
            app.MarkDirty();
        }

        void CopyFloor(int from, int to)
        {
            PushUndo();
            for (int z = 0; z < L.depth; z++) for (int x = 0; x < L.width; x++) L.Set(to, x, z, L.Get(from, x, z));
            EndStroke();
        }

        public static string DefaultName(RoomType t, int n)
        {
            string b = Labels.Room(t);
            if (t == RoomType.Door || t == RoomType.Stairs || t == RoomType.Elevator) return n == 1 ? b : b + " " + n;
            if (t == RoomType.Ward) return "Oddział " + n;
            return n == 1 ? b : b + " " + n;
        }

        public static Color DefaultColor(RoomType t, int idx)
        {
            float h, s = 0.75f;
            switch (t)
            {
                case RoomType.Ward: h = 0.0f + 0.04f * (idx % 3); break;
                case RoomType.WaitingRoom: h = 0.15f; break;
                case RoomType.StaffRoom: h = 0.6f; break;
                case RoomType.Entrance: h = 0.33f; break;
                case RoomType.Exit: h = 0.85f; break;
                case RoomType.AssemblyPoint: h = 0.08f; break;
                case RoomType.Door: h = 0.12f; s = 0.35f; break;
                case RoomType.Stairs: h = 0.5f; break;
                case RoomType.Elevator: h = 0.75f; break;
                case RoomType.Corridor: h = 0f; s = 0f; break;
                default: h = 0.95f; s = 0.3f; break;
            }
            return Color.HSVToRGB(h, s, Mathf.Clamp01(0.95f - 0.12f * (idx % 3)));
        }

        void DrawWaypointsTab()
        {
            GUILayout.Label("<b>Waypointy</b>");
            Hint("Waypointy to punkty, po których chodzą agenci. Program stawia je sam na podstawie namalowanych sal (białe krzyżyki, linie to połączenia). " +
                 "Możesz dodać własne punkty, np. dokładne miejsca łóżek - kliknij na planie. Shift+klik usuwa punkt.");
            GUILayout.Space(4);
            var before = JsonUtility.ToJson(wpEdit);
            smallGui.DrawObject(wpEdit, "wp");
            if (JsonUtility.ToJson(wpEdit) != before)
            {
                L.waypointSpacing = wpEdit.waypointSpacing;
                L.wardWaypointSpacing = wpEdit.wardWaypointSpacing;
                L.waypointHeight = wpEdit.waypointHeight;
                previewDirty = true;
                app.MarkDirty();
            }
            if (preview != null)
            {
                int gen = 0, man = 0;
                foreach (var n in preview.nodes) { if (n.manual) man++; else gen++; }
                GUILayout.Label("Wygenerowane: " + gen + "   ręczne: " + man + "   połączenia pięter: " + preview.connectors.Count, hintStyle);
                if (preview.nodes.Count > 1500) Hint("<color=#ffcc55>Dużo waypointów. Start symulacji może potrwać - zwiększ odstęp.</color>");
            }
            if (P.waypoints.Count > 0 && GUILayout.Button("Usuń wszystkie ręczne waypointy na tym piętrze"))
            {
                PushUndo();
                P.waypoints.RemoveAll(w => w.floor == Floor);
                previewDirty = true; cellsDirty = true;
                app.MarkDirty();
            }
        }

        void DrawSimTab()
        {
            GUILayout.Label("<b>Ustawienia symulacji</b>");
            Hint("Te ustawienia zapisują się w pliku projektu. Kliknij nazwę sekcji, żeby ją rozwinąć. Najedź na nazwę pola, żeby zobaczyć opis.");
            settingsGui.Changed = false;
            settingsGui.DrawSections(P.settings);
            if (settingsGui.Changed) app.MarkDirty();
            GUILayout.Space(6);
            if (GUILayout.Button("Przywróć domyślne ustawienia"))
            {
                PushUndo();
                P.settings = new SimSettingsBundle();
                app.MarkDirty();
            }
        }

        void DrawCheckTab()
        {
            GUILayout.Label("<b>Sprawdzenie projektu</b>");
            if (GUILayout.Button("Sprawdź ponownie", GUILayout.Height(28))) check = LayoutBaker.Compute(L, P.waypoints);
            if (check == null) return;
            var prev = GUI.color;
            if (check.errors.Count == 0 && check.warnings.Count == 0)
            {
                GUI.color = new Color(0.5f, 1f, 0.5f);
                GUILayout.Label("Wszystko w porządku. Można uruchomić symulację.", hintStyle);
            }
            GUI.color = new Color(1f, 0.5f, 0.5f);
            foreach (var e in check.errors) GUILayout.Label("BŁĄD: " + e, hintStyle);
            GUI.color = new Color(1f, 0.85f, 0.4f);
            foreach (var w in check.warnings) GUILayout.Label("Uwaga: " + w, hintStyle);
            GUI.color = prev;

            GUILayout.Space(8);
            int wards = 0, beds = 0, doctors = 0;
            foreach (var r in check.rooms)
            {
                var d = L.rooms[r.def];
                if (d.type != RoomType.Ward) continue;
                wards++;
                beds += d.capacity < 0 ? 3 : d.capacity;
                doctors += d.minDoctors;
            }
            GUILayout.Label("Sale: " + check.rooms.Count + "   oddziały: " + wards + "   łóżka: " + beds, hintStyle);
            GUILayout.Label("Waypointy: " + check.nodes.Count + "   połączenia: " + check.linkCount + "   schody i windy: " + check.connectors.Count, hintStyle);
            GUILayout.Label("Lekarze wymagani przez oddziały: " + doctors + "   ustawieni w symulacji: " + P.settings.population.doctors, hintStyle);
            if (doctors > P.settings.population.doctors)
                Hint("<color=#ffcc55>Za mało lekarzy, żeby każdy oddział miał swoje minimum. Zwiększ liczbę w Symulacja → Populacja.</color>");
            if (beds == 0 && wards > 0) Hint("Łóżka bez limitu - pacjenci nie będą czekać w poczekalni.");
        }
    }
}
