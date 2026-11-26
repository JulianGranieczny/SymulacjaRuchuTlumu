using System;
using System.Collections.Generic;

namespace HospitalSim
{
    //polskie nazwy typow sal specjalizacji i pol ustawien ktore widac w programie
    public static class Labels
    {
        public static string Room(RoomType t)
        {
            switch (t)
            {
                case RoomType.Corridor: return "Korytarz";
                case RoomType.Entrance: return "Wejście";
                case RoomType.Exit: return "Wyjście";
                case RoomType.WaitingRoom: return "Poczekalnia";
                case RoomType.Ward: return "Oddział";
                case RoomType.StaffRoom: return "Dyżurka";
                case RoomType.AssemblyPoint: return "Miejsce zbiórki";
                case RoomType.Door: return "Drzwi";
                case RoomType.Stairs: return "Schody";
                case RoomType.Elevator: return "Winda";
                default: return "Inne";
            }
        }

        public static string RoomHint(RoomType t)
        {
            switch (t)
            {
                case RoomType.Corridor: return "Korytarze i hole. Tędy chodzą wszyscy.";
                case RoomType.Entrance: return "Miejsce, w którym pojawiają się nowi pacjenci i odwiedzający.";
                case RoomType.Exit: return "Tu wychodzą wyleczeni pacjenci i odwiedzający.";
                case RoomType.WaitingRoom: return "Pacjenci czekają tu na wolne łóżko.";
                case RoomType.Ward: return "Sala z łóżkami. Pojemność = liczba łóżek. Lekarze są przypisani do oddziałów.";
                case RoomType.StaffRoom: return "Lekarze odpoczywają tu między wizytami.";
                case RoomType.AssemblyPoint: return "Miejsce zbiórki przy ewakuacji (zwykle na zewnątrz).";
                case RoomType.Door: return "Przejście między dwiema salami. Maluj w otworach ścian. Bez drzwi sale nie są połączone.";
                case RoomType.Stairs: return "Schody. Bieg z kierunkiem maluj tylko na niższym piętrze, a schody 'pionowo' w tym samym miejscu na sąsiednich piętrach.";
                case RoomType.Elevator: return "Winda. Maluj w tym samym miejscu na sąsiednich piętrach.";
                default: return "Pomieszczenie, do którego chodzą tylko sprzątacze.";
            }
        }

        public static string Spec(Specialty s)
        {
            switch (s)
            {
                case Specialty.Cardiology: return "Kardiologia";
                case Specialty.Neurology: return "Neurologia";
                case Specialty.Orthopedics: return "Ortopedia";
                case Specialty.Pediatrics: return "Pediatria";
                case Specialty.Oncology: return "Onkologia";
                case Specialty.Surgery: return "Chirurgia";
                case Specialty.Emergency: return "SOR";
                default: return "Interna";
            }
        }

        public static string Stairs(StairsDir d)
        {
            switch (d)
            {
                case StairsDir.North: return "w górę planu (Z+)";
                case StairsDir.South: return "w dół planu (Z-)";
                case StairsDir.East: return "w prawo (X+)";
                case StairsDir.West: return "w lewo (X-)";
                default: return "pionowo (ta sama komórka)";
            }
        }

        public static string Enum(object v)
        {
            if (v is StairsDir) return Stairs((StairsDir)v);
            if (v is RoomType) return Room((RoomType)v);
            if (v is Specialty) return Spec((Specialty)v);
            if (v is TimeMode) return SimClock.ModeLabel[(int)(TimeMode)v];
            if (v is Sex) return (Sex)v == Sex.Female ? "Kobieta" : "Mężczyzna";
            if (v is SimEventKind) return (SimEventKind)v == SimEventKind.Evacuation ? "Ewakuacja" : "Zwykłe (komunikat)";
            return v.ToString();
        }

