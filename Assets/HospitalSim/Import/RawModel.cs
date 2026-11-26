using System;
using System.Collections.Generic;
using System.IO;

namespace HospitalSim.Import
{
    /// <summary>
    /// Pojedyncza siatka trójkątów niezależna od Unity.
    /// Pozycje są zapisane w metrach w układzie Unity: lewoskrętnym, z osią Y skierowaną w górę.
    /// </summary>
    public class RawMesh
    {
        public string name = "mesh";
        public float r = 0.8f, g = 0.8f, b = 0.8f;
        public float[] pos = new float[0];
        public int[] idx = new int[0];
        public int VertexCount => pos.Length / 3;
        public int TriangleCount => idx.Length / 3;
    }

    /// <summary>
    /// Cały wczytany model: lista siatek, ostrzeżenia z importu i nazwa pliku źródłowego.
    /// </summary>
    public class RawModel
    {
        public List<RawMesh> meshes = new List<RawMesh>();
        public List<string> warnings = new List<string>();
        public string sourceName = "";

        public long TriangleCount { get { long n = 0; foreach (var m in meshes) n += m.TriangleCount; return n; } }
        public long VertexCount { get { long n = 0; foreach (var m in meshes) n += m.VertexCount; return n; } }

        /// <summary>
        /// Wyznacza prostopadłościan otaczający wszystkie wierzchołki.
        /// Zwraca <c>false</c>, gdy model nie ma wierzchołków; wtedy wszystkie granice są równe 0.
        /// </summary>
        public bool GetBounds(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            minX = minY = minZ = float.MaxValue;
            maxX = maxY = maxZ = float.MinValue;
            bool any = false;
            foreach (var m in meshes)
            {
                var p = m.pos;
                for (int i = 0; i + 2 < p.Length; i += 3)
                {
                    any = true;
                    if (p[i] < minX) minX = p[i];
                    if (p[i] > maxX) maxX = p[i];
                    if (p[i + 1] < minY) minY = p[i + 1];
                    if (p[i + 1] > maxY) maxY = p[i + 1];
                    if (p[i + 2] < minZ) minZ = p[i + 2];
                    if (p[i + 2] > maxZ) maxZ = p[i + 2];
                }
            }
            if (!any) { minX = minY = minZ = maxX = maxY = maxZ = 0f; }
            return any;
        }

        /// <summary>
        /// Mnoży wszystkie współrzędne wszystkich siatek przez <paramref name="s"/>.
        /// Używane w edytorze do korekty jednostek (np. milimetry na metry).
        /// </summary>
        public void Scale(float s)
        {
            foreach (var m in meshes) for (int i = 0; i < m.pos.Length; i++) m.pos[i] *= s;
        }

        /// <summary>
        /// Obraca model o 90 stopni wokół osi X.
        /// Dla <paramref name="dir"/> &gt; 0: (y, z) → (−z, y); w przeciwnym razie (y, z) → (z, −y).
        /// Służy do poprawienia modeli zapisanych z osią Z skierowaną w górę.
        /// </summary>
        public void RotateX90(int dir)
        {
            foreach (var m in meshes)
                for (int i = 0; i + 2 < m.pos.Length; i += 3)
                {
                    float y = m.pos[i + 1], z = m.pos[i + 2];
                    if (dir > 0) { m.pos[i + 1] = -z; m.pos[i + 2] = y; }
                    else { m.pos[i + 1] = z; m.pos[i + 2] = -y; }
                }
        }

        /// <summary>
        /// Obraca model o 90 stopni wokół osi Y.
        /// Dla <paramref name="dir"/> &gt; 0: (x, z) → (z, −x); w przeciwnym razie (x, z) → (−z, x).
        /// </summary>
        public void RotateY90(int dir)
        {
            foreach (var m in meshes)
                for (int i = 0; i + 2 < m.pos.Length; i += 3)
                {
                    float x = m.pos[i], z = m.pos[i + 2];
                    if (dir > 0) { m.pos[i] = z; m.pos[i + 2] = -x; }
                    else { m.pos[i] = -z; m.pos[i + 2] = x; }
                }
        }

        /// <summary>
        /// Przesuwa wszystkie wierzchołki o wektor (dx, dy, dz).
        /// </summary>
        public void Translate(float dx, float dy, float dz)
        {
            foreach (var m in meshes)
                for (int i = 0; i + 2 < m.pos.Length; i += 3) { m.pos[i] += dx; m.pos[i + 1] += dy; m.pos[i + 2] += dz; }
        }

