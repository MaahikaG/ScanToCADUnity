using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;

public class VoxelRenderer : MonoBehaviour
{
    ROSConnection ros;
    public string topicName         = "/point_cloud";
    public string scanCompleteTopic = "/scan_complete";
    public string exportTopic       = "/run_parsenet";

    // Local shared folder — must match BASE_DIR in run_pipeline.py
    public string scanToCADFolder = "/Users/maahikagupta/ScanToCAD";

    // Python executable — matches your homebrew install
    public string pythonPath = "/opt/homebrew/bin/python3.12";

    ParticleSystem system;
    private List<ParticleSystem.Particle> particleList = new List<ParticleSystem.Particle>();
    private List<Vector3> originalPoints = new List<Vector3>();
    bool voxelsUpdated = false;

    public float voxelScale = 0.1f;
    public float scale      = 1f;

    // UI
    private Canvas canvas;
    private Button exportButton;
    private Text   exportLabel;

    // CAD model display
    private GameObject cadModelObject;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PointCloud2Msg>(topicName, OnPointCloudReceived);
        ros.RegisterPublisher<BoolMsg>(scanCompleteTopic);
        ros.RegisterPublisher<BoolMsg>(exportTopic);

        system = GetComponent<ParticleSystem>();
        var main = system.main;
        main.maxParticles      = 500000;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.playOnAwake       = false;
        main.loop              = false;
        var emission = system.emission;
        emission.rateOverTime  = 0;