        static readonly Dictionary<string, string> fields = new Dictionary<string, string>
        {
            {"clock", "Czas i data"}, {"population", "Populacja"}, {"arrivals", "Napływ ludzi"}, {"health", "Zdrowie"},
            {"treatment", "Leczenie"}, {"movement", "Ruch"}, {"collisions", "Kolizje"}, {"routine", "Rutyna dnia"},
            {"evacuation", "Ewakuacja"}, {"events", "Zdarzenia"}, {"people", "Imiona i wiek"}, {"appearance", "Wygląd"},
            {"navigation", "Nawigacja i ściany"},

            {"startYear", "Rok startu"}, {"startMonth", "Miesiąc startu"}, {"startDay", "Dzień startu"}, {"startHour", "Godzina startu"},
            {"startMode", "Prędkość czasu na starcie"}, {"tickIntervalSimMinutes", "Tick zdarzeń co (min gry)"},

            {"doctors", "Lekarze"}, {"patients", "Pacjenci (maks. naraz)"}, {"visitors", "Odwiedzający (maks. naraz)"}, {"cleaners", "Sprzątacze"},
            {"seed", "Ziarno losowania"}, {"doctorsStartInOwnRoom", "Lekarze zaczynają na swoich oddziałach"},
            {"patientsAlreadyInside", "Pacjenci w środku na starcie (część)"}, {"visitorsAlreadyInside", "Odwiedzający w środku na starcie (część)"},

            {"patientsPerHour", "Nowi pacjenci na godzinę"}, {"visitorsPerHour", "Nowi odwiedzający na godzinę"},
            {"patientsSpawnInWaitingRoom", "Pacjenci pojawiają się od razu w poczekalni"}, {"visitorsSpawnInWaitingRoom", "Odwiedzający pojawiają się od razu w poczekalni"},

            {"maxHp", "Maksymalne HP"}, {"patientHpLossPerHour", "Utrata HP na godzinę czekania"}, {"severityRange", "Losowy rozrzut utraty HP (od-do)"},
            {"stageChance", "Częstość stopni choroby (wagi)"}, {"stageHpLoss", "Utrata HP dla stopnia choroby (mnożnik)"},
            {"stageDuration", "Czas leczenia dla stopnia choroby (min gry)"},
            {"stage1", "Stopień 1 (lekki)"}, {"stage2", "Stopień 2 (umiarkowany)"}, {"stage3", "Stopień 3 (poważny)"},
            {"stage4", "Stopień 4 (ciężki)"}, {"stage5", "Stopień 5 (krytyczny)"},
            {"visitorMinStage", "Odwiedziny od stopnia choroby"}, {"visitorsPerPatient", "Maks. odwiedzających na pacjenta"},
            {"initialMinHpFraction", "Min. HP pacjentów na starcie (część)"},

            {"durationMinutes", "Czas (min gry)"}, {"otherDoctorsCanHelp", "Lekarze z innych oddziałów pomagają"},

            {"walkSpeed", "Prędkość chodu (m/s)"}, {"speedVariation", "Rozrzut prędkości"}, {"roleSpeed", "Mnożnik prędkości dla roli"},
            {"globalSpeedMultiplier", "Mnożnik prędkości wszystkich"}, {"offsetRadius", "Rozrzut wokół waypointa (m)"},

            {"agentCollisions", "Agenci nie wchodzą na siebie"}, {"agentRadius", "Promień agenta (m)"}, {"separationStrength", "Siła rozsuwania"},
            {"maxPushPerFrame", "Maks. przesunięcie na klatkę (m)"}, {"maxNeighbors", "Maks. sąsiadów (wydajność)"}, {"gridCellSize", "Komórka siatki agentów (m)"},

            {"openHour", "Otwarcie (godz.)"}, {"closeHour", "Zamknięcie (godz.)"}, {"doctorOwnWardStay", "Lekarz na swoim oddziale (min)"},
            {"doctorOtherWardStay", "Lekarz na innym oddziale (min)"}, {"doctorStaffRoomStay", "Lekarz w dyżurce (min)"},
            {"doctorVisitOtherWardChance", "Szansa wizyty lekarza na innym oddziale"}, {"doctorsStayOnOwnFloor", "Lekarze zostają na swoim piętrze"},
            {"visitorWaitingRoomStay", "Odwiedzający w poczekalni (min)"}, {"visitorWardStay", "Odwiedzający na oddziale (min)"},
            {"cleanerRoomStay", "Sprzątacz w jednej sali (min)"}, {"cleanersStayOnOwnFloor", "Sprzątacze zostają na swoim piętrze"},

            {"speedMultiplier", "Mnożnik prędkości ucieczki"}, {"maxDurationMinutes", "Maks. czas ewakuacji (min)"}, {"returnDelayMinutes", "Powrót personelu po (min)"},

            {"name", "Nazwa"}, {"kind", "Rodzaj"}, {"chancePerTick", "Szansa przy każdym ticku"}, {"enabled", "Włączone"},

            {"firstNames", "Imiona"}, {"surnames", "Nazwiska"}, {"sex", "Płeć"}, {"masculine", "Forma męska"}, {"feminine", "Forma żeńska"},
            {"doctorAge", "Wiek lekarzy"}, {"patientAge", "Wiek pacjentów"}, {"visitorAge", "Wiek odwiedzających"}, {"cleanerAge", "Wiek sprzątaczy"},
            {"min", "od"}, {"max", "do"},

            {"roleColors", "Kolory (lekarz, pacjent, odwiedzający, sprzątacz)"}, {"roleScale", "Wielkość kulki (m)"},
            {"doctor", "lekarz"}, {"patient", "pacjent"}, {"visitor", "odwiedzający"}, {"cleaner", "sprzątacz"},

            {"floors", "Liczba pięter"}, {"floorHeight", "Wysokość piętra (m)"}, {"groundY", "Wysokość podłogi parteru (m)"},
            {"cutHeight", "Wysokość przekroju nad podłogą (m)"}, {"cellSize", "Rozmiar komórki siatki (m)"},
            {"startX", "Początek siatki X (m)"}, {"startZ", "Początek siatki Z (m)"}, {"sizeX", "Szerokość siatki (m)"}, {"sizeZ", "Głębokość siatki (m)"},
            {"fitMargin", "Zapas wokół budynku przy dopasowaniu (m)"},
            {"waypointSpacing", "Odstęp waypointów (m)"}, {"wardWaypointSpacing", "Odstęp waypointów na oddziałach (m)"},
            {"waypointHeight", "Wysokość waypointów nad podłogą (m)"},

            {"wallCellSize", "Dokładność mapy ścian (m)"}, {"wallClearance", "Odstęp od ścian (m)"}, {"wallCheckHalfHeight", "Pół wysokości szukania ścian (m)"}, {"wallCheckHeight", "Wysokość szukania ścian nad podłogą (m)"},
            {"maxLinkDistance", "Zasięg łączenia ręcznych waypointów (m)"}, {"linkRadius", "Promień przy sprawdzaniu połączeń (m)"},
        };

        public static string Field(string fieldName)
        {
            string s;
            if (fields.TryGetValue(fieldName, out s)) return s;
            //jak nie ma tlumaczenia to przynajmniej rozbijamy camelcase na slowa
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < fieldName.Length; i++)
            {
                char c = fieldName[i];
                if (i > 0 && char.IsUpper(c)) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
