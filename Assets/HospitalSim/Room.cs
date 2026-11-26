using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    //sala w scenie jej obszar to boxcollider jako trigger dolna krawedz na wysokosci podlogi
    //sale nie moga byc obrocone osie musza byc rownolegle do swiata
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class Room : MonoBehaviour
    {
        [Header("Typ sali")]
        public RoomType type = RoomType.Ward;
        public string displayName;
        [Tooltip("Tylko dla typu Ward")]
        public Specialty specialty = Specialty.InternalMedicine;
        [Tooltip("Minimalna liczba lekarzy przypisanych do tej sali (Ward)")]
        [Min(0)] public int minDoctors = 2;
        [Tooltip("Oddział: liczba łóżek (pacjent zajmuje jedno łóżko, dopóki nie zostanie wyleczony). -1 = domyślnie 3, 0 = bez limitu. " +
                 "Gdy wszystkie łóżka są zajęte, pacjenci czekają w poczekalni. " +
                 "Poczekalnia: limit miejsc, gdy pełna, nowi pacjenci się nie pojawiają (-1 i 0 = bez limitu).")]
        public int capacity = -1;

        [Header("Generator siatki waypointów (menu komponentu → ⋮)")]
        public float gridSpacing = 2f;
        public float wallMargin = 0.7f;
        public float waypointHeight = 0.5f;

        [System.NonSerialized] public List<int> nodes = new List<int>();
        //waypointy lozek jesli ktos postawil je recznie w edytorze
        [System.NonSerialized] public List<int> bedNodes = new List<int>();
        [System.NonSerialized] public int assignedDoctors;
        [System.NonSerialized] public int floor; //ustawia hospitalmap
        [System.NonSerialized] public int occupants; //ile miejsc zajmuja teraz pacjenci

        BoxCollider box;
        public Bounds WorldBounds { get { if (!box) box = GetComponent<BoxCollider>(); return box.bounds; } }
        public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
        //prawdziwy limit miejsc 0 znaczy bez limitu
        public int Capacity => capacity >= 0 ? capacity : (type == RoomType.Ward ? 3 : 0);
        public bool HasSpace => Capacity <= 0 || occupants < Capacity;

        void Reset()
        {
            var b = GetComponent<BoxCollider>();
            b.isTrigger = true;
            displayName = name;
        }

        public bool ContainsXZ(Vector3 p)
        {
            var b = WorldBounds;
            return p.x >= b.min.x && p.x <= b.max.x &&
                   p.z >= b.min.z && p.z <= b.max.z &&
                   p.y >= b.min.y - 0.3f && p.y <= b.max.y + 0.3f;
        }

#if UNITY_EDITOR
        [ContextMenu("Generuj siatkę waypointów")]
        void GenerateGrid()
        {
            var old = transform.Find("AutoWaypoints");
            if (old) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("AutoWaypoints");
            UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Generate waypoints");
            root.transform.SetParent(transform, false);

            var b = WorldBounds;
            int nx = Mathf.Max(1, Mathf.FloorToInt((b.size.x - 2f * wallMargin) / gridSpacing) + 1);
            int nz = Mathf.Max(1, Mathf.FloorToInt((b.size.z - 2f * wallMargin) / gridSpacing) + 1);
            float y = b.min.y + waypointHeight;

            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var go = new GameObject($"WP_{ix}_{iz}");
                go.transform.SetParent(root.transform, true);
                go.transform.position = new Vector3(
                    b.center.x + (ix - (nx - 1) * 0.5f) * gridSpacing,
                    y,
                    b.center.z + (iz - (nz - 1) * 0.5f) * gridSpacing);
                go.AddComponent<Waypoint>();
            }
            Debug.Log($"{Label}: wygenerowano {nx * nz} waypointów.");
        }
#endif

        void OnDrawGizmos()
        {
            var bc = GetComponent<BoxCollider>();
            if (!bc) return;
            Color c;
            switch (type)
            {
                case RoomType.Ward: c = new Color(1f, 0.3f, 0.3f); break;
                case RoomType.WaitingRoom: c = new Color(1f, 0.9f, 0.2f); break;
                case RoomType.StaffRoom: c = new Color(0.3f, 0.5f, 1f); break;
                case RoomType.Entrance: c = new Color(0.2f, 1f, 0.3f); break;
                case RoomType.Exit: c = new Color(1f, 0.2f, 1f); break;
                case RoomType.AssemblyPoint: c = new Color(1f, 0.55f, 0f); break;
                case RoomType.Corridor: c = new Color(0.7f, 0.7f, 0.7f); break;
                default: c = Color.white; break;
            }
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(c.r, c.g, c.b, 0.15f);
            Gizmos.DrawCube(bc.center, bc.size);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
            Gizmos.DrawWireCube(bc.center, bc.size);
        }
    }
}
