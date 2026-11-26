using System;
using UnityEngine;

namespace HospitalSim
{
    [Serializable]
    public class RoleFloat
    {
        public float doctor = 1f, patient = 1f, visitor = 1f, cleaner = 1f;
        public RoleFloat() { }
        public RoleFloat(float d, float p, float v, float c) { doctor = d; patient = p; visitor = v; cleaner = c; }
        public float Get(Role r)
        {
            switch (r)
            {
                case Role.Doctor: return doctor;
                case Role.Patient: return patient;
                case Role.Visitor: return visitor;
                default: return cleaner;
            }
        }
    }

    //po jednej wartosci na kazdy stopien choroby
    [Serializable]
    public class StageFloat
    {
        public float stage1 = 1f, stage2 = 1f, stage3 = 1f, stage4 = 1f, stage5 = 1f;
        public StageFloat() { }
        public StageFloat(float a, float b, float c, float d, float e) { stage1 = a; stage2 = b; stage3 = c; stage4 = d; stage5 = e; }
        public float Get(int s)
        {
            switch (s)
            {
                case 1: return stage1;
                case 2: return stage2;
                case 3: return stage3;
                case 4: return stage4;
                default: return stage5;
            }
        }
    }

    //przedzial od do w minutach gry dla kazdego stopnia
    [Serializable]
    public class StageRange
    {
        public Vector2 stage1, stage2, stage3, stage4, stage5;
        public StageRange() { }
        public StageRange(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 e) { stage1 = a; stage2 = b; stage3 = c; stage4 = d; stage5 = e; }
        public Vector2 Get(int s)
        {
            switch (s)
            {
                case 1: return stage1;
                case 2: return stage2;
                case 3: return stage3;
                case 4: return stage4;
                default: return stage5;
            }
        }
    }

    [Serializable]
    public class PopulationSettings
    {
        [Min(0)] public int doctors = 40;
        [Tooltip("Maksymalna liczba pacjentów w szpitalu jednocześnie (rozmiar puli)")]
        [Min(0)] public int patients = 300;
        [Tooltip("Maksymalna liczba odwiedzających jednocześnie (rozmiar puli)")]
        [Min(0)] public int visitors = 150;
        [Min(0)] public int cleaners = 20;
        [Tooltip("Ziarno losowości - to samo ziarno = ta sama symulacja")]
        public uint seed = 12345;

        [Header("Start symulacji")]
        [Tooltip("Lekarze zaczynają w swoich salach (przypisanych oddziałach)")]
        public bool doctorsStartInOwnRoom = true;
        [Range(0f, 1f)] public float patientsAlreadyInside = 0.2f;
        [Range(0f, 1f)] public float visitorsAlreadyInside = 0.1f;
    }

    [Serializable]
    public class ArrivalSettings
    {
        [Tooltip("Średnio tylu nowych pacjentów na godzinę gry. Losowanie odbywa się co minutę gry (tylko w godzinach otwarcia)")]
        [Min(0f)] public float patientsPerHour = 10f;
        [Min(0f)] public float visitorsPerHour = 8f;
        [Tooltip("Pacjenci pojawiają się od razu w poczekalni (zamiast wchodzić przez wejście)")]
        public bool patientsSpawnInWaitingRoom = true;
        public bool visitorsSpawnInWaitingRoom = false;
    }

    [Serializable]
    public class MovementSettings
    {
        [Tooltip("Bazowa prędkość chodu w metrach na sekundę gry. Człowiek idzie zwykle 1,2-1,5 m/s")]
        public float walkSpeed = 1.35f;
        [Range(0f, 0.5f)] public float speedVariation = 0.15f;
        [Tooltip("Mnożnik prędkości dla roli (lekarz, pacjent, odwiedzający, sprzątacz)")]
        public RoleFloat roleSpeed = new RoleFloat(1.05f, 0.75f, 0.95f, 0.9f);
        [Tooltip("Zmieniane na żywo w trakcie gry")]
        [Min(0f)] public float globalSpeedMultiplier = 1f;
        [Tooltip("Losowe przesunięcie agenta względem waypointa")]
        public float offsetRadius = 0.4f;
    }

    [Serializable]
    public class CollisionSettings
    {
        [Header("Kolizje agent - agent")]
        public bool agentCollisions = true;
        [Tooltip("Promień agenta; minimalny dystans między agentami = 2 x promień")]
        public float agentRadius = 0.3f;
        [Range(0f, 30f)] public float separationStrength = 10f;
        [Tooltip("Maks. przesunięcie wypychające w jednej klatce")]
        public float maxPushPerFrame = 0.12f;
        [Tooltip("Ilu sąsiadów maksymalnie sprawdza każdy agent (wydajność)")]
        [Range(4, 64)] public int maxNeighbors = 16;
        [Tooltip("Rozmiar komórki siatki przestrzennej agentów")]
        public float gridCellSize = 1f;
    }

