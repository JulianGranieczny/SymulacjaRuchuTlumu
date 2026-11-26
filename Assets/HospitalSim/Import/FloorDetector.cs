using System;
using System.Collections.Generic;

namespace HospitalSim.Import
{
    /// <summary>
    /// Automatycznie wykrywa poziomy podłóg na podstawie powierzchni poziomych trójkątów.
    /// </summary>
    // Zasada: duże skupiska poziomej powierzchni na podobnej wysokości to stropy i podłogi.
    // Najwyższe skupisko jest zwykle dachem i zostaje odrzucone, jeśli nad nim nie ma już geometrii.
    public static class FloorDetector
    {
        /// <summary>
        /// Wynik wykrywania: wysokość parteru, wysokość piętra, liczba pięter i lista poziomów.
        /// Pole <see cref="ok"/> ma wartość <c>false</c>, gdy nie udało się znaleźć żadnego poziomu.
        /// </summary>
        public class Result
        {
            public bool ok;
            public float groundY, floorHeight;
            public int floors;
            public List<float> levels = new List<float>();
        }

        /// <summary>
        /// Analizuje model i zwraca proponowane poziomy pięter.
        /// Wysokość piętra to mediana różnic między kolejnymi poziomami; przy jednym poziomie
        /// jest to odległość do najwyższego punktu modelu, ograniczona do przedziału [2,5 m; 6 m].
        /// </summary>
        public static Result Detect(RawModel model)
        {
            var res = new Result();
            const float bin = 0.05f;
            // Powierzchnia jest zliczana w przedziałach wysokości co 5 cm, osobno dla wszystkich poziomych trójkątów
            // i osobno dla tych o dodatniej składowej Y normalnej (wierzch podłogi), bo spód stropu ma normalną skierowaną w dół.
            var areaUp = new Dictionary<int, double>();
            var areaAll = new Dictionary<int, double>();
            var yUp = new Dictionary<int, double>();
            var yAll = new Dictionary<int, double>();
            double totalUp = 0, totalAll = 0;
            foreach (var m in model.meshes)
            {
                var p = m.pos; var t = m.idx;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = t[i] * 3, b = t[i + 1] * 3, c = t[i + 2] * 3;
                    double ux = p[b] - p[a], uy = p[b + 1] - p[a + 1], uz = p[b + 2] - p[a + 2];
                    double vx = p[c] - p[a], vy = p[c + 1] - p[a + 1], vz = p[c + 2] - p[a + 2];
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len < 1e-9) continue;
                    // Uwzględniane są tylko trójkąty prawie poziome: |n.y| / |n| ≥ 0,97, czyli odchylenie od poziomu do około 14°.
                    if (Math.Abs(ny) / len < 0.97) continue;
                    double ar = len * 0.5;
                    double cy = (p[a + 1] + p[b + 1] + p[c + 1]) / 3.0;
                    int k = (int)Math.Floor(cy / bin);
                    double v;
                    areaAll.TryGetValue(k, out v);
                    areaAll[k] = v + ar;
                    totalAll += ar;
                    yAll.TryGetValue(k, out v);
                    yAll[k] = v + ar * cy;
                    if (ny > 0)
                    {
                        areaUp.TryGetValue(k, out v);
                        areaUp[k] = v + ar;
                        totalUp += ar;
                        yUp.TryGetValue(k, out v);
                        yUp[k] = v + ar * cy;
                    }
                }
            }
            // Jeżeli powierzchnia skierowana w górę stanowi nie więcej niż 20% całości, model ma najpewniej odwrócone
            // ściany. Wtedy używana jest powierzchnia wszystkich poziomych trójkątów.
            bool useUp = totalUp > totalAll * 0.2;
            var area = useUp ? areaUp : areaAll;
            var sumY = useUp ? yUp : yAll;
            if (area.Count == 0) return res;

            // Przedziały odległe od siebie o mniej niż 0,6 m łączone są w skupiska. Skupisko o łącznej powierzchni
            // mniejszej niż 25% największego jest pomijane. Poziomem podłogi jest najwyższy przedział skupiska,
            // którego powierzchnia wynosi co najmniej 25% maksimum w tym skupisku (średnia wysokość ważona powierzchnią).
            // Dzięki temu grubość stropu nie tworzy dwóch poziomów, a meble o małej powierzchni nie są brane pod uwagę.
            // Poziom bliższy niż 2,0 m od poprzedniego zaakceptowanego poziomu jest pomijany.
            var keys = new List<int>(area.Keys);
            keys.Sort();
            var clusters = new List<List<int>>();
            foreach (int k in keys)
            {
                if (clusters.Count > 0)
                {
                    var last = clusters[clusters.Count - 1];
                    if ((k - last[last.Count - 1]) * bin < 0.6) { last.Add(k); continue; }
                }
                clusters.Add(new List<int> { k });
            }

            var sums = new List<double>();
            double maxA = 0;
            foreach (var c in clusters)
            {
                double s = 0;
                foreach (int k in c) s += area[k];
                sums.Add(s);
                maxA = Math.Max(maxA, s);
            }
            var levels = new List<float>();
            for (int ci = 0; ci < clusters.Count; ci++)
            {
                if (sums[ci] < maxA * 0.25) continue;
                var c = clusters[ci];
                double peak = 0;
                foreach (int k in c) peak = Math.Max(peak, area[k]);
                float top = 0;
                foreach (int k in c) if (area[k] >= peak * 0.25) top = (float)(sumY[k] / area[k]);
                if (levels.Count > 0 && top - levels[levels.Count - 1] < 2.0f) continue;
                levels.Add(top);
            }
            if (levels.Count == 0) return res;

            // Najwyższy poziom jest uznawany za dach i usuwany, gdy wykryto co najmniej dwa poziomy
            // i najwyższy punkt modelu leży mniej niż 1,5 m nad nim.
            float bx0, by0, bz0, bx1, by1, bz1;
            model.GetBounds(out bx0, out by0, out bz0, out bx1, out by1, out bz1);
            if (levels.Count >= 2 && by1 - levels[levels.Count - 1] < 1.5f) levels.RemoveAt(levels.Count - 1);
            res.levels = levels;
            res.groundY = levels[0];
            res.floors = levels.Count;
            if (levels.Count >= 2)
            {
                var diffs = new List<float>();
                for (int i = 1; i < levels.Count; i++) diffs.Add(levels[i] - levels[i - 1]);
                diffs.Sort();
                res.floorHeight = diffs[diffs.Count / 2];
            }
            else
            {
                float minX, minY, minZ, maxX, maxY, maxZ;
                model.GetBounds(out minX, out minY, out minZ, out maxX, out maxY, out maxZ);
                res.floorHeight = Math.Max(2.5f, Math.Min(6f, maxY - levels[0]));
            }
            res.ok = true;
            return res;
        }
    }
}
