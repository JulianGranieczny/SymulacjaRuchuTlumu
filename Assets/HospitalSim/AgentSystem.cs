using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace HospitalSim
{
    //ponizej trzy joby burst ktore licza sie rownolegle na wszystkich rdzeniach

    //krok 1 z 3 wrzucamy widocznych agentow do siatki przestrzennej zeby potem szybko znalezc sasiadow
    [BurstCompile]
    internal struct BuildGridJob : IJob
    {
        [ReadOnly] public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<byte> hidden;
        public NativeArray<int> cellStart;
        public NativeArray<int> cellFill;
        public NativeArray<int> sorted;
        public NativeArray<int> cellOf;
        public GridParams grid;

        public void Execute()
        {
            int cells = grid.w * grid.h * grid.floors;
            for (int c = 0; c <= cells; c++) cellStart[c] = 0;
            for (int c = 0; c < cells; c++) cellFill[c] = 0;
            int n = pos.Length;
            for (int i = 0; i < n; i++)
            {
                if (hidden[i] != 0) { cellOf[i] = -1; continue; }
                int c = grid.Index(pos[i]);
                cellOf[i] = c;
                cellStart[c + 1] = cellStart[c + 1] + 1;
            }
            for (int c = 0; c < cells; c++) cellStart[c + 1] = cellStart[c + 1] + cellStart[c];
            for (int i = 0; i < n; i++)
            {
                int c = cellOf[i];
                if (c < 0) continue;
                int f = cellFill[c];
                sorted[cellStart[c] + f] = i;
                cellFill[c] = f + 1;
            }
        }
    }

    //krok 2 z 3 liczymy jak mocno agenci odpychaja sie nawzajem
    [BurstCompile]
    internal struct SepJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<byte> hidden;
        [ReadOnly] public NativeArray<int> sorted;
        [ReadOnly] public NativeArray<int> cellStart;
        public GridParams grid;
        public float minDist;
        public int maxNeighbors;
        [WriteOnly] public NativeArray<float2> push;

        public void Execute(int i)
        {
            if (hidden[i] != 0) { push[i] = float2.zero; return; }
            float3 p = pos[i];
            int f = grid.Floor(p.y);
            int cx = grid.CellX(p.x), cz = grid.CellZ(p.z);
            float2 acc = float2.zero;
            int seen = 0;
            float minSq = minDist * minDist;

            for (int dz = -1; dz <= 1 && seen < maxNeighbors; dz++)
            {
                int z = cz + dz;
                if (z < 0 || z >= grid.h) continue;
                for (int dx = -1; dx <= 1 && seen < maxNeighbors; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= grid.w) continue;
                    int c = (f * grid.h + z) * grid.w + x;
                    int end = cellStart[c + 1];
                    for (int k = cellStart[c]; k < end; k++)
                    {
                        int j = sorted[k];
                        if (j == i) continue;
                        float3 q = pos[j];
                        if (math.abs(q.y - p.y) > 1.5f) continue;
                        float2 d = new float2(p.x - q.x, p.z - q.z);
                        float sq = math.lengthsq(d);
                        if (sq < minSq)
                        {
                            float len = math.sqrt(sq);
                            float2 dir;
                            if (len > 1e-4f) dir = d / len;
                            else { float a = i * 2.39996f; dir = new float2(math.cos(a), math.sin(a)); }
                            acc += dir * (minDist - len);
                        }
                        seen++;
                        if (seen >= maxNeighbors) break;
                    }
                }
            }
            push[i] = acc * 0.5f;
        }
    }

    //krok 3 z 3 ruch po grafie sciany timery i macierze do rysowania kulek
    [BurstCompile]
    internal struct MoveJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> nodePos;
        [ReadOnly] public NativeArray<int> nextHop;
        //255 to przejscie czyli drzwi albo schody tam idziemy dokladnie srodkiem bez losowego przesuniecia
        [ReadOnly] public NativeArray<byte> nodeRoomType;
        //1 jesli waypoint to lozko pacjent kladzie sie na srodku a reszta staje obok
        [ReadOnly] public NativeArray<byte> nodeBed;
        public int nodeCount;
        public WallGrid wall;
        public GridParams grid;

        //dtsim to ile sekund gry minelo w tej klatce a dtreal ile naprawde
        public float dtSim, dtReal, globalSpeed, evacMul;
        public bool usePush;
        public float pushStrength, maxPush;
        public int visMin, visMax;

        public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<float2> offset;
        public NativeArray<int> cur;
        public NativeArray<int> tgt;
        [ReadOnly] public NativeArray<int> dest;
        [ReadOnly] public NativeArray<float> speed;
        [ReadOnly] public NativeArray<float> scale;
        [ReadOnly] public NativeArray<float> dwell;
        public NativeArray<float> wait;
        //stan 0 idzie 1 czeka 2 czeka az watek glowny zdecyduje co dalej
        public NativeArray<byte> state;
        [ReadOnly] public NativeArray<byte> hidden;
        [ReadOnly] public NativeArray<byte> evac;
        [ReadOnly] public NativeArray<byte> lie;
        [ReadOnly] public NativeArray<float2> push;
        public NativeArray<float> stuck;
        [WriteOnly] public NativeArray<Matrix4x4> matrices;

        public void Execute(int i)
        {
            if (hidden[i] != 0)
            {
                matrices[i] = new Matrix4x4(new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 1));
                return;
            }

            float3 p = pos[i];
            byte s = state[i];

            //najpierw przepychanki z innymi agentami
            if (usePush)
            {
                float2 ps = push[i] * (pushStrength * dtReal);
                float l = math.length(ps);
                if (l > maxPush) ps *= maxPush / l;
                if (l > 1e-5f)
                {
                    float3 np = p; np.x += ps.x; np.z += ps.y;
                    if (!wall.Blocked(np)) p = np;
                }
            }

            if (s == 0)
            {
                //przy duzym przyspieszeniu agent potrafi w jednej klatce minac kilka waypointow wiec idziemy w petli az wyczerpie krok
                float step = speed[i] * globalSpeed * (evac[i] != 0 ? evacMul : 1f) * dtSim;
                float left = step;
                float realPerMeter = step > 1e-5f ? dtReal / step : 0f;
                int guard = 0;
                while (left > 1e-5f && guard++ < 1024)
                {
                    int t = tgt[i];
                    float3 center = nodePos[t];
                    float3 target = center;
                    if (stuck[i] <= 0.6f && nodeRoomType[t] != 255)
                    {
                        float2 o = offset[i];
                        if (nodeBed[t] != 0)
                        {
                            //pacjent lezy na srodku lozka a lekarz czy rodzina stoja przy nim a nie na nim
                            if (lie[i] != 0) o = float2.zero;
                            else
                            {
                                float ol = math.length(o);
                                o = ol > 1e-3f ? o / ol * 0.85f : new float2(0.85f, 0f);
                            }
                        }
                        target.x += o.x; target.z += o.y;
                        if (wall.Blocked(target)) target = center;
                    }

                    float sstep = math.min(left, 0.25f);
                    float dtr = sstep * realPerMeter;
                    float3 d = target - p;
                    float dist = math.length(d);
                    //po schodach chodzi sie wolniej niz po plaskim
                    float kf = math.abs(d.y) > 0.2f * dist ? 0.6f : 1f;
                    float mv = sstep * kf;
                    if (dist <= mv)
                    {
                        p = target;
                        left -= dist / kf;
                        cur[i] = t;
                        stuck[i] = 0f;
                        int dst = dest[i];
                        if (t == dst) { state[i] = 1; wait[i] = dwell[i]; break; }
                        int nx = nextHop[t * nodeCount + dst];
                        if (nx < 0) { state[i] = 1; wait[i] = dwell[i]; break; }
                        tgt[i] = nx;
                        continue;
                    }
                    left -= sstep;

                    float3 np = p + d / dist * mv;
                    if (!wall.Blocked(np))
                    {
                        p = np;
                        stuck[i] = math.max(0f, stuck[i] - dtr);
                    }
                    else
                    {
                        //slizgamy sie wzdluz sciany ale tylko jak to faktycznie rusza agenta
                        //wczesniej stal w miejscu a licznik zaklinowania sie nie zwiekszal i tak w kolko
                        float3 ax = new float3(np.x, np.y, p.z);
                        float3 az = new float3(p.x, np.y, np.z);
                        bool mx = math.abs(np.x - p.x) > 1e-4f && !wall.Blocked(ax);
                        bool mz = math.abs(np.z - p.z) > 1e-4f && !wall.Blocked(az);
                        if (mx) p = ax;
                        else if (mz) p = az;
                        else stuck[i] += dtr;
                        if (mx || mz) stuck[i] += dtr * 0.25f;
                    }

                    //jak ktos utknal na dluzej to wraca na srodek waypointa
                    if (stuck[i] > 2f) { p = center; stuck[i] = 0f; }
                }
            }
            else if (s == 1)
            {
                float w = wait[i] - dtSim;
                wait[i] = w;
                if (w <= 0f) state[i] = 2;
            }

            pos[i] = p;

            int fl = (int)math.round((p.y - grid.baseY) / grid.floorH);
            bool visibleFloor = fl >= visMin && fl <= visMax;
            float sc = visibleFloor ? scale[i] : 0f;
            matrices[i] = new Matrix4x4(
                new Vector4(sc, 0, 0, 0),
                new Vector4(0, sc, 0, 0),
                new Vector4(0, 0, sc, 0),
                new Vector4(p.x, p.y, p.z, 1));
        }
    }

    //tu juz glowny skrypt ktory trzyma wszystkich agentow

    public class AgentSystem : MonoBehaviour
    {
        public static AgentSystem Instance { get; private set; }

        public enum EvacState { None, Evacuating, Cooldown }

        [Header("Populacja i start")]
        [SerializeField] PopulationSettings population = new PopulationSettings();
        [Header("Napływ pacjentów i odwiedzających")]
        [SerializeField] ArrivalSettings arrivals = new ArrivalSettings();
        [Header("Zdrowie")]
        [SerializeField] HealthSettings health = new HealthSettings();
        [Header("Leczenie")]
        [SerializeField] TreatmentSettings treatment = new TreatmentSettings();
        [Header("Ruch")]
        [SerializeField] MovementSettings movement = new MovementSettings();
        [Header("Kolizje")]
        [SerializeField] CollisionSettings collisions = new CollisionSettings();
        [Header("Rutyna dnia")]
        [SerializeField] RoutineSettings routine = new RoutineSettings();
        [Header("Ewakuacja")]
        [SerializeField] EvacuationSettings evacuation = new EvacuationSettings();
        [Header("Wygląd")]
        [SerializeField] VisualSettings visual = new VisualSettings();

        public PopulationSettings Population => population;
        public ArrivalSettings Arrivals => arrivals;
        public HealthSettings Health => health;
        public TreatmentSettings Treatment => treatment;
        public MovementSettings Movement => movement;
        public CollisionSettings Collisions => collisions;
        public RoutineSettings Routine => routine;
        public EvacuationSettings Evacuation => evacuation;

        public int Total { get; private set; }
        public int Inside { get; private set; }
        public int Waiting { get; private set; }
        public int Healed { get; private set; }
        public int Deaths { get; private set; }
        //ile osob z kazdej roli jest teraz w srodku
        public readonly int[] InsideByRole = new int[4];
        public int DoctorsBusy { get; private set; }
        public int PatientsInBed { get; private set; }
        public int PatientsTreated { get; private set; }
        public int RoleStart(Role r) => roleStart[(int)r];
        public int RoleEnd(Role r) => roleStart[(int)r + 1];
        public bool Ready { get; private set; }
        public EvacState Evac { get; private set; }
        //true od momentu ogloszenia ewakuacji az personel wroci do pracy
        public bool EvacActive => evacRequested || Evac != EvacState.None;
        public int EvacRemaining => evacRemaining;
        public float EvacTimerSeconds => evacTimer;
        public event Action OnRespawn;
        public event Action OnEvacuationStarted;
        public event Action OnEvacuationEnded;

        const float Never = 1e9f;

        //etapy pacjenta 1 poczekalnia 2 idzie do lozka albo czeka na lekarza 3 leczenie 4 wychodzi
        //u odwiedzajacego 1 poczekalnia 2 siedzi u pacjenta 4 wychodzi
        const byte StWait = 1, StBed = 2, StTreat = 3, StLeave = 4;

        struct QEntry { public int id; public int ticket; }
        struct TreatmentEntry { public int doc, pat; public float remaining, total; public bool started; }

        SimClock clock;
        HospitalMap map;
        Random rng;
        int visMin = -1000, visMax = 1000;

        //dane agentow trzymamy w nativearray zeby joby mogly z nich korzystac
        NativeArray<float3> pos;
        NativeArray<float2> offset, push;
        NativeArray<int> cur, tgt, dest;
        NativeArray<float> speed, scale, wait, dwell, stuck;
        NativeArray<byte> state, hidden, evac, lie;
        NativeArray<Matrix4x4> matrices;
        //siatka do szukania sasiadow
        NativeArray<int> cellStart, cellFill, sorted, cellOf;
        GridParams agentGrid;
        //a to juz zwykle tablice do ktorych zaglada tylko watek glowny
        byte[] stage;
        byte[] staffAway;
        byte[] docBusy;
        Room[] assignedRoom;
        Room[] occRoom;
        int[] homeFloor;
        int[] roleStart;
        int[] bedNode;
        int[] patDoc;
        int[] ticket;
        float[] hp, maxHp, severity;
        //stopien choroby od 1 do 5
        byte[] illness;
        //patgen rosnie za kazdym razem jak pacjent wychodzi dzieki temu odwiedzajacy wie ze jego pacjenta juz nie ma
        int[] visitTarget, visitGen, patGen, visitorsOf;
        //co robi lekarz 0 swoj oddzial 1 konsultacja na innym 2 dyzurka
        byte[] docAct;

        //pacjenci i odwiedzajacy ktorzy sa akurat poza szpitalem czekaja tu az ich wylosujemy
        readonly Stack<int> freePatients = new Stack<int>();
        readonly Stack<int> freeVisitors = new Stack<int>();

        //kolejki na oddzialy zajete lozka i trwajace leczenia
        readonly Dictionary<Room, Queue<QEntry>> wardQueues = new Dictionary<Room, Queue<QEntry>>();
        readonly Dictionary<Room, List<int>> wardDoctors = new Dictionary<Room, List<int>>();
        readonly HashSet<int> usedBeds = new HashSet<int>();
        readonly List<TreatmentEntry> treatments = new List<TreatmentEntry>();
        readonly List<int> awaitBuf = new List<int>();
        Comparison<int> hpCompare;

        //stan ewakuacji
        bool evacRequested, evacCancelRequested;
        float evacTimer;
        int evacRemaining;

        Material[] mats;
        Material crossMat, healMat;
        Mesh crossMesh;
        NativeArray<Matrix4x4> crossMatrices, healMatrices;
        int crossCount, healCount;
        JobHandle handle;
        bool scheduled;
        static readonly Bounds HugeBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        public static Material MakeUnlit(Color c, bool instancing)
        {
            return MaterialUtil.Make(c, true, instancing);
        }

        //wolamy przed startem symulacji ustawienia ida prosto z pliku projektu
        public void Configure(SimSettingsBundle s)
        {
            population = ProjectData.Clone(s.population);
            arrivals = ProjectData.Clone(s.arrivals);
            health = ProjectData.Clone(s.health);
            treatment = ProjectData.Clone(s.treatment);
            movement = ProjectData.Clone(s.movement);
            collisions = ProjectData.Clone(s.collisions);
            routine = ProjectData.Clone(s.routine);
            evacuation = ProjectData.Clone(s.evacuation);
            if (s.appearance != null)
            {
                if (s.appearance.roleColors != null && s.appearance.roleColors.Length >= 4) visual.roleColors = (Color[])s.appearance.roleColors.Clone();
                if (s.appearance.roleScale != null) visual.roleScale = ProjectData.Clone(s.appearance.roleScale);
            }
        }

        bool IsOpen { get { int h = clock.Hour; return h >= routine.openHour && h < routine.closeHour; } }

        public Role RoleOf(int i)
        {
            if (i < roleStart[1]) return Role.Doctor;
            if (i < roleStart[2]) return Role.Patient;
            if (i < roleStart[3]) return Role.Visitor;
            return Role.Cleaner;
        }

        public static string RoleNamePl(Role r)
        {
            switch (r)
            {
                case Role.Doctor: return "lekarz";
                case Role.Patient: return "pacjent";
                case Role.Visitor: return "odwiedzający";
                default: return "sprzątacz";
            }
        }

        void Awake()
        {
            Instance = this;
            if (PersonDatabase.Instance == null) gameObject.AddComponent<PersonDatabase>();
            hpCompare = (a, b) => hp[a].CompareTo(hp[b]);
        }

        void Start()
        {
            clock = SimClock.Instance;
            map = HospitalMap.Instance;
            if (clock == null || map == null || map.NodeCount == 0)
            {
                Debug.LogError("AgentSystem: brak SimClock / HospitalMap / waypointów."); enabled = false; return;
            }

            if (visual.mesh == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Destroy(go);
            }
            mats = new Material[4];
            for (int r = 0; r < 4; r++)
            {
                if (visual.roleMaterials != null && r < visual.roleMaterials.Length && visual.roleMaterials[r] != null) mats[r] = visual.roleMaterials[r];
                //z oswietleniem kulki wygladaja mniej plasko i nie bija tak po oczach
                else mats[r] = MaterialUtil.Make(visual.roleColors[r], false, true);
            }
            crossMesh = MakeCrossMesh();
            crossMat = MaterialUtil.Make(new Color(0.85f, 0.1f, 0.12f), true, true, true);
            healMat = MaterialUtil.Make(new Color(0.35f, 0.95f, 0.45f), true, true, true);

            Spawn();
            Ready = true;
            clock.OnMinute += HandleMinute;
        }

        [ContextMenu("Respawn agentów")]
        public void Respawn()
        {
            if (!Ready) return;
            if (scheduled) { handle.Complete(); scheduled = false; }
            Free();
            Spawn();
            OnRespawn?.Invoke();
        }

        //tworzenie wszystkich agentow

        void Spawn()
        {
            int required = map.RequiredDoctors();
            if (population.doctors < required)
            {
                Debug.LogWarning($"Sale wymagają min. {required} lekarzy - zwiększam liczbę lekarzy z {population.doctors} do {required}.");
                population.doctors = required;
            }
            if (map.Wards.Count == 0) Debug.LogWarning("Brak sal typu Ward - lekarze i pacjenci nie mają dokąd iść.");
            if (map.WaitingRooms.Count == 0 && map.Entrances.Count == 0) Debug.LogWarning("Brak sali typu WaitingRoom i Entrance - pacjenci/odwiedzający nie pojawią się.");
            if (map.Exits.Count == 0) Debug.LogWarning("Brak sali typu Exit - pacjenci/odwiedzający nie mają którędy wyjść.");
            if (map.AssemblyPoints.Count == 0) Debug.LogWarning("Brak sali typu AssemblyPoint - ewakuacja nie zadziała.");

            foreach (var r in map.AllRooms) r.occupants = 0;
            freePatients.Clear(); freeVisitors.Clear();
            wardQueues.Clear(); wardDoctors.Clear(); usedBeds.Clear(); treatments.Clear();
            foreach (var w in map.Wards) wardQueues[w] = new Queue<QEntry>();
            Healed = 0; Deaths = 0; Waiting = 0;
            Evac = EvacState.None; evacRequested = false; evacCancelRequested = false; evacRemaining = 0; evacTimer = 0f;

            int D = population.doctors, P = population.patients, V = population.visitors, C = population.cleaners;
            roleStart = new[] { 0, D, D + P, D + P + V, D + P + V + C };
            Total = roleStart[4];

            var A = Allocator.Persistent;
            pos = new NativeArray<float3>(Total, A);
            offset = new NativeArray<float2>(Total, A);
            push = new NativeArray<float2>(Total, A);
            cur = new NativeArray<int>(Total, A);
            tgt = new NativeArray<int>(Total, A);
            dest = new NativeArray<int>(Total, A);
            speed = new NativeArray<float>(Total, A);
            scale = new NativeArray<float>(Total, A);
            wait = new NativeArray<float>(Total, A);
            dwell = new NativeArray<float>(Total, A);
            stuck = new NativeArray<float>(Total, A);
            state = new NativeArray<byte>(Total, A);
            hidden = new NativeArray<byte>(Total, A);
            evac = new NativeArray<byte>(Total, A);
            lie = new NativeArray<byte>(Total, A);
            matrices = new NativeArray<Matrix4x4>(Total, A);
            sorted = new NativeArray<int>(Total, A);
            cellOf = new NativeArray<int>(Total, A);
            stage = new byte[Total];
            staffAway = new byte[Total];
            docBusy = new byte[Total];
            assignedRoom = new Room[Total];
            occRoom = new Room[Total];
            homeFloor = new int[Total];
            bedNode = new int[Total];
            patDoc = new int[Total];
            ticket = new int[Total];
            hp = new float[Total];
            maxHp = new float[Total];
            severity = new float[Total];
            illness = new byte[Total];
            docAct = new byte[Total];
            visitTarget = new int[Total];
            visitGen = new int[Total];
            patGen = new int[Total];
            visitorsOf = new int[Total];
            crossMatrices = new NativeArray<Matrix4x4>(Mathf.Max(1, D), A);
            healMatrices = new NativeArray<Matrix4x4>(Mathf.Max(1, D), A);

            //siatka do szukania sasiadow
            float cs = Mathf.Max(0.5f, collisions.gridCellSize);
            float2 gmin = map.BoundsMin - 3f, gmax = map.BoundsMax + 3f;
            int gw = Mathf.Max(1, Mathf.CeilToInt((gmax.x - gmin.x) / cs));
            int gh = Mathf.Max(1, Mathf.CeilToInt((gmax.y - gmin.y) / cs));
            agentGrid = new GridParams { origin = gmin, invCell = 1f / cs, w = gw, h = gh, floors = map.FloorCount, baseY = map.BaseY, floorH = map.floorHeight };
            int cells = gw * gh * map.FloorCount;
            cellStart = new NativeArray<int>(cells + 1, A);
            cellFill = new NativeArray<int>(cells, A);

            var docRooms = map.AssignDoctorsToWards(D);
            rng = new Random(population.seed == 0 ? 1u : population.seed);

            for (int i = 0; i < Total; i++)
            {
                var role = RoleOf(i);
                float2 o = rng.NextFloat2Direction() * math.sqrt(rng.NextFloat()) * movement.offsetRadius;
                offset[i] = o;
                speed[i] = movement.walkSpeed * movement.roleSpeed.Get(role) *
                           rng.NextFloat(1f - movement.speedVariation, 1f + movement.speedVariation);
                scale[i] = visual.roleScale.Get(role);
                maxHp[i] = health.maxHp.Get(role);
                hp[i] = maxHp[i];
                severity[i] = 1f;
                illness[i] = 0;
                visitTarget[i] = -1;
                homeFloor[i] = -1;
                bedNode[i] = -1;
                patDoc[i] = -1;
                Place(i, 0);
                state[i] = 1;

                switch (role)
                {
                    case Role.Doctor:
                    {
                        var r = i < docRooms.Length ? docRooms[i] : null;
                        assignedRoom[i] = r;
                        homeFloor[i] = r != null ? r.floor : -1;
                        if (r != null)
                        {
                            if (!wardDoctors.TryGetValue(r, out var list)) { list = new List<int>(); wardDoctors[r] = list; }
                            list.Add(i);
                        }
                        bool inRoom = population.doctorsStartInOwnRoom && r != null;
                        Place(i, inRoom ? r.nodes[rng.NextInt(r.nodes.Count)] : rng.NextInt(map.NodeCount));
                        stage[i] = (byte)(inRoom ? 1 : 0);
                        wait[i] = rng.NextFloat(0f, Mathf.Max(1f, routine.doctorOwnWardStay.y * 60f));
                        break;
                    }
                    case Role.Patient:
                    {
                        if (rng.NextFloat() < population.patientsAlreadyInside && map.Wards.Count > 0) PlaceWaitingPatient(i);
                        else MakeHidden(i);
                        break;
                    }
                    case Role.Visitor:
                    {
                        if (rng.NextFloat() < population.visitorsAlreadyInside && map.Wards.Count > 0) PlaceInsideVisitor(i);
                        else MakeHidden(i);
                        break;
                    }
                    default:
                    {
                        int c = i - roleStart[3];
                        homeFloor[i] = routine.cleanersStayOnOwnFloor ? c % map.FloorCount : -1;
                        Place(i, map.RandomNodeOnFloor(homeFloor[i], ref rng));
                        wait[i] = rng.NextFloat(0f, 120f);
                        break;
                    }
                }
            }

            Debug.Log($"AgentSystem: {Total} agentów (lekarze {D}, pacjenci {P}, odwiedzający {V}, sprzątacze {C}).");
        }

        //pacjenci ktorzy sa w szpitalu od samego startu po prostu siedza w poczekalni
        void PlaceWaitingPatient(int i)
        {
            var ward = RandomOf(map.Wards);
            var wr = PickWaitingRoomWithSpace();
            if (wr == null || ward == null) { MakeHidden(i); return; }
            hidden[i] = 0; evac[i] = 0; stuck[i] = 0f;
            assignedRoom[i] = ward;
            InitPatientHealth(i, true);
            stage[i] = StWait;
            Occupy(i, wr);
            Place(i, wr.nodes[rng.NextInt(wr.nodes.Count)]);
            Wait(i, Never);
            Enqueue(i);
        }

        void PlaceInsideVisitor(int i)
        {
            int p = PickVisitTarget();
            if (p < 0) { MakeHidden(i); return; }
            SetVisitTarget(i, p);
            assignedRoom[i] = assignedRoom[p];
            hidden[i] = 0; evac[i] = 0;
            bool toWard = rng.NextFloat() < 0.5f || map.WaitingRooms.Count == 0;
            Vector2 range;
            if (toWard)
            {
                //na starcie pacjenci siedza w poczekalni wiec odwiedzajacy siada obok nich
                Place(i, PatientSpot(p));
                stage[i] = 2;
                range = routine.visitorWardStay;
            }
            else
            {
                var wr = RandomOf(map.WaitingRooms);
                Place(i, wr.nodes[rng.NextInt(wr.nodes.Count)]);
                stage[i] = StWait;
                range = routine.visitorWaitingRoomStay;
            }
            Wait(i, rng.NextFloat(0f, Mathf.Max(1f, range.y * 60f)));
        }

        void InitPatientHealth(int i, bool initial)
        {
            maxHp[i] = health.maxHp.Get(Role.Patient);
            hp[i] = initial ? maxHp[i] * rng.NextFloat(health.initialMinHpFraction, 1f) : maxHp[i];
            severity[i] = rng.NextFloat(Mathf.Min(health.severityRange.x, health.severityRange.y), Mathf.Max(health.severityRange.x, health.severityRange.y));
            illness[i] = (byte)RollIllness();
            patDoc[i] = -1;
            visitorsOf[i] = 0;
        }

        //stopien choroby losujemy z wag dlatego jedynek jest najwiecej a czworki i piatki trafiaja sie rzadko
        int RollIllness()
        {
            var w = health.stageChance;
            float sum = 0f;
            for (int k = 1; k <= 5; k++) sum += Mathf.Max(0f, w.Get(k));
            if (sum <= 0f) return 1;
            float r = rng.NextFloat() * sum;
            for (int k = 1; k <= 5; k++)
            {
                r -= Mathf.Max(0f, w.Get(k));
                if (r < 0f) return k;
            }
            return 5;
        }

        //odwiedziny
        bool VisitablePatient(int p)
        {
            if (hidden[p] != 0 || evac[p] != 0) return false;
            byte s = stage[p];
            return s == StWait || s == StBed || s == StTreat;
        }

        //szukamy ciezko chorego pacjenta ktory nie ma jeszcze kompletu gosci
        int PickVisitTarget()
        {
            int a = roleStart[1], b = roleStart[2];
            int n = b - a;
            if (n <= 0) return -1;
            int start = rng.NextInt(n);
            int minStage = Mathf.Clamp(routine.visitorMinStage, 1, 5);
            int perPatient = Mathf.Max(1, routine.visitorsPerPatient);
            for (int k = 0; k < n; k++)
            {
                int p = a + (start + k) % n;
                if (!VisitablePatient(p) || illness[p] < minStage || visitorsOf[p] >= perPatient) continue;
                return p;
            }
            return -1;
        }

        void SetVisitTarget(int v, int p)
        {
            visitTarget[v] = p;
            visitGen[v] = patGen[p];
            visitorsOf[p]++;
        }

        void ClearVisitTarget(int v)
        {
            int p = visitTarget[v];
            if (p >= 0 && visitGen[v] == patGen[p]) visitorsOf[p] = Mathf.Max(0, visitorsOf[p] - 1);
            visitTarget[v] = -1;
        }

        //sprawdza czy pacjent ktorego ktos odwiedza nadal jest w szpitalu
        bool VisitStillValid(int v)
        {
            int p = visitTarget[v];
            return p >= 0 && visitGen[v] == patGen[p] && VisitablePatient(p);
        }

        //gdzie stanac przy pacjencie przy jego lozku a jak jeszcze go nie ma to tam gdzie akurat jest
        int PatientSpot(int p) => bedNode[p] >= 0 ? bedNode[p] : (stage[p] == StBed || stage[p] == StTreat ? dest[p] : cur[p]);

        //glowna petla

        void Update()
        {
            if (!Ready) return;
            var fv = FloorViewController.Instance;
            visMin = fv != null ? fv.VisMin : -1000;
            visMax = fv != null ? fv.VisMax : 1000;

            float dsim = clock.SimDelta;
            if (dsim <= 0f) return;

            JobHandle h = default;
            bool collide = collisions.agentCollisions;
            if (collide)
            {
                h = new BuildGridJob
                {
                    pos = pos, hidden = hidden, cellStart = cellStart, cellFill = cellFill,
                    sorted = sorted, cellOf = cellOf, grid = agentGrid
                }.Schedule();
                h = new SepJob
                {
                    pos = pos, hidden = hidden, sorted = sorted, cellStart = cellStart, grid = agentGrid,
                    minDist = collisions.agentRadius * 2f, maxNeighbors = collisions.maxNeighbors, push = push
                }.Schedule(Total, 64, h);
            }

            handle = new MoveJob
            {
                nodePos = map.NodePos, nextHop = map.NextHop, nodeRoomType = map.NodeRoomType, nodeBed = map.NodeBed, nodeCount = map.NodeCount,
                wall = map.Wall, grid = agentGrid,
                dtSim = dsim, dtReal = Mathf.Min(Time.deltaTime, 0.1f), globalSpeed = movement.globalSpeedMultiplier, evacMul = evacuation.speedMultiplier,
                usePush = collide, pushStrength = collisions.separationStrength, maxPush = collisions.maxPushPerFrame,
                visMin = visMin, visMax = visMax,
                pos = pos, offset = offset, cur = cur, tgt = tgt, dest = dest, speed = speed, scale = scale,
                dwell = dwell, wait = wait, state = state, hidden = hidden, evac = evac, lie = lie, push = push,
                stuck = stuck, matrices = matrices
            }.Schedule(Total, 128, h);
            scheduled = true;
        }

        void LateUpdate()
        {
            if (!Ready) return;
            if (scheduled) { handle.Complete(); scheduled = false; }

            //ewakuacje odpalamy dopiero tutaj bo wtedy zaden job nie grzebie w tablicach
            if (evacCancelRequested) { evacCancelRequested = false; FinishEvacuationNow(); }
            if (evacRequested) { evacRequested = false; BeginEvacuation(); }

            //decyzje podejmujemy tylko dla tych ktorym skonczyl sie timer reszta nas nie interesuje
            int inside = 0;
            for (int r = 0; r < 4; r++) InsideByRole[r] = 0;
            for (int i = 0; i < Total; i++)
            {
                if (hidden[i] != 0) continue;
                inside++;
                if (state[i] == 2) Advance(i);
            }
            for (int r = 0; r < 4; r++)
                for (int i = roleStart[r]; i < roleStart[r + 1]; i++)
                    if (hidden[i] == 0) InsideByRole[r]++;
            Inside = inside;
            CheckVisitors();

            float simDt = clock.SimDelta;
            UpdateTreatments(simDt);
            if (Evac == EvacState.None)
            {
                DispatchBeds();
                DispatchTreatments();
            }
            UpdateHealth(simDt);
            UpdateEvacuation(simDt);
            CountStates();

            Render();
        }

        //jesli pacjent zostal wyleczony umarl albo wyszedl to odwiedzajacy tez sie zbiera
        void CheckVisitors()
        {
            if (Evac != EvacState.None) return;
            for (int v = roleStart[2]; v < roleStart[3]; v++)
            {
                if (hidden[v] != 0 || evac[v] != 0) continue;
                if (stage[v] != StWait && stage[v] != 2) continue;
                if (VisitStillValid(v)) continue;
                ClearVisitTarget(v);
                stage[v] = StLeave;
                var ex = RandomOf(map.Exits);
                if (ex == null || ex.nodes.Count == 0) { MakeHidden(v); continue; }
                Redirect(v, ex.nodes[rng.NextInt(ex.nodes.Count)], 0f);
            }
        }

        void CountStates()
        {
            int busy = 0, bed = 0, treated = 0;
            for (int d = 0; d < roleStart[1]; d++) if (hidden[d] == 0 && docBusy[d] != 0) busy++;
            for (int p = roleStart[1]; p < roleStart[2]; p++)
            {
                if (hidden[p] != 0) continue;
                if (stage[p] == StBed) bed++;
                else if (stage[p] == StTreat) treated++;
            }
            DoctorsBusy = busy; PatientsInBed = bed; PatientsTreated = treated;
        }

        //plaski krzyz w plaszczyznie xy ktory potem obracamy w strone kamery
        static Mesh MakeCrossMesh()
        {
            const float L = 0.5f, T = 0.17f;
            var v = new[]
            {
                new Vector3(-L, -T, 0), new Vector3(L, -T, 0), new Vector3(L, T, 0), new Vector3(-L, T, 0),
                new Vector3(-T, -L, 0), new Vector3(T, -L, 0), new Vector3(T, L, 0), new Vector3(-T, L, 0),
            };
            var m = new Mesh { name = "Krzyz" };
            m.vertices = v;
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        //czerwony krzyz na kulce lekarza i pulsujacy zielony krzyz nad tym kogo akurat leczy
        void BuildMarkers()
        {
            crossCount = 0; healCount = 0;
            var cam = Camera.main;
            if (cam == null) return;
            Quaternion rot = cam.transform.rotation;
            Vector3 fwd = cam.transform.forward;
            Vector3 up = cam.transform.up;
            int D = roleStart[1];
            for (int d = 0; d < D; d++)
            {
                var mtx = matrices[d];
                float sc = mtx.m00;
                if (sc <= 0f) continue;
                Vector3 p = new Vector3(mtx.m03, mtx.m13, mtx.m23);
                //krzyz przyklejony do kulki od strony kamery
                Vector3 at = p - fwd * (sc * 0.5f + 0.01f);
                crossMatrices[crossCount++] = Matrix4x4.TRS(at, rot, Vector3.one * (sc * 0.62f));
            }

            float tReal = Time.realtimeSinceStartup;
            for (int k = 0; k < treatments.Count && healCount < healMatrices.Length; k++)
            {
                var t = treatments[k];
                if (!t.started) continue;
                var mtx = matrices[t.pat];
                float sc = mtx.m00;
                if (sc <= 0f) continue;
                Vector3 p = new Vector3(mtx.m03, mtx.m13, mtx.m23);
                float pulse = 1f + 0.18f * Mathf.Sin(tReal * 5f + t.pat);
                float bob = 0.08f * Mathf.Sin(tReal * 2.5f + t.pat * 0.7f);
                Vector3 at = p + Vector3.up * (sc * 0.5f + 0.35f + bob) + up * (sc * 0.4f) - fwd * (sc * 0.6f);
                healMatrices[healCount++] = Matrix4x4.TRS(at, rot, Vector3.one * (sc * 0.55f * pulse));
            }
        }

        void RenderMarkers()
        {
            BuildMarkers();
            if (crossCount > 0) DrawInstanced(crossMat, crossMatrices, crossCount);
            if (healCount > 0) DrawInstanced(healMat, healMatrices, healCount);
        }

        void DrawInstanced(Material m, NativeArray<Matrix4x4> arr, int count)
        {
            var rp = new RenderParams(m) { worldBounds = HugeBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int off = 0; off < count; off += 1023)
                Graphics.RenderMeshInstanced(rp, crossMesh, 0, arr, Mathf.Min(1023, count - off), off);
        }

        void Render()
        {
            RenderMarkers();
            for (int r = 0; r < 4; r++)
            {
                int start = roleStart[r], count = roleStart[r + 1] - start;
                if (count <= 0) continue;
                var rp = new RenderParams(mats[r])
                {
                    worldBounds = HugeBounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false
                };
                for (int off = 0; off < count; off += 1023)
                {
                    int c = Mathf.Min(1023, count - off);
                    Graphics.RenderMeshInstanced(rp, visual.mesh, 0, matrices, c, start + off);
                }
            }
        }

        //co minute gry losujemy czy ktos nowy przyszedl

        void HandleMinute()
        {
            if (!Ready || Evac != EvacState.None || !IsOpen) return;
            SpawnRoll(true, arrivals.patientsPerHour);
            SpawnRoll(false, arrivals.visitorsPerHour);
        }

        //szansa na minute to po prostu liczba na godzine podzielona przez 60
        void SpawnRoll(bool patient, float perHour)
        {
            if (perHour <= 0f) return;
            float expected = perHour / 60f;
            int n = (int)expected;
            if (rng.NextFloat() < expected - n) n++;
            for (int k = 0; k < n; k++)
                if (!SpawnArrival(patient)) break;
        }

        bool SpawnArrival(bool patient)
        {
            var pool = patient ? freePatients : freeVisitors;
            if (pool.Count == 0) return false;

            Room wr = patient ? PickWaitingRoomWithSpace() : RandomOf(map.WaitingRooms);
            if (patient && wr == null && map.WaitingRooms.Count > 0) return false;

            bool inWaiting = patient ? arrivals.patientsSpawnInWaitingRoom : arrivals.visitorsSpawnInWaitingRoom;
            Room ent = RandomOf(map.Entrances);
            if (wr == null && ent == null) return false;
            if (!(inWaiting && wr != null) && ent == null) inWaiting = true;

            //odwiedzajacy przychodzi tylko wtedy gdy jest ktos ciezko chory kogo moze odwiedzic
            int visitP = -1;
            if (!patient)
            {
                visitP = PickVisitTarget();
                if (visitP < 0) return false;
            }

            int i = pool.Pop();
            hidden[i] = 0; evac[i] = 0; stage[i] = StWait; stuck[i] = 0f;
            assignedRoom[i] = patient ? RandomOf(map.Wards) : assignedRoom[visitP];
            if (!patient) SetVisitTarget(i, visitP);

            if (patient)
            {
                InitPatientHealth(i, false);
                if (wr != null) Occupy(i, wr);
                if (inWaiting && wr != null) { Place(i, wr.nodes[rng.NextInt(wr.nodes.Count)]); Wait(i, Never); }
                else
                {
                    Place(i, ent.nodes[rng.NextInt(ent.nodes.Count)]);
                    if (wr != null) GoToRoomSec(i, wr, Never); else Wait(i, Never);
                }
                Enqueue(i);
            }
            else
            {
                var stay = routine.visitorWaitingRoomStay;
                if (inWaiting && wr != null) { Place(i, wr.nodes[rng.NextInt(wr.nodes.Count)]); Wait(i, Dwell(stay)); }
                else
                {
                    Place(i, ent.nodes[rng.NextInt(ent.nodes.Count)]);
                    if (wr != null) GoToRoom(i, wr, stay); else Wait(i, 60f);
                }
            }
            return true;
        }

        Room PickWaitingRoomWithSpace()
        {
            var list = map.WaitingRooms;
            int n = list.Count;
            if (n == 0) return null;
            int start = rng.NextInt(n);
            for (int k = 0; k < n; k++)
            {
                var r = list[(start + k) % n];
                if (r.HasSpace) return r;
            }
            return null;
        }

        void Occupy(int i, Room r)
        {
            Release(i);
            occRoom[i] = r;
            r.occupants++;
        }

        void Release(int i)
        {
            var r = occRoom[i];
            if (r != null) { r.occupants = Mathf.Max(0, r.occupants - 1); occRoom[i] = null; }
        }

        //kolejka do lozek i leczenie

        void Enqueue(int i)
        {
            var ward = assignedRoom[i];
            if (ward == null || !wardQueues.TryGetValue(ward, out var q)) return;
            ticket[i]++;
            q.Enqueue(new QEntry { id = i, ticket = ticket[i] });
        }

        //jak na oddziale zwolni sie lozko to bierzemy kolejnego z poczekalni
        void DispatchBeds()
        {
            foreach (var kv in wardQueues)
            {
                var ward = kv.Key;
                var q = kv.Value;
                while (q.Count > 0 && ward.HasSpace)
                {
                    var e = q.Dequeue();
                    int p = e.id;
                    if (hidden[p] != 0 || stage[p] != StWait || ticket[p] != e.ticket || assignedRoom[p] != ward) continue;
                    SendToBed(p, ward);
                }
            }
        }

        void SendToBed(int p, Room ward)
        {
            Occupy(p, ward);
            int node = PickBedNode(ward);
            bedNode[p] = node;
            usedBeds.Add(node);
            stage[p] = StBed;
            patDoc[p] = -1;
            lie[p] = 1;
            SetDest(p, node, Never);
        }

        //najpierw prawdziwe lozka z modelu a jak ich nie ma to dowolny punkt na oddziale
        int PickBedNode(Room ward)
        {
            var list = ward.bedNodes.Count > 0 ? ward.bedNodes : ward.nodes;
            int n = list.Count;
            int start = rng.NextInt(n);
            for (int k = 0; k < n; k++)
            {
                int node = list[(start + k) % n];
                if (!usedBeds.Contains(node)) return node;
            }
            return list[start];
        }

        void ReleaseBed(int i)
        {
            lie[i] = 0;
            if (bedNode[i] >= 0) { usedBeds.Remove(bedNode[i]); bedNode[i] = -1; }
        }

        bool DocFree(int d) => hidden[d] == 0 && evac[d] == 0 && docBusy[d] == 0;

        int FindFreeDoctor(int p)
        {
            var ward = assignedRoom[p];
            float3 bp = map.NodePos[bedNode[p]];
            int best = -1; float bd = float.MaxValue;
            if (ward != null && wardDoctors.TryGetValue(ward, out var list))
            {
                for (int k = 0; k < list.Count; k++)
                {
                    int d = list[k];
                    if (!DocFree(d)) continue;
                    float dd = math.distancesq(pos[d], bp);
                    if (dd < bd) { bd = dd; best = d; }
                }
            }
            if (best >= 0 || !treatment.otherDoctorsCanHelp) return best;
            for (int d = 0; d < roleStart[1]; d++)
            {
                if (!DocFree(d)) continue;
                float dd = math.distancesq(pos[d], bp);
                if (dd < bd) { bd = dd; best = d; }
            }
            return best;
        }

        //lekarze ida najpierw do tych co maja najmniej hp
        void DispatchTreatments()
        {
            awaitBuf.Clear();
            for (int i = roleStart[1]; i < roleStart[2]; i++)
                if (hidden[i] == 0 && stage[i] == StBed && state[i] != 0 && patDoc[i] < 0) awaitBuf.Add(i);
            if (awaitBuf.Count == 0) return;
            awaitBuf.Sort(hpCompare);

            for (int k = 0; k < awaitBuf.Count; k++)
            {
                int p = awaitBuf[k];
                int d = FindFreeDoctor(p);
                if (d < 0) continue;
                docBusy[d] = 1;
                patDoc[p] = d;
                float dur = Mathf.Max(60f, Dwell(treatment.stageDuration.Get(Mathf.Max(1, (int)illness[p]))));
                treatments.Add(new TreatmentEntry { doc = d, pat = p, remaining = dur, total = dur, started = false });
                Redirect(d, bedNode[p], Never);
            }
        }

        void UpdateTreatments(float simDt)
        {
            for (int k = treatments.Count - 1; k >= 0; k--)
            {
                var t = treatments[k];
                if (!t.started)
                {
                    //leczenie startuje dopiero jak lekarz i pacjent sa juz przy lozku
                    if (state[t.doc] != 0 && state[t.pat] != 0)
                    {
                        t.started = true;
                        stage[t.pat] = StTreat;
                    }
                }
                else
                {
                    float before = t.remaining;
                    t.remaining -= simDt;
                    if (t.remaining <= 0f)
                    {
                        treatments.RemoveAt(k);
                        FinishTreatment(t);
                        continue;
                    }
                    //hp powoli rosnie i na koniec leczenia dochodzi do maksimum
                    int pi = t.pat;
                    hp[pi] = Mathf.Min(maxHp[pi], hp[pi] + (maxHp[pi] - hp[pi]) * Mathf.Min(1f, simDt / Mathf.Max(before, 1f)));
                }
                treatments[k] = t;
            }
        }

        void FinishTreatment(TreatmentEntry t)
        {
            int p = t.pat;
            hp[p] = maxHp[p];
            Healed++;
            FreeDoctor(t.doc);
            patDoc[p] = -1;
            Release(p);
            ReleaseBed(p);
            stage[p] = StLeave;
            GoToRoomSec(p, RandomOf(map.Exits), 0f);
        }

        void FreeDoctor(int d)
        {
            docBusy[d] = 0;
            if (hidden[d] != 0) return;
            stage[d] = 0;
            float w = rng.NextFloat(5f, 30f);
            //jak lekarz byl w drodze to najpierw dochodzi do najblizszego waypointa inaczej potrafil przejsc przez sciane
            if (state[d] == 0) { dest[d] = tgt[d]; dwell[d] = w; }
            else Wait(d, w);
        }

        void CancelTreatmentsOf(int i)
        {
            for (int k = treatments.Count - 1; k >= 0; k--)
            {
                var t = treatments[k];
                if (t.pat != i && t.doc != i) continue;
                treatments.RemoveAt(k);
                if (t.doc != i) FreeDoctor(t.doc);
                if (t.pat != i)
                {
                    //lekarz zniknal wiec pacjent czeka na nastepnego
                    patDoc[t.pat] = -1;
                    if (stage[t.pat] == StTreat) stage[t.pat] = StBed;
                }
            }
        }

        //pacjent traci zdrowie kiedy czeka a jak spadnie do zera to umiera

        void UpdateHealth(float simDt)
        {
            float perSec = health.patientHpLossPerHour / 3600f * simDt;
            int waiting = 0;
            for (int i = roleStart[1]; i < roleStart[2]; i++)
            {
                if (hidden[i] != 0 || evac[i] != 0) continue;
                byte s = stage[i];
                if (s != StWait && s != StBed) continue;
                if (s == StWait) waiting++;
                if (perSec <= 0f) continue;
                hp[i] -= perSec * severity[i] * health.stageHpLoss.Get(Mathf.Max(1, (int)illness[i]));
                if (hp[i] <= 0f) Die(i);
            }
            Waiting = waiting;
        }

        void Die(int i)
        {
            hp[i] = 0f;
            Deaths++;
            var role = RoleOf(i);
            string name = "Nieznany";
            if (PersonDatabase.Instance != null)
            {
                var p = PersonDatabase.Instance.Generate(i, role);
                name = p.first + " " + p.last;
            }
            Debug.Log($"{name}, {RoleNamePl(role)} umiera");
            MakeHidden(i);
        }

        //ewakuacja

        public void StartEvacuation() { if (Evac == EvacState.None) evacRequested = true; }

        public void CancelEvacuation() { if (Evac != EvacState.None) evacCancelRequested = true; }

        void BeginEvacuation()
        {
            if (Evac != EvacState.None) return;
            if (map.AssemblyPoints.Count == 0)
            {
                Debug.LogWarning("Ewakuacja: brak sali typu AssemblyPoint (miejsce zbiórki) - dodaj ją w scenie.");
                return;
            }

            Evac = EvacState.Evacuating;
            evacTimer = 0f;
            evacRemaining = 0;
            treatments.Clear();
            foreach (var q in wardQueues.Values) q.Clear();
            for (int d = 0; d < roleStart[1]; d++) docBusy[d] = 0;

            for (int i = 0; i < Total; i++)
            {
                if (hidden[i] != 0) continue;
                evac[i] = 1;
                evacRemaining++;
                Release(i);
                ReleaseBed(i);
                patDoc[i] = -1;
                var room = NearestAssembly(pos[i]);
                Redirect(i, room.nodes[rng.NextInt(room.nodes.Count)], 0f);
            }
            Debug.Log($"EWAKUACJA: {evacRemaining} osób w drodze na miejsce zbiórki.");
            OnEvacuationStarted?.Invoke();
        }

        Room NearestAssembly(float3 p)
        {
            Room best = null; float bd = float.MaxValue;
            var v = (Vector3)p;
            foreach (var r in map.AssemblyPoints)
            {
                float d = (r.WorldBounds.center - v).sqrMagnitude;
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        void UpdateEvacuation(float simDt)
        {
            if (Evac == EvacState.Evacuating)
            {
                evacTimer += simDt;
                if (evacRemaining <= 0 || evacTimer >= evacuation.maxDurationMinutes * 60f)
                {
                    for (int i = 0; i < Total; i++) if (evac[i] != 0) MakeHidden(i);
                    Evac = EvacState.Cooldown;
                    evacTimer = 0f;
                    Debug.Log("EWAKUACJA zakończona - wszyscy są na miejscu zbiórki. Personel wróci po przerwie.");
                }
            }
            else if (Evac == EvacState.Cooldown)
            {
                evacTimer += simDt;
                if (evacTimer >= evacuation.returnDelayMinutes * 60f) EndEvacuation();
            }
        }

        void FinishEvacuationNow()
        {
            for (int i = 0; i < Total; i++) if (evac[i] != 0) MakeHidden(i);
            EndEvacuation();
        }

        void EndEvacuation()
        {
            for (int i = 0; i < Total; i++) ReturnStaff(i);
            Evac = EvacState.None;
            evacTimer = 0f; evacRemaining = 0;
            Debug.Log("Koniec ewakuacji - personel wrócił, pacjenci znów mogą przychodzić.");
            OnEvacuationEnded?.Invoke();
        }

        void ReturnStaff(int i)
        {
            if (staffAway[i] == 0) return;
            staffAway[i] = 0;
            hidden[i] = 0; evac[i] = 0; stage[i] = 0; stuck[i] = 0f; docBusy[i] = 0;
            int node;
            if (RoleOf(i) == Role.Doctor && assignedRoom[i] != null && assignedRoom[i].nodes.Count > 0)
                node = assignedRoom[i].nodes[rng.NextInt(assignedRoom[i].nodes.Count)];
            else node = map.RandomNodeOnFloor(homeFloor[i], ref rng);
            Place(i, node);
            Wait(i, rng.NextFloat(5f, 60f));
        }

        //jak ktos jest w drodze to najpierw dochodzi do najblizszego punktu i dopiero wtedy skreca
        void Redirect(int i, int node, float dwellSec)
        {
            if (state[i] == 0) { dest[i] = node; dwell[i] = dwellSec; }
            else SetDest(i, node, dwellSec);
        }

        //co kto robi na co dzien

        void Advance(int i)
        {
            if (evac[i] != 0) { MakeHidden(i); return; }

            switch (RoleOf(i))
            {
                case Role.Doctor: AdvanceDoctor(i); break;
                case Role.Patient:
                    //pacjent sam o niczym nie decyduje dopoki nie dojdzie do wyjscia
                    if (stage[i] == StLeave) MakeHidden(i);
                    else Wait(i, Never);
                    break;
                case Role.Visitor: AdvanceVisitor(i); break;
                default: GoToRoom(i, Pick(-1, homeFloor[i]), routine.cleanerRoomStay); break;
            }
            if (state[i] == 2) Wait(i, 60f);
        }

        void AdvanceDoctor(int i)
        {
            int home = homeFloor[i];
            if (stage[i] == 0)
            {
                GoToRoom(i, assignedRoom[i], routine.doctorOwnWardStay);
                stage[i] = 1;
                docAct[i] = 0;
            }
            else if (rng.NextFloat() < routine.doctorVisitOtherWardChance)
            {
                var w = Pick((int)RoomType.Ward, routine.doctorsStayOnOwnFloor ? home : -1);
                GoToRoom(i, w, routine.doctorOtherWardStay);
                docAct[i] = (byte)(w == assignedRoom[i] ? 0 : 1);
            }
            else
            {
                GoToRoom(i, Pick((int)RoomType.StaffRoom, home), routine.doctorStaffRoomStay);
                stage[i] = 0;
                docAct[i] = 2;
            }
        }

        void AdvanceVisitor(int i)
        {
            switch (stage[i])
            {
                case StWait:
                    if (!VisitStillValid(i))
                    {
                        ClearVisitTarget(i);
                        GoToRoomSec(i, RandomOf(map.Exits), 0f);
                        stage[i] = StLeave;
                        break;
                    }
                    SetDest(i, PatientSpot(visitTarget[i]), Dwell(routine.visitorWardStay));
                    stage[i] = 2;
                    break;
                case 2:
                    ClearVisitTarget(i);
                    GoToRoomSec(i, RandomOf(map.Exits), 0f);
                    stage[i] = StLeave;
                    break;
                default:
                    MakeHidden(i);
                    break;
            }
        }

        //rozne drobiazgi

        float Dwell(Vector2 minutes) => minutes.y > minutes.x ? rng.NextFloat(minutes.x, minutes.y) * 60f : minutes.x * 60f;

        Room RandomOf(List<Room> list) => list.Count == 0 ? null : list[rng.NextInt(list.Count)];

        //losowa sala danego typu na pietrze a jak takiej nie ma to z calego szpitala
        Room Pick(int typeKey, int floor)
        {
            var list = map.RoomsOf(typeKey, floor);
            if (list.Count == 0 && floor >= 0) list = map.RoomsOf(typeKey, -1);
            return RandomOf(list);
        }

        void Wait(int i, float sec) { state[i] = 1; wait[i] = sec; }

        void GoToRoom(int i, Room room, Vector2 dwellMinutes) => GoToRoomSec(i, room, Dwell(dwellMinutes));

        void GoToRoomSec(int i, Room room, float dwellSec)
        {
            if (room == null || room.nodes.Count == 0) { Wait(i, 60f); return; }
            SetDest(i, room.nodes[rng.NextInt(room.nodes.Count)], dwellSec);
        }

        float3 SafePos(int node, float2 o)
        {
            float3 p = map.NodePos[node];
            float3 q = p; q.x += o.x; q.z += o.y;
            return map.Wall.Blocked(q) ? p : q;
        }

        void Place(int i, int node)
        {
            cur[i] = node; tgt[i] = node; dest[i] = node;
            pos[i] = SafePos(node, offset[i]);
        }

        void SetDest(int i, int node, float dwellSec)
        {
            dest[i] = node; dwell[i] = dwellSec;
            int c = cur[i];
            if (c == node) { Wait(i, dwellSec); return; }
            int nx = map.NextHop[c * map.NodeCount + node];
            if (nx < 0) { Place(i, node); Wait(i, dwellSec); return; }
            tgt[i] = nx;
            state[i] = 0;
        }

        //agent znika pacjenci i odwiedzajacy wracaja do puli a personel czeka na koniec ewakuacji
        void MakeHidden(int i)
        {
            if (evac[i] != 0) { evac[i] = 0; evacRemaining--; }
            if (treatments.Count > 0) CancelTreatmentsOf(i);
            Release(i);
            ReleaseBed(i);
            patDoc[i] = -1;
            hidden[i] = 1;
            state[i] = 1;
            wait[i] = Never;
            stage[i] = 0;
            var role = RoleOf(i);
            if (role == Role.Visitor) ClearVisitTarget(i);
            if (role == Role.Patient) { patGen[i]++; visitorsOf[i] = 0; illness[i] = 0; }
            if (role == Role.Patient) freePatients.Push(i);
            else if (role == Role.Visitor) freeVisitors.Push(i);
            else staffAway[i] = 1;
        }

        //rzeczy ktore czyta interfejs

        public bool IsHidden(int i) => hidden[i] != 0;
        public int FloorOf(int i) => Mathf.Max(0, Mathf.RoundToInt((pos[i].y - map.BaseY) / map.floorHeight));
        public bool IsVisible(int i) { if (hidden[i] != 0) return false; int f = FloorOf(i); return f >= visMin && f <= visMax; }
        public Vector3 PositionOf(int i) => pos[i];
        public float ScaleOf(int i) => scale[i];
        public float HpOf(int i) => hp[i];
        public float MaxHpOf(int i) => maxHp[i];
        public int IllnessOf(int i) => illness[i];
        public Color RoleColor(Role r) => visual.roleColors != null && visual.roleColors.Length > (int)r ? visual.roleColors[(int)r] : Color.white;
        public Room AssignedRoomOf(int i) => assignedRoom[i];
        public int HomeFloorOf(int i) => homeFloor[i];
        public bool IsEvacuating(int i) => evac[i] != 0;

        //dla lekarza zwraca leczonego pacjenta a dla pacjenta lekarza albo minus jeden jak nikogo nie ma
        public int TreatmentPartnerOf(int i)
        {
            for (int k = 0; k < treatments.Count; k++)
            {
                var t = treatments[k];
                if (t.doc == i) return t.pat;
                if (t.pat == i) return t.doc;
            }
            return -1;
        }

        //postep leczenia od 0 do 1 albo minus jeden jak leczenie jeszcze nie ruszylo
        public float TreatmentProgressOf(int i)
        {
            for (int k = 0; k < treatments.Count; k++)
            {
                var t = treatments[k];
                if (t.pat != i && t.doc != i) continue;
                if (!t.started) return -1f;
                return Mathf.Clamp01(1f - t.remaining / Mathf.Max(1f, t.total));
            }
            return -1f;
        }

        //ile jeszcze zostalo leczenia w sekundach gry
        public float TreatmentRemainingOf(int i)
        {
            for (int k = 0; k < treatments.Count; k++)
            {
                var t = treatments[k];
                if (t.pat == i || t.doc == i) return t.started ? t.remaining : t.total;
            }
            return 0f;
        }

        //trwajace leczenia do paskow postepu nad glowami
        public int TreatmentCount => treatments.Count;
        public bool TreatmentAt(int k, out int doc, out int pat, out float progress)
        {
            var t = treatments[k];
            doc = t.doc; pat = t.pat;
            progress = t.started ? Mathf.Clamp01(1f - t.remaining / Mathf.Max(1f, t.total)) : 0f;
            return t.started;
        }

        //kogo odwiedza dany odwiedzajacy albo minus jeden
        public int VisitTargetOf(int v) => (visitTarget[v] >= 0 && VisitStillValid(v)) ? visitTarget[v] : -1;
        public int VisitorsOf(int p) => visitorsOf[p];

        //krotki opis tego co ktos akurat robi do okienka postaci
        public string StatusOf(int i)
        {
            if (hidden[i] != 0) return "poza szpitalem";
            if (evac[i] != 0) return "ewakuuje się";
            switch (RoleOf(i))
            {
                case Role.Doctor:
                {
                    if (docBusy[i] != 0)
                    {
                        float pr = TreatmentProgressOf(i);
                        return pr < 0f ? "idzie do pacjenta" : "leczy pacjenta (" + Mathf.RoundToInt(pr * 100f) + "%)";
                    }
                    switch (docAct[i])
                    {
                        case 2: return state[i] == 0 ? "idzie do dyżurki" : "odpoczywa w dyżurce";
                        case 1: return state[i] == 0 ? "idzie na konsultację na inny oddział" : "konsultacja na innym oddziale";
                        default: return state[i] == 0 ? "idzie na swój oddział" : "obchód na swoim oddziale";
                    }
                }
                case Role.Patient:
                    switch (stage[i])
                    {
                        case StWait: return state[i] == 0 ? "idzie do poczekalni" : "czeka w poczekalni na łóżko";
                        case StBed:
                            if (state[i] == 0) return "idzie do łóżka";
                            return patDoc[i] >= 0 ? "lekarz jest w drodze" : "czeka w łóżku na lekarza";
                        case StTreat: return "jest leczony (" + Mathf.RoundToInt(Mathf.Max(0f, TreatmentProgressOf(i)) * 100f) + "%)";
                        case StLeave: return "wyleczony, wychodzi";
                    }
                    return "w szpitalu";
                case Role.Visitor:
                    switch (stage[i])
                    {
                        case StWait: return state[i] == 0 ? "idzie do poczekalni" : "czeka w poczekalni";
                        case 2: return state[i] == 0 ? "idzie do pacjenta" : "odwiedza pacjenta";
                        default: return "wychodzi";
                    }
                default:
                    return state[i] == 0 ? "idzie sprzątać" : "sprząta";
            }
        }

        //szukamy najblizszej widocznej kulki do kursora liczac w pikselach
        public int FindNearestScreen(Camera cam, Vector2 mouse, float maxPixels)
        {
            float maxSq = maxPixels * maxPixels, best = float.MaxValue; int idx = -1;
            for (int i = 0; i < Total; i++)
            {
                if (!IsVisible(i)) continue;
                Vector3 sp = cam.WorldToScreenPoint(pos[i]);
                if (sp.z <= 0f) continue;
                float dx = sp.x - mouse.x, dy = sp.y - mouse.y;
                float d2 = dx * dx + dy * dy;
                if (d2 > maxSq) continue;
                float score = d2 + sp.z * 0.5f;
                if (score < best) { best = score; idx = i; }
            }
            return idx;
        }

        void Free()
        {
            if (pos.IsCreated) pos.Dispose();
            if (offset.IsCreated) offset.Dispose();
            if (push.IsCreated) push.Dispose();
            if (cur.IsCreated) cur.Dispose();
            if (tgt.IsCreated) tgt.Dispose();
            if (dest.IsCreated) dest.Dispose();
            if (speed.IsCreated) speed.Dispose();
            if (scale.IsCreated) scale.Dispose();
            if (wait.IsCreated) wait.Dispose();
            if (dwell.IsCreated) dwell.Dispose();
            if (stuck.IsCreated) stuck.Dispose();
            if (state.IsCreated) state.Dispose();
            if (hidden.IsCreated) hidden.Dispose();
            if (evac.IsCreated) evac.Dispose();
            if (lie.IsCreated) lie.Dispose();
            if (matrices.IsCreated) matrices.Dispose();
            if (sorted.IsCreated) sorted.Dispose();
            if (cellOf.IsCreated) cellOf.Dispose();
            if (cellStart.IsCreated) cellStart.Dispose();
            if (cellFill.IsCreated) cellFill.Dispose();
            if (crossMatrices.IsCreated) crossMatrices.Dispose();
            if (healMatrices.IsCreated) healMatrices.Dispose();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (clock != null) clock.OnMinute -= HandleMinute;
            if (scheduled) handle.Complete();
            Free();
            if (crossMesh != null) Destroy(crossMesh);
            if (crossMat != null) Destroy(crossMat);
            if (healMat != null) Destroy(healMat);
        }
    }
}
