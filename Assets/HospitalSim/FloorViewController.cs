using UnityEngine;

namespace HospitalSim
{
    //pokazuje tylko wybrane pietro chowa wyzsze pietra i agentow z innych pieter
    //w floorroots podpinasz rodzicow geometrii kazdego pietra 0 to parter 1 to pierwsze pietro itd
    public class FloorViewController : MonoBehaviour
    {
        public static FloorViewController Instance { get; private set; }
        public enum Mode { All, OnlyFloor, UpToFloor }

        [Tooltip("Indeks 0 = parter, 1 = 1. piętro, 2 = 2. piętro...")]
        public GameObject[] floorRoots;
        public Mode mode = Mode.All;
        public int floor;
        [Tooltip("Ukrywaj geometrię pięter powyżej wybranego (żeby było widać wnętrze)")]
        public bool hideUpperGeometry = true;

        public int VisMin { get; private set; } = -1000;
        public int VisMax { get; private set; } = 1000;

        void Awake() { Instance = this; Apply(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnValidate() { if (Application.isPlaying) Apply(); }

        public void Set(Mode m, int f) { mode = m; floor = f; Apply(); }

        void Apply()
        {
            switch (mode)
            {
                case Mode.OnlyFloor: VisMin = floor; VisMax = floor; break;
                case Mode.UpToFloor: VisMin = -1000; VisMax = floor; break;
                default: VisMin = -1000; VisMax = 1000; break;
            }
            if (floorRoots == null) return;
            for (int i = 0; i < floorRoots.Length; i++)
            {
                if (floorRoots[i] == null) continue;
                bool show = mode == Mode.All || !hideUpperGeometry || i <= floor;
                floorRoots[i].SetActive(show);
            }
        }
    }
}
