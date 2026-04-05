using System;
using UnityEngine;
using UnityEngine.UI;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using System.Collections.Generic;
using UnityEngine.EventSystems;


public class VoxelRenderer : MonoBehaviour
{
    ROSConnection ros;
    public string topicName = "/point_cloud";
    public string scanCompleteTopic = "/scan_complete";
    public string exportTopic = "/run_parsenet";

    ParticleSystem system;
    private List<ParticleSystem.Particle> particleList = new List<ParticleSystem.Particle>();
    bool voxelsUpdated = false;

    public float voxelScale = 0.1f;
    public float scale = 1f;

    // UI
    private Canvas canvas;
    private Button exportButton;
    private Text exportLabel;
    bool scanComplete = false;

    void Start()
    {
        // Add EventSystem if missing
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PointCloud2Msg>(topicName, OnPointCloudReceived);
        ros.Subscribe<BoolMsg>(scanCompleteTopic, OnScanComplete);
        ros.RegisterPublisher<BoolMsg>(exportTopic);

        system = GetComponent<ParticleSystem>();
        var main = system.main;
        main.maxParticles = 500000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = false;
        var emission = system.emission;
        emission.rateOverTime = 0;

        CreateExportButton();
    }

    void CreateExportButton()
    {
        
        // Create Canvas
        GameObject canvasObj = new GameObject("ExportCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Create Button
        GameObject buttonObj = new GameObject("ExportButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        exportButton = buttonObj.AddComponent<Button>();

        // Button background image
        Image img = buttonObj.AddComponent<Image>();
        img.color = new Color(0.2f, 0.8f, 0.2f);   // green

        // Position bottom center
        RectTransform rt = buttonObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 20f);
        rt.sizeDelta = new Vector2(200f, 50f);

        // Button label
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(buttonObj.transform, false);
        exportLabel = textObj.AddComponent<Text>();
        exportLabel.text = "Export to CAD";
        exportLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        exportLabel.alignment = TextAnchor.MiddleCenter;
        exportLabel.color = Color.white;
        exportLabel.fontSize = 18;
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        // Hide until scan complete
        exportButton.gameObject.SetActive(false);
        exportButton.onClick.AddListener(OnExportClicked);
    }

    void OnScanComplete(BoolMsg msg)
    {
        if (msg.data)
        {
            scanComplete = true;
            exportButton.gameObject.SetActive(true);
            Debug.Log("Scan complete — Export button enabled");
        }
    }


    void OnExportClicked()
    {
        Debug.Log("Export button clicked!");  // ← check Unity console for this
        Debug.Log($"Publishing to: {exportTopic}");
        exportLabel.text = "Converting...";
        exportButton.interactable = false;

        BoolMsg trigger = new BoolMsg();
        trigger.data = true;
        ros.Publish(exportTopic, trigger);
        Debug.Log("Published!");
    }
    void Update()
    {
        if (voxelsUpdated)
        {
            var arr = particleList.ToArray();
            system.SetParticles(arr, arr.Length);
            voxelsUpdated = false;
        }
    }

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

        byte[] raw = msg.data;
        int step = (int)msg.point_step;

        for (int i = 0; i < pointCount; i++)
        {
            int baseIndex = i * step;
            float x = BitConverter.ToSingle(raw, baseIndex + offsetX);
            float y = BitConverter.ToSingle(raw, baseIndex + offsetY);
            float z = BitConverter.ToSingle(raw, baseIndex + offsetZ);
            positions[i] = new Vector3(x, y, z);

            if (offsetR >= 0 && offsetG >= 0 && offsetB >= 0)
            {
                colors[i] = new Color(
                    raw[baseIndex + offsetR] / 255f,
                    raw[baseIndex + offsetG] / 255f,
                    raw[baseIndex + offsetB] / 255f
                );
            }
            else
            {
                colors[i] = Color.white;
            }
        }
        return positions;
    }

    void OnPointCloudReceived(PointCloud2Msg msg)
    {
        Vector3[] positions = ExtractPoints(msg, out Color[] colors);
        for (int i = 0; i < positions.Length; i++)
        {
            ParticleSystem.Particle p = new ParticleSystem.Particle();
            p.position = new Vector3(
                -positions[i].y,
                 positions[i].z,
                 positions[i].x
            ) * scale;
            p.startColor = Color.white;
            p.startSize = voxelScale;
            p.remainingLifetime = float.MaxValue;
            p.startLifetime = float.MaxValue;
            particleList.Add(p);
        }
        voxelsUpdated = true;
    }

    public void ClearPointCloud()
    {
        particleList.Clear();
        system.Clear();
        voxelsUpdated = false;
    }
}