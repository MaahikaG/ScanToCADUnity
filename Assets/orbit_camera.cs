using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;           // what to orbit around (leave empty for world origin)
    public float orbitSpeed = 300f;
    public float panSpeed = 0.5f;
    public float zoomSpeed = 5f;
    public float distance = 3f;

    private float theta = 0f;          // horizontal angle
    private float phi = 30f;           // vertical angle
    private Vector3 pivot = Vector3.zero;

    void Update()
    {
        if (target != null)
            pivot = target.position;

        // ── Orbit: left mouse drag ────────────────────────────────────────────
        if (Input.GetMouseButton(0))
        {
            theta += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
            phi   -= Input.GetAxis("Mouse Y") * orbitSpeed * Time.deltaTime;
            phi    = Mathf.Clamp(phi, -89f, 89f);
        }

        // ── Pan: middle mouse drag ────────────────────────────────────────────
        if (Input.GetMouseButton(2))
        {
            pivot -= transform.right   * Input.GetAxis("Mouse X") * panSpeed;
            pivot -= transform.up      * Input.GetAxis("Mouse Y") * panSpeed;
        }

        // ── Zoom: scroll wheel ────────────────────────────────────────────────
        distance -= Input.GetAxis("Mouse ScrollWheel") * zoomSpeed;
        distance  = Mathf.Clamp(distance, 0.5f, 50f);

        // ── Apply ─────────────────────────────────────────────────────────────
        Quaternion rotation = Quaternion.Euler(phi, theta, 0f);
        transform.position  = pivot + rotation * new Vector3(0f, 0f, -distance);
        transform.LookAt(pivot);
    }
}