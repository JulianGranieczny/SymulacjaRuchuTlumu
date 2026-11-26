using System;
using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    //waypoint postawiony recznie w edytorze wysokosc bierze sie z pietra
    [Serializable]
    public class ManualWaypoint
    {
        public int floor;
        public float x, z;
    }

    [Serializable]
    public class ClockSettings
    {
        public int startYear = 2026;
        [Range(1, 12)] public int startMonth = 10;
        [Range(1, 31)] public int startDay = 1;
        [Range(0, 23)] public int startHour = 6;
        public TimeMode startMode = TimeMode.X10;
        [Tooltip("Co ile minut gry następuje tick, czyli losowanie zdarzeń")]
        [Min(1f)] public float tickIntervalSimMinutes = 5f;
    }

    [Serializable]
    public class AppearanceSettings
    {
        [Tooltip("Lekarz, pacjent, odwiedzający, sprzątacz")]
        public Color[] roleColors = DefaultColors();

        //stonowane kolory rol
        public static Color[] DefaultColors()
        {
            return new[]
            {
                new Color(0.88f, 0.88f, 0.86f),
                new Color(0.38f, 0.63f, 0.43f),
                new Color(0.85f, 0.6f, 0.38f),
                new Color(0.52f, 0.41f, 0.31f)
            };
        }

        //kolory ze starej wersji programu potrzebne przy migracji starych projektow
        public static Color[] OldColors()
        {
            return new[] { Color.white, new Color(0.1f, 0.8f, 0.2f), new Color(1f, 0.5f, 0f), new Color(0.45f, 0.27f, 0.1f) };
        }
        [Tooltip("Wielkość kulki dla każdej roli (m)")]
        public RoleFloat roleScale = new RoleFloat(0.75f, 0.7f, 0.7f, 0.7f);
    }

    [Serializable]
    public class PeopleSettings
    {
        [Tooltip("Zmień, żeby wylosować inne osoby")]
        public uint seed = 1;
        public List<FirstNameEntry> firstNames = PersonDatabase.DefaultFirstNames();
        public List<SurnameEntry> surnames = PersonDatabase.DefaultSurnames();
        public AgeRange doctorAge = new AgeRange(27, 65);
        public AgeRange patientAge = new AgeRange(1, 92);
        public AgeRange visitorAge = new AgeRange(16, 80);
        public AgeRange cleanerAge = new AgeRange(20, 65);
    }

    [Serializable]
    public class NavigationSettings
    {
        [Tooltip("Dokładność mapy ścian w metrach. Mniej = dokładniej, ale dłuższe przygotowanie symulacji")]
        [Min(0.1f)] public float wallCellSize = 0.3f;
        [Tooltip("Dodatkowy odstęp agentów od ścian (m)")]
        [Min(0f)] public float wallClearance = 0.1f;
        [Tooltip("Na jakiej wysokości nad podłogą szukamy ścian. Powyżej łóżek i stołów, poniżej górnej krawędzi drzwi")]
        [Range(0.3f, 1.9f)] public float wallCheckHeight = 1.1f;
        [Tooltip("Połowa wysokości pasa, w którym szukamy ścian")]
        [Min(0.05f)] public float wallCheckHalfHeight = 0.3f;
        [Tooltip("Ręczne waypointy łączą się z innymi w tej odległości, jeśli nie ma między nimi ściany")]
        [Min(0.5f)] public float maxLinkDistance = 4.5f;
        [Tooltip("Promień agenta przy sprawdzaniu, czy połączenie nie przechodzi przez ścianę")]
        [Min(0.01f)] public float linkRadius = 0.2f;
    }

    [Serializable]
    public class EventSettings
    {
        public uint seed = 999;
        public List<RandomEventDef> events = RandomEventSystem.DefaultEvents();
    }

    //wszystkie ustawienia symulacji ktore zapisujemy w pliku projektu
    [Serializable]
    public class SimSettingsBundle
    {
        public const int CurrentVersion = 2;
        //wersja ustawien dzieki niej starsze pliki dostaja nowe wartosci domyslne w migrate
        [HideInInspector] public int settingsVersion = CurrentVersion;
        public ClockSettings clock = new ClockSettings();
        public PopulationSettings population = new PopulationSettings();
        public ArrivalSettings arrivals = new ArrivalSettings();
        public HealthSettings health = new HealthSettings();
        public TreatmentSettings treatment = new TreatmentSettings();
        public MovementSettings movement = new MovementSettings();
        public CollisionSettings collisions = new CollisionSettings();
        public RoutineSettings routine = new RoutineSettings();
        public EvacuationSettings evacuation = new EvacuationSettings();
        public EventSettings events = new EventSettings();
        public PeopleSettings people = new PeopleSettings();
        public AppearanceSettings appearance = new AppearanceSettings();
        public NavigationSettings navigation = new NavigationSettings();

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        //stare wartosci domyslne zamieniamy na nowe a to co uzytkownik zmienil sam zostawiamy
        public void Migrate(int fromVersion)
        {
            if (fromVersion < 2)
            {
                if (movement != null)
                {
                    if (Near(movement.walkSpeed, 2.5f)) movement.walkSpeed = 1.35f;
                    if (Near(movement.speedVariation, 0.2f)) movement.speedVariation = 0.15f;
                    var rs = movement.roleSpeed;
                    if (rs != null && Near(rs.doctor, 1.15f) && Near(rs.patient, 0.7f) && Near(rs.visitor, 1f) && Near(rs.cleaner, 0.95f))
                        movement.roleSpeed = new RoleFloat(1.05f, 0.75f, 0.95f, 0.9f);
                }
                if (health != null && Near(health.severityRange.x, 0.6f) && Near(health.severityRange.y, 1.4f))
                    health.severityRange = new Vector2(0.85f, 1.15f);
                if (events != null && events.events != null)
                    foreach (var e in events.events)
                    {
                        if (e.kind == SimEventKind.Custom && Near(e.chancePerTick, 0.003f)) e.chancePerTick = RandomEventSystem.DefaultCustomChance;
                        if (e.kind == SimEventKind.Evacuation && Near(e.chancePerTick, 0.0005f)) e.chancePerTick = RandomEventSystem.DefaultEvacuationChance;
                    }
                if (appearance != null && appearance.roleColors != null && appearance.roleColors.Length >= 4)
                {
                    var old = AppearanceSettings.OldColors();
                    bool same = true;
                    for (int k = 0; k < 4; k++) if (((Vector4)(appearance.roleColors[k] - old[k])).sqrMagnitude > 1e-4f) same = false;
                    if (same) appearance.roleColors = AppearanceSettings.DefaultColors();
                }
            }
            settingsVersion = CurrentVersion;
        }
    }

    [Serializable]
    public class ProjectData
    {
        public int formatVersion = HospppsFile.FormatVersion;
        public string name = "Nowy szpital";
        public string sourceModel = "";
        public string created = "";
        public string modified = "";
        public LayoutData layout = new LayoutData();
        public List<ManualWaypoint> waypoints = new List<ManualWaypoint>();
        public SimSettingsBundle settings = new SimSettingsBundle();

        //kopia przez json zeby symulacja nie ruszala ustawien zapisanych w projekcie
        public static T Clone<T>(T obj) => JsonUtility.FromJson<T>(JsonUtility.ToJson(obj));
    }
}
