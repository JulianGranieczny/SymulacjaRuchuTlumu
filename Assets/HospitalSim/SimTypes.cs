namespace HospitalSim
{
    //kolejnosc jest wazna bo tak leza agenci w tablicach lekarze na poczatku sprzatacze na koncu
    public enum Role : byte { Doctor = 0, Patient = 1, Visitor = 2, Cleaner = 3 }

    //assemblypoint to miejsce zbiorki przy ewakuacji zwykle na zewnatrz
    //door stairs i elevator to przejscia na siatce wiec nie robimy z nich sal
    public enum RoomType { Corridor, Entrance, Exit, WaitingRoom, Ward, StaffRoom, Other, AssemblyPoint, Door, Stairs, Elevator }

    public enum Specialty { Cardiology, Neurology, Orthopedics, Pediatrics, Oncology, Surgery, Emergency, InternalMedicine }

    public enum ConnectorType { Stairs, Elevator }

    //w ktora strone bieg schodow idzie w gore samecell to stare schody w tej samej komorce na obu pietrach
    //bieg z kierunkiem malujesz tylko na nizszym pietrze dolny koniec laczy sie z tym pietrem a gorny z pietrem wyzej
    public enum StairsDir { SameCell, North, South, East, West }

    public static class RoomTypeExt
    {
        //przejscia lacza sale ze soba i pietra miedzy soba
        public static bool IsPassage(this RoomType t) => t == RoomType.Door || t == RoomType.Stairs || t == RoomType.Elevator;
    }

    //stopien choroby od 1 lekki do 5 krytyczny
    public static class Illness
    {
        public const int MinStage = 1, MaxStage = 5;

        public static string Name(int stage)
        {
            switch (stage)
            {
                case 1: return "lekki";
                case 2: return "umiarkowany";
                case 3: return "poważny";
                case 4: return "ciężki";
                case 5: return "krytyczny";
                default: return "brak";
            }
        }

        //kolor stopnia od zielonego do czerwonego
        public static UnityEngine.Color Color(int stage)
        {
            float t = UnityEngine.Mathf.Clamp01((stage - 1) / 4f);
            return UnityEngine.Color.Lerp(new UnityEngine.Color(0.55f, 0.9f, 0.55f), new UnityEngine.Color(1f, 0.4f, 0.35f), t);
        }
    }
}
