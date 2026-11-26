using System;
using UnityEngine;

namespace HospitalSim
{
    //kamera aplikacji ma dwa tryby
    //rzut to widok z gory bez perspektywy ppm albo srodkowy przesuwa kolko przybliza wasd tez przesuwa
    //w rzucie ctrl albo alt z kolkiem zmienia predkosc przesuwania
    //3d to wolna kamera freecam ppm rozglada sie wasd chodzi q i e w dol i w gore a kolko zmienia predkosc
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public enum View { Plan, Free }

        public View Mode { get; private set; } = View.Plan;
        public Camera Cam { get; private set; }
        //zwraca true jak kursor jest nad interfejsem wtedy kamera nie reaguje na mysz
        public Func<bool> overUi;

        FreeCam free;
        Vector3 lastMouse;
        bool dragging;
        float planHeight = 200f;
        //mnoznik predkosci przesuwania w rzucie zmieniany ctrl i kolkiem
        public float panSpeedMultiplier = 1f;
        float speedHintUntil;
        string speedHint;
        GUIStyle hintStyle;

        //krotki napis na dole ekranu po zmianie predkosci kamery
        public void ShowSpeedHint(string text)
        {
            speedHint = text;
            speedHintUntil = Time.unscaledTime + 1.6f;
        }

        void Awake()
        {
            Cam = GetComponent<Camera>();
            free = GetComponent<FreeCam>();
            if (free == null) free = gameObject.AddComponent<FreeCam>();
            free.rig = this;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 3000f;
            SetMode(View.Plan);
        }

        public void SetMode(View m)
        {
            Mode = m;
            if (m == View.Plan)
            {
                free.enabled = false;
                Cam.orthographic = true;
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var p = transform.position;
                transform.position = new Vector3(p.x, planHeight, p.z);
            }
            else
            {
                Cam.orthographic = false;
                Cam.fieldOfView = 60f;
                free.enabled = true;
            }
        }

        public void Toggle() { SetMode(Mode == View.Plan ? View.Free : View.Plan); }

        //ustawia kamere tak zeby bylo widac caly obszar
        public void Frame(Bounds b)
        {
            planHeight = b.max.y + 100f;
            Cam.orthographicSize = Mathf.Max(5f, Mathf.Max(b.size.z, b.size.x / Mathf.Max(0.3f, Cam.aspect)) * 0.6f);
            if (Mode == View.Plan)
                transform.position = new Vector3(b.center.x, planHeight, b.center.z);
            else
            {
                float d = Mathf.Max(b.size.x, b.size.z);
                transform.position = b.center + new Vector3(0f, d * 0.6f, -d * 0.8f);
                transform.rotation = Quaternion.LookRotation(b.center - transform.position);
            }
            free.ResetAngles();
        }

        //przesuwa kamere nad dany punkt i nie rusza przyblizenia
        public void Focus(Vector3 p)
        {
            if (Mode == View.Plan)
                transform.position = new Vector3(p.x, transform.position.y, p.z);
            else
            {
                float dist = Mathf.Clamp(Vector3.Distance(transform.position, p), 6f, 25f);
                Vector3 dir = transform.forward;
                if (dir.y > -0.3f) dir = (dir + Vector3.down * 0.8f).normalized;
                transform.position = p - dir * dist;
                transform.rotation = Quaternion.LookRotation(p - transform.position);
                free.ResetAngles();
            }
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(speedHint) || Time.unscaledTime > speedHintUntil) return;
            if (hintStyle == null) hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(Screen.width * 0.5f - 170f, Screen.height - 110f, 340f, 34f);
            GUI.Box(r, speedHint, hintStyle);
        }

        void LateUpdate()
        {
            if (Mode != View.Plan) return;
            bool ui = overUi != null && overUi();
            bool typing = GUIUtility.keyboardControl != 0;

            if (!ui)
            {
                float scroll = Input.mouseScrollDelta.y;
                bool speedMod = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt);
                if (Mathf.Abs(scroll) > 0.01f && speedMod)
                {
                    //ctrl z kolkiem zmienia predkosc przesuwania
                    panSpeedMultiplier = Mathf.Clamp(panSpeedMultiplier * Mathf.Pow(1.25f, scroll), 0.1f, 10f);
                    ShowSpeedHint("Prędkość przesuwania: " + panSpeedMultiplier.ToString("0.0") + "x");
                }
                else if (Mathf.Abs(scroll) > 0.01f)
                {
                    //przyblizamy do miejsca pod kursorem a nie do srodka ekranu
                    Vector3 before = Cam.ScreenToWorldPoint(Input.mousePosition);
                    Cam.orthographicSize = Mathf.Clamp(Cam.orthographicSize * Mathf.Pow(0.88f, scroll), 2f, 2000f);
                    Vector3 after = Cam.ScreenToWorldPoint(Input.mousePosition);
                    transform.position += new Vector3(before.x - after.x, 0f, before.z - after.z);
                }
                if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) { dragging = true; lastMouse = Input.mousePosition; }
            }
            if (!Input.GetMouseButton(1) && !Input.GetMouseButton(2)) dragging = false;
            if (dragging)
            {
                Vector3 d = Input.mousePosition - lastMouse;
                float k = Cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                transform.position -= new Vector3(d.x * k, 0f, d.y * k);
                lastMouse = Input.mousePosition;
            }

            if (!typing)
            {
                float speed = Cam.orthographicSize * 1.2f * panSpeedMultiplier * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f);
                float h = 0f, v = 0f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
                transform.position += new Vector3(h, 0f, v) * (speed * Time.unscaledDeltaTime);
            }
        }

        //punkt na poziomej plaszczyznie y ktory jest pod kursorem
        public bool MouseOnPlane(float y, out Vector3 hit)
        {
            var ray = Cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            float t;
            if (plane.Raycast(ray, out t)) { hit = ray.GetPoint(t); return true; }
            hit = Vector3.zero;
            return false;
        }
    }
}
