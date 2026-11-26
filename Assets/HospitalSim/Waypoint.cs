using System.Collections.Generic;
using UnityEngine;

namespace HospitalSim
{
    //punkt grafu nawigacji mozna go postawic recznie w korytarzu drzwiach albo sali ale zwykle robi to generator z siatki
    public class Waypoint : MonoBehaviour
    {
        [System.NonSerialized] public int index;
        [System.NonSerialized] public Room room;

        //ponizsze pola ustawia generator z siatki
        //explicitlinks znaczy ze waypoint laczy sie tylko z tymi z listy links a sale ma podana z gory
        public bool explicitLinks;
        //sala bierze sie z fixedroom a nie z pudelka room
        public bool lockRoom;
        public Room fixedRoom;
        //to jest lozko na oddziale tu kladzie sie pacjent
        public bool isBed;
        public List<Waypoint> links = new List<Waypoint>();

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            if (!explicitLinks) return;
            Gizmos.color = new Color(0f, 0.9f, 1f, 0.35f);
            foreach (var w in links) if (w != null) Gizmos.DrawLine(transform.position, w.transform.position);
        }
    }
}
