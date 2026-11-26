using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HospitalSim.Import
{
    /// <summary>
    /// Importer formatu glTF 2.0 w wersji tekstowej (.gltf) i binarnej (.glb).
    /// Odczytywana jest geometria trójkątów oraz kolor bazowy materiału (baseColorFactor); tekstury są pomijane.
    /// </summary>
    // glTF używa układu prawoskrętnego z osią Y w górę. Tak jak importer Unity,
    // negujemy współrzędną X, co daje układ lewoskrętny.
    public static class GltfImporter
    {
        class Ctx
        {
            public Dictionary<string, object> root;
            public List<byte[]> buffers = new List<byte[]>();
            public MeshBuilder builder = new MeshBuilder();
            public List<string> warnings = new List<string>();
            public int skippedPrims;
        }

        /// <summary>
        /// Wczytuje plik glTF lub GLB i przechodzi drzewo węzłów domyślnej sceny, składając macierze transformacji.
        /// Pliki wymagające kompresji Draco lub meshopt są odrzucane z czytelnym komunikatem.
        /// </summary>
        public static RawModel Load(string path)
        {
            var ctx = new Ctx();
            string dir = Path.GetDirectoryName(path) ?? "";
            byte[] glbBin = null;
            string json;

            byte[] data = File.ReadAllBytes(path);
            if (data.Length >= 12 && BitConverter.ToUInt32(data, 0) == 0x46546C67)
            {
                // GLB: 12-bajtowy nagłówek ("glTF", wersja, długość), a po nim fragmenty (chunks):
                // każdy ma długość (uint32), typ (uint32) i dane. Typ 0x4E4F534A to JSON, a 0x004E4942 to BIN.
                int o = 12;
                json = null;
                while (o + 8 <= data.Length)
                {
                    int len = (int)BitConverter.ToUInt32(data, o);
                    uint type = BitConverter.ToUInt32(data, o + 4);
                    o += 8;
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(data, o, len);
                    else if (type == 0x004E4942) { glbBin = new byte[len]; Buffer.BlockCopy(data, o, glbBin, 0, len); }
                    o += len;
                }
                if (json == null) throw new ImportException("GLB: brak części JSON w pliku.");
            }
            else json = Encoding.UTF8.GetString(data);

            ctx.root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (ctx.root == null) throw new ImportException("glTF: niepoprawny plik.");

            var req = MiniJson.Arr(ctx.root, "extensionsRequired");
            if (req != null)
                foreach (var e in req)
                {
                    string s = e as string;
                    if (s == "KHR_draco_mesh_compression" || s == "EXT_meshopt_compression")
                        throw new ImportException("Plik używa kompresji siatki (" + s + "). Wyeksportuj go ponownie bez kompresji (w Blenderze odznacz 'Compression').");
                }

            // Bufor może pochodzić z fragmentu BIN pliku GLB (brak "uri"), z adresu data: zakodowanego w Base64
            // albo z zewnętrznego pliku .bin znajdującego się obok pliku .gltf.
            var bufs = MiniJson.Arr(ctx.root, "buffers");
            if (bufs != null)
                foreach (var b in bufs)
                {
                    string uri = MiniJson.Str(b, "uri");
                    if (uri == null) ctx.buffers.Add(glbBin ?? new byte[0]);
                    else if (uri.StartsWith("data:", StringComparison.Ordinal))
                    {
                        int comma = uri.IndexOf(',');
                        ctx.buffers.Add(Convert.FromBase64String(uri.Substring(comma + 1)));
                    }
                    else
                    {
                        string p = Path.Combine(dir, Uri.UnescapeDataString(uri));
                        if (!File.Exists(p)) throw new ImportException("glTF: brak pliku z danymi '" + uri + "'. Trzymaj pliki .bin obok pliku .gltf albo użyj formatu .glb.");
                        ctx.buffers.Add(File.ReadAllBytes(p));
                    }
                }

            var nodes = MiniJson.Arr(ctx.root, "nodes") ?? new List<object>();
            var roots = new List<int>();
            var scenes = MiniJson.Arr(ctx.root, "scenes");
            if (scenes != null && scenes.Count > 0)
            {
                int si = Math.Min(MiniJson.Int(ctx.root, "scene", 0), scenes.Count - 1);
                var sn = MiniJson.Arr(scenes[si], "nodes");
                if (sn != null) foreach (var n in sn) roots.Add((int)(double)n);
            }
            else
            {
                // Plik bez scen: korzeniami są wszystkie węzły, które nie występują na żadnej liście "children".
                var isChild = new bool[nodes.Count];
                foreach (var n in nodes)
                {
                    var ch = MiniJson.Arr(n, "children");
                    if (ch != null) foreach (var c in ch) isChild[(int)(double)c] = true;
                }
                for (int i = 0; i < nodes.Count; i++) if (!isChild[i]) roots.Add(i);
            }

            foreach (int r in roots) Visit(ctx, nodes, r, Mat4d.Identity(), 0);

            if (ctx.skippedPrims > 0) ctx.warnings.Add("glTF: pominięto " + ctx.skippedPrims + " elementów, które nie są trójkątami (linie, punkty).");
            var model = ctx.builder.Build(Path.GetFileName(path));
            model.warnings.AddRange(ctx.warnings);
            return model;
        }

        /// <summary>
        /// Rekurencyjnie przetwarza węzeł i jego dzieci. Transformacja lokalna pochodzi z pola "matrix"
        /// albo jest składana jako T × R × S z pól translation, rotation i scale. Głębokość rekurencji jest ograniczona do 200.
        /// </summary>
        static void Visit(Ctx ctx, List<object> nodes, int index, Mat4d parent, int depth)
        {
            if (index < 0 || index >= nodes.Count || depth > 200) return;
            var node = nodes[index];
            Mat4d local;
            var mat = MiniJson.Arr(node, "matrix");
            if (mat != null && mat.Count == 16)
            {
                var a = new double[16];
                for (int i = 0; i < 16; i++) a[i] = (double)mat[i];
                local = Mat4d.FromColumnMajor(a);
            }
            else
            {
                var t = MiniJson.Arr(node, "translation");
                var r = MiniJson.Arr(node, "rotation");
                var s = MiniJson.Arr(node, "scale");
                local = Mat4d.Identity();
                if (t != null) local = local * Mat4d.Translation((double)t[0], (double)t[1], (double)t[2]);
                if (r != null) local = local * Mat4d.FromQuat((double)r[0], (double)r[1], (double)r[2], (double)r[3]);
                if (s != null) local = local * Mat4d.Scale((double)s[0], (double)s[1], (double)s[2]);
            }
            var world = parent * local;

            if (MiniJson.Has(node, "mesh")) AddMesh(ctx, MiniJson.Int(node, "mesh", -1), world);

            var ch = MiniJson.Arr(node, "children");
            if (ch != null) foreach (var c in ch) Visit(ctx, nodes, (int)(double)c, world, depth + 1);
        }

        static void AddMesh(Ctx ctx, int meshIndex, Mat4d world)
        {
            var meshes = MiniJson.Arr(ctx.root, "meshes");
            if (meshes == null || meshIndex < 0 || meshIndex >= meshes.Count) return;
            var prims = MiniJson.Arr(meshes[meshIndex], "primitives");
            if (prims == null) return;
            var materials = MiniJson.Arr(ctx.root, "materials");
            // Negacja osi X jest odbiciem, więc trójkąty trzeba odwrócić. Wyjątek stanowi sytuacja, w której macierz
            // węzła sama zawiera odbicie (ujemny wyznacznik): dwa odbicia się znoszą i kolejność pozostaje bez zmian.
            bool flip = world.Det3() >= 0;

            foreach (var prim in prims)
            {
                int mode = MiniJson.Int(prim, "mode", 4);
                if (mode != 4 && mode != 5 && mode != 6) { ctx.skippedPrims++; continue; }
                var attrs = MiniJson.Obj(prim, "attributes");
                if (attrs == null || !attrs.ContainsKey("POSITION")) continue;

                double[] p = ReadAccessor(ctx, MiniJson.Int(attrs, "POSITION", -1));
                int vc = p.Length / 3;
                int[] ind;
                if (MiniJson.Has(prim, "indices"))
                {
                    double[] di = ReadAccessor(ctx, MiniJson.Int(prim, "indices", -1));
                    ind = new int[di.Length];
                    for (int i = 0; i < di.Length; i++) ind[i] = (int)di[i];
                }
                else
                {
                    ind = new int[vc];
                    for (int i = 0; i < vc; i++) ind[i] = i;
                }

                // Prymityw bez materiału jest szary (0,8). Materiał bez baseColorFactor jest biały, zgodnie z wartością domyślną specyfikacji.
                string mname = "material";
                float cr = 0.8f, cg = 0.8f, cb = 0.8f;
                int mi = MiniJson.Int(prim, "material", -1);
                if (materials != null && mi >= 0 && mi < materials.Count)
                {
                    mname = MiniJson.Str(materials[mi], "name") ?? ("material" + mi);
                    var pbr = MiniJson.Obj(materials[mi], "pbrMetallicRoughness");
                    var bc = MiniJson.Arr(pbr, "baseColorFactor");
                    if (bc != null && bc.Count >= 3) { cr = (float)(double)bc[0]; cg = (float)(double)bc[1]; cb = (float)(double)bc[2]; }
                    else { cr = cg = cb = 1f; }
                }

                var g = ctx.builder.Get(mname, cr, cg, cb);
                int baseV = g.pos.Count / 3;
                for (int i = 0; i < vc; i++)
                {
                    double x, y, z;
                    world.Apply(p[i * 3], p[i * 3 + 1], p[i * 3 + 2], out x, out y, out z);
                    g.AddVertex(-x, y, z);
                }

                // Tryb 4: lista trójkątów. Tryb 5: pas trójkątów, w którym co drugi trójkąt ma zamienione dwa pierwsze
                // wierzchołki, aby zachować spójny kierunek. Tryb 6: wachlarz od pierwszego wierzchołka.
                if (mode == 4)
                {
                    for (int i = 0; i + 2 < ind.Length; i += 3) Tri(g, baseV, vc, ind[i], ind[i + 1], ind[i + 2], flip);
                }
                else if (mode == 5)
                {
                    for (int i = 0; i + 2 < ind.Length; i++)
                        if ((i & 1) == 0) Tri(g, baseV, vc, ind[i], ind[i + 1], ind[i + 2], flip);
                        else Tri(g, baseV, vc, ind[i + 1], ind[i], ind[i + 2], flip);
                }
                else
                {
                    for (int i = 1; i + 1 < ind.Length; i++) Tri(g, baseV, vc, ind[0], ind[i], ind[i + 1], flip);
                }
            }
        }

        /// <summary>
        /// Dodaje trójkąt przesunięty o <paramref name="baseV"/>; trójkąty z indeksem spoza zakresu [0, vc) są pomijane.
        /// </summary>
        static void Tri(MeshBuilder.Group g, int baseV, int vc, int a, int b, int c, bool flip)
        {
            if (a < 0 || b < 0 || c < 0 || a >= vc || b >= vc || c >= vc) return;
            g.AddTri(baseV + a, baseV + b, baseV + c, flip);
        }

        /// <summary>
        /// Zwraca liczbę składowych dla typu akcesora glTF (SCALAR, VEC2 … MAT4).
        /// </summary>
        static int Components(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
                default: return 1;
            }
        }

        /// <summary>
        /// Zwraca rozmiar składowej w bajtach: 1 dla BYTE i UNSIGNED_BYTE, 2 dla SHORT i UNSIGNED_SHORT, 4 dla pozostałych.
        /// </summary>
        static int ComponentSize(int ct)
        {
            switch (ct)
            {
                case 5120: case 5121: return 1;
                case 5122: case 5123: return 2;
                default: return 4;
            }
        }

        /// <summary>
        /// Odczytuje akcesor do płaskiej tablicy o długości count × liczba składowych.
        /// Obsługiwane są wszystkie typy składowych glTF, w tym wartości znormalizowane. Akcesor bez bufferView zwraca zera,
        /// a dane typu "sparse" nie są uwzględniane (zgłaszane jest ostrzeżenie).
        /// </summary>
        static double[] ReadAccessor(Ctx ctx, int index)
        {
            var accs = MiniJson.Arr(ctx.root, "accessors");
            if (accs == null || index < 0 || index >= accs.Count) throw new ImportException("glTF: błędny akcesor " + index);
            var acc = accs[index];
            int count = MiniJson.Int(acc, "count", 0);
            int nc = Components(MiniJson.Str(acc, "type"));
            int ct = MiniJson.Int(acc, "componentType", 5126);
            bool norm = MiniJson.Has(acc, "normalized") && (bool)((Dictionary<string, object>)acc)["normalized"];
            var result = new double[count * nc];

            if (MiniJson.Has(acc, "sparse")) ctx.warnings.Add("glTF: dane 'sparse' nie są obsługiwane, część geometrii może być błędna.");
            if (!MiniJson.Has(acc, "bufferView")) return result;

            var views = MiniJson.Arr(ctx.root, "bufferViews");
            var view = views[MiniJson.Int(acc, "bufferView", 0)];
            int bi = MiniJson.Int(view, "buffer", 0);
            if (bi < 0 || bi >= ctx.buffers.Count) throw new ImportException("glTF: brak bufora danych " + bi);
            byte[] buf = ctx.buffers[bi];
            int csize = ComponentSize(ct);
            // Brak byteStride oznacza dane ułożone ciasno, bez przerw między elementami.
            int stride = MiniJson.Int(view, "byteStride", 0);
            if (stride <= 0) stride = csize * nc;
            int start = MiniJson.Int(view, "byteOffset", 0) + MiniJson.Int(acc, "byteOffset", 0);

            for (int i = 0; i < count; i++)
            {
                int o = start + i * stride;
                for (int c = 0; c < nc; c++)
                {
                    int q = o + c * csize;
                    if (q + csize > buf.Length) throw new ImportException("glTF: dane wychodzą poza bufor.");
                    double v;
                    switch (ct)
                    {
                        case 5120: v = (sbyte)buf[q]; if (norm) v = Math.Max(v / 127.0, -1); break;
                        case 5121: v = buf[q]; if (norm) v /= 255.0; break;
                        case 5122: v = BitConverter.ToInt16(buf, q); if (norm) v = Math.Max(v / 32767.0, -1); break;
                        case 5123: v = BitConverter.ToUInt16(buf, q); if (norm) v /= 65535.0; break;
                        case 5125: v = BitConverter.ToUInt32(buf, q); break;
                        default: v = BitConverter.ToSingle(buf, q); break;
                    }
                    result[i * nc + c] = v;
                }
            }
            return result;
        }
    }
}