    [Serializable]
    public class RoutineSettings
    {
        [Header("Godziny otwarcia (pacjenci / odwiedzający pojawiają się tylko wtedy)")]
        [Range(0, 23)] public int openHour = 7;
        [Range(0, 24)] public int closeHour = 20;

        [Header("Lekarz  (przedziały w minutach symulacji: min, max)")]
        public Vector2 doctorOwnWardStay = new Vector2(5f, 15f);
        public Vector2 doctorOtherWardStay = new Vector2(3f, 8f);
        public Vector2 doctorStaffRoomStay = new Vector2(10f, 20f);
        [Range(0f, 1f)] public float doctorVisitOtherWardChance = 0.6f;
        [Tooltip("Lekarz odwiedza tylko sale i dyżurki na swoim piętrze")]
        public bool doctorsStayOnOwnFloor = true;

        [Header("Odwiedzający")]
        public Vector2 visitorWaitingRoomStay = new Vector2(5f, 15f);
        public Vector2 visitorWardStay = new Vector2(15f, 40f);
        [Tooltip("Odwiedzający przychodzą tylko do pacjentów z co najmniej takim stopniem choroby")]
        [Range(1, 5)] public int visitorMinStage = 4;
        [Tooltip("Ilu odwiedzających naraz może mieć jeden pacjent")]
        [Range(1, 5)] public int visitorsPerPatient = 2;

        [Header("Sprzątacz")]
        public Vector2 cleanerRoomStay = new Vector2(5f, 20f);
        [Tooltip("Każdy sprzątacz dostaje 'swoje' piętro i sprząta tylko tam")]
        public bool cleanersStayOnOwnFloor = true;
    }

    [Serializable]
    public class HealthSettings
    {
        [Tooltip("Maksymalne hp dla każdej roli (lekarz, pacjent, odwiedzający, sprzątacz)")]
        public RoleFloat maxHp = new RoleFloat(100f, 100f, 100f, 100f);
        [Tooltip("Ile hp pacjent traci na godzinę gry, gdy czeka (w poczekalni, w drodze do łóżka i na lekarza)")]
        [Min(0f)] public float patientHpLossPerHour = 15f;
        [Tooltip("Jak często trafia się każdy stopień choroby (wagi). Najczęściej 1, potem 2 itd., 4 i 5 są rzadkie")]
        public StageFloat stageChance = new StageFloat(40f, 27f, 18f, 10f, 5f);
        [Tooltip("Mnożnik utraty hp dla stopnia choroby - ciężej chorzy słabną szybciej")]
        public StageFloat stageHpLoss = new StageFloat(0.5f, 0.75f, 1f, 1.4f, 1.9f);
        [Tooltip("Dodatkowy losowy mnożnik utraty hp każdego pacjenta (od, do)")]
        public Vector2 severityRange = new Vector2(0.85f, 1.15f);
        [Tooltip("Pacjenci obecni przy starcie symulacji mają hp z przedziału od tej wartości do 100 procent")]
        [Range(0f, 1f)] public float initialMinHpFraction = 0.7f;
    }

    [Serializable]
    public class TreatmentSettings
    {
        [Tooltip("Czas leczenia w minutach gry (od, do) dla każdego stopnia choroby")]
        public StageRange stageDuration = new StageRange(
            new Vector2(15f, 30f), new Vector2(30f, 60f), new Vector2(60f, 120f), new Vector2(120f, 200f), new Vector2(180f, 300f));
        [Tooltip("Gdy lekarze oddziału są zajęci, pomóc może wolny lekarz z innego oddziału")]
        public bool otherDoctorsCanHelp = true;
    }

    [Serializable]
    public class EvacuationSettings
    {
        [Tooltip("Mnożnik prędkości uciekających")]
        [Min(0.1f)] public float speedMultiplier = 1.6f;
        [Tooltip("Po tylu minutach symulacji ewakuacja kończy się siłą (pozostali znikają)")]
        [Min(1f)] public float maxDurationMinutes = 30f;
        [Tooltip("Po ewakuacji: tyle minut nikt się nie pojawia, potem personel wraca do pracy i znów przychodzą pacjenci")]
        [Min(0f)] public float returnDelayMinutes = 20f;
    }

    [Serializable]
    public class VisualSettings
    {
        [Tooltip("Puste = wbudowana kula")]
        public Mesh mesh;
        [Tooltip("Lekarz, pacjent, odwiedzający, sprzątacz")]
        public Color[] roleColors = AppearanceSettings.DefaultColors();
        [Tooltip("Opcjonalne własne materiały (GPU Instancing włączony). Puste = tworzone automatycznie.")]
        public Material[] roleMaterials = new Material[4];
        public RoleFloat roleScale = new RoleFloat(0.75f, 0.7f, 0.7f, 0.7f);
    }
}
