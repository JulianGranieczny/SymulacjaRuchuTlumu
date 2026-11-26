using UnityEngine;

namespace HospitalSim
{
    //polaczenie miedzy pietrami czyli schody albo winda wskazujesz dwa waypointy po jednym na kazdym pietrze
    //jak schody maja podest to robisz kilka takich polaczen dol podest i podest gora
    public class FloorConnector : MonoBehaviour
    {
        public Waypoint a;
        public Waypoint b;
        public ConnectorType type = ConnectorType.Stairs;
        [Tooltip("Mnożnik kosztu trasy. >1 = agenci niechętniej tego używają")]
        public float costMultiplier = 1.5f;
        [Tooltip("Stały koszt w metrach (np. oczekiwanie na windę)")]
        public float extraCost = 0f;

        void Reset()
        {
            costMultiplier = 1.5f;
        }

        void OnValidate()
        {
            //przy zmianie na winde ustawiamy od razu rozsadne domyslne wartosci
            if (type == ConnectorType.Elevator && Mathf.Approximately(costMultiplier, 1.5f) && extraCost == 0f)
            {
                costMultiplier = 1f; extraCost = 8f;
            }
        }

        void OnDrawGizmos()
        {
            if (a == null || b == null) return;
            Gizmos.color = type == ConnectorType.Stairs ? new Color(1f, 0.8f, 0f) : new Color(1f, 0.2f, 1f);
            Gizmos.DrawLine(a.transform.position, b.transform.position);
            Gizmos.DrawWireSphere(a.transform.position, 0.25f);
            Gizmos.DrawWireSphere(b.transform.position, 0.25f);
        }
    }
}
