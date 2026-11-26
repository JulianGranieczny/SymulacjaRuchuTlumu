using System;
using UnityEngine;

namespace HospitalSim
{
    //tworzy z projektu wszystkie obiekty potrzebne do symulacji
    //kazdy skrypt dodajemy do wylaczonego obiektu ustawiamy i dopiero wtedy wlaczamy
    //dzieki temu awake widzi juz ustawienia z pliku projektu
    public static class SimLauncher
    {
        static Transform root;

        static T Spawn<T>(string name, Action<T> configure) where T : Component
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(root, false);
            var c = go.AddComponent<T>();
            configure?.Invoke(c);
            go.SetActive(true);
            return c;
        }

        public static void Launch(ProjectData project, LayoutBaker.Result res, Transform parent, Camera cam, bool withInterface)
        {
            root = parent;
            var L = project.layout;
            var S = project.settings;

            var gen = new GameObject("Sale i waypointy").transform;
            gen.SetParent(parent, false);
            LayoutBaker.Instantiate(res, L, gen);
            foreach (var w in res.warnings) Debug.LogWarning(w);

            Spawn<SimClock>("Zegar", c => c.Apply(S.clock));
            Spawn<PersonDatabase>("Osoby", p => p.Apply(S.people));
            Spawn<FloorViewController>("Widok pięter", f => { f.floorRoots = new GameObject[0]; f.mode = FloorViewController.Mode.OnlyFloor; f.floor = 0; });
            var map = Spawn<HospitalMap>("Mapa", m =>
            {
                m.floorHeight = L.floorHeight;
                m.floorNames = L.floorNames.ToArray();
                m.wallMask = 1;
                m.maxLinkDistance = S.navigation.maxLinkDistance;
                m.linkRadius = S.navigation.linkRadius;
                m.cellSize = S.navigation.wallCellSize;
                m.wallClearance = S.navigation.wallClearance;
                m.bakeHalfHeight = S.navigation.wallCheckHalfHeight;
                m.checkLift = S.navigation.wallCheckHeight - L.waypointHeight;
                m.isPassable = (f, x, z) => L.IsPassageAt(f, x, z);
            });
            if (map.NodeCount == 0) throw new Exception("Mapa nie ma żadnych waypointów.");
            Spawn<AgentSystem>("Agenci", a => a.Configure(S));
            Spawn<RandomEventSystem>("Zdarzenia", e => e.Apply(S.events));
            if (withInterface)
            {
                Spawn<SimHud>("Interfejs symulacji", null);
                Spawn<AgentPicker>("Wybór postaci", p => p.cam = cam);
            }
            root = null;
        }
    }
}
