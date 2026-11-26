using System;
using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    [Serializable]
    public class RoomDef
    {
        public string name = "sala";
        public RoomType type = RoomType.Ward;
        public Specialty specialty = Specialty.InternalMedicine;
        [Min(0)] public int minDoctors = 2;
        //na oddziale to liczba lozek minus 1 znaczy domyslnie 3 a 0 bez limitu w poczekalni to limit miejsc
        public int capacity = -1;
        public Color color = Color.white;
        //tylko dla schodow w ktora strone prowadza w gore
        public StairsDir stairsUp = StairsDir.SameCell;
    }

    //siatka szpitala w kazdej komorce numer sali 0 to pusto 1 to pierwsza sala z listy rooms
    //pietro f lezy na wysokosci originy plus f razy floorheight
    [Serializable]
    public class LayoutData
    {
        public float originX, originY, originZ;
        public float cellSize = 1f;
        public int width = 60, depth = 40, floors = 3;
        public float floorHeight = 4f;
        public List<string> floorNames = new List<string> { "Parter", "1 piętro", "2 piętro" };
        //wysokosc przekroju nad podloga wszystko wyzej chowamy w widoku pietra
        public float cutHeight = 1.6f;

        public float waypointSpacing = 3f;
        public float wardWaypointSpacing = 2f;
        public float waypointHeight = 0.5f;
        public float roomBoxHeight = 2.5f;

        public List<RoomDef> rooms = new List<RoomDef>();

        //w pliku komorki sa zapisane parami wartosc i ile razy z rzedu
        public int[] cellsRle = new int[0];
        [NonSerialized] public ushort[] cells;

        public int Index(int f, int x, int z) => (f * depth + z) * width + x;
        public bool InRange(int f, int x, int z) => f >= 0 && f < floors && x >= 0 && x < width && z >= 0 && z < depth;

        public int Get(int f, int x, int z)
        {
            if (!InRange(f, x, z) || cells == null) return 0;
            return cells[Index(f, x, z)];
        }

        public void Set(int f, int x, int z, int value)
        {
            if (!InRange(f, x, z) || cells == null) return;
            cells[Index(f, x, z)] = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);
        }

        public RoomDef DefAt(int f, int x, int z)
        {
            int v = Get(f, x, z);
            return v > 0 && v <= rooms.Count ? rooms[v - 1] : null;
        }

        public float FloorY(int f) => originY + f * floorHeight;
        public Vector3 CellCenter(int f, int x, int z) => new Vector3(originX + (x + 0.5f) * cellSize, FloorY(f), originZ + (z + 0.5f) * cellSize);

        public bool WorldToCell(float wx, float wz, out int x, out int z)
        {
            x = Mathf.FloorToInt((wx - originX) / cellSize);
            z = Mathf.FloorToInt((wz - originZ) / cellSize);
            return x >= 0 && x < width && z >= 0 && z < depth;
        }

        public string FloorLabel(int f)
        {
            if (floorNames != null && f >= 0 && f < floorNames.Count && !string.IsNullOrEmpty(floorNames[f])) return floorNames[f];
            return f == 0 ? "Parter" : f + " piętro";
        }

        //czy w tym miejscu jest przejscie drzwi schody albo winda bo tam siatka scian nie blokuje
        public bool IsPassageAt(int f, float wx, float wz)
        {
            int x, z;
            if (!WorldToCell(wx, wz, out x, out z)) return false;
            var d = DefAt(f, x, z);
            return d != null && d.type.IsPassage();
        }

        public void EnsureCells()
        {
            int need = Mathf.Max(1, width) * Mathf.Max(1, depth) * Mathf.Max(1, floors);
            if (cells == null || cells.Length != need) cells = new ushort[need];
            while (floorNames.Count < floors) floorNames.Add(floorNames.Count == 0 ? "Parter" : floorNames.Count + " piętro");
        }

        //zmiana rozmiaru siatki namalowane komorki zostaja
        public void Resize(int newW, int newD, int newF)
        {
            newW = Mathf.Clamp(newW, 1, 2000); newD = Mathf.Clamp(newD, 1, 2000); newF = Mathf.Clamp(newF, 1, 50);
            EnsureCells();
            var n = new ushort[newW * newD * newF];
            int mf = Mathf.Min(floors, newF), mz = Mathf.Min(depth, newD), mx = Mathf.Min(width, newW);
            for (int f = 0; f < mf; f++)
                for (int z = 0; z < mz; z++)
                    for (int x = 0; x < mx; x++)
                        n[(f * newD + z) * newW + x] = cells[Index(f, x, z)];
            width = newW; depth = newD; floors = newF;
            cells = n;
            EnsureCells();
        }

        //nowe polozenie i rozmiar komorki sale zostaja w tym samym miejscu swiata
        public void Resample(float newOriginX, float newOriginZ, float newCell, int newW, int newD)
        {
            EnsureCells();
            newCell = Mathf.Max(0.1f, newCell);
            newW = Mathf.Clamp(newW, 1, 2000); newD = Mathf.Clamp(newD, 1, 2000);
            var old = cells;
            float ox = originX, oz = originZ, oc = cellSize;
            int ow = width, od = depth;
            var n = new ushort[newW * newD * floors];
            for (int f = 0; f < floors; f++)
                for (int z = 0; z < newD; z++)
                    for (int x = 0; x < newW; x++)
                    {
                        float wx = newOriginX + (x + 0.5f) * newCell, wz = newOriginZ + (z + 0.5f) * newCell;
                        int cx = Mathf.FloorToInt((wx - ox) / oc), cz = Mathf.FloorToInt((wz - oz) / oc);
                        if (cx < 0 || cz < 0 || cx >= ow || cz >= od) continue;
                        n[(f * newD + z) * newW + x] = old[(f * od + cz) * ow + cx];
                    }
            originX = newOriginX; originZ = newOriginZ; cellSize = newCell; width = newW; depth = newD;
            cells = n;
        }

        public void Pack()
        {
            EnsureCells();
            var l = new List<int>();
            int i = 0;
            while (i < cells.Length)
            {
                int v = cells[i], run = 1;
                while (i + run < cells.Length && cells[i + run] == v) run++;
                l.Add(v); l.Add(run);
                i += run;
            }
            cellsRle = l.ToArray();
        }

        public void Unpack()
        {
            cells = null;
            EnsureCells();
            int p = 0;
            for (int i = 0; i + 1 < cellsRle.Length; i += 2)
            {
                int v = cellsRle[i], run = cellsRle[i + 1];
                for (int k = 0; k < run && p < cells.Length; k++) cells[p++] = (ushort)v;
            }
        }

        //ile komorek ma kazda sala potrzebne do listy sal
        public int[] CountCells()
        {
            var c = new int[rooms.Count + 1];
            if (cells == null) return c;
            foreach (var v in cells) if (v < c.Length) c[v]++;
            return c;
        }

        //po usunieciu sali numery kolejnych przesuwaja sie o jeden
        public void RemoveRoom(int index)
        {
            if (index < 0 || index >= rooms.Count) return;
            EnsureCells();
            for (int i = 0; i < cells.Length; i++)
            {
                int v = cells[i];
                if (v == index + 1) cells[i] = 0;
                else if (v > index + 1) cells[i] = (ushort)(v - 1);
            }
            rooms.RemoveAt(index);
        }
    }
}
