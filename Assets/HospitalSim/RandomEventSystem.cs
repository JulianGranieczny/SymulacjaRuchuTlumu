using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace HospitalSim
{
    public enum SimEventKind { Evacuation, Custom }

    [Serializable]
    public class RandomEventDef
    {
        public string name = "zdarzenie";
        public SimEventKind kind = SimEventKind.Custom;
        [Tooltip("Szansa na wystąpienie przy każdym ticku (domyślnie tick = 5 min gry)")]
        [Range(0f, 1f)] public float chancePerTick = RandomEventSystem.DefaultCustomChance;
        [Tooltip("Jak długo zdarzenie trwa w minutach gry. W tym czasie nie może wystąpić żadne inne. Ewakuacja trwa tyle, ile jej własny przebieg")]
        [Min(1f)] public float durationMinutes = 30f;
        public bool enabled = true;
    }

    //przy kazdym ticku losujemy czy cos sie wydarzy
    //naraz moze trwac tylko jedno zdarzenie
    //evacuation to alarm wszyscy uciekaja na miejsce zbiorki
    //custom na razie tylko wypisuje komunikat i odpala oneventtriggered
    public class RandomEventSystem : MonoBehaviour
    {
        public static RandomEventSystem Instance { get; private set; }

        public List<RandomEventDef> events = DefaultEvents();

        //przy ticku co 5 minut zwykle zdarzenie wypada srednio raz na pare dni a ewakuacja raz na kilka tygodni
        public const float DefaultCustomChance = 0.0004f;
        public const float DefaultEvacuationChance = 0.00008f;

        public static List<RandomEventDef> DefaultEvents()
        {
            return new List<RandomEventDef>
            {
                new RandomEventDef { name = "zdarzenie1" },
                new RandomEventDef { name = "zdarzenie2" },
                new RandomEventDef { name = "zdarzenie3" },
                new RandomEventDef { name = "zdarzenie ewakuacja", kind = SimEventKind.Evacuation, chancePerTick = DefaultEvacuationChance },
            };
        }

        //wolamy przed startem symulacji ustawienia ida prosto z pliku projektu
        public void Apply(EventSettings s)
        {
            if (s == null) return;
            seed = s.seed;
            events = ProjectData.Clone(s).events ?? DefaultEvents();
        }
        public bool logEvents = true;
        public uint seed = 999;

        //zdarzenie ktore teraz trwa albo null
        public RandomEventDef ActiveEvent { get; private set; }

        public float ActiveRemainingMinutes
        {
            get
            {
                if (ActiveEvent == null || SimClock.Instance == null) return 0f;
                return Mathf.Max(0f, (float)(activeEndsAt - SimClock.Instance.TotalSimSeconds) / 60f);
            }
        }

        public event Action<RandomEventDef> OnEventTriggered;
        public event Action<RandomEventDef> OnEventEnded;

        Random rng;
        double activeEndsAt;

        void Awake() { Instance = this; }

        void Start()
        {
            rng = new Random(seed == 0 ? 1u : seed);
            if (SimClock.Instance != null) SimClock.Instance.OnTick += HandleTick;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (SimClock.Instance != null) SimClock.Instance.OnTick -= HandleTick;
        }

        void Update()
        {
            if (ActiveEvent == null) return;

            bool ended;
            if (ActiveEvent.kind == SimEventKind.Evacuation)
            {
                var sys = AgentSystem.Instance;
                ended = sys == null || !sys.EvacActive;
            }
            else ended = SimClock.Instance.TotalSimSeconds >= activeEndsAt;

            if (ended) EndActive();
        }

        void HandleTick(int tick)
        {
            if (ActiveEvent != null || events.Count == 0) return;

            //zaczynamy od losowego miejsca na liscie zeby zadne zdarzenie nie bylo uprzywilejowane
            int n = events.Count;
            int start = rng.NextInt(n);
            for (int k = 0; k < n; k++)
            {
                var e = events[(start + k) % n];
                if (!e.enabled) continue;
                if (rng.NextFloat() < e.chancePerTick && TryTrigger(e)) break;
            }
        }

        //reczne wywolanie z przycisku albo z kodu zwraca false jak cos juz trwa
        public bool TriggerManual(int index)
        {
            if (index < 0 || index >= events.Count) return false;
            return TryTrigger(events[index]);
        }

        public bool TryTrigger(RandomEventDef e)
        {
            if (e == null || ActiveEvent != null) return false;

            var sys = AgentSystem.Instance;
            //ewakuacja odpalona z innego miejsca tez blokuje nowe zdarzenia
            if (sys != null && sys.EvacActive) return false;

            if (e.kind == SimEventKind.Evacuation)
            {
                var map = HospitalMap.Instance;
                if (sys == null || map == null || map.AssemblyPoints.Count == 0)
                {
                    Debug.LogWarning("ewakuacja nie moze sie zaczac bo brak sali typu assemblypoint");
                    return false;
                }
                sys.StartEvacuation();
            }

            ActiveEvent = e;
            activeEndsAt = SimClock.Instance.TotalSimSeconds + e.durationMinutes * 60.0;
            if (logEvents) Debug.Log($"wystąpiło {e.name}");
            OnEventTriggered?.Invoke(e);
            return true;
        }

        void EndActive()
        {
            var e = ActiveEvent;
            ActiveEvent = null;
            if (logEvents) Debug.Log($"zakończyło się {e.name}");
            OnEventEnded?.Invoke(e);
        }
    }
}
