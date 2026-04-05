using UnityEditor;
using UnityEngine;

// SUBSCRIBER FOR LIDAR
public class VoxelSceneWindow : EditorWindow
{
    private Camera voxelCamera;
    private GameObject cameraObject;
    private Vector3 cameraPosition = new Vector3(0, 5, -10);
    private Vector3 cameraRotation = new Vector3(15, 0, 0);
    private float cameraDistance = 10f;
    private Vector3 pivot = Vector3.zero;
    
    // Mouse interaction
    private Vector2 lastMousePosition;
    private bool isDragging = false;
    private bool isPanning = false;
    
    [MenuItem("Window/Voxel 3D Scene Viewer")]
    public static void ShowWindow()
    {
        GetWindow<VoxelSceneWindow>("Voxel 3D Scene");
    }
    
    void OnEnable()
    {
        SetupVoxelCamera();
    }
    
    void OnDisable()
    {
        if (cameraObject != null)
        {
            DestroyImmediate(cameraObject);
        }
    }
    
    void SetupVoxelCamera()
    {
        // Create a temporary camera for this window
        cameraObject = new GameObject("VoxelSceneWindowCamera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        
        voxelCamera = cameraObject.AddComponent<Camera>();
        voxelCamera.clearFlags = CameraClearFlags.SolidColor;
        voxelCamera.backgroundColor = Color.black;
        voxelCamera.fieldOfView = 60f;
        voxelCamera.nearClipPlane = 0.1f;
        voxelCamera.farClipPlane = 1000f;
        
        // Set the camera to only render the VoxelLayer
        // Find the layer number for "VoxelLayer"
        int voxelLayerIndex = LayerMask.NameToLayer("VoxelLayer");
        if (voxelLayerIndex == -1)
        {
            Debug.LogWarning("VoxelLayer not found. Please create a layer named 'VoxelLayer'");
            voxelCamera.cullingMask = -1; // Show all layers as fallback
        }
        else
        {
            voxelCamera.cullingMask = 1 << voxelLayerIndex;
            Debug.Log($"VoxelSceneWindow: Set camera to only show layer {voxelLayerIndex} (VoxelLayer). Culling mask: {voxelCamera.cullingMask}");
        }
        
        UpdateCameraTransform();
    }
    
    void UpdateCameraTransform()
    {
        if (voxelCamera == null) return;
        
        // Convert spherical coordinates to world position
        Vector3 offset = new Vector3(
            Mathf.Sin(cameraRotation.y * Mathf.Deg2Rad) * Mathf.Cos(cameraRotation.x * Mathf.Deg2Rad),
            Mathf.Sin(cameraRotation.x * Mathf.Deg2Rad),
            Mathf.Cos(cameraRotation.y * Mathf.Deg2Rad) * Mathf.Cos(cameraRotation.x * Mathf.Deg2Rad)
        ) * cameraDistance;
        
        voxelCamera.transform.position = pivot + offset;
        voxelCamera.transform.LookAt(pivot);
    }
    
    void OnGUI()
    {
        if (voxelCamera == null)
        {
            EditorGUILayout.HelpBox("Voxel Camera not initialized", MessageType.Warning);
            if (GUILayout.Button("Initialize Camera"))
            {
                SetupVoxelCamera();
            }
            return;
        }
        
        // Handle mouse input for camera controls
        HandleMouseInput();
        
        // Draw camera controls
        DrawCameraControls();
        
        // Get the rect for the 3D view (leave space for controls at top)
        Rect viewRect = new Rect(0, 80, position.width, position.height - 80);
        
        // Create a render texture if needed
        if (voxelCamera.targetTexture == null || 
            voxelCamera.targetTexture.width != (int)viewRect.width || 
            voxelCamera.targetTexture.height != (int)viewRect.height)
        {
            if (voxelCamera.targetTexture != null)
            {
                voxelCamera.targetTexture.Release();
            }
            
            voxelCamera.targetTexture = new RenderTexture((int)viewRect.width, (int)viewRect.height, 24);
            voxelCamera.targetTexture.Create();
        }
        
        // Render the camera
        voxelCamera.Render();
        
        // Display the rendered view
        GUI.DrawTexture(viewRect, voxelCamera.targetTexture, ScaleMode.StretchToFill);
        
        // Draw overlay controls
        DrawOverlayControls(viewRect);
        
        // Repaint continuously for smooth interaction
        Repaint();
    }
    
    void DrawCameraControls()
    {
        GUILayout.Label("Voxel Layer 3D Viewer", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Distance:", GUILayout.Width(60));
        cameraDistance = EditorGUILayout.Slider(cameraDistance, 1f, 100f);
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Pivot:", GUILayout.Width(60));
        pivot = EditorGUILayout.Vector3Field("", pivot);
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset View"))
        {
            cameraRotation = new Vector3(15, 0, 0);
            cameraDistance = 10f;
            pivot = Vector3.zero;
        }
        if (GUILayout.Button("Focus on Particles"))
        {
            FocusOnVoxelObjects();
        }
        GUILayout.EndHorizontal();
        
        UpdateCameraTransform();
    }
    
    void DrawOverlayControls(Rect viewRect)
    {
        // Draw controls overlay
        GUILayout.BeginArea(new Rect(viewRect.x + 10, viewRect.y + 10, 200, 100));
        GUILayout.BeginVertical("box");
        GUILayout.Label("Controls:", EditorStyles.boldLabel);
        GUILayout.Label("• Left Mouse: Orbit");
        GUILayout.Label("• Right Mouse: Pan");
        GUILayout.Label("• Scroll: Zoom");
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    void HandleMouseInput()
    {
        Event e = Event.current;
        
        if (e.type == EventType.MouseDown)
        {
            if (e.button == 0) // Left mouse button
            {
                isDragging = true;
                lastMousePosition = e.mousePosition;
            }
            else if (e.button == 1) // Right mouse button
            {
                isPanning = true;
                lastMousePosition = e.mousePosition;
            }
        }
        else if (e.type == EventType.MouseUp)
        {
            isDragging = false;
            isPanning = false;
        }
        else if (e.type == EventType.MouseDrag)
        {
            Vector2 mouseDelta = e.mousePosition - lastMousePosition;
            
            if (isDragging)
            {
                // Orbit around pivot
                cameraRotation.y += mouseDelta.x * 0.5f;
                cameraRotation.x -= mouseDelta.y * 0.5f;
                cameraRotation.x = Mathf.Clamp(cameraRotation.x, -89f, 89f);
            }
            else if (isPanning)
            {
                // Pan the pivot point
                Vector3 right = voxelCamera.transform.right;
                Vector3 up = voxelCamera.transform.up;
                float panSpeed = cameraDistance * 0.001f;
                
                pivot -= right * mouseDelta.x * panSpeed;
                pivot += up * mouseDelta.y * panSpeed;
            }
            
            lastMousePosition = e.mousePosition;
        }
        else if (e.type == EventType.ScrollWheel)
        {
            // Zoom
            float zoomSpeed = cameraDistance * 0.1f;
            cameraDistance += e.delta.y * zoomSpeed;
            cameraDistance = Mathf.Clamp(cameraDistance, 0.5f, 100f);
        }
    }
    
    void FocusOnVoxelObjects()
    {
        // Find all objects on the Voxel Layer and center the view on them
        int voxelLayerIndex = LayerMask.NameToLayer("VoxelLayer");
        if (voxelLayerIndex == -1) return;
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == voxelLayerIndex && obj.activeInHierarchy)
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
        }
        
        if (hasBounds)
        {
            pivot = bounds.center;
            cameraDistance = Mathf.Max(bounds.size.magnitude, 5f);
        }
    }
    
    void OnInspectorUpdate()
    {
        Repaint();
    }
}
