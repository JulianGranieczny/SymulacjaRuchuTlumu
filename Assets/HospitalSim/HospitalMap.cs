using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace HospitalSim
{
    //parametry siatki 2d razy liczba pieter przydaja sie i do scian i do szukania sasiadow
    public struct GridParams
    {
        public float2 origin;
        public float invCell;
        public int w, h, floors;
        public float baseY, floorH;

        public int Floor(float y) => math.clamp((int)math.round((y - baseY) / floorH), 0, floors - 1);
        public int CellX(float x) => math.clamp((int)math.floor((x - origin.x) * invCell), 0, w - 1);
        public int CellZ(float z) => math.clamp((int)math.floor((z - origin.y) * invCell), 0, h - 1);
        public int Index(float3 p) => (Floor(p.y) * h + CellZ(p.z)) * w + CellX(p.x);
    }

    //upieczona mapa scian 0 to wolne 1 to sciana uklad pietro z x
    public struct WallGrid
    {
        public bool active;
        public GridParams g;
        [ReadOnly] public NativeArray<byte> blocked;

        public bool Blocked(float3 p)
        {
            if (!active) return false;
            int f = (int)math.round((p.y - g.baseY) / g.floorH);
            if (f < 0 || f >= g.floors) return false;
            //na schodach albo w windzie miedzy pietrami w ogole nie sprawdzamy scian
            if (math.abs(p.y - (g.baseY + f * g.floorH)) > 0.7f) return false;
            int cx = (int)math.floor((p.x - g.origin.x) * g.invCell);
            int cz = (int)math.floor((p.z - g.origin.y) * g.invCell);
            if (cx < 0 || cz < 0 || cx >= g.w || cz >= g.h) return false;
            return blocked[(f * g.h + cz) * g.w + cx] != 0;
        }
    }

    [BurstCompile(DisableSafetyChecks = true)]
    internal struct FloydWarshallJob : IJob
    {
        const float INF = 1e30f;
        public int n;
        public NativeArray<float> dist;
        public NativeArray<int> next;

        public void Execute()
        {
            for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
            {
                float dik = dist[i * n + k];
                if (dik >= INF) continue;
                int nik = next[i * n + k];
                for (int j = 0; j < n; j++)
                {
                    float nd = dik + dist[k * n + j];
                    if (nd < dist[i * n + j])
                    {
                        dist[i * n + j] = nd;
                        next[i * n + j] = nik;
                    }
                }
            }
        }
    }

    //buduje graf dla calego budynku przypisuje waypointy do sal liczy tablice nexthop
    //i na koniec piecze siatke scian
    [DefaultExecutionOrder(-50)]
    public class HospitalMap : MonoBehaviour
    {
        public static HospitalMap Instance { get; private set; }

        [Header("Piętra")]
        [Tooltip("Odległość w pionie między poziomami podłóg (m). Waypointy parteru = BaseY, 1. piętra = BaseY + FloorHeight itd.")]
        public float floorHeight = 4f;
        public string[] floorNames = { "Parter", "1 piętro", "2 piętro" };

        [Header("Graf nawigacji")]
        [Tooltip("Warstwa ścian (tylko pionowe ściany/przeszkody - NIE podłogi, sufity i schody)")]
        public LayerMask wallMask;
        public float maxLinkDistance = 4.5f;
        [Tooltip("Promień 'ciała' agenta przy sprawdzaniu połączeń między waypointami")]
        public float linkRadius = 0.2f;
        [Tooltip("Maks. różnica wysokości waypointów, które łączą się automatycznie (między piętrami służą FloorConnectory)")]
        public float maxLinkHeightDiff = 0.8f;
        public bool drawLinks;

        [Header("Kolizje ze ścianami (siatka)")]
        public bool bakeWallGrid = true;
        [Tooltip("Mniejsza komórka = dokładniej, ale dłuższe wypiekanie i więcej pamięci")]
        public float cellSize = 0.3f;
        [Tooltip("Dodatkowy margines od ścian (m)")]
        public float wallClearance = 0.1f;
        [Tooltip("Połowa wysokości sprawdzanego pudełka, wokół wysokości waypointów")]
        public float bakeHalfHeight = 0.3f;
        public float gridMargin = 3f;
        [Tooltip("O ile wyżej od waypointów szukać ścian. Dzięki temu meble (łóżka, stoły) nie blokują ruchu, a ściany tak")]
        public float checkLift = 0f;
        public bool drawWallGrid;
        public int debugFloor;

        public int NodeCount { get; private set; }
        public int FloorCount { get; private set; } = 1;
        public float BaseY { get; private set; }
        public float2 BoundsMin { get; private set; }
        public float2 BoundsMax { get; private set; }

        public NativeArray<float3> NodePos;
        public NativeArray<int> NextHop;
        public NativeArray<byte> NodeRoomType; //typ sali jako bajt albo 255 dla przejsc
        public NativeArray<byte> NodeBed; //1 jesli waypoint jest lozkiem
        public int[] NodeFloor;
        public Room[] NodeRoom;
        public WallGrid Wall;

        public readonly List<Room> AllRooms = new List<Room>();
        public readonly List<Room> Wards = new List<Room>();
        public readonly List<Room> WaitingRooms = new List<Room>();
        public readonly List<Room> StaffRooms = new List<Room>();
        public readonly List<Room> Entrances = new List<Room>();
        public readonly List<Room> Exits = new List<Room>();
        public readonly List<Room> AssemblyPoints = new List<Room>();

        readonly List<int> edgeA = new List<int>();
        readonly List<int> edgeB = new List<int>();
        readonly Dictionary<int, List<Room>> roomCache = new Dictionary<int, List<Room>>();
        List<int>[] nodesByFloor;
        const float INF = 1e30f;

        //komorki przejsc czyli drzwi schody i windy tam sciany nie blokuja ruchu
        //argumenty to pietro oraz x i z w swiecie
        public System.Func<int, float, float, bool> isPassable;

        void Awake()
        {
            Instance = this;
            Build();
        }

        public string FloorName(int f) => (floorNames != null && f >= 0 && f < floorNames.Length) ? floorNames[f] : $"Piętro {f}";

        //sale danego typu na danym pietrze minus jeden w typie albo pietrze znaczy wszystkie
        public List<Room> RoomsOf(int typeKey, int floor)
        {
            int key = (typeKey + 1) * 1000 + (floor + 1);
            if (roomCache.TryGetValue(key, out var l)) return l;
            l = new List<Room>();
            foreach (var r in AllRooms)
            {
                //sprzatacze i reszta nie chodza na zbiorke ani nie sprzataja drzwi
                if (typeKey < 0 && (r.type == RoomType.AssemblyPoint || r.type.IsPassage())) continue;
                if ((typeKey < 0 || (int)r.type == typeKey) && (floor < 0 || r.floor == floor)) l.Add(r);
            }
            roomCache[key] = l;
            return l;
        }

        public int RandomNodeOnFloor(int floor, ref Random rng)
        {
            if (floor >= 0 && floor < FloorCount && nodesByFloor[floor].Count > 0)
                return nodesByFloor[floor][rng.NextInt(nodesByFloor[floor].Count)];
            return rng.NextInt(NodeCount);
        }

        void Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wps = FindObjectsByType<Waypoint>(FindObjectsSortMode.None);
            var rooms = FindObjectsByType<Room>(FindObjectsSortMode.None);
            var connectors = FindObjectsByType<FloorConnector>(FindObjectsSortMode.None);
            int n = wps.Length;
            if (n == 0) { Debug.LogError("HospitalMap: brak Waypointów w scenie!"); return; }

            foreach (var r in rooms) { r.nodes.Clear(); r.bedNodes.Clear(); r.assignedDoctors = 0; r.occupants = 0; }
            roomCache.Clear();

            NodeCount = n;
            NodePos = new NativeArray<float3>(n, Allocator.Persistent);
            NodeRoomType = new NativeArray<byte>(n, Allocator.Persistent);
            NodeBed = new NativeArray<byte>(n, Allocator.Persistent);
            NodeRoom = new Room[n];
            NodeFloor = new int[n];

            //pozycje poziom parteru i granice budynku
            float minY = float.MaxValue;
            float2 bmin = new float2(float.MaxValue), bmax = new float2(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                wps[i].index = i;
                Vector3 p = wps[i].transform.position;
                NodePos[i] = p;
                minY = Mathf.Min(minY, p.y);
                bmin = math.min(bmin, new float2(p.x, p.z));
                bmax = math.max(bmax, new float2(p.x, p.z));
            }
            BaseY = minY;
            BoundsMin = bmin; BoundsMax = bmax;

            //numer pietra i przypisanie do sal
            int maxFloor = 0, offLevel = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = NodePos[i];
                int f = Mathf.Max(0, Mathf.RoundToInt((p.y - BaseY) / floorHeight));
                NodeFloor[i] = f;
                maxFloor = Mathf.Max(maxFloor, f);
                if (Mathf.Abs(p.y - (BaseY + f * floorHeight)) > 0.7f) offLevel++;

                Room best = null; float bestVol = float.MaxValue;
                if (wps[i].explicitLinks || wps[i].lockRoom)
                {
                    //waypointy z siatki maja sale z gory a drzwi i schody nie naleza do zadnej
                    best = wps[i].fixedRoom;
                }
                else
                {
                    foreach (var r in rooms)
                    {
                        if (!r.ContainsXZ(p)) continue;
                        var s = r.WorldBounds.size;
                        float vol = s.x * s.y * s.z;
                        if (vol < bestVol) { bestVol = vol; best = r; }
                    }
                }
                wps[i].room = best;
                NodeRoom[i] = best;
                NodeRoomType[i] = best != null ? (byte)best.type : (byte)255;
                if (best != null) best.nodes.Add(i);
                if (best != null && wps[i].isBed && best.type == RoomType.Ward) { best.bedNodes.Add(i); NodeBed[i] = 1; }
            }
            FloorCount = maxFloor + 1;
            nodesByFloor = new List<int>[FloorCount];
            for (int f = 0; f < FloorCount; f++) nodesByFloor[f] = new List<int>();
            for (int i = 0; i < n; i++) nodesByFloor[NodeFloor[i]].Add(i);
            foreach (var r in rooms) if (r.nodes.Count > 0) r.floor = NodeFloor[r.nodes[0]];

            if (offLevel > 0)
                Debug.LogWarning($"HospitalMap: {offLevel} waypointów leży poza poziomem piętra (BaseY + n * floorHeight, tolerancja 0.7 m). " +
                                 $"Sprawdź 'floorHeight' (teraz {floorHeight}) i wysokość waypointów - to wpływa na kolizje ze ścianami.");

            //polaczenia
            var dist = new NativeArray<float>(n * n, Allocator.TempJob);
            NextHop = new NativeArray<int>(n * n, Allocator.Persistent);
            for (int k = 0; k < n * n; k++) { dist[k] = INF; NextHop[k] = -1; }
            for (int i = 0; i < n; i++) { dist[i * n + i] = 0f; NextHop[i * n + i] = i; }

            float maxSq = maxLinkDistance * maxLinkDistance;
            edgeA.Clear(); edgeB.Clear();
            Physics.SyncTransforms();
            for (int i = 0; i < n; i++)
            {
                Vector3 a = NodePos[i];
                for (int j = i + 1; j < n; j++)
                {
                    //waypointy z siatki nie lacza sie same bo ich polaczenia wynikaja z drzwi
                    if (wps[i].explicitLinks && wps[j].explicitLinks) continue;
                    Vector3 b = NodePos[j];
                    if (Mathf.Abs(a.y - b.y) > maxLinkHeightDiff) continue;
                    Vector3 d = b - a;
                    float sq = d.sqrMagnitude;
                    if (sq > maxSq) continue;
                    float len = Mathf.Sqrt(sq);
                    if (len > 0.0001f &&
                        Physics.SphereCast(a + Vector3.up * checkLift, linkRadius, d / len, out RaycastHit _, len, wallMask, QueryTriggerInteraction.Ignore))
                        continue;
                    dist[i * n + j] = len; dist[j * n + i] = len;
                    NextHop[i * n + j] = j; NextHop[j * n + i] = i;
                    edgeA.Add(i); edgeB.Add(j);
                }
            }

            //schody i windy
            int connCount = 0;
            foreach (var c in connectors)
            {
                if (c.a == null || c.b == null) { Debug.LogWarning($"FloorConnector '{c.name}' nie ma przypisanych obu waypointów."); continue; }
                int ia = c.a.index, ib = c.b.index;
                float len = Vector3.Distance(c.a.transform.position, c.b.transform.position) * c.costMultiplier + c.extraCost;
                if (len < dist[ia * n + ib])
                {
                    dist[ia * n + ib] = len; dist[ib * n + ia] = len;
                    NextHop[ia * n + ib] = ib; NextHop[ib * n + ia] = ia;
                }
                edgeA.Add(ia); edgeB.Add(ib);
                connCount++;
            }

            //polaczenia ktore generator z siatki podal wprost
            int explicitCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (!wps[i].explicitLinks) continue;
                foreach (var other in wps[i].links)
                {
                    if (other == null || other == wps[i]) continue;
                    int ia = i, ib = other.index;
                    float len = Vector3.Distance(NodePos[ia], NodePos[ib]);
                    if (len < dist[ia * n + ib])
                    {
                        dist[ia * n + ib] = len; dist[ib * n + ia] = len;
                        NextHop[ia * n + ib] = ib; NextHop[ib * n + ia] = ia;
                    }
                    edgeA.Add(ia); edgeB.Add(ib);
                    explicitCount++;
                }
            }

            new FloydWarshallJob { n = n, dist = dist, next = NextHop }.Schedule().Complete();
            dist.Dispose();

            int unreachable = 0;
            for (int j = 0; j < n; j++) if (NextHop[j] < 0) unreachable++;
            if (unreachable > 0)
                Debug.LogWarning($"HospitalMap: {unreachable} waypointów jest nieosiągalnych z '{wps[0].name}'. Graf jest niespójny - " +
                                 "sprawdź drzwi (waypoint w przejściu), schody (FloorConnector), warstwę ścian i maxLinkDistance. Włącz 'Draw Links'.");

            //listy sal
            AllRooms.Clear(); Wards.Clear(); WaitingRooms.Clear(); StaffRooms.Clear(); Entrances.Clear(); Exits.Clear(); AssemblyPoints.Clear();
            foreach (var r in rooms)
            {
                if (r.nodes.Count == 0) { Debug.LogWarning($"Sala '{r.Label}' nie ma żadnego waypointa w środku - zostanie pominięta."); continue; }
                AllRooms.Add(r);
                switch (r.type)
                {
                    case RoomType.Ward: Wards.Add(r); break;
                    case RoomType.WaitingRoom: WaitingRooms.Add(r); break;
                    case RoomType.StaffRoom: StaffRooms.Add(r); break;
                    case RoomType.Entrance: Entrances.Add(r); break;
                    case RoomType.Exit: Exits.Add(r); break;
                    case RoomType.AssemblyPoint: AssemblyPoints.Add(r); break;
                }
            }
            Wards.Sort((x, y) => string.CompareOrdinal(x.Label, y.Label));
            //jak oddzial ma recznie postawione lozka to miejsc jest tyle ile lozek
            foreach (var w in Wards)
            {
                int beds = w.bedNodes.Count;
                if (beds == 0) continue;
                if (w.Capacity <= 0 || w.Capacity > beds)
                {
                    Debug.Log($"{w.Label}: {beds} łóżek w modelu, liczba miejsc ustawiona na {beds}.");
                    w.capacity = beds;
                }
            }
            //jak nie ma wyjscia to wychodzimy wejsciem
            if (Exits.Count == 0) Exits.AddRange(Entrances);

            long graphMs = sw.ElapsedMilliseconds;
            BakeWallGrid();
            sw.Stop();

            Debug.Log($"HospitalMap: {n} waypointów, {edgeA.Count} połączeń (w tym {connCount} schodów/wind), {FloorCount} pięter, " +
                      $"{AllRooms.Count} sal, oddziałów: {Wards.Count}. Graf: {graphMs} ms, razem z siatką ścian: {sw.ElapsedMilliseconds} ms.");
        }

        void BakeWallGrid()
        {
            Wall = new WallGrid { active = false, blocked = new NativeArray<byte>(1, Allocator.Persistent) };
            if (!bakeWallGrid) return;

            Physics.SyncTransforms();
            float2 min = BoundsMin - gridMargin, max = BoundsMax + gridMargin;
            float cs = Mathf.Max(0.1f, cellSize);
            int w, h; long total;
            while (true)
            {
                w = Mathf.CeilToInt((max.x - min.x) / cs);
                h = Mathf.CeilToInt((max.y - min.y) / cs);
                total = (long)w * h * FloorCount;
                if (total <= 40000000L) break;
                cs *= 1.25f;
            }
            if (!Mathf.Approximately(cs, cellSize))
                Debug.LogWarning($"HospitalMap: siatka kolizji za duża - zwiększono cellSize do {cs:0.00}.");

            var blocked = new NativeArray<byte>((int)total, Allocator.Persistent);
            Vector3 ext = new Vector3(cs * 0.5f + wallClearance, bakeHalfHeight, cs * 0.5f + wallClearance);
            int blockedCount = 0;
            for (int f = 0; f < FloorCount; f++)
            {
                float y = BaseY + f * floorHeight + checkLift;
                for (int cz = 0; cz < h; cz++)
                for (int cx = 0; cx < w; cx++)
                {
                    var c = new Vector3(min.x + (cx + 0.5f) * cs, y, min.y + (cz + 0.5f) * cs);
                    if (isPassable != null && isPassable(f, c.x, c.z)) continue;
                    if (Physics.CheckBox(c, ext, Quaternion.identity, wallMask, QueryTriggerInteraction.Ignore))
                    {
                        blocked[(f * h + cz) * w + cx] = 1;
                        blockedCount++;
                    }
                }
            }

            Wall.blocked.Dispose();
            Wall = new WallGrid
            {
                active = true,
                blocked = blocked,
                g = new GridParams { origin = min, invCell = 1f / cs, w = w, h = h, floors = FloorCount, baseY = BaseY, floorH = floorHeight }
            };

            int bad = 0;
            for (int i = 0; i < NodeCount; i++) if (Wall.Blocked(NodePos[i])) bad++;
            if (bad > 0)
                Debug.LogWarning($"HospitalMap: {bad} waypointów leży w zablokowanej komórce (za blisko ściany). Przesuń je lub zmniejsz wallClearance/cellSize.");
            Debug.Log($"HospitalMap: siatka ścian {w}x{h}x{FloorCount}, zablokowanych komórek: {blockedCount}.");
        }

        public int RequiredDoctors()
        {
            int sum = 0;
            foreach (var w in Wards) sum += w.minDoctors;
            return sum;
        }

        //najpierw kazdy oddzial dostaje swoje minimum lekarzy a reszte rozdajemy po kolei
        public Room[] AssignDoctorsToWards(int count)
        {
            var result = new Room[count];
            if (Wards.Count == 0) return result;
            int idx = 0;
            foreach (var w in Wards)
                for (int k = 0; k < w.minDoctors && idx < count; k++) { result[idx++] = w; w.assignedDoctors++; }
            int wi = 0;
            while (idx < count)
            {
                var w = Wards[wi++ % Wards.Count];
                result[idx++] = w; w.assignedDoctors++;
            }
            return result;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NodePos.IsCreated) NodePos.Dispose();
            if (NextHop.IsCreated) NextHop.Dispose();
            if (NodeRoomType.IsCreated) NodeRoomType.Dispose();
            if (NodeBed.IsCreated) NodeBed.Dispose();
            if (Wall.blocked.IsCreated) Wall.blocked.Dispose();
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || !NodePos.IsCreated) return;
            if (drawLinks)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
                for (int e = 0; e < edgeA.Count; e++)
                    Gizmos.DrawLine(NodePos[edgeA[e]], NodePos[edgeB[e]]);
            }
            if (drawWallGrid && Wall.active)
            {
                int f = Mathf.Clamp(debugFloor, 0, FloorCount - 1);
                float y = BaseY + f * floorHeight;
                float cs = 1f / Wall.g.invCell;
                Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
                for (int cz = 0; cz < Wall.g.h; cz++)
                for (int cx = 0; cx < Wall.g.w; cx++)
                    if (Wall.blocked[(f * Wall.g.h + cz) * Wall.g.w + cx] != 0)
                        Gizmos.DrawCube(new Vector3(Wall.g.origin.x + (cx + 0.5f) * cs, y, Wall.g.origin.y + (cz + 0.5f) * cs), new Vector3(cs, 0.1f, cs));
            }
        }
    }
}
