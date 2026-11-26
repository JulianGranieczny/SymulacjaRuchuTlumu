using UnityEngine;
using UnityEngine.Rendering;

namespace HospitalSim
{
    //okienko po kliknieciu w kulke pokazuje kto to jest na jakim oddziale jak bardzo chory i co akurat robi
    //nie uzywamy colliderow bo przy paru tysiacach agentow by to zabilo fpsy wiec szukamy po prostu najblizszego w pikselach
    [DefaultExecutionOrder(10)]
    public class AgentPicker : MonoBehaviour
    {
        public static AgentPicker Instance { get; private set; }

        public Camera cam;
        [Tooltip("Promień kliknięcia w pikselach ekranu")]
        public float pickRadiusPixels = 28f;
        public Color markerColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        public Color haloColor = new Color(1f, 0.95f, 0.55f, 0.32f);

        int selected = -1;
        public int Selected => selected;
        PersonData person;
        Rect window = new Rect(0, 140, 320, 10);
        bool windowPlaced;
        Transform ring, halo;
        Material ringMat, haloMat;
        Mesh ringMesh, haloMesh;
        AgentSystem sys;
        GUIStyle smallStyle;
        Texture2D whiteTex;

        void Awake() { Instance = this; }

        void Start()
        {
            sys = AgentSystem.Instance;
            if (cam == null) cam = Camera.main;

            //maly pierscien pod nogami zeby bylo widac kogo zaznaczylismy
            ringMesh = MakeRing(0.6f, 0.76f, 48);
            ringMat = MaterialUtil.VertexColor();
            ringMat.color = markerColor;
            ring = MakeMarker("ZaznaczeniePierscien", ringMesh, ringMat);

            //do tego lekka poswiata dookola kulki
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haloMesh = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            Destroy(tmp);
            var cols = new Color[haloMesh.vertexCount];
            for (int k = 0; k < cols.Length; k++) cols[k] = Color.white;
            haloMesh.colors = cols;
            haloMat = MaterialUtil.VertexColor();
            haloMat.color = haloColor;
            halo = MakeMarker("ZaznaczeniePoswiata", haloMesh, haloMat);

            if (sys != null) sys.OnRespawn += () => Select(-1);
        }

        Transform MakeMarker(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            //wieszamy znacznik pod pickerem zeby sam zniknal razem z symulacja
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            return go.transform;
        }

        //plaski pierscien lezacy na podlodze promienie liczone wzgledem srednicy kulki
        static Mesh MakeRing(float inner, float outer, int seg)
        {
            var v = new Vector3[seg * 2];
            var c = new Color[seg * 2];
            var t = new int[seg * 6];
            for (int k = 0; k < seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                v[k * 2] = new Vector3(cx * inner, 0f, cz * inner);
                v[k * 2 + 1] = new Vector3(cx * outer, 0f, cz * outer);
                c[k * 2] = Color.white; c[k * 2 + 1] = Color.white;
                int n = (k + 1) % seg;
                int o = k * 6;
                t[o] = k * 2; t[o + 1] = n * 2; t[o + 2] = k * 2 + 1;
                t[o + 3] = k * 2 + 1; t[o + 4] = n * 2; t[o + 5] = n * 2 + 1;
            }
            var m = new Mesh { name = "Pierscien" };
            m.vertices = v; m.colors = c; m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ring != null) Destroy(ring.gameObject);
            if (halo != null) Destroy(halo.gameObject);
            if (ringMesh != null) Destroy(ringMesh);
            if (haloMesh != null) Destroy(haloMesh);
            if (ringMat != null) Destroy(ringMat);
            if (haloMat != null) Destroy(haloMat);
            if (whiteTex != null) Destroy(whiteTex);
        }

        void SetMarkers(bool on)
        {
            if (ring != null) ring.gameObject.SetActive(on);
            if (halo != null) halo.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            if (sys == null || !sys.Ready || cam == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                var gm = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                bool overUi = SimHud.IsOverUI(gm) || (selected >= 0 && window.Contains(gm));
                if (!overUi) Select(sys.FindNearestScreen(cam, Input.mousePosition, pickRadiusPixels));
            }

            if (selected < 0) return;
            if (sys.IsHidden(selected)) { Select(-1); return; } //ktos wyszedl albo uciekl przy ewakuacji wiec odznaczamy
            if (!sys.IsVisible(selected)) { SetMarkers(false); return; } //jest na pietrze ktorego teraz nie widac

            SetMarkers(true);
            float s = sys.ScaleOf(selected);
            Vector3 p = sys.PositionOf(selected);
            ring.position = p + Vector3.down * (s * 0.5f - 0.03f);
            ring.localScale = new Vector3(s, 1f, s);
            float pulse = 1.22f + 0.08f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            halo.position = p;
            halo.localScale = Vector3.one * (s * pulse);
        }

        public void Select(int idx)
        {
            selected = idx;
            SetMarkers(idx >= 0);
            if (idx >= 0 && PersonDatabase.Instance != null)
                person = PersonDatabase.Instance.Generate(idx, sys.RoleOf(idx));
        }

        //klikniecie na liscie osob przelacza na jego pietro i przesuwa tam kamere
        public void SelectAndFocus(int idx)
        {
            Select(idx);
            if (idx < 0 || sys == null) return;
            var fv = FloorViewController.Instance;
            int f = sys.FloorOf(idx);
            if (fv != null && !sys.IsVisible(idx)) fv.Set(fv.mode == FloorViewController.Mode.All ? FloorViewController.Mode.All : fv.mode, f);
            var rig = cam != null ? cam.GetComponent<CameraRig>() : null;
            if (rig != null) rig.Focus(sys.PositionOf(idx));
        }

