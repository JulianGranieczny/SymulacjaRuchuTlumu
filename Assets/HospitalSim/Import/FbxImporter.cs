using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HospitalSim.Import
{
    /// <summary>
    /// Importer binarnego formatu FBX w wersji 7.x (Blender, 3ds Max, Maya, Revit, SketchUp).
    /// </summary>
    // Odczytywane są obiekty Geometry, Model i Material oraz sekcja Connections, która je ze sobą wiąże.
    // Hierarchia modeli jest odtwarzana, a każda geometria trafia do świata przez macierz swojego modelu.
    // Wariant tekstowy (ASCII) nie jest obsługiwany; użytkownik otrzymuje komunikat z prośbą o eksport binarny.
    public static class FbxImporter
    {
        class Node
        {
            public string name;
            public readonly List<object> props = new List<object>();
            public readonly List<Node> children = new List<Node>();

            public Node Child(string n)
            {
                foreach (var c in children) if (c.name == n) return c;
                return null;
            }
        }

        class Model
        {
            public long id;
            public string name;
            public Node node;
            public long parent;
            public Mat4d local, geo;
            public Mat4d? world;
            public readonly List<long> materials = new List<long>();
        }

        /// <summary>
        /// Wczytuje binarny plik FBX. Nagłówek musi zaczynać się od "Kaydara FBX Binary",
        /// a numer wersji (offset 23) musi wynosić co najmniej 7000.
        /// </summary>
        /// <exception cref="ImportException">Dla pliku tekstowego, zbyt starej wersji lub pliku bez sekcji Objects.</exception>
        public static RawModel Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 27 || Encoding.ASCII.GetString(data, 0, 18) != "Kaydara FBX Binary")
            {
                string head = Encoding.ASCII.GetString(data, 0, Math.Min(200, data.Length));
                if (head.Contains("FBX")) throw new ImportException("To jest FBX tekstowy (ASCII). Zapisz plik jako FBX binarny - w Blenderze jest to domyślne.");
                throw new ImportException("To nie jest poprawny plik FBX.");
            }
            int version = BitConverter.ToInt32(data, 23);
            if (version < 7000) throw new ImportException("Za stara wersja FBX (" + version + "). Wyeksportuj jako FBX 2011 lub nowszy.");

            var top = new List<Node>();
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                br.BaseStream.Position = 27;
                // Węzły najwyższego poziomu zaczynają się od offsetu 27; ostatnie 13 bajtów należy do stopki pliku.
                while (br.BaseStream.Position < data.Length - 13)
                {
                    var n = ReadNode(br, version);
                    if (n == null) break;
                    top.Add(n);
                }
            }

            Node objects = null, connections = null, global = null;
            foreach (var n in top)
            {
                if (n.name == "Objects") objects = n;
                else if (n.name == "Connections") connections = n;
                else if (n.name == "GlobalSettings") global = n;
            }
            if (objects == null) throw new ImportException("FBX: brak sekcji Objects.");

            // Macierz konwersji układu na podstawie GlobalSettings: oś CoordAxis trafia do X (z negacją, co zmienia
            // układ na lewoskrętny), UpAxis do Y, a FrontAxis do Z, z uwzględnieniem znaków osi.
            // UnitScaleFactor podaje liczbę centymetrów w jednostce pliku, dlatego dzielimy przez 100, aby uzyskać metry.
            var gp = Props(global);
            int upAxis = (int)Num(gp, "UpAxis", 1), upSign = (int)Num(gp, "UpAxisSign", 1);
            int frontAxis = (int)Num(gp, "FrontAxis", 2), frontSign = (int)Num(gp, "FrontAxisSign", 1);
            int coordAxis = (int)Num(gp, "CoordAxis", 0), coordSign = (int)Num(gp, "CoordAxisSign", 1);
            double unit = Num(gp, "UnitScaleFactor", 1.0) / 100.0;
            var conv = new Mat4d { m = new double[16] };
            conv.m[15] = 1;
            conv.m[0 * 4 + Clamp3(coordAxis)] = -coordSign * unit;
            conv.m[1 * 4 + Clamp3(upAxis)] = upSign * unit;
            conv.m[2 * 4 + Clamp3(frontAxis)] = frontSign * unit;

            var geoms = new Dictionary<long, Node>();
            var models = new Dictionary<long, Model>();
            var mats = new Dictionary<long, float[]>();
            var matNames = new Dictionary<long, string>();
            foreach (var o in objects.children)
            {
                if (o.props.Count < 1 || !(o.props[0] is long)) continue;
                long id = (long)o.props[0];
                string nm = o.props.Count > 1 ? CleanName(o.props[1] as string) : "";
                if (o.name == "Geometry" && o.props.Count > 2 && (o.props[2] as string) == "Mesh") geoms[id] = o;
                else if (o.name == "Model") models[id] = new Model { id = id, name = nm, node = o };
                else if (o.name == "Material")
                {
                    var mp = Props(o);
                    float[] c = Color(mp, "DiffuseColor") ?? Color(mp, "Diffuse") ?? new[] { 0.8f, 0.8f, 0.8f };
                    mats[id] = c;
                    matNames[id] = nm;
                }
            }

            // Każde połączenie ma postać (typ, dziecko, rodzic). Rozpoznawane są trzy przypadki:
            // geometria → model, model → model nadrzędny oraz materiał → model. Kolejność materiałów odpowiada slotom.
            var geoToModels = new List<long[]>();
            if (connections != null)
                foreach (var c in connections.children)
                {
                    if (c.props.Count < 3 || !(c.props[1] is long) || !(c.props[2] is long)) continue;
                    long child = (long)c.props[1], parent = (long)c.props[2];
                    Model m;
                    if (geoms.ContainsKey(child) && models.ContainsKey(parent)) geoToModels.Add(new[] { child, parent });
                    else if (models.TryGetValue(child, out m) && models.ContainsKey(parent)) m.parent = parent;
                    else if (mats.ContainsKey(child) && models.TryGetValue(parent, out m)) m.materials.Add(child);
                }

            foreach (var m in models.Values) ComputeLocal(m);

            var builder = new MeshBuilder();
            var warnings = new List<string>();
            int polys = 0;
            foreach (var gm in geoToModels)
            {
                var geo = geoms[gm[0]];
                var model = models[gm[1]];
                var full = conv * World(model, models, 0) * model.geo;
                bool flip = full.Det3() < 0;
                polys += AddGeometry(builder, geo, model, full, flip, mats, matNames);
            }
            if (geoToModels.Count == 0 && geoms.Count > 0)
            {
                // Plik bez połączeń geometrii z modelami: geometria jest umieszczana w miejscu, z samą konwersją układu.
                foreach (var g in geoms.Values)
                {
                    var dummy = new Model { name = "mesh", local = Mat4d.Identity(), geo = Mat4d.Identity() };
                    polys += AddGeometry(builder, g, dummy, conv, conv.Det3() < 0, mats, matNames);
                }
            }

            var result = builder.Build(Path.GetFileName(path));
            result.warnings.AddRange(warnings);
            return result;
        }

        static int Clamp3(int a) { return a < 0 ? 0 : a > 2 ? 2 : a; }

        /// <summary>
        /// Obcina nazwę obiektu FBX na pierwszym znaku \0; dalej zapisana jest klasa obiektu.
        /// </summary>
        static string CleanName(string s)
        {
            if (s == null) return "";
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        /// <summary>
        /// Zwraca macierz świata modelu jako iloczyn macierzy lokalnych wzdłuż łańcucha rodziców.
        /// Wynik jest zapamiętywany; głębokość łańcucha jest ograniczona do 100, co chroni przed cyklami.
        /// </summary>
        static Mat4d World(Model m, Dictionary<long, Model> all, int depth)
        {
            if (m.world.HasValue) return m.world.Value;
            Mat4d w = m.local;
            Model p;
            if (depth < 100 && m.parent != 0 && all.TryGetValue(m.parent, out p)) w = World(p, all, depth + 1) * m.local;
            m.world = w;
            return w;
        }

        static Mat4d Euler(double x, double y, double z, int order)
        {
            var rx = Mat4d.RotX(x); var ry = Mat4d.RotY(y); var rz = Mat4d.RotZ(z);
            // Wartości RotationOrder: 0 = XYZ, 1 = XZY, 2 = YZX, 3 = YXZ, 4 = ZXY, 5 = ZYX.
            // Dla XYZ obrót X jest stosowany jako pierwszy, więc macierz ma postać Rz * Ry * Rx.
            switch (order)
            {
                case 1: return ry * rz * rx;
                case 2: return rx * rz * ry;
                case 3: return rz * rx * ry;
                case 4: return ry * rx * rz;
                case 5: return rx * ry * rz;
                default: return rz * ry * rx;
            }
        }

        /// <summary>
        /// Transponuje część 3×3 macierzy. Dla macierzy obrotu daje to macierz odwrotną.
        /// </summary>
        static Mat4d Transpose3(Mat4d a)
        {
            var r = Mat4d.Identity();
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) r.m[i * 4 + j] = a.m[j * 4 + i];
            return r;
        }

        /// <summary>
        /// Oblicza macierz lokalną zgodnie ze wzorem FBX SDK:
        /// T * Roff * Rp * Rpre * R * Rpost⁻¹ * Rp⁻¹ * Soff * Sp * S * Sp⁻¹.
        /// Oddzielnie wyznaczana jest transformacja geometryczna Tg * Rg * Sg, która dotyczy tylko geometrii modelu
        /// i nie jest dziedziczona przez modele potomne.
        /// </summary>
        static void ComputeLocal(Model m)
        {
            var p = Props(m.node);
            var t = Vec(p, "Lcl Translation", 0);
            var r = Vec(p, "Lcl Rotation", 0);
            var s = Vec(p, "Lcl Scaling", 1);
            var pre = Vec(p, "PreRotation", 0);
            var post = Vec(p, "PostRotation", 0);
            var roff = Vec(p, "RotationOffset", 0);
            var rp = Vec(p, "RotationPivot", 0);
            var soff = Vec(p, "ScalingOffset", 0);
            var sp = Vec(p, "ScalingPivot", 0);
            int order = (int)Num(p, "RotationOrder", 0);

            var T = Mat4d.Translation(t[0], t[1], t[2]);
            var R = Euler(r[0], r[1], r[2], order);
            var Rpre = Euler(pre[0], pre[1], pre[2], 0);
            var RpostInv = Transpose3(Euler(post[0], post[1], post[2], 0));
            var S = Mat4d.Scale(s[0], s[1], s[2]);
            m.local = T * Mat4d.Translation(roff[0], roff[1], roff[2]) * Mat4d.Translation(rp[0], rp[1], rp[2]) * Rpre * R * RpostInv *
                      Mat4d.Translation(-rp[0], -rp[1], -rp[2]) * Mat4d.Translation(soff[0], soff[1], soff[2]) *
                      Mat4d.Translation(sp[0], sp[1], sp[2]) * S * Mat4d.Translation(-sp[0], -sp[1], -sp[2]);

            var gt = Vec(p, "GeometricTranslation", 0);
            var gr = Vec(p, "GeometricRotation", 0);
            var gs = Vec(p, "GeometricScaling", 1);
            m.geo = Mat4d.Translation(gt[0], gt[1], gt[2]) * Euler(gr[0], gr[1], gr[2], 0) * Mat4d.Scale(gs[0], gs[1], gs[2]);
        }

        /// <summary>
        /// Dodaje do budowniczego siatki wielokąty z węzła Geometry i zwraca ich liczbę.
        /// W PolygonVertexIndex ostatni wierzchołek wielokąta jest zapisany jako dopełnienie bitowe (~indeks).
        /// Wielokąty są dzielone na trójkąty metodą wachlarza; wielokąt z indeksem spoza zakresu jest pomijany.
        /// </summary>
        static int AddGeometry(MeshBuilder builder, Node geo, Model model, Mat4d full, bool flip,
                               Dictionary<long, float[]> mats, Dictionary<long, string> matNames)
        {
            var vn = geo.Child("Vertices");
            var pn = geo.Child("PolygonVertexIndex");
            if (vn == null || pn == null || vn.props.Count == 0 || pn.props.Count == 0) return 0;
            double[] v = vn.props[0] as double[];
            int[] pi = pn.props[0] as int[];
            if (v == null || pi == null) return 0;

            // Przypisanie materiałów z LayerElementMaterial: przy mapowaniu "AllSame" cała geometria używa pierwszego slotu,
            // w pozostałych przypadkach tablica Materials podaje slot dla każdego wielokąta.
            int[] matIdx = null;
            bool allSame = true;
            var lem = geo.Child("LayerElementMaterial");
            if (lem != null)
            {
                var mn = lem.Child("Materials");
                var map = lem.Child("MappingInformationType");
                if (mn != null && mn.props.Count > 0) matIdx = mn.props[0] as int[];
                if (map != null && map.props.Count > 0) allSame = (map.props[0] as string) == "AllSame";
            }

            int vc = v.Length / 3;
            var groupsUsed = new Dictionary<int, MeshBuilder.Group>();
            var remap = new Dictionary<MeshBuilder.Group, int[]>();
            var poly = new List<int>();
            int polyIndex = 0, count = 0;

            for (int i = 0; i < pi.Length; i++)
            {
                int raw = pi[i];
                bool end = raw < 0;
                int vi = end ? ~raw : raw;
                poly.Add(vi);
                if (!end) continue;

                int mslot = 0;
                if (matIdx != null && matIdx.Length > 0) mslot = allSame ? matIdx[0] : (polyIndex < matIdx.Length ? matIdx[polyIndex] : 0);
                MeshBuilder.Group g;
                if (!groupsUsed.TryGetValue(mslot, out g))
                {
                    float[] col = { 0.8f, 0.8f, 0.8f };
                    string name = model.name;
                    if (mslot >= 0 && mslot < model.materials.Count)
                    {
                        long mid = model.materials[mslot];
                        col = mats[mid];
                        name = matNames[mid];
                    }
                    g = builder.Get(name, col[0], col[1], col[2]);
                    groupsUsed[mslot] = g;
                }

                // Wierzchołki są przekształcane i dopisywane do grupy dopiero przy pierwszym użyciu,
                // dzięki czemu każda grupa materiałowa zawiera tylko własne wierzchołki.
                int[] rm;
                if (!remap.TryGetValue(g, out rm))
                {
                    rm = new int[vc];
                    for (int k = 0; k < vc; k++) rm[k] = -1;
                    remap[g] = rm;
                }
                bool ok = true;
                for (int k = 0; k < poly.Count; k++)
                {
                    int a = poly[k];
                    if (a < 0 || a >= vc) { ok = false; break; }
                    if (rm[a] < 0)
                    {
                        double x, y, z;
                        full.Apply(v[a * 3], v[a * 3 + 1], v[a * 3 + 2], out x, out y, out z);
                        rm[a] = g.AddVertex(x, y, z);
                    }
                }
                if (ok)
                    for (int k = 1; k + 1 < poly.Count; k++) g.AddTri(rm[poly[0]], rm[poly[k]], rm[poly[k + 1]], flip);
                poly.Clear();
                polyIndex++;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Zwraca właściwości z węzła Properties70 jako słownik: nazwa → wartości.
        /// Każdy wpis P ma postać (nazwa, typ, podtyp, flagi, wartości...), więc pomijane są pierwsze cztery pola.
        /// </summary>
        static Dictionary<string, List<object>> Props(Node n)
        {
            var d = new Dictionary<string, List<object>>();
            if (n == null) return d;
            var p70 = n.Child("Properties70");
            if (p70 == null) return d;
            foreach (var p in p70.children)
            {
                if (p.name != "P" || p.props.Count < 4) continue;
                string key = p.props[0] as string;
                if (key == null) continue;
                d[key] = p.props.GetRange(4, p.props.Count - 4);
            }
            return d;
        }

        static double ToD(object o)
        {
            if (o is double) return (double)o;
            if (o is float) return (float)o;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            if (o is short) return (short)o;
            return 0;
        }

        static double Num(Dictionary<string, List<object>> p, string key, double def)
        {
            List<object> v;
            if (p.TryGetValue(key, out v) && v.Count > 0) return ToD(v[0]);
            return def;
        }

        static double[] Vec(Dictionary<string, List<object>> p, string key, double def)
        {
            List<object> v;
            if (p.TryGetValue(key, out v) && v.Count >= 3) return new[] { ToD(v[0]), ToD(v[1]), ToD(v[2]) };
            return new[] { def, def, def };
        }

        static float[] Color(Dictionary<string, List<object>> p, string key)
        {
            List<object> v;
            if (p.TryGetValue(key, out v) && v.Count >= 3) return new[] { (float)ToD(v[0]), (float)ToD(v[1]), (float)ToD(v[2]) };
            return null;
        }

        /// <summary>
        /// Odczytuje jeden rekord węzła. Nagłówek zawiera: offset końca rekordu, liczbę właściwości,
        /// długość listy właściwości oraz długość nazwy (1 bajt). Od wersji 7500 trzy pierwsze pola mają 64 bity, wcześniej 32.
        /// Rekord z offsetem końca równym 0 oznacza koniec listy węzłów i powoduje zwrócenie <c>null</c>.
        /// </summary>
        static Node ReadNode(BinaryReader br, int version)
        {
            bool wide = version >= 7500;
            long end = wide ? (long)br.ReadUInt64() : br.ReadUInt32();
            long numProps = wide ? (long)br.ReadUInt64() : br.ReadUInt32();
            if (wide) br.ReadUInt64(); else br.ReadUInt32();
            int nameLen = br.ReadByte();
            if (end == 0) return null;
            var n = new Node { name = Encoding.ASCII.GetString(br.ReadBytes(nameLen)) };
            for (long i = 0; i < numProps; i++) n.props.Add(ReadProp(br));
            while (br.BaseStream.Position < end)
            {
                var c = ReadNode(br, version);
                if (c == null) break;
                n.children.Add(c);
            }
            br.BaseStream.Position = end;
            return n;
        }

        /// <summary>
        /// Odczytuje jedną właściwość węzła na podstawie kodu typu: Y (int16), C (bool), I (int32), F (float), D (double),
        /// L (int64), S (tekst), R (bajty) oraz tablice f, d, l, i, b, c, które mogą być skompresowane.
        /// Tablice float są zwracane jako double[], a tablice b i c jako surowe bajty.
        /// </summary>
        static object ReadProp(BinaryReader br)
        {
            char t = (char)br.ReadByte();
            switch (t)
            {
                case 'Y': return br.ReadInt16();
                case 'C': return br.ReadByte() != 0;
                case 'I': return br.ReadInt32();
                case 'F': return br.ReadSingle();
                case 'D': return br.ReadDouble();
                case 'L': return br.ReadInt64();
                case 'S': return Encoding.UTF8.GetString(br.ReadBytes((int)br.ReadUInt32()));
                case 'R': return br.ReadBytes((int)br.ReadUInt32());
                case 'f': case 'd': case 'l': case 'i': case 'b': case 'c':
                {
                    int len = (int)br.ReadUInt32();
                    int enc = (int)br.ReadUInt32();
                    int clen = (int)br.ReadUInt32();
                    int esize = t == 'd' || t == 'l' ? 8 : (t == 'b' || t == 'c') ? 1 : 4;
                    byte[] raw = br.ReadBytes(clen);
                    byte[] bytes = enc == 1 ? Inflate(raw, len * esize) : raw;
                    switch (t)
                    {
                        case 'd': { var a = new double[len]; Buffer.BlockCopy(bytes, 0, a, 0, len * 8); return a; }
                        case 'f': { var a = new float[len]; Buffer.BlockCopy(bytes, 0, a, 0, len * 4); var d = new double[len]; for (int i = 0; i < len; i++) d[i] = a[i]; return d; }
                        case 'i': { var a = new int[len]; Buffer.BlockCopy(bytes, 0, a, 0, len * 4); return a; }
                        case 'l': { var a = new long[len]; Buffer.BlockCopy(bytes, 0, a, 0, len * 8); return a; }
                        default: return bytes;
                    }
                }
                default: throw new ImportException("FBX: nieznany typ właściwości '" + t + "' - plik może być uszkodzony.");
            }
        }

        /// <summary>
        /// Rozpakowuje tablicę zapisaną w formacie zlib. Pomijane są 2 bajty nagłówka zlib,
        /// a dane rozpakowywane algorytmem Deflate do oczekiwanej długości; suma kontrolna Adler-32 nie jest sprawdzana.
        /// </summary>
        static byte[] Inflate(byte[] raw, int expected)
        {
            var output = new byte[expected];
            using (var ms = new MemoryStream(raw, 2, raw.Length - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expected)
                {
                    int r = ds.Read(output, read, expected - read);
                    if (r <= 0) break;
                    read += r;
                }
            }
            return output;
        }
    }
}
