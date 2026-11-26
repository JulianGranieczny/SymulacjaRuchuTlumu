using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HospitalSim.Import
{
    /// <summary>
    /// Importer tekstowego formatu Wavefront OBJ.
    /// Odczytuje wierzchołki (v), ściany (f), przypisania materiałów (usemtl) oraz kolor rozproszenia Kd z pliku MTL (mtllib).
    /// Współrzędne tekstur i normalne są ignorowane.
    /// </summary>
    // OBJ używa układu prawoskrętnego, a Unity lewoskrętnego. Dlatego współrzędna X jest
    // negowana, a kolejność wierzchołków w każdym trójkącie odwracana.
    public static class ObjImporter
    {
        static readonly char[] Ws = { ' ', '\t' };

        /// <summary>
        /// Wczytuje plik OBJ. Wielokąty są dzielone na trójkąty metodą wachlarza od pierwszego wierzchołka.
        /// Ściany z błędnymi indeksami są pomijane i zliczane w ostrzeżeniu.
        /// </summary>
        public static RawModel Load(string path)
        {
            var verts = new List<double>();
            var mats = new Dictionary<string, float[]>();
            var builder = new MeshBuilder();
            var maps = new Dictionary<MeshBuilder.Group, Dictionary<int, int>>();
            string curName = "material";
            float[] curCol = { 0.8f, 0.8f, 0.8f };
            var group = builder.Get(curName, curCol[0], curCol[1], curCol[2]);
            var poly = new List<int>();
            string dir = Path.GetDirectoryName(path);
            var warnings = new List<string>();
            int badFaces = 0;

            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string line;
                var sb = new StringBuilder();
                while ((line = reader.ReadLine()) != null)
                {
                    // Linia zakończona ukośnikiem wstecznym jest kontynuowana w następnej linii.
                    if (line.EndsWith("\\")) { sb.Append(line, 0, line.Length - 1).Append(' '); continue; }
                    if (sb.Length > 0) { sb.Append(line); line = sb.ToString(); sb.Clear(); }

                    line = line.Trim();
                    if (line.Length < 2 || line[0] == '#') continue;
                    int sp = line.IndexOfAny(Ws);
                    if (sp < 0) continue;
                    string key = line.Substring(0, sp);
                    string rest = line.Substring(sp + 1).Trim();

                    // Brakujące współrzędne zastępowane są zerami, aby nie przesunąć numeracji kolejnych wierzchołków.
                    if (key == "v")
                    {
                        var t = rest.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length < 3) { verts.Add(0); verts.Add(0); verts.Add(0); continue; }
                        verts.Add(D(t[0])); verts.Add(D(t[1])); verts.Add(D(t[2]));
                    }
                    else if (key == "f")
                    {
                        var t = rest.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                        poly.Clear();
                        int vcount = verts.Count / 3;
                        bool ok = true;
                        foreach (var tok in t)
                        {
                            // Z zapisu v/vt/vn używany jest tylko indeks wierzchołka. Indeksy ujemne liczone są od końca listy.
                            int slash = tok.IndexOf('/');
                            string s = slash >= 0 ? tok.Substring(0, slash) : tok;
                            int vi;
                            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out vi)) { ok = false; break; }
                            vi = vi < 0 ? vcount + vi : vi - 1;
                            if (vi < 0 || vi >= vcount) { ok = false; break; }
                            poly.Add(vi);
                        }
                        if (!ok || poly.Count < 3) { badFaces++; continue; }

                        // Każda grupa materiałowa ma własną numerację wierzchołków; słownik mapuje indeks globalny na lokalny.
                        Dictionary<int, int> map;
                        if (!maps.TryGetValue(group, out map)) { map = new Dictionary<int, int>(); maps[group] = map; }
                        for (int k = 0; k < poly.Count; k++)
                        {
                            int gi = poly[k], li;
                            if (!map.TryGetValue(gi, out li))
                            {
                                li = group.AddVertex(-verts[gi * 3], verts[gi * 3 + 1], verts[gi * 3 + 2]);
                                map[gi] = li;
                            }
                            poly[k] = li;
                        }
                        for (int k = 1; k + 1 < poly.Count; k++) group.AddTri(poly[0], poly[k], poly[k + 1], true);
                    }
                    else if (key == "usemtl")
                    {
                        curName = rest;
                        float[] c;
                        curCol = mats.TryGetValue(rest, out c) ? c : new[] { 0.8f, 0.8f, 0.8f };
                        group = builder.Get(curName, curCol[0], curCol[1], curCol[2]);
                    }
                    else if (key == "mtllib")
                    {
                        LoadMtl(Path.Combine(dir ?? "", rest), mats, warnings);
                    }
                }
            }

            if (badFaces > 0) warnings.Add("OBJ: pominięto " + badFaces + " błędnych ścian.");
            var model = builder.Build(Path.GetFileName(path));
            model.warnings.AddRange(warnings);
            return model;
        }

        /// <summary>
        /// Odczytuje z pliku MTL nazwy materiałów (newmtl) i ich kolor rozproszenia (Kd).
        /// Materiał bez Kd otrzymuje kolor szary (0,8; 0,8; 0,8). Brak pliku MTL powoduje ostrzeżenie, a nie błąd.
        /// </summary>
        static void LoadMtl(string path, Dictionary<string, float[]> mats, List<string> warnings)
        {
            if (!File.Exists(path)) { warnings.Add("OBJ: brak pliku materiałów " + Path.GetFileName(path) + " - kolory będą szare."); return; }
            string cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("newmtl", StringComparison.Ordinal)) { cur = line.Substring(6).Trim(); mats[cur] = new[] { 0.8f, 0.8f, 0.8f }; }
                else if (cur != null && line.StartsWith("Kd ", StringComparison.Ordinal))
                {
                    var t = line.Substring(3).Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length >= 3) mats[cur] = new[] { (float)D(t[0]), (float)D(t[1]), (float)D(t[2]) };
                }
            }
        }

        /// <summary>
        /// Parsuje liczbę w notacji niezależnej od ustawień regionalnych; przy błędzie zwraca 0.
        /// </summary>
        static double D(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