        CreateExportButton();
    }

    // ─────────────────────────────────────────────────────────────────────────
    void CreateExportButton()
    {
        GameObject canvasObj = new GameObject("ExportCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject buttonObj = new GameObject("ExportButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        exportButton = buttonObj.AddComponent<Button>();

        Image img = buttonObj.AddComponent<Image>();
        img.color = new Color(0.2f, 0.8f, 0.2f);

        RectTransform rt = buttonObj.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 20f);
        rt.sizeDelta        = new Vector2(200f, 50f);

        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(buttonObj.transform, false);
        exportLabel = textObj.AddComponent<Text>();
        exportLabel.text      = "Export to CAD";
        exportLabel.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        exportLabel.alignment = TextAnchor.MiddleCenter;
        exportLabel.color     = Color.white;
        exportLabel.fontSize  = 18;
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        exportButton.gameObject.SetActive(true);
        exportButton.onClick.AddListener(OnExportClicked);
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnExportClicked()
    {
        UnityEngine.Debug.Log("Export button clicked");
        exportLabel.text          = "Saving scan...";
        exportButton.interactable = false;

        BoolMsg scanComplete = new BoolMsg();
        scanComplete.data = true;
        ros.Publish(scanCompleteTopic, scanComplete);

        BoolMsg trigger = new BoolMsg();
        trigger.data = true;
        ros.Publish(exportTopic, trigger);

        ClearPointCloud();
        
        StartCoroutine(SaveAndPollLocally());
    }

    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator SaveAndPollLocally()
    {
        // ── 1. Build PCD string ───────────────────────────────────────────────
        int n = originalPoints.Count;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# .PCD v0.7");
        sb.AppendLine("VERSION 0.7");
        sb.AppendLine("FIELDS x y z");
        sb.AppendLine("SIZE 4 4 4");
        sb.AppendLine("TYPE F F F");
        sb.AppendLine("COUNT 1 1 1");
        sb.AppendLine($"WIDTH {n}");
        sb.AppendLine("HEIGHT 1");
        sb.AppendLine("VIEWPOINT 0 0 0 1 0 0 0");
        sb.AppendLine($"POINTS {n}");
        sb.AppendLine("DATA ascii");
        foreach (var p in originalPoints)
            sb.AppendLine($"{p.x:F6} {p.y:F6} {p.z:F6}");

        // ── 2. Write PCD to shared folder ─────────────────────────────────────
        string pcdPath    = Path.Combine(scanToCADFolder, "latest.pcd");
        string readyPath  = Path.Combine(scanToCADFolder, "output_ready.txt");
        string objPath    = Path.Combine(scanToCADFolder, "cad_output.obj");
        string scriptPath = Path.Combine(scanToCADFolder, "run_pipeline.py");

        Directory.CreateDirectory(scanToCADFolder);

        // Delete stale files from previous run
        if (File.Exists(readyPath)) File.Delete(readyPath);
        if (File.Exists(objPath))   File.Delete(objPath);

        File.WriteAllText(pcdPath, sb.ToString());
        UnityEngine.Debug.Log($"Saved {n} points to {pcdPath}");

        // ── 3. Launch run_pipeline.py automatically ───────────────────────────
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName               = pythonPath,
            Arguments              = "-u " + scriptPath,  // -u forces unbuffered output
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        Process proc = Process.Start(psi);
        UnityEngine.Debug.Log("Pipeline launched, PID: " + proc.Id);
        exportLabel.text = "Processing...";

        // Log stdout and stderr from the pipeline to Unity Console
        proc.OutputDataReceived += (sender, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                UnityEngine.Debug.Log("[Pipeline] " + args.Data);
        };
        proc.ErrorDataReceived += (sender, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                UnityEngine.Debug.LogWarning("[Pipeline] " + args.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        System.Threading.Tasks.Task.Run(() => {
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                UnityEngine.Debug.LogError($"[Pipeline] Process exited with code {proc.ExitCode} — pipeline failed.");
            else
                UnityEngine.Debug.Log("[Pipeline] Process completed successfully.");
        });

        // ── 4. Poll for output_ready.txt ──────────────────────────────────────
        UnityEngine.Debug.Log("Polling for pipeline output...");
        while (!File.Exists(readyPath))
        {
            yield return new WaitForSeconds(2f);
        }

        UnityEngine.Debug.Log("output_ready.txt found — loading CAD model...");
        exportLabel.text = "Loading CAD model...";

        // ── 5. Wait one extra second for file to finish writing ───────────────
        yield return new WaitForSeconds(1f);

        // ── 6. Load and display the OBJ ───────────────────────────────────────
        if (!File.Exists(objPath))
        {
            UnityEngine.Debug.LogError($"cad_output.obj not found at {objPath}");
            exportLabel.text          = "Error: OBJ not found";
            exportButton.interactable = true;
            yield break;
        }

        string objText = File.ReadAllText(objPath);
        Mesh mesh = ParseOBJ(objText);

        if (cadModelObject != null)
            Destroy(cadModelObject);

        cadModelObject = new GameObject("CADModel");
        cadModelObject.AddComponent<MeshFilter>().mesh = mesh;

        var meshRenderer = cadModelObject.AddComponent<MeshRenderer>();
        meshRenderer.material       = new Material(Shader.Find("Standard"));
        meshRenderer.material.color = new Color(0.8f, 0.85f, 0.95f);

        // Position at centroid of point cloud
        Vector3 centroid = Vector3.zero;
        foreach (var pt in originalPoints) centroid += pt;
        centroid /= originalPoints.Count;
        cadModelObject.transform.position = centroid;

        exportLabel.text          = "CAD model loaded!";
        exportButton.interactable = true;
        UnityEngine.Debug.Log("CAD model displayed successfully.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    Mesh ParseOBJ(string objText)
    {
        var vertices  = new List<Vector3>();
        var triangles = new List<int>();

        foreach (string rawLine in objText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] parts = line.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0] == "v" && parts.Length >= 4)
            {
                vertices.Add(new Vector3(
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)
                ));
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                int a = int.Parse(parts[1].Split('/')[0]) - 1;
                int b = int.Parse(parts[2].Split('/')[0]) - 1;
                int c = int.Parse(parts[3].Split('/')[0]) - 1;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);

                // Handle quads
                if (parts.Length >= 5)
                {
                    int d = int.Parse(parts[4].Split('/')[0]) - 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices    = vertices.ToArray();
        mesh.triangles   = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (voxelsUpdated)
        {
            var arr = particleList.ToArray();
            system.SetParticles(arr, arr.Length);
            voxelsUpdated = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    public static Vector3[] ExtractPoints(PointCloud2Msg msg, out Color[] colors)
    {
        int pointCount = (int)msg.width * (int)msg.height;
        Vector3[] positions = new Vector3[pointCount];
        colors = new Color[pointCount];

        int offsetX = -1, offsetY = -1, offsetZ = -1;
        int offsetR = -1, offsetG = -1, offsetB = -1;

        foreach (var field in msg.fields)
        {
            switch (field.name)
            {
                case "x": offsetX = (int)field.offset; break;
                case "y": offsetY = (int)field.offset; break;
                case "z": offsetZ = (int)field.offset; break;
                case "r": offsetR = (int)field.offset; break;
                case "g": offsetG = (int)field.offset; break;
                case "b": offsetB = (int)field.offset; break;
            }
        }

        byte[] raw  = msg.data;
        int    step = (int)msg.point_step;

        for (int i = 0; i < pointCount; i++)
        {
            int baseIndex = i * step;
            float x = BitConverter.ToSingle(raw, baseIndex + offsetX);
            float y = BitConverter.ToSingle(raw, baseIndex + offsetY);
            float z = BitConverter.ToSingle(raw, baseIndex + offsetZ);
            positions[i] = new Vector3(x, y, z);

            colors[i] = (offsetR >= 0 && offsetG >= 0 && offsetB >= 0)
                ? new Color(
                    raw[baseIndex + offsetR] / 255f,
                    raw[baseIndex + offsetG] / 255f,
                    raw[baseIndex + offsetB] / 255f)
                : Color.white;
        }
        return positions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnPointCloudReceived(PointCloud2Msg msg)
    {
        Vector3[] positions = ExtractPoints(msg, out Color[] colors);
        for (int i = 0; i < positions.Length; i++)
        {
            originalPoints.Add(positions[i]);

            ParticleSystem.Particle p = new ParticleSystem.Particle();
            p.position = new Vector3(
                -positions[i].y,
                 positions[i].z,
                 positions[i].x
            ) * scale;
            p.startColor        = Color.white;
            p.startSize         = voxelScale;
            p.remainingLifetime = float.MaxValue;
            p.startLifetime     = float.MaxValue;
            particleList.Add(p);
        }
        voxelsUpdated = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    public void ClearPointCloud()
    {
        particleList.Clear();
        originalPoints.Clear();
        system.Clear();
        voxelsUpdated = false;
    }
}