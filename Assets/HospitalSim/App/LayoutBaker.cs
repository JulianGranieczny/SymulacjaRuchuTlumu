using System;
using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    //zamienia namalowana siatke na sale waypointy i polaczenia
    //compute liczy same dane dlatego korzysta z niego tez podglad w edytorze
    //instantiate robi z tego obiekty room waypoint i floorconnector tuz przed symulacja
    public static class LayoutBaker
    {
        public class BNode
        {
            public int f, x, z, def = -1;
            public Vector3 pos;
            public bool passage, manual, bed;
            public int room = -1;
            public readonly HashSet<int> links = new HashSet<int>();
        }

        public class BRoom
        {
            public int def, f;
            public string label;
            public Vector3 center, size;
        }

        public class BConnector
        {
            public int a, b;
            public ConnectorType type;
        }

        public class Result
        {
            public readonly List<BNode> nodes = new List<BNode>();
            public readonly List<BRoom> rooms = new List<BRoom>();
            public readonly List<BConnector> connectors = new List<BConnector>();
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> errors = new List<string>();
            public int linkCount;
            public bool Ok => errors.Count == 0;
        }

        static readonly int[] Dx = { 1, -1, 0, 0 };
        static readonly int[] Dz = { 0, 0, 1, -1 };

        //bieg schodow z kierunkiem agenci ida po nim po skosie z pietra na pietro
        public static bool IsFlight(RoomDef d) => d != null && d.type == RoomType.Stairs && d.stairsUp != StairsDir.SameCell;

        static void DirOf(StairsDir s, out int dx, out int dz)
        {
            dx = 0; dz = 0;
            switch (s)
            {
                case StairsDir.North: dz = 1; break;
                case StairsDir.South: dz = -1; break;
                case StairsDir.East: dx = 1; break;
                case StairsDir.West: dx = -1; break;
            }
        }

        public static Result Compute(LayoutData L, List<ManualWaypoint> manual)
        {
            var res = new Result();
            L.EnsureCells();
            int W = L.width, D = L.depth, F = L.floors, R = L.rooms.Count;
            if (R == 0) { res.errors.Add("Nie ma żadnej sali. Dodaj sale w zakładce 'Sale' i namaluj je na siatce."); return res; }
            float cs = L.cellSize;
            int total = W * D * F;

            //granice sal na kazdym pietrze
            var minX = new int[F * R]; var maxX = new int[F * R]; var minZ = new int[F * R]; var maxZ = new int[F * R];
            for (int i = 0; i < F * R; i++) { minX[i] = minZ[i] = int.MaxValue; maxX[i] = maxZ[i] = -1; }
            for (int f = 0; f < F; f++)
                for (int z = 0; z < D; z++)
                    for (int x = 0; x < W; x++)
                    {
                        int v = L.Get(f, x, z);
                        if (v == 0 || v > R) continue;
                        int k = f * R + v - 1;
                        if (x < minX[k]) minX[k] = x;
                        if (x > maxX[k]) maxX[k] = x;
                        if (z < minZ[k]) minZ[k] = z;
                        if (z > maxZ[k]) maxZ[k] = z;
                    }
            var floorsUsed = new int[R];
            for (int k = 0; k < F * R; k++) if (maxX[k] >= 0) floorsUsed[k % R]++;

            var roomIndex = new int[F * R];
            for (int k = 0; k < F * R; k++)
            {
                roomIndex[k] = -1;
                int d = k % R, f = k / R;
                if (maxX[k] < 0 || L.rooms[d].type.IsPassage()) continue;
                float sx = (maxX[k] - minX[k] + 1) * cs, sz = (maxZ[k] - minZ[k] + 1) * cs;
                var def = L.rooms[d];
                res.rooms.Add(new BRoom
                {
                    def = d, f = f,
                    label = floorsUsed[d] > 1 ? def.name + " [" + L.FloorLabel(f) + "]" : def.name,
                    center = new Vector3(L.originX + minX[k] * cs + sx * 0.5f, L.FloorY(f) + L.roomBoxHeight * 0.5f, L.originZ + minZ[k] * cs + sz * 0.5f),
                    size = new Vector3(sx, L.roomBoxHeight, sz)
                });
                roomIndex[k] = res.rooms.Count - 1;
            }

            var owner = new int[total];
            for (int i = 0; i < total; i++) owner[i] = -1;
            var visited = new bool[total];

            for (int f = 0; f < F; f++)
            {
                //kazda komorka przejscia to osobny waypoint
                var passages = new List<int>();
                for (int z = 0; z < D; z++)
                    for (int x = 0; x < W; x++)
                    {
                        int v = L.Get(f, x, z);
                        if (v == 0 || v > R || !L.rooms[v - 1].type.IsPassage()) continue;
                        if (IsFlight(L.rooms[v - 1])) continue;
                        passages.Add(AddNode(res, L, owner, f, x, z, v - 1, -1, true));
                    }

                //spojne kawalki sali dostaja waypointy w rastrze a reszta komorek trafia do najblizszego
                for (int z = 0; z < D; z++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = L.Index(f, x, z);
                        if (visited[idx]) continue;
                        int v = L.Get(f, x, z);
                        if (v == 0 || v > R) continue;
                        int d = v - 1;
                        var def = L.rooms[d];
                        if (def.type.IsPassage()) continue;

                        var region = new List<int>();
                        var q = new Queue<int>();
                        q.Enqueue(idx); visited[idx] = true;
                        while (q.Count > 0)
                        {
                            int c = q.Dequeue();
                            region.Add(c);
                            int cx = c % W, cz = (c / W) % D;
                            for (int n = 0; n < 4; n++)
                            {
                                int nx = cx + Dx[n], nz = cz + Dz[n];
                                if (!L.InRange(f, nx, nz)) continue;
                                int ni = L.Index(f, nx, nz);
                                if (!visited[ni] && L.Get(f, nx, nz) == v) { visited[ni] = true; q.Enqueue(ni); }
                            }
                        }

                        var seeds = PickSeeds(L, def, region, W, D);
                        int room = roomIndex[f * R + d];
                        var bfs = new Queue<int>();
                        foreach (int c in seeds)
                        {
                            AddNode(res, L, owner, f, c % W, (c / W) % D, d, room, false);
                            bfs.Enqueue(c);
                        }
                        while (bfs.Count > 0)
                        {
                            int c = bfs.Dequeue();
                            int cx = c % W, cz = (c / W) % D;
                            for (int n = 0; n < 4; n++)
                            {
                                int nx = cx + Dx[n], nz = cz + Dz[n];
                                if (!L.InRange(f, nx, nz) || L.Get(f, nx, nz) != v) continue;
                                int ni = L.Index(f, nx, nz);
                                if (owner[ni] >= 0) continue;
                                owner[ni] = owner[c];
                                bfs.Enqueue(ni);
                            }
                        }
                        //sasiednie kawalki tej samej sali laczymy ze soba
                        foreach (int c in region)
                        {
                            int cx = c % W, cz = (c / W) % D;
                            if (cx + 1 < W && L.Get(f, cx + 1, cz) == v) Link(res, owner[c], owner[L.Index(f, cx + 1, cz)]);
                            if (cz + 1 < D && L.Get(f, cx, cz + 1) == v) Link(res, owner[c], owner[L.Index(f, cx, cz + 1)]);
                        }
                    }

                //przejscie laczy sie ze wszystkim co jest obok
                //jak waypoint sali jest dalej niz sasiednia komorka to stawiamy punkt dojscia tuz za drzwiami
                //bez tego agent wchodzil w drzwi po skosie i zahaczal o framuge
                var approach = new Dictionary<int, int>();
                foreach (int pn in passages)
                {
                    var nd = res.nodes[pn];
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = nd.x + Dx[n], nz = nd.z + Dz[n];
                        if (!L.InRange(f, nx, nz)) continue;
                        int nv = L.Get(f, nx, nz);
                        if (nv == 0 || nv > R) continue;
                        int ni = L.Index(f, nx, nz);
                        int o = owner[ni];
                        if (o < 0) continue;
                        var on = res.nodes[o];
                        if (on.passage || (on.x == nx && on.z == nz)) { Link(res, pn, o); continue; }
                        int ap;
                        if (!approach.TryGetValue(ni, out ap))
                        {
                            ap = res.nodes.Count;
                            res.nodes.Add(new BNode { f = f, x = nx, z = nz, def = nv - 1, room = on.room, pos = L.CellCenter(f, nx, nz) + Vector3.up * L.waypointHeight });
                            approach[ni] = ap;
                            Link(res, ap, o);
                        }
                        Link(res, pn, ap);
                    }
                }
            }

            BakeFlights(L, res, owner);

            //schody i windy w tym samym miejscu na sasiednich pietrach czyli stary sposob
            for (int f = 0; f + 1 < F; f++)
                for (int z = 0; z < D; z++)
                    for (int x = 0; x < W; x++)
                    {
                        var a = L.DefAt(f, x, z); var b = L.DefAt(f + 1, x, z);
                        if (a == null || b == null || a.type != b.type) continue;
                        if (IsFlight(a) || IsFlight(b)) continue;
                        if (a.type != RoomType.Stairs && a.type != RoomType.Elevator) continue;
                        int n1 = owner[L.Index(f, x, z)], n2 = owner[L.Index(f + 1, x, z)];
                        if (n1 < 0 || n2 < 0) continue;
                        res.connectors.Add(new BConnector { a = n1, b = n2, type = a.type == RoomType.Stairs ? ConnectorType.Stairs : ConnectorType.Elevator });
                    }

            int generated = res.nodes.Count;

            //reczne waypointy lacza sie same w symulacji po sprawdzeniu scian
            if (manual != null)
                foreach (var m in manual)
                {
                    int x, z;
                    if (m.floor < 0 || m.floor >= F) continue;
                    int room = -1, def = -1;
                    bool bed = false;
                    if (L.WorldToCell(m.x, m.z, out x, out z))
                    {
                        int v = L.Get(m.floor, x, z);
                        if (v > 0 && v <= R && !L.rooms[v - 1].type.IsPassage())
                        {
                            def = v - 1;
                            room = roomIndex[m.floor * R + def];
                            //reczny waypoint na oddziale traktujemy jak lozko
                            bed = L.rooms[def].type == RoomType.Ward;
                        }
                    }
                    res.nodes.Add(new BNode
                    {
                        f = m.floor, x = x, z = z, def = def, room = room, manual = true, bed = bed,
                        pos = new Vector3(m.x, L.FloorY(m.floor) + L.waypointHeight, m.z)
                    });
                }

            Validate(L, res, generated);
            return res;
        }

        //kazdy bieg schodow dostaje dwa waypointy dolny na swoim pietrze i gorny pietro wyzej
        //dolny laczymy z tym co jest za dolnym koncem biegu a gorny z tym co jest za gornym koncem na wyzszym pietrze
        static void BakeFlights(LayoutData L, Result res, int[] owner)
        {
            int W = L.width, D = L.depth, F = L.floors, R = L.rooms.Count;
            var seen = new bool[W * D * F];
            var approach = new Dictionary<int, int>();
            for (int f = 0; f < F; f++)
                for (int z0 = 0; z0 < D; z0++)
                    for (int x0 = 0; x0 < W; x0++)
                    {
                        int idx = L.Index(f, x0, z0);
                        if (seen[idx]) continue;
                        int v = L.Get(f, x0, z0);
                        if (v == 0 || v > R || !IsFlight(L.rooms[v - 1])) continue;
                        var def = L.rooms[v - 1];

                        var region = new List<int>();
                        var q = new Queue<int>();
                        q.Enqueue(idx); seen[idx] = true;
                        while (q.Count > 0)
                        {
                            int c = q.Dequeue();
                            region.Add(c);
                            int cx = c % W, cz = (c / W) % D;
                            for (int n = 0; n < 4; n++)
                            {
                                int nx = cx + Dx[n], nz = cz + Dz[n];
                                if (!L.InRange(f, nx, nz)) continue;
                                int ni = L.Index(f, nx, nz);
                                if (!seen[ni] && L.Get(f, nx, nz) == v) { seen[ni] = true; q.Enqueue(ni); }
                            }
                        }

                        if (f + 1 >= F)
                        {
                            res.errors.Add("Schody '" + def.name + "' na piętrze " + L.FloorLabel(f) + " prowadzą w górę, a wyżej nie ma piętra. Bieg schodów maluj na niższym z dwóch pięter.");
                            continue;
                        }

                        int dx, dz;
                        DirOf(def.stairsUp, out dx, out dz);
                        //pozycja komorki liczona wzdluz kierunku biegu
                        int lo = int.MaxValue, hi = int.MinValue;
                        foreach (int c in region)
                        {
                            int a = dx != 0 ? (c % W) * dx : ((c / W) % D) * dz;
                            lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
                        }
                        if (hi == lo) res.warnings.Add("Schody '" + def.name + "' mają tylko jedną komórkę długości - pomaluj cały bieg w kierunku wejścia w górę.");

                        var low = new List<int>(); var high = new List<int>();
                        foreach (int c in region)
                        {
                            int a = dx != 0 ? (c % W) * dx : ((c / W) % D) * dz;
                            if (a == lo) low.Add(c);
                            if (a == hi) high.Add(c);
                        }

                        int bottom = FlightEnd(res, L, low, f, f, v - 1);
                        int top = FlightEnd(res, L, high, f, f + 1, v - 1);
                        foreach (int c in low) LinkToCell(res, L, owner, approach, bottom, f, c % W - dx, (c / W) % D - dz);
                        foreach (int c in high) LinkToCell(res, L, owner, approach, top, f + 1, c % W + dx, (c / W) % D + dz);
                        if (res.nodes[bottom].links.Count == 0)
                            res.errors.Add("Schody '" + def.name + "' [" + L.FloorLabel(f) + "]: za dolnym końcem biegu nic nie ma. Namaluj tam korytarz lub klatkę schodową.");
                        if (res.nodes[top].links.Count == 0)
                            res.errors.Add("Schody '" + def.name + "' [" + L.FloorLabel(f) + "]: na piętrze " + L.FloorLabel(f + 1) + " za górnym końcem biegu nic nie ma. Namaluj tam podest (korytarz lub klatkę schodową).");
                        res.connectors.Add(new BConnector { a = bottom, b = top, type = ConnectorType.Stairs });
                    }
        }

        //waypoint na srodku krawedzi biegu na danym pietrze
        static int FlightEnd(Result res, LayoutData L, List<int> edge, int paintFloor, int floor, int def)
        {
            int W = L.width, D = L.depth;
            Vector3 sum = Vector3.zero;
            foreach (int c in edge) sum += L.CellCenter(floor, c % W, (c / W) % D);
            Vector3 p = sum / edge.Count;
            int mid = edge[edge.Count / 2];
            res.nodes.Add(new BNode { f = floor, x = mid % W, z = (mid / W) % D, def = def, passage = true, pos = new Vector3(p.x, L.FloorY(floor) + L.waypointHeight, p.z) });
            return res.nodes.Count - 1;
        }

        //laczy waypoint z sasiednia komorka tak samo jak drzwi czyli z punktem dojscia jak waypoint sali jest dalej
        static void LinkToCell(Result res, LayoutData L, int[] owner, Dictionary<int, int> approach, int pn, int f, int nx, int nz)
        {
            int R = L.rooms.Count;
            if (!L.InRange(f, nx, nz)) return;
            int nv = L.Get(f, nx, nz);
            if (nv == 0 || nv > R) return;
            int ni = L.Index(f, nx, nz);
            int o = owner[ni];
            if (o < 0) return;
            var on = res.nodes[o];
            if (on.passage || (on.x == nx && on.z == nz)) { Link(res, pn, o); return; }
            int ap;
            if (!approach.TryGetValue(ni, out ap))
            {
                ap = res.nodes.Count;
                res.nodes.Add(new BNode { f = f, x = nx, z = nz, def = nv - 1, room = on.room, pos = L.CellCenter(f, nx, nz) + Vector3.up * L.waypointHeight });
                approach[ni] = ap;
                Link(res, ap, o);
            }
            Link(res, pn, ap);
        }

        static int AddNode(Result res, LayoutData L, int[] owner, int f, int x, int z, int def, int room, bool passage)
        {
            var n = new BNode { f = f, x = x, z = z, def = def, room = room, passage = passage };
            n.pos = L.CellCenter(f, x, z) + Vector3.up * L.waypointHeight;
            res.nodes.Add(n);
            int id = res.nodes.Count - 1;
            owner[L.Index(f, x, z)] = id;
            return id;
        }

        static void Link(Result res, int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return;
            if (res.nodes[a].links.Add(b)) res.linkCount++;
            res.nodes[b].links.Add(a);
        }

        //punkty co waypointspacing a na oddziale co najmniej tyle ile jest lozek
        static List<int> PickSeeds(LayoutData L, RoomDef def, List<int> region, int W, int D)
        {
            float spacing = def.type == RoomType.Ward ? L.wardWaypointSpacing : L.waypointSpacing;
            int step = Mathf.Max(1, Mathf.RoundToInt(spacing / L.cellSize));
            var seeds = new List<int>();
            foreach (int c in region)
                if ((c % W) % step == step / 2 && ((c / W) % D) % step == step / 2) seeds.Add(c);

            if (seeds.Count == 0)
            {
                float mx = 0, mz = 0;
                foreach (int c in region) { mx += c % W; mz += (c / W) % D; }
                mx /= region.Count; mz /= region.Count;
                int best = region[0]; float bd = float.MaxValue;
                foreach (int c in region)
                {
                    float dx = (c % W) - mx, dz = ((c / W) % D) - mz;
                    if (dx * dx + dz * dz < bd) { bd = dx * dx + dz * dz; best = c; }
                }
                seeds.Add(best);
            }

            //punkty przy krawedzi sali przesuwamy o komorke do srodka bo zwykle stoi tam sciana
            var inRegion = new HashSet<int>(region);
            var taken = new HashSet<int>(seeds);
            for (int i = 0; i < seeds.Count; i++)
            {
                int c = seeds[i];
                if (Interior(c, inRegion, W)) continue;
                int cx = c % W;
                int[] cand = { c + 1, c - 1, c + W, c - W, c + 1 + W, c - 1 + W, c + 1 - W, c - 1 - W };
                foreach (int n in cand)
                {
                    if (!inRegion.Contains(n) || taken.Contains(n)) continue;
                    if (System.Math.Abs((n % W) - cx) > 1) continue;
                    if (!Interior(n, inRegion, W)) continue;
                    taken.Remove(c); taken.Add(n); seeds[i] = n;
                    break;
                }
            }

            int need = def.type == RoomType.Ward ? Mathf.Max(1, def.capacity >= 0 ? def.capacity : 3) : 1;
            var used = new HashSet<int>(seeds);
            while (seeds.Count < need && seeds.Count < region.Count)
            {
                int best = -1; float bd = -1f;
                foreach (int c in region)
                {
                    if (used.Contains(c)) continue;
                    float md = float.MaxValue;
                    foreach (int s in seeds)
                    {
                        float dx = (c % W) - (s % W), dz = ((c / W) % D) - ((s / W) % D);
                        md = Mathf.Min(md, dx * dx + dz * dz);
                    }
                    if (md > bd) { bd = md; best = c; }
                }
                if (best < 0) break;
                seeds.Add(best); used.Add(best);
            }
            return seeds;
        }

        //wywala polaczenia ktore ida przez sciany modelu blocked dostaje dwa punkty i mowi czy jest miedzy nimi sciana
        //przy przejsciach pomijamy kawalek w samym otworze bo czasem stoi tam skrzydlo drzwi
        public static int PruneBlocked(Result res, LayoutData L, Func<Vector3, Vector3, bool> blocked)
        {
            int removed = 0;
            float cut = L.cellSize * 0.6f;
            for (int i = 0; i < res.nodes.Count; i++)
            {
                var a = res.nodes[i];
                var drop = new List<int>();
                foreach (int j in a.links)
                {
                    if (j < i) continue;
                    var b = res.nodes[j];
                    Vector3 pa = a.pos, pb = b.pos;
                    if (a.passage) pa = Vector3.MoveTowards(pa, pb, cut);
                    if (b.passage) pb = Vector3.MoveTowards(pb, pa, cut);
                    if ((pb - pa).magnitude < 0.05f) continue;
                    if (blocked(pa, pb)) drop.Add(j);
                }
                foreach (int j in drop)
                {
                    a.links.Remove(j);
                    res.nodes[j].links.Remove(i);
                    res.linkCount--;
                    removed++;
                }
            }
            string err = Disconnected(L, res);
            if (err != null) res.errors.Add(err + " Część połączeń przechodziła przez ściany modelu, więc została usunięta - sprawdź, czy drzwi leżą dokładnie w otworach ścian.");
            return removed;
        }

        static bool Interior(int c, HashSet<int> region, int W)
        {
            return region.Contains(c + 1) && region.Contains(c - 1) && region.Contains(c + W) && region.Contains(c - W);
        }

        //odsuwa waypointy od scian blocked mowi czy w danym punkcie jest sciana
        //szukamy wolnego miejsca nie dalej niz w obrebie tej samej komorki
        public static int NudgeFromWalls(Result res, LayoutData L, Func<Vector3, bool> blocked)
        {
            int moved = 0;
            float cs = L.cellSize;
            foreach (var n in res.nodes)
            {
                if (n.passage || !blocked(n.pos)) continue;
                Vector3 best = n.pos; float bd = float.MaxValue; bool found = false;
                for (int iz = -3; iz <= 3; iz++)
                    for (int ix = -3; ix <= 3; ix++)
                    {
                        var p = n.pos + new Vector3(ix * cs * 0.15f, 0f, iz * cs * 0.15f);
                        if (blocked(p)) continue;
                        float d = (p - n.pos).sqrMagnitude;
                        if (d < bd) { bd = d; best = p; found = true; }
                    }
                if (found) { n.pos = best; moved++; }
            }
            return moved;
        }

        static void Validate(LayoutData L, Result res, int generated)
        {
            if (generated == 0) { res.errors.Add("Siatka jest pusta - namaluj sale na planie."); return; }
            string err = Disconnected(L, res);
            if (err != null) res.errors.Add(err);
            ValidateTypes(L, res, generated);
        }

        //sprawdza czy wszystkie wygenerowane waypointy sa w jednym grafie zwraca opis problemu albo null
        static string Disconnected(LayoutData L, Result res)
        {
            int generated = res.nodes.Count;
            var parent = new int[generated];
            for (int i = 0; i < generated; i++) parent[i] = i;
            Func<int, int> find = null;
            find = a => { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; };
            int counted = 0;
            for (int i = 0; i < generated; i++)
            {
                if (res.nodes[i].manual) continue;
                counted++;
                foreach (int j in res.nodes[i].links) if (j < generated) { int ra = find(i), rb = find(j); if (ra != rb) parent[ra] = rb; }
            }
            if (counted == 0) return null;
            foreach (var c in res.connectors) { int ra = find(c.a), rb = find(c.b); if (ra != rb) parent[ra] = rb; }

            var size = new Dictionary<int, int>();
            for (int i = 0; i < generated; i++) { if (res.nodes[i].manual) continue; int r = find(i); size[r] = size.ContainsKey(r) ? size[r] + 1 : 1; }
            if (size.Count > 1)
            {
                int big = -1, bigSize = 0;
                foreach (var kv in size) if (kv.Value > bigSize) { bigSize = kv.Value; big = kv.Key; }
                var names = new HashSet<string>();
                for (int i = 0; i < generated; i++)
                {
                    if (res.nodes[i].manual || find(i) == big) continue;
                    var n = res.nodes[i];
                    names.Add(n.room >= 0 ? res.rooms[n.room].label : L.rooms[n.def].name + " [" + L.FloorLabel(n.f) + "]");
                }
                return "Nie wszystkie sale są połączone. Odcięte: " + string.Join(", ", names) +
                       ". Sale łączą się tylko przez drzwi (Door), a piętra przez bieg schodów z kierunkiem (namalowany na niższym piętrze, z podestem za oboma końcami) albo przez schody lub windę w tym samym miejscu na obu piętrach.";
            }
            return null;
        }

        static void ValidateTypes(LayoutData L, Result res, int generated)
        {
            var has = new HashSet<RoomType>();
            foreach (var r in res.rooms) has.Add(L.rooms[r.def].type);
            if (!has.Contains(RoomType.Ward)) res.errors.Add("Brak oddziału (Ward) - pacjenci nie mają gdzie się leczyć.");
            if (!has.Contains(RoomType.WaitingRoom)) res.errors.Add("Brak poczekalni (WaitingRoom).");
            if (!has.Contains(RoomType.Entrance) && !has.Contains(RoomType.Exit)) res.errors.Add("Brak wejścia (Entrance) lub wyjścia (Exit).");
            if (!has.Contains(RoomType.StaffRoom)) res.warnings.Add("Brak dyżurki (StaffRoom) - lekarze będą chodzić tylko po oddziałach.");
            if (!has.Contains(RoomType.Exit)) res.warnings.Add("Brak wyjścia (Exit) - pacjenci wyjdą przez wejście.");
            if (!has.Contains(RoomType.AssemblyPoint)) res.warnings.Add("Brak miejsca zbiórki (AssemblyPoint) - ewakuacja nie zadziała.");

            bool ground = false;
            for (int i = 0; i < generated; i++) if (res.nodes[i].f == 0) { ground = true; break; }
            if (!ground) res.errors.Add("Na parterze nic nie namalowano. Numer piętra liczy się od najniższego waypointa, więc parter musi mieć sale.");

            if (res.nodes.Count > 1500)
                res.warnings.Add("Dużo waypointów (" + res.nodes.Count + "). Przygotowanie symulacji może trwać długo - zwiększ odstęp waypointów w zakładce 'Waypointy'.");
            if (res.nodes.Count > 3000)
                res.errors.Add("Za dużo waypointów (" + res.nodes.Count + ", limit 3000). Zwiększ odstęp waypointów albo rozmiar komórki siatki.");
        }

        //tworzy obiekty w scenie dla symulacji
        public static void Instantiate(Result res, LayoutData L, Transform parent)
        {
            var roomsRoot = Child(parent, "Rooms");
            var wpRoot = Child(parent, "Waypoints");
            var conRoot = Child(parent, "Connectors");

            var rooms = new Room[res.rooms.Count];
            for (int i = 0; i < res.rooms.Count; i++)
            {
                var br = res.rooms[i];
                var def = L.rooms[br.def];
                var go = Child(roomsRoot, br.label).gameObject;
                go.layer = 2;
                go.transform.position = br.center;
                var room = go.AddComponent<Room>();
                var box = go.GetComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = br.size;
                room.type = def.type;
                room.displayName = br.label;
                room.specialty = def.specialty;
                room.minDoctors = def.minDoctors;
                room.capacity = def.capacity;
                rooms[i] = room;
            }

            var wps = new Waypoint[res.nodes.Count];
            for (int i = 0; i < res.nodes.Count; i++)
            {
                var n = res.nodes[i];
                var go = Child(wpRoot, (n.manual ? "WPm_" : "WP_") + n.f + "_" + n.x + "_" + n.z).gameObject;
                go.transform.position = n.pos;
                var wp = go.AddComponent<Waypoint>();
                wp.explicitLinks = !n.manual;
                wp.lockRoom = true;
                wp.fixedRoom = n.room >= 0 ? rooms[n.room] : null;
                wp.isBed = n.bed;
                wps[i] = wp;
            }
            for (int i = 0; i < res.nodes.Count; i++)
                foreach (int j in res.nodes[i].links)
                    if (j > i) wps[i].links.Add(wps[j]);

            foreach (var c in res.connectors)
            {
                var go = Child(conRoot, "Conn_" + c.a + "_" + c.b).gameObject;
                var fc = go.AddComponent<FloorConnector>();
                fc.a = wps[c.a]; fc.b = wps[c.b];
                fc.type = c.type;
                fc.costMultiplier = c.type == ConnectorType.Stairs ? 1.5f : 1f;
                fc.extraCost = c.type == ConnectorType.Stairs ? 0f : 8f;
            }
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
