using UnityEngine;

namespace HospitalSim
{
    //zwykla wolna kamera trzymasz ppm i sie rozgladasz wasd chodzisz a q i e w dol i w gore
    //shift przyspiesza ctrl zwalnia a kolkiem zmieniasz predkosc
    [RequireComponent(typeof(Camera))]
    public class FreeCam : MonoBehaviour
    {
        public float moveSpeed = 15f;
        public float fastMultiplier = 3f;
        public float slowMultiplier = 0.25f;
        public float lookSensitivity = 2.5f;
        //jak kamera aplikacji jest podpieta to pokazujemy napis z predkoscia i wiemy czy kursor jest nad interfejsem
        [System.NonSerialized] public CameraRig rig;

        float yaw, pitch;

        void Start() { ResetAngles(); }

        //jak ktos ustawi kamere z zewnatrz to trzeba odczytac katy na nowo
        public void ResetAngles()
        {
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void OnEnable() { ResetAngles(); }

        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        void Update()
        {
            if (Input.GetMouseButtonDown(1)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Input.GetMouseButtonUp(1)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            //kolko zmienia predkosc ale nie wtedy gdy przewijasz jakis panel
            float scroll = Input.mouseScrollDelta.y;
            bool overUi = rig != null && rig.overUi != null && rig.overUi();
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
            {
                moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(1.25f, scroll), 0.5f, 300f);
                if (rig != null) rig.ShowSpeedHint("Prędkość kamery: " + moveSpeed.ToString(moveSpeed < 10f ? "0.0" : "0") + " m/s");
            }

            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift)) speed *= fastMultiplier;
            if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier;

            if (GUIUtility.keyboardControl != 0) return;
            Vector3 dir = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;
            transform.position += dir * (speed * Time.unscaledDeltaTime);
        }
    }
}