        void OnGUI()
        {
            if (selected < 0 || sys == null) return;
            GuiSkinUtil.Setup();
            if (smallStyle == null) smallStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            if (!windowPlaced) { window.x = Screen.width - 340f; windowPlaced = true; }
            window.y = Mathf.Max(window.y, SimHud.BarBottom + 56f);
            window.height = 0;
            window = GUILayout.Window(77, window, DrawWindow, "Postać", GUILayout.Width(320));
            window.x = Mathf.Clamp(window.x, 0, Screen.width - 100);
            window.y = Mathf.Clamp(window.y, 0, Screen.height - 40);
        }

        string Name(int i)
        {
            if (PersonDatabase.Instance == null) return "#" + i;
            var p = PersonDatabase.Instance.Generate(i, sys.RoleOf(i));
            return p.first + " " + p.last;
        }

        string WardText(Room w)
        {
            if (w == null) return "brak";
            var map = HospitalMap.Instance;
            string floor = map != null ? map.FloorName(w.floor) : "";
            string label = w.Label;
            //czasem nazwa sali ma juz pietro w nawiasie wtedy nie dopisujemy drugi raz
            if (!string.IsNullOrEmpty(floor) && !label.Contains("[")) label += " [" + floor + "]";
            return label;
        }

        void ProgressBar(float pr, Color col)
        {
            if (whiteTex == null) { whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply(); }
            var r = GUILayoutUtility.GetRect(280, 14, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, whiteTex);
            GUI.color = col;
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(pr), r.height - 2), whiteTex);
            GUI.color = prev;
        }

        void DrawWindow(int id)
        {
            var role = sys.RoleOf(selected);
            var rc = sys.RoleColor(role);
            GUILayout.Label($"<b>{person.first} {person.last}</b>  <color=#{ColorUtility.ToHtmlStringRGB(rc)}>({AgentSystem.RoleNamePl(role)})</color>", smallStyle);
            GUILayout.Label($"Płeć: {person.SexLabel}    Wiek: {person.age}");

            switch (role)
            {
                case Role.Doctor:
                {
                    var w = sys.AssignedRoomOf(selected);
                    GUILayout.Label($"Oddział: <b>{WardText(w)}</b>", smallStyle);
                    if (w != null) GUILayout.Label($"Specjalizacja: {Labels.Spec(w.specialty)}");
                    int p = sys.TreatmentPartnerOf(selected);
                    if (p >= 0)
                    {
                        int st = sys.IllnessOf(p);
                        GUILayout.Label($"Pacjent: {Name(p)} (st. {st})", smallStyle);
                    }
                    break;
                }
                case Role.Patient:
                {
                    var w = sys.AssignedRoomOf(selected);
                    GUILayout.Label($"Musi trafić na oddział: <b>{WardText(w)}</b>", smallStyle);
                    if (w != null) GUILayout.Label($"Specjalizacja: {Labels.Spec(w.specialty)}");
                    int st = sys.IllnessOf(selected);
                    string col = ColorUtility.ToHtmlStringRGB(Illness.Color(st));
                    GUILayout.Label($"Stopień choroby: <color=#{col}><b>{st}/5 - {Illness.Name(st)}</b></color>", smallStyle);
                    int d = sys.TreatmentPartnerOf(selected);
                    if (d >= 0) GUILayout.Label($"Lekarz: {Name(d)}", smallStyle);
                    int v = sys.VisitorsOf(selected);
                    if (v > 0) GUILayout.Label($"Odwiedzający przy pacjencie: {v}");
                    break;
                }
                case Role.Visitor:
                {
                    int p = sys.VisitTargetOf(selected);
                    if (p >= 0)
                    {
                        int st = sys.IllnessOf(p);
                        GUILayout.Label($"Odwiedza: <b>{Name(p)}</b> (st. {st} - {Illness.Name(st)})", smallStyle);
                        GUILayout.Label($"Oddział: {WardText(sys.AssignedRoomOf(p))}", smallStyle);
                    }
                    break;
                }
                default:
                {
                    var map = HospitalMap.Instance;
                    int f = sys.HomeFloorOf(selected);
                    GUILayout.Label($"Sprząta: {(f >= 0 && map != null ? map.FloorName(f) : "cały szpital")}");
                    break;
                }
            }

            GUILayout.Label($"Teraz: <i>{sys.StatusOf(selected)}</i>", smallStyle);
            float pr = sys.TreatmentProgressOf(selected);
            if (pr >= 0f)
            {
                float left = sys.TreatmentRemainingOf(selected) / 60f;
                GUILayout.Label($"Leczenie: {Mathf.RoundToInt(pr * 100f)}%   (jeszcze ok. {Mathf.CeilToInt(left)} min gry)");
                ProgressBar(pr, Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(0.3f, 0.95f, 0.4f), pr));
            }

            float hpNow = sys.HpOf(selected), hpMax = sys.MaxHpOf(selected);
            float frac = hpMax > 0f ? hpNow / hpMax : 1f;
            GUILayout.Label($"Zdrowie: {Mathf.CeilToInt(hpNow)}/{Mathf.CeilToInt(hpMax)}");
            ProgressBar(frac, Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.4f), frac));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pokaż")) SelectAndFocus(selected);
            if (GUILayout.Button("Zamknij")) Select(-1);
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }
    }
}