        /// <summary>
        /// Przesuwa model tak, aby środek prostopadłościanu otaczającego w płaszczyźnie XZ znalazł się w punkcie (0, 0),
        /// a najniższy wierzchołek na wysokości Y = 0.
        /// </summary>
        public void CenterOnGround()
        {
            float a, b, c, d, e, f;
            if (!GetBounds(out a, out b, out c, out d, out e, out f)) return;
            Translate(-(a + d) * 0.5f, -b, -(c + f) * 0.5f);
        }
    }

    /// <summary>
    /// Wyjątek z komunikatem dla użytkownika, zgłaszany przy błędach odczytu modelu.
    /// </summary>
    public class ImportException : Exception
    {
        public ImportException(string msg) : base(msg) { }
    }

    /// <summary>
    /// Macierz przekształcenia 4×4 w podwójnej precyzji.
    /// Elementy są przechowywane wierszami: element w wierszu r i kolumnie c ma indeks r * 4 + c.
    /// Macierze działają na wektorach kolumnowych, więc w iloczynie A * B najpierw stosowane jest B.
    /// </summary>
    public struct Mat4d
    {
        public double[] m;

        public static Mat4d Identity()
        {
            var r = new Mat4d { m = new double[16] };
            r.m[0] = r.m[5] = r.m[10] = r.m[15] = 1;
            return r;
        }

        public static Mat4d Translation(double x, double y, double z)
        {
            var r = Identity();
            r.m[3] = x; r.m[7] = y; r.m[11] = z;
            return r;
        }

        public static Mat4d Scale(double x, double y, double z)
        {
            var r = Identity();
            r.m[0] = x; r.m[5] = y; r.m[10] = z;
            return r;
        }

        public static Mat4d RotX(double deg)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            var r = Identity();
            r.m[5] = c; r.m[6] = -s; r.m[9] = s; r.m[10] = c;
            return r;
        }

