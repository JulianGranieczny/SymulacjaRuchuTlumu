using System;
using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    public enum Sex : byte { Male, Female }

    [Serializable]
    public struct FirstNameEntry
    {
        public string name;
        public Sex sex;
        public FirstNameEntry(string n, Sex s) { name = n; sex = s; }
    }

    [Serializable]
    public struct SurnameEntry
    {
        [Tooltip("Forma dla mężczyzn")] public string masculine;
        [Tooltip("Forma dla kobiet (puste = taka sama jak męska)")] public string feminine;
        public SurnameEntry(string m, string f) { masculine = m; feminine = f; }
    }

    [Serializable]
    public struct AgeRange
    {
        [Min(0)] public int min;
        [Min(0)] public int max;
        public AgeRange(int a, int b) { min = a; max = b; }
    }

    public struct PersonData
    {
        public string first, last;
        public Sex sex;
        public int age;
        public string SexLabel => sex == Sex.Female ? "Kobieta" : "Mężczyzna";
    }

    //listy imion kazde z plcia nazwisk w formie meskiej i zenskiej i zakresy wieku dla rol
    //dane postaci losujemy dopiero jak ktos kliknie i zawsze z numeru agenta i seeda
    //dzieki temu nic nie zajmuje pamieci a po kolejnym kliknieciu ta sama osoba ma te same dane
    //listy mozna edytowac w inspectorze dodawac usuwac i zmieniac plec
    public class PersonDatabase : MonoBehaviour
    {
        public static PersonDatabase Instance { get; private set; }

        [Tooltip("Zmień, żeby wylosować inne osoby")]
        public uint seed = 1;

        [Header("Imiona (każde ma płeć)")]
        public List<FirstNameEntry> firstNames = DefaultFirstNames();

        [Header("Nazwiska (z formą żeńską)")]
        public List<SurnameEntry> surnames = DefaultSurnames();

        [Header("Zakresy wieku")]
        public AgeRange doctorAge = new AgeRange(27, 65);
        public AgeRange patientAge = new AgeRange(1, 92);
        public AgeRange visitorAge = new AgeRange(16, 80);
        public AgeRange cleanerAge = new AgeRange(20, 65);

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        //wolamy przed startem symulacji ustawienia ida prosto z pliku projektu
        public void Apply(PeopleSettings s)
        {
            if (s == null) return;
            seed = s.seed;
            if (s.firstNames != null && s.firstNames.Count > 0) firstNames = new List<FirstNameEntry>(s.firstNames);
            if (s.surnames != null && s.surnames.Count > 0) surnames = new List<SurnameEntry>(s.surnames);
            doctorAge = s.doctorAge; patientAge = s.patientAge; visitorAge = s.visitorAge; cleanerAge = s.cleanerAge;
        }

        [ContextMenu("Przywróć domyślne imiona i nazwiska")]
        void RestoreDefaults()
        {
            firstNames = DefaultFirstNames();
            surnames = DefaultSurnames();
        }

        AgeRange RangeFor(Role r)
        {
            switch (r)
            {
                case Role.Doctor: return doctorAge;
                case Role.Patient: return patientAge;
                case Role.Visitor: return visitorAge;
                default: return cleanerAge;
            }
        }

        public PersonData Generate(int id, Role role)
        {
            int s;
            unchecked { s = (int)(seed * 2654435761u) ^ (id * 7919 + 17); }
            var r = new System.Random(s);

            var p = new PersonData();
            if (firstNames != null && firstNames.Count > 0)
            {
                var e = firstNames[r.Next(firstNames.Count)];
                p.first = e.name; p.sex = e.sex;
            }
            else { p.first = "Jan"; p.sex = Sex.Male; }

            if (surnames != null && surnames.Count > 0)
            {
                var e = surnames[r.Next(surnames.Count)];
                p.last = (p.sex == Sex.Female && !string.IsNullOrEmpty(e.feminine)) ? e.feminine : e.masculine;
            }
            else p.last = "Nowak";

            var ar = RangeFor(role);
            int lo = Mathf.Min(ar.min, ar.max), hi = Mathf.Max(ar.min, ar.max);
            p.age = r.Next(lo, hi + 1);
            return p;
        }

        public static List<FirstNameEntry> DefaultFirstNames()
        {
            var l = new List<FirstNameEntry>();
            foreach (var n in new[] { "Jan", "Piotr", "Krzysztof", "Andrzej", "Tomasz", "Paweł", "Michał", "Marcin", "Marek", "Adam", "Łukasz", "Jakub", "Stanisław", "Robert", "Rafał" })
                l.Add(new FirstNameEntry(n, Sex.Male));
            foreach (var n in new[] { "Anna", "Maria", "Katarzyna", "Małgorzata", "Agnieszka", "Barbara", "Ewa", "Magdalena", "Joanna", "Zofia", "Monika", "Aleksandra", "Natalia", "Dorota", "Julia" })
                l.Add(new FirstNameEntry(n, Sex.Female));
            return l;
        }

        public static List<SurnameEntry> DefaultSurnames()
        {
            return new List<SurnameEntry>
            {
                new SurnameEntry("Kowalski", "Kowalska"), new SurnameEntry("Nowak", "Nowak"),
                new SurnameEntry("Wiśniewski", "Wiśniewska"), new SurnameEntry("Wójcik", "Wójcik"),
                new SurnameEntry("Kowalczyk", "Kowalczyk"), new SurnameEntry("Kamiński", "Kamińska"),
                new SurnameEntry("Lewandowski", "Lewandowska"), new SurnameEntry("Zieliński", "Zielińska"),
                new SurnameEntry("Szymański", "Szymańska"), new SurnameEntry("Woźniak", "Woźniak"),
                new SurnameEntry("Dąbrowski", "Dąbrowska"), new SurnameEntry("Kozłowski", "Kozłowska"),
                new SurnameEntry("Jankowski", "Jankowska"), new SurnameEntry("Mazur", "Mazur"),
                new SurnameEntry("Krawczyk", "Krawczyk"), new SurnameEntry("Piotrowski", "Piotrowska"),
                new SurnameEntry("Grabowski", "Grabowska"), new SurnameEntry("Pawłowski", "Pawłowska"),
                new SurnameEntry("Michalski", "Michalska"), new SurnameEntry("Król", "Król"),
            };
        }
    }
}
