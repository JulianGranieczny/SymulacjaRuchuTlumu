using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HospitalSim.Import
{
    /// <summary>
    /// Importer formatu STL w wersji binarnej i tekstowej (ASCII).
    /// STL zawiera wyłącznie trójkąty, bez kolorów i bez informacji o jednostkach, dlatego cały model trafia do jednej szarej grupy.
    /// </summary>
    // Pliki STL zwykle mają oś Z skierowaną w górę. Zamiana osi Y i Z ustawia oś pionową poprawnie
    // i jednocześnie zmienia układ z prawoskrętnego na lewoskrętny, dlatego trójkąty są odwracane.
    public static class StlImporter
    {
        /// <summary>
        /// Wczytuje plik STL. Wersja binarna jest rozpoznawana po tym, że rozmiar pliku wynosi dokładnie
        /// 84 + 50 × (liczba trójkątów z nagłówka) bajtów; w przeciwnym razie plik jest traktowany jako tekstowy.
        /// </summary>
        public static RawModel Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            var builder = new MeshBuilder();
            var g = builder.Get("stl", 0.8f, 0.8f, 0.8f);
            // Wierzchołki o identycznych współrzędnych są scalane, co zmniejsza rozmiar siatki.
            var map = new Dictionary<long, int>();
            var tri = new int[3];

            bool binary = data.Length >= 84 && 84L + 50L * BitConverter.ToUInt32(data, 80) == data.Length;
            if (binary)
            {
                // Układ binarny: 80 bajtów nagłówka, liczba trójkątów (uint32), następnie po 50 bajtów na trójkąt:
                // normalna (3 × float), trzy wierzchołki (9 × float) i 2 bajty atrybutów. Normalna jest pomijana.
                uint n = BitConverter.ToUInt32(data, 80);
                int o = 84;
                for (uint t = 0; t < n; t++, o += 50)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int p = o + 12 + k * 12;
                        tri[k] = Vert(g, map, BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));
                    }
                    g.AddTri(tri[0], tri[1], tri[2], true);
                }
            }
            else
            {
                // W wersji tekstowej każde trzy kolejne linie "vertex x y z" tworzą jeden trójkąt.
                string text = Encoding.ASCII.GetString(data);
                int k = 0;
                foreach (var raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                    var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length < 4) continue;
                    tri[k++] = Vert(g, map, F(t[1]), F(t[2]), F(t[3]));
                    if (k == 3) { g.AddTri(tri[0], tri[1], tri[2], true); k = 0; }
                }
            }

            var model = builder.Build(Path.GetFileName(path));
            model.warnings.Add("STL nie zapisuje jednostek - sprawdź skalę budynku (często są to milimetry).");
            return model;
        }

        /// <summary>
        /// Zwraca indeks wierzchołka (x, y, z), zapisanego w grupie jako (x, z, y).
        /// Kluczem słownika jest skrót pozycji zaokrąglonej do 0,1 mm. Istniejący wierzchołek jest używany tylko wtedy,
        /// gdy jego współrzędne są dokładnie równe; przy kolizji skrótu tworzony jest nowy wierzchołek.
        /// </summary>
        static int Vert(MeshBuilder.Group g, Dictionary<long, int> map, float x, float y, float z)
        {
            long key;
            unchecked
            {
                key = (long)Math.Round(x * 10000.0) * 73856093L ^ (long)Math.Round(y * 10000.0) * 19349663L ^ (long)Math.Round(z * 10000.0) * 83492791L;
            }
            int i;
            if (map.TryGetValue(key, out i))
            {
                int b = i * 3;
                if (g.pos[b] == x && g.pos[b + 1] == z && g.pos[b + 2] == y) return i;
            }
            i = g.AddVertex(x, z, y);
            map[key] = i;
            return i;
        }

        /// <summary>
        /// Parsuje liczbę w notacji niezależnej od ustawień regionalnych; przy błędzie zwraca 0.
        /// </summary>
        static float F(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
    }
}