        public static Mat4d RotY(double deg)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            var r = Identity();
            r.m[0] = c; r.m[2] = s; r.m[8] = -s; r.m[10] = c;
            return r;
        }

        public static Mat4d RotZ(double deg)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            var r = Identity();
            r.m[0] = c; r.m[1] = -s; r.m[4] = s; r.m[5] = c;
            return r;
        }

        /// <summary>
        /// Tworzy macierz obrotu z kwaternionu jednostkowego (x, y, z, w).
        /// </summary>
        public static Mat4d FromQuat(double x, double y, double z, double w)
        {
            var r = Identity();
            r.m[0] = 1 - 2 * (y * y + z * z); r.m[1] = 2 * (x * y - z * w); r.m[2] = 2 * (x * z + y * w);
            r.m[4] = 2 * (x * y + z * w); r.m[5] = 1 - 2 * (x * x + z * z); r.m[6] = 2 * (y * z - x * w);
            r.m[8] = 2 * (x * z - y * w); r.m[9] = 2 * (y * z + x * w); r.m[10] = 1 - 2 * (x * x + y * y);
            return r;
        }

        /// <summary>
        /// Tworzy macierz z 16 liczb zapisanych kolumnami, tak jak w polu "matrix" formatu glTF.
        /// </summary>
        public static Mat4d FromColumnMajor(IList<double> a)
        {
            var r = new Mat4d { m = new double[16] };
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++) r.m[row * 4 + c] = a[c * 4 + row];
            return r;
        }

        public static Mat4d operator *(Mat4d a, Mat4d b)
        {
            var r = new Mat4d { m = new double[16] };
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double s = 0;
                    for (int k = 0; k < 4; k++) s += a.m[i * 4 + k] * b.m[k * 4 + j];
                    r.m[i * 4 + j] = s;
                }
            return r;
        }

        /// <summary>
        /// Przekształca punkt (x, y, z) z uwzględnieniem przesunięcia; dolny wiersz macierzy jest pomijany.
        /// </summary>
        public void Apply(double x, double y, double z, out double ox, out double oy, out double oz)
        {
            ox = m[0] * x + m[1] * y + m[2] * z + m[3];
            oy = m[4] * x + m[5] * y + m[6] * z + m[7];
            oz = m[8] * x + m[9] * y + m[10] * z + m[11];
        }

        /// <summary>
        /// Zwraca wyznacznik lewej górnej części 3×3.
        /// Wartość ujemna oznacza, że przekształcenie zawiera odbicie lustrzane;
        /// wtedy kolejność wierzchołków w trójkątach trzeba odwrócić, aby zachować kierunek ścian.
        /// </summary>
        public double Det3()
        {
            return m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
        }
    }

    /// <summary>
    /// Zbiera trójkąty pogrupowane według materiału.
    /// Grupa jest identyfikowana nazwą i kolorem (z dokładnością do trzech miejsc po przecinku);
    /// każda niepusta grupa staje się osobną siatką <see cref="RawMesh"/>.
    /// </summary>
    public class MeshBuilder
    {
        public class Group
        {
            public string name;
            public float r, g, b;
            public readonly List<float> pos = new List<float>();
            public readonly List<int> idx = new List<int>();

            public int AddVertex(double x, double y, double z)
            {
                pos.Add((float)x); pos.Add((float)y); pos.Add((float)z);
                return pos.Count / 3 - 1;
            }

            /// <summary>
            /// Dodaje trójkąt o podanych indeksach. Trójkąty zdegenerowane (z powtórzonym indeksem) są pomijane.
            /// Gdy <paramref name="flip"/> ma wartość <c>true</c>, kolejność wierzchołków jest odwracana na (a, c, b).
            /// </summary>
            public void AddTri(int a, int b, int c, bool flip)
            {
                if (a == b || b == c || a == c) return;
                idx.Add(a);
                if (flip) { idx.Add(c); idx.Add(b); }
                else { idx.Add(b); idx.Add(c); }
            }
        }

        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        readonly List<Group> order = new List<Group>();

        /// <summary>
        /// Zwraca grupę dla danej nazwy i koloru, tworząc ją przy pierwszym użyciu. Pusta nazwa zostaje zastąpiona przez "material".
        /// </summary>
        public Group Get(string name, float r, float g, float b)
        {
            if (string.IsNullOrEmpty(name)) name = "material";
            string key = name + "|" + r.ToString("0.000") + g.ToString("0.000") + b.ToString("0.000");
            Group gr;
            if (!groups.TryGetValue(key, out gr))
            {
                gr = new Group { name = name, r = r, g = g, b = b };
                groups[key] = gr;
                order.Add(gr);
            }
            return gr;
        }

        /// <summary>
        /// Tworzy <see cref="RawModel"/> z grup w kolejności ich utworzenia, pomijając grupy bez trójkątów.
        /// </summary>
        public RawModel Build(string source)
        {
            var model = new RawModel { sourceName = source };
            foreach (var gr in order)
            {
                if (gr.idx.Count == 0) continue;
                model.meshes.Add(new RawMesh { name = gr.name, r = gr.r, g = gr.g, b = gr.b, pos = gr.pos.ToArray(), idx = gr.idx.ToArray() });
            }
            return model;
        }
    }

    /// <summary>
    /// Punkt wejścia importu: lista obsługiwanych rozszerzeń i wybór właściwego importera.
    /// </summary>
    public static class ModelImporter
    {
        public static readonly string[] Extensions = { ".fbx", ".obj", ".glb", ".gltf", ".stl" };

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        /// <summary>
        /// Wczytuje model z pliku, wybierając importer na podstawie rozszerzenia.
        /// Metoda nie korzysta z API Unity, dlatego można ją wywołać z wątku roboczego.
        /// </summary>
        /// <exception cref="ImportException">Gdy plik nie istnieje, format nie jest obsługiwany albo plik nie zawiera trójkątów.</exception>
        public static RawModel Load(string path)
        {
            if (!File.Exists(path)) throw new ImportException("Plik nie istnieje: " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            RawModel m;
            switch (ext)
            {
                case ".obj": m = ObjImporter.Load(path); break;
                case ".stl": m = StlImporter.Load(path); break;
                case ".gltf":
                case ".glb": m = GltfImporter.Load(path); break;
                case ".fbx": m = FbxImporter.Load(path); break;
                default:
                    throw new ImportException("Nieobsługiwany format '" + ext + "'. Obsługiwane: FBX (binarny), OBJ, GLB, glTF, STL. " +
                                              "Inne formaty (np. DAE, 3DS, BLEND, SKP) przekonwertuj w Blenderze do FBX lub GLB.");
            }
            if (m.meshes.Count == 0) throw new ImportException("W pliku nie znaleziono żadnej geometrii (trójkątów).");
            return m;
        }
    }
}
