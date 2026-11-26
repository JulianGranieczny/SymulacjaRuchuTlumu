using UnityEngine;

namespace HospitalSim
{
    public static class GuiSkinUtil
    {
        public static void Setup()
        {
            GUI.skin.label.richText = true;
            GUI.skin.label.fontSize = 15;
            GUI.skin.button.fontSize = 15;
            GUI.skin.toggle.fontSize = 15;
            GUI.skin.window.fontSize = 16;
            GUI.skin.box.fontSize = 15;
            GUI.skin.textField.fontSize = 15;
        }

        public static float Slider(string label, float v, float min, float max, string fmt = "0.0")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(210));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString(fmt), GUILayout.Width(52));
            GUILayout.EndHorizontal();
            return v;
        }

        public static int IntSlider(string label, int v, int min, int max)
        {
            return Mathf.RoundToInt(Slider(label, v, min, max, "0"));
        }
    }

    //gorny pasek z czasem predkoscia licznikami i przyciskiem ewakuacji
    //plus panele z pietrami salami lista osob i ustawieniami ktore mozna zmieniac w trakcie
    public class SimHud : MonoBehaviour
    {
        static Rect bar, banner, eventsPanel, floorsPanel, roomsPanel, settingsPanel, peoplePanel;
        static bool showBanner, showEvents = true, showFloors = true, showRooms, showSettings, showPeople;

        //dolna krawedz gornego paska pod nia ustawiamy reszte paneli
        public static float BarBottom => bar.height > 0f ? bar.yMax : 80f;

        //tu aplikacja dorzuca swoje elementy np pasek z przyciskami edytor i menu
        public static System.Func<Vector2, bool> ExtraUiHit;

        public static bool IsOverUI(Vector2 g) =>
            (ExtraUiHit != null && ExtraUiHit(g)) || bar.Contains(g) || (showBanner && banner.Contains(g)) || (showEvents && eventsPanel.Contains(g)) ||
            (showFloors && floorsPanel.Contains(g)) || (showRooms && roomsPanel.Contains(g)) || (showSettings && settingsPanel.Contains(g)) ||
            (showPeople && peoplePanel.Contains(g));

        float fps = 60f;
        Vector2 scrollRooms, scrollSettings, scrollPeople;
        FloorViewController fv;
        GUIStyle bannerStyle, rowStyle, rowSelStyle, countStyle;
        Role peopleRole = Role.Patient;
        readonly System.Collections.Generic.List<int> peopleBuf = new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.Dictionary<int, string> names = new System.Collections.Generic.Dictionary<int, string>();
        static Texture2D whiteTex;

        static readonly string[] RoleTabs = { "Lekarze", "Pacjenci", "Odwiedzający", "Sprzątacze" };
        const float RowH = 24f;

        void Start()
        {
            //jak ktos zapomnial dodac floorviewcontroller to tworzymy go sami
            fv = FloorViewController.Instance;
            if (fv == null) fv = gameObject.AddComponent<FloorViewController>();
            var sys = AgentSystem.Instance;
            if (sys != null) sys.OnRespawn += () => names.Clear();
        }

        //imiona trzymamy w pamieci zeby nie losowac ich co klatke
        string NameOf(int i, Role r)
        {
            string n;
            if (names.TryGetValue(i, out n)) return n;
            var db = PersonDatabase.Instance;
            if (db == null) return "#" + i;
            var p = db.Generate(i, r);
            n = p.first + " " + p.last;
            names[i] = n;
            return n;
        }

        static Texture2D White
        {
            get
            {
                if (whiteTex == null) { whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply(); }
                return whiteTex;
            }
        }

        void OnDestroy() { if (whiteTex != null) { Destroy(whiteTex); whiteTex = null; } }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
        }

        void OnGUI()
        {
            var clock = SimClock.Instance;
            if (clock == null) return;
            GuiSkinUtil.Setup();
            var sys = AgentSystem.Instance;
            var map = HospitalMap.Instance;

            //paski postepu leczenia rysujemy najpierw zeby panele byly nad nimi
            if (sys != null && sys.Ready) DrawTreatmentBars(sys);

            //pasek ma dwa wiersze na gorze czas i predkosc a pod spodem kto jest w szpitalu
            bar = new Rect(10, 10, Mathf.Min(Screen.width - 20, 1500), 72);
            GUILayout.BeginArea(bar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            //data dd mm rrrr i godzina z sekundami
            GUILayout.Label($"{clock.DateString}  {clock.TimeString}", GUILayout.Width(200));
            //tryby czasu od pauzy do 1000x
            for (int k = 0; k < SimClock.ModeLabel.Length; k++)
            {
                var prev = GUI.backgroundColor;
                if ((int)clock.mode == k) GUI.backgroundColor = Color.green;
                if (GUILayout.Button(SimClock.ModeLabel[k], GUILayout.Width(k == 1 || k == 7 ? 58 : (k == 8 ? 66 : 48)))) clock.SetMode((TimeMode)k);
                GUI.backgroundColor = prev;
            }
            GUILayout.Space(10);

            //przycisk do przerwania ewakuacji
            if (sys != null && sys.Evac != AgentSystem.EvacState.None)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = Color.yellow;
                if (GUILayout.Button("Zakończ ewak.", GUILayout.Width(120))) sys.CancelEvacuation();
                GUI.backgroundColor = prevBg;
            }

            showEvents = GUILayout.Toggle(showEvents, "Zdarzenia", GUILayout.Width(100));
            showFloors = GUILayout.Toggle(showFloors, "Piętra", GUILayout.Width(80));
            showRooms = GUILayout.Toggle(showRooms, "Sale", GUILayout.Width(70));
            showPeople = GUILayout.Toggle(showPeople, "Osoby", GUILayout.Width(80));
            showSettings = GUILayout.Toggle(showSettings, "Ustawienia", GUILayout.Width(110));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"FPS: {fps:0}", GUILayout.Width(80));
            GUILayout.EndHorizontal();

            if (sys != null && sys.Ready)
            {
                if (countStyle == null) countStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
                var ib = sys.InsideByRole;
                int docTotal = sys.RoleEnd(Role.Doctor) - sys.RoleStart(Role.Doctor);
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{RoleTag(sys, Role.Doctor)} Lekarze: <b>{ib[0]}</b>/{docTotal} (leczy: {sys.DoctorsBusy})     " +
                    $"{RoleTag(sys, Role.Patient)} Pacjenci: <b>{ib[1]}</b> (poczekalnia: {sys.Waiting}, w łóżku: {sys.PatientsInBed}, leczeni: {sys.PatientsTreated})     " +
                    $"{RoleTag(sys, Role.Visitor)} Odwiedzający: <b>{ib[2]}</b>     " +
                    $"{RoleTag(sys, Role.Cleaner)} Sprzątacze: <b>{ib[3]}</b>     " +
                    $"│  W szpitalu: <b>{sys.Inside}</b>   Wyleczeni: <b>{sys.Healed}</b>   Zgony: <b>{sys.Deaths}</b>", countStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();

            //baner przy ewakuacji albo innym zdarzeniu
            var es = RandomEventSystem.Instance;
            bool evacOn = sys != null && sys.Evac != AgentSystem.EvacState.None;
            bool customOn = es != null && es.ActiveEvent != null && es.ActiveEvent.kind != SimEventKind.Evacuation;
            showBanner = evacOn || customOn;
            if (showBanner)
            {
                if (bannerStyle == null)
                    bannerStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold, richText = true };
                banner = new Rect(Screen.width * 0.5f - 280f, BarBottom + 4f, 560f, 44f);
                var prev = GUI.backgroundColor;
                string text;
                if (evacOn && sys.Evac == AgentSystem.EvacState.Evacuating)
                {
                    GUI.backgroundColor = new Color(1f, 0.2f, 0.2f);
                    text = $"EWAKUACJA - jeszcze w drodze: {sys.EvacRemaining}";
                }
                else if (evacOn)
                {
                    GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
                    float left = Mathf.Max(0f, sys.Evacuation.returnDelayMinutes * 60f - sys.EvacTimerSeconds) / 60f;
                    text = $"Ewakuacja zakończona - powrót personelu za {left:0} min";
                }
                else
                {
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
                    text = $"{es.ActiveEvent.name} - jeszcze {es.ActiveRemainingMinutes:0} min";
                }
                GUI.Box(banner, text, bannerStyle);
                GUI.backgroundColor = prev;
            }

            float y = BarBottom + 6f;

            //zdarzenia z przyciskami do recznego odpalenia
            if (showEvents)
            {
                int count = es != null ? es.events.Count : 0;
                eventsPanel = new Rect(10, y, 330, 52 + count * 32);
                GUILayout.BeginArea(eventsPanel, GUI.skin.box);
                if (es == null) GUILayout.Label("Brak RandomEventSystem w scenie");
                else
                {
                    GUILayout.Label(es.ActiveEvent == null ? "Brak aktywnego zdarzenia" :
                        (es.ActiveEvent.kind == SimEventKind.Evacuation ? $"Trwa: {es.ActiveEvent.name}" : $"Trwa: {es.ActiveEvent.name} (jeszcze {es.ActiveRemainingMinutes:0} min)"));
                    //jak cos juz trwa to przyciski sa wyszarzone bo drugie zdarzenie i tak by nie weszlo
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = es.ActiveEvent == null && !(sys != null && sys.EvacActive);
                    for (int k = 0; k < es.events.Count; k++)
                        if (GUILayout.Button($"Wywołaj: {es.events[k].name}")) es.TriggerManual(k);
                    GUI.enabled = prevEnabled;
                }
                GUILayout.EndArea();
                y += eventsPanel.height + 6f;
            }

            //pietra
            if (showFloors && map != null && fv != null)
            {
                floorsPanel = new Rect(10, y, 330, 118);
                GUILayout.BeginArea(floorsPanel, GUI.skin.box);
                GUILayout.BeginHorizontal();
                ModeButton("Wszystkie", FloorViewController.Mode.All);
                ModeButton("Tylko to", FloorViewController.Mode.OnlyFloor);
                ModeButton("Do tego", FloorViewController.Mode.UpToFloor);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                for (int f = 0; f < map.FloorCount; f++)
                {
                    var prev = GUI.backgroundColor;
                    if (fv.floor == f && fv.mode != FloorViewController.Mode.All) GUI.backgroundColor = Color.cyan;
                    if (GUILayout.Button(map.FloorName(f))) fv.Set(fv.mode, f);
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
                bool hide = GUILayout.Toggle(fv.hideUpperGeometry, "Ukrywaj wyższe piętra (geometria)");
                if (hide != fv.hideUpperGeometry) { fv.hideUpperGeometry = hide; fv.Set(fv.mode, fv.floor); }
                GUILayout.EndArea();
                y += floorsPanel.height + 6f;
            }

            //sale
            if (showRooms && map != null)
            {
                roomsPanel = new Rect(10, y, 360, Mathf.Min(Screen.height - y - 10, 360));
                GUILayout.BeginArea(roomsPanel, GUI.skin.box);
                scrollRooms = GUILayout.BeginScrollView(scrollRooms);
                GUILayout.Label("<b>Oddziały</b>  lekarze / zajęte łóżka");
                foreach (var w in map.Wards)
                {
                    bool ok = w.assignedDoctors >= w.minDoctors;
                    var c = GUI.color;
                    GUI.color = ok ? Color.white : Color.red;
                    string cap = w.Capacity > 0 ? w.Capacity.ToString() : "∞";
                    GUILayout.Label($"{w.Label} [{map.FloorName(w.floor)}]: lek. {w.assignedDoctors}/{w.minDoctors}, łóżka {w.occupants}/{cap}");
                    GUI.color = c;
                }
                GUILayout.Space(4);
                GUILayout.Label("<b>Poczekalnie</b>");
                foreach (var w in map.WaitingRooms)
                {
                    string cap = w.Capacity > 0 ? w.Capacity.ToString() : "∞";
                    GUILayout.Label($"{w.Label} [{map.FloorName(w.floor)}]: {w.occupants}/{cap}");
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                y += roomsPanel.height + 6f;
            }

            //lista osob w szpitalu
            if (showPeople && sys != null && sys.Ready)
            {
                float h = Mathf.Max(160f, Mathf.Min(Screen.height - y - 10f, 460f));
                peoplePanel = new Rect(10, y, 400, h);
                DrawPeople(sys, map);
                y += peoplePanel.height + 6f;
            }

            //ustawienia
            if (showSettings && sys != null)
            {
                float sx = 420f;
                settingsPanel = new Rect(sx, BarBottom + 6f, 440, Mathf.Min(Screen.height - BarBottom - 16f, 680));
                GUILayout.BeginArea(settingsPanel, GUI.skin.box);
                scrollSettings = GUILayout.BeginScrollView(scrollSettings);
                DrawSettings(clock, sys);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        void ModeButton(string text, FloorViewController.Mode m)
        {
            var prev = GUI.backgroundColor;
            if (fv.mode == m) GUI.backgroundColor = Color.green;
            if (GUILayout.Button(text)) fv.Set(m, fv.floor);
            GUI.backgroundColor = prev;
        }

        void DrawSettings(SimClock clock, AgentSystem sys)
        {
            GUILayout.Label("<b>Czas</b>  (spacja = pauza, [ i ] = wolniej i szybciej)");
            clock.tickIntervalSimMinutes = GuiSkinUtil.Slider("Tick zdarzeń co (min gry)", clock.tickIntervalSimMinutes, 1f, 60f, "0");

            GUILayout.Space(6);
            GUILayout.Label("<b>Ruch</b>");
            var mv = sys.Movement;
            mv.globalSpeedMultiplier = GuiSkinUtil.Slider("Mnożnik prędkości", mv.globalSpeedMultiplier, 0.2f, 4f, "0.00");

            GUILayout.Space(6);
            GUILayout.Label("<b>Kolizje</b>");
            var co = sys.Collisions;
            co.agentCollisions = GUILayout.Toggle(co.agentCollisions, "Kolizje agent-agent (wyłącz dla max wydajności)");
            co.agentRadius = GuiSkinUtil.Slider("Promień agenta", co.agentRadius, 0.1f, 0.6f, "0.00");
            co.separationStrength = GuiSkinUtil.Slider("Siła wypychania", co.separationStrength, 0f, 30f, "0");
            co.maxNeighbors = GuiSkinUtil.IntSlider("Maks. sąsiadów", co.maxNeighbors, 4, 64);

            GUILayout.Space(6);
            GUILayout.Label("<b>Napływ ludzi</b>");
            var ar = sys.Arrivals;
            ar.patientsPerHour = GuiSkinUtil.Slider("Pacjenci / godz. gry", ar.patientsPerHour, 0f, 120f, "0");
            ar.visitorsPerHour = GuiSkinUtil.Slider("Odwiedzający / godz. gry", ar.visitorsPerHour, 0f, 120f, "0");
            ar.patientsSpawnInWaitingRoom = GUILayout.Toggle(ar.patientsSpawnInWaitingRoom, "Pacjenci pojawiają się w poczekalni");
            ar.visitorsSpawnInWaitingRoom = GUILayout.Toggle(ar.visitorsSpawnInWaitingRoom, "Odwiedzający pojawiają się w poczekalni");

            GUILayout.Space(6);
            GUILayout.Label("<b>Rutyna</b>");
            var rt = sys.Routine;
            rt.openHour = GuiSkinUtil.IntSlider("Otwarcie (godz.)", rt.openHour, 0, 23);
            rt.closeHour = GuiSkinUtil.IntSlider("Zamknięcie (godz.)", rt.closeHour, 1, 24);
            rt.doctorVisitOtherWardChance = GuiSkinUtil.Slider("Szansa konsultacji lekarza", rt.doctorVisitOtherWardChance, 0f, 1f, "0.00");
            rt.visitorMinStage = GuiSkinUtil.IntSlider("Odwiedziny od stopnia choroby", rt.visitorMinStage, 1, 5);

            GUILayout.Space(6);
            GUILayout.Label("<b>Zdrowie i leczenie</b>");
            var hl = sys.Health;
            hl.patientHpLossPerHour = GuiSkinUtil.Slider("Utrata hp / godz. czekania", hl.patientHpLossPerHour, 0f, 100f, "0.0");
            var tr = sys.Treatment;
            GUILayout.Label("Czas leczenia (min gry) dla stopnia choroby:");
            tr.stageDuration.stage1 = StageRow(1, tr.stageDuration.stage1);
            tr.stageDuration.stage2 = StageRow(2, tr.stageDuration.stage2);
            tr.stageDuration.stage3 = StageRow(3, tr.stageDuration.stage3);
            tr.stageDuration.stage4 = StageRow(4, tr.stageDuration.stage4);
            tr.stageDuration.stage5 = StageRow(5, tr.stageDuration.stage5);
            tr.otherDoctorsCanHelp = GUILayout.Toggle(tr.otherDoctorsCanHelp, "Lekarze z innych oddziałów mogą pomagać");

            GUILayout.Space(6);
            GUILayout.Label("<b>Ewakuacja</b>");
            var ev = sys.Evacuation;
            ev.speedMultiplier = GuiSkinUtil.Slider("Mnożnik prędkości", ev.speedMultiplier, 0.5f, 4f, "0.00");
            ev.maxDurationMinutes = GuiSkinUtil.Slider("Maks. czas (min)", ev.maxDurationMinutes, 1f, 120f, "0");
            ev.returnDelayMinutes = GuiSkinUtil.Slider("Powrót personelu po (min)", ev.returnDelayMinutes, 0f, 120f, "0");

            GUILayout.Space(6);
            GUILayout.Label("<b>Populacja</b> (zastosuj przyciskiem)");
            var po = sys.Population;
            po.doctors = GuiSkinUtil.IntSlider("Lekarze", po.doctors, 0, 2000);
            po.patients = GuiSkinUtil.IntSlider("Pacjenci (maks. naraz)", po.patients, 0, 20000);
            po.visitors = GuiSkinUtil.IntSlider("Odwiedzający (maks. naraz)", po.visitors, 0, 20000);
            po.cleaners = GuiSkinUtil.IntSlider("Sprzątacze", po.cleaners, 0, 1000);
            po.doctorsStartInOwnRoom = GUILayout.Toggle(po.doctorsStartInOwnRoom, "Lekarze startują w swoich salach");
            po.patientsAlreadyInside = GuiSkinUtil.Slider("Pacjenci już w środku", po.patientsAlreadyInside, 0f, 1f, "0.00");
            po.visitorsAlreadyInside = GuiSkinUtil.Slider("Odwiedzający już w środku", po.visitorsAlreadyInside, 0f, 1f, "0.00");
            if (GUILayout.Button("Zastosuj i zrestartuj agentów (Respawn)")) sys.Respawn();
        }

        static Vector2 StageRow(int stage, Vector2 v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("  st. " + stage + " od", GUILayout.Width(90));
            v.x = Mathf.Round(GUILayout.HorizontalSlider(v.x, 1f, 600f));
            GUILayout.Label(v.x.ToString("0"), GUILayout.Width(36));
            GUILayout.Label("do", GUILayout.Width(24));
            v.y = Mathf.Round(GUILayout.HorizontalSlider(v.y, 1f, 600f));
            GUILayout.Label(v.y.ToString("0"), GUILayout.Width(36));
            GUILayout.EndHorizontal();
            if (v.y < v.x) v.y = v.x;
            return v;
        }

        //maly kwadracik w kolorze roli
        static string RoleTag(AgentSystem sys, Role r)
        {
            var c = sys.RoleColor(r);
            return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">■</color>";
        }

        //pasek postepu nad kazdym leczonym pacjentem
        void DrawTreatmentBars(AgentSystem sys)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var prev = GUI.color;
            for (int k = 0; k < sys.TreatmentCount; k++)
            {
                int doc, pat; float pr;
                if (!sys.TreatmentAt(k, out doc, out pat, out pr)) continue;
                if (!sys.IsVisible(pat)) continue;
                Vector3 wp = sys.PositionOf(pat) + Vector3.up * (sys.ScaleOf(pat) * 0.5f + 0.2f);
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0f) continue;
                float w = 46f, h = 8f;
                var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - 26f, w, h);
                if (r.xMax < 0 || r.x > Screen.width || r.yMax < 0 || r.y > Screen.height) continue;
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), White);
                GUI.color = Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(0.3f, 0.95f, 0.4f), pr);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * pr, r.height), White);
            }
            GUI.color = prev;
        }

        void DrawPeople(AgentSystem sys, HospitalMap map)
        {
            if (rowStyle == null)
            {
                rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true, fontSize = 13, fixedHeight = RowH - 2f, margin = new RectOffset(0, 0, 1, 1), padding = new RectOffset(6, 4, 2, 2) };
                rowSelStyle = new GUIStyle(rowStyle);
                //osobny stan zeby nie przemalowac przy okazji zwyklych wierszy
                rowSelStyle.normal = new GUIStyleState { background = rowStyle.active.background, textColor = new Color(1f, 0.92f, 0.4f) };
            }
            GUILayout.BeginArea(peoplePanel, GUI.skin.box);
            GUILayout.BeginHorizontal();
            for (int r = 0; r < 4; r++)
            {
                var prev = GUI.backgroundColor;
                if ((int)peopleRole == r) GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button(RoleTabs[r] + " (" + sys.InsideByRole[r] + ")", GUILayout.Height(24))) { peopleRole = (Role)r; scrollPeople = Vector2.zero; }
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();

            peopleBuf.Clear();
            for (int i = sys.RoleStart(peopleRole); i < sys.RoleEnd(peopleRole); i++)
                if (!sys.IsHidden(i)) peopleBuf.Add(i);
            //pacjentow sortujemy od najciezszego stopnia
            if (peopleRole == Role.Patient)
                peopleBuf.Sort((a, b) => { int c = sys.IllnessOf(b).CompareTo(sys.IllnessOf(a)); return c != 0 ? c : sys.HpOf(a).CompareTo(sys.HpOf(b)); });

            var picker = AgentPicker.Instance;
            int sel = picker != null ? picker.Selected : -1;
            float viewH = peoplePanel.height - 44f;
            scrollPeople = GUILayout.BeginScrollView(scrollPeople);
            int n = peopleBuf.Count;
            if (n == 0) GUILayout.Label("Nikogo z tej grupy nie ma teraz w szpitalu.");
            int first = Mathf.Clamp(Mathf.FloorToInt(scrollPeople.y / RowH) - 1, 0, Mathf.Max(0, n - 1));
            int last = Mathf.Min(n, first + Mathf.CeilToInt(viewH / RowH) + 3);
            if (first > 0) GUILayout.Space(first * RowH);
            for (int k = first; k < last; k++)
            {
                int i = peopleBuf[k];
                if (GUILayout.Button(PersonRow(sys, map, i), i == sel ? rowSelStyle : rowStyle) && picker != null) picker.SelectAndFocus(i);
            }
            if (last < n) GUILayout.Space((n - last) * RowH);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        string PersonRow(AgentSystem sys, HospitalMap map, int i)
        {
            var role = sys.RoleOf(i);
            string name = NameOf(i, role);
            switch (role)
            {
                case Role.Doctor:
                {
                    var w = sys.AssignedRoomOf(i);
                    string ward = w != null ? w.Label : "bez oddziału";
                    string busy = sys.TreatmentProgressOf(i) >= 0f ? "  <color=#7fe08a>leczy</color>" : "";
                    return $"<b>{name}</b>  <color=#bbbbbb>{ward}</color>{busy}";
                }
                case Role.Patient:
                {
                    int st = sys.IllnessOf(i);
                    var w = sys.AssignedRoomOf(i);
                    string col = ColorUtility.ToHtmlStringRGB(Illness.Color(st));
                    return $"<color=#{col}>st. {st}</color>  <b>{name}</b>  <color=#bbbbbb>{(w != null ? w.Label : "-")}</color>  HP {Mathf.CeilToInt(sys.HpOf(i))}";
                }
                case Role.Visitor:
                {
                    int p = sys.VisitTargetOf(i);
                    return p >= 0 ? $"<b>{name}</b>  <color=#bbbbbb>do: {NameOf(p, Role.Patient)}</color>" : $"<b>{name}</b>  <color=#bbbbbb>wychodzi</color>";
                }
                default:
                {
                    int f = sys.HomeFloorOf(i);
                    return $"<b>{name}</b>  <color=#bbbbbb>{(f >= 0 && map != null ? map.FloorName(f) : "cały szpital")}</color>";
                }
            }
        }
    }
}
