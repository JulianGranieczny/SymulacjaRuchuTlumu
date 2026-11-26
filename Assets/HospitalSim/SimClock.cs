using System;
using System.Globalization;
using UnityEngine;

namespace HospitalSim
{
    //tryby czasu symulacji
    public enum TimeMode { Pause, Slow, Real, X2, X5, X10, X60, X300, X1000 }

    [DefaultExecutionOrder(-100)]
    public class SimClock : MonoBehaviour
    {
        public static SimClock Instance { get; private set; }

        //ile sekund gry mija w jednej prawdziwej sekundzie w kazdym trybie
        //nowe tryby dopisujemy tylko na koncu bo w pliku projektu tryb siedzi jako numer
        public static readonly float[] ModeScale = { 0f, 0.5f, 1f, 2f, 5f, 10f, 60f, 300f, 1000f };
        public static readonly string[] ModeLabel = { "II", "0.5x", "1x", "2x", "5x", "10x", "60x", "300x", "1000x" };

        [Header("data startowa")]
        public int startYear = 2026;
        [Range(1, 12)] public int startMonth = 10;
        [Range(1, 31)] public int startDay = 1;

        [Header("czas")]
        [Range(0, 23)] public int startHour = 6;
        [Tooltip("Tryb czasu - mozna zmieniac na zywo. 1x = 1 sekunda gry to 1 sekunda realna, 0.5x = czas zwolniony")]
        public TimeMode mode = TimeMode.X10;
        [Tooltip("Klawisze: spacja = pauza, [ i ] = wolniej i szybciej")]
        public bool hotkeys = true;

        [Header("ticki")]
        [Tooltip("Co ile minut gry nastepuje tick (losowe zdarzenia). Pacjenci pojawiaja sie osobno co minute gry")]
        public float tickIntervalSimMinutes = 5f;

        public double TotalSimSeconds { get; private set; }
        //ile sekund gry minelo w tej klatce z tego licza sie ruch timery i cala reszta
        public float SimDelta { get; private set; }
        public int TickCount { get; private set; }
        public float TimeScale => ModeScale[(int)mode];

        public int Day => (int)(TotalSimSeconds / 86400.0) + 1;
        public int Hour => (int)((TotalSimSeconds % 86400.0) / 3600.0);
        public int Minute => (int)((TotalSimSeconds % 3600.0) / 60.0);
        public int Second => (int)(TotalSimSeconds % 60.0);

        //data gry przeskakuje o dzien po kazdej polnocy
        public DateTime CurrentDate => startDate.AddDays(Day - 1);
        public string DateString => CurrentDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        public string TimeString => Hour.ToString("00") + ":" + Minute.ToString("00") + ":" + Second.ToString("00");

        public event Action<int> OnTick;
        public event Action OnMinute;
        public event Action<int> OnHourChanged;
        public event Action<int> OnNewDay;

        DateTime startDate = new DateTime(2026, 10, 1);
        float tickAcc, minuteAcc;
        int lastHour, lastDay;
        TimeMode lastRunning = TimeMode.X10;

        void Awake()
        {
            Instance = this;
            int sy = Mathf.Clamp(startYear, 1, 9998);
            int sm = Mathf.Clamp(startMonth, 1, 12);
            int sd = Mathf.Clamp(startDay, 1, DateTime.DaysInMonth(sy, sm));
            startDate = new DateTime(sy, sm, sd);
            TotalSimSeconds = startHour * 3600.0;
            lastHour = Hour;
            lastDay = Day;
            if (mode != TimeMode.Pause) lastRunning = mode;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        //wolamy przed awake czyli jak obiekt jest jeszcze wylaczony
        public void Apply(ClockSettings s)
        {
            if (s == null) return;
            startYear = s.startYear; startMonth = s.startMonth; startDay = s.startDay;
            startHour = s.startHour; mode = s.startMode; tickIntervalSimMinutes = s.tickIntervalSimMinutes;
        }

        public void SetMode(TimeMode m)
        {
            mode = m;
            if (m != TimeMode.Pause) lastRunning = m;
        }

        //o jeden tryb szybciej albo wolniej ale nigdy nie wpadamy w pauze
        public void StepMode(int dir)
        {
            int idx = Mathf.Clamp((int)(mode == TimeMode.Pause ? lastRunning : mode) + dir, 1, ModeScale.Length - 1);
            SetMode((TimeMode)idx);
        }

        void Update()
        {
            if (hotkeys)
            {
                if (Input.GetKeyDown(KeyCode.Space)) SetMode(mode == TimeMode.Pause ? lastRunning : TimeMode.Pause);
                if (Input.GetKeyDown(KeyCode.RightBracket)) StepMode(1);
                if (Input.GetKeyDown(KeyCode.LeftBracket)) StepMode(-1);
            }

            SimDelta = Mathf.Min(Time.deltaTime, 0.1f) * TimeScale;
            if (SimDelta <= 0f) return;

            TotalSimSeconds += SimDelta;

            //ticki co minute z nich pojawiaja sie nowi pacjenci
            minuteAcc += SimDelta;
            int guard = 0;
            while (minuteAcc >= 60f && guard++ < 300)
            {
                minuteAcc -= 60f;
                OnMinute?.Invoke();
            }

            //ticki zdarzen
            float interval = Mathf.Max(1f, tickIntervalSimMinutes * 60f);
            tickAcc += SimDelta;
            guard = 0;
            while (tickAcc >= interval && guard++ < 100)
            {
                tickAcc -= interval;
                TickCount++;
                OnTick?.Invoke(TickCount);
            }

            int h = Hour;
            if (h != lastHour) { lastHour = h; OnHourChanged?.Invoke(h); }
            int d = Day;
            if (d != lastDay) { lastDay = d; OnNewDay?.Invoke(d); }
        }
    }
}
