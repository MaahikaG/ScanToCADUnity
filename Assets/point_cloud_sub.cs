// using System;
// using System.Collections;
// using UnityEngine;
// using UnityEngine.UI;
// using UnityEngine.Networking;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std;
// using Unity.Robotics.ROSTCPConnector;
// using System.Collections.Generic;
// using UnityEngine.EventSystems;

// public class VoxelRenderer : MonoBehaviour
// {
//     ROSConnection ros;
//     public string topicName = "/point_cloud";
//     public string scanCompleteTopic = "/scan_complete";
//     public string exportTopic = "/run_parsenet";

//     // Replace with your deployed Apps Script URL
//     public string appsScriptUrl = "https://script.google.com/macros/s/AKfycbxIsHvlSf-D6-PdGlta7OPbcv5SfahcpRe6YiZxf8SksRzMnwV-mEthvSw5vgs7inI/exec";

//     ParticleSystem system;
//     private List<ParticleSystem.Particle> particleList = new List<ParticleSystem.Particle>();
//     private List<Vector3> originalPoints = new List<Vector3>();
//     bool voxelsUpdated = false;

//     public float voxelScale = 0.1f;
//     public float scale = 1f;

//     // UI
//     private Canvas canvas;
//     private Button exportButton;
//     private Text exportLabel;

//     void Start()
//     {
//         if (FindObjectOfType<EventSystem>() == null)
//         {
//             GameObject es = new GameObject("EventSystem");
//             es.AddComponent<EventSystem>();
//             es.AddComponent<StandaloneInputModule>();
//         }

//         ros = ROSConnection.GetOrCreateInstance();
//         ros.Subscribe<PointCloud2Msg>(topicName, OnPointCloudReceived);
//         ros.RegisterPublisher<BoolMsg>(scanCompleteTopic);
//         ros.RegisterPublisher<BoolMsg>(exportTopic);

//         system = GetComponent<ParticleSystem>();
//         var main = system.main;
//         main.maxParticles = 500000;
//         main.simulationSpace = ParticleSystemSimulationSpace.World;
//         main.playOnAwake = false;
//         main.loop = false;
//         var emission = system.emission;
//         emission.rateOverTime = 0;

//         CreateExportButton();
//     }

//     void CreateExportButton()
//     {
//         GameObject canvasObj = new GameObject("ExportCanvas");
//         canvas = canvasObj.AddComponent<Canvas>();
//         canvas.renderMode = RenderMode.ScreenSpaceOverlay;
//         canvasObj.AddComponent<CanvasScaler>();
//         canvasObj.AddComponent<GraphicRaycaster>();

//         GameObject buttonObj = new GameObject("ExportButton");
//         buttonObj.transform.SetParent(canvasObj.transform, false);
//         exportButton = buttonObj.AddComponent<Button>();

//         Image img = buttonObj.AddComponent<Image>();
//         img.color = new Color(0.2f, 0.8f, 0.2f);

//         RectTransform rt = buttonObj.GetComponent<RectTransform>();
//         rt.anchorMin = new Vector2(0.5f, 0f);
//         rt.anchorMax = new Vector2(0.5f, 0f);
//         rt.pivot     = new Vector2(0.5f, 0f);
//         rt.anchoredPosition = new Vector2(0f, 20f);
//         rt.sizeDelta = new Vector2(200f, 50f);

//         GameObject textObj = new GameObject("Label");
//         textObj.transform.SetParent(buttonObj.transform, false);
//         exportLabel = textObj.AddComponent<Text>();
//         exportLabel.text = "Export to CAD";
//         exportLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
//         exportLabel.alignment = TextAnchor.MiddleCenter;
//         exportLabel.color = Color.white;
//         exportLabel.fontSize = 18;
//         RectTransform textRt = textObj.GetComponent<RectTransform>();
//         textRt.anchorMin = Vector2.zero;
//         textRt.anchorMax = Vector2.one;
//         textRt.sizeDelta = Vector2.zero;

//         exportButton.gameObject.SetActive(true);
//         exportButton.onClick.AddListener(OnExportClicked);
//     }

//     void OnExportClicked()
//     {
//         Debug.Log("Export button clicked");
//         exportLabel.text = "Uploading...";
//         exportButton.interactable = false;

//         // Publish ROS topics
//         BoolMsg scanComplete = new BoolMsg();
//         scanComplete.data = true;
//         ros.Publish(scanCompleteTopic, scanComplete);

//         BoolMsg trigger = new BoolMsg();
//         trigger.data = true;
//         ros.Publish(exportTopic, trigger);

//         // Upload PCD to Google Drive and wait for result
//         StartCoroutine(SaveAndUploadPCD());
//     }

//     IEnumerator SaveAndUploadPCD()
//     {
//         // Build PCD content from accumulated points
//         int n = originalPoints.Count;
//         var sb = new System.Text.StringBuilder();
//         sb.AppendLine("# .PCD v0.7");
//         sb.AppendLine("VERSION 0.7");
//         sb.AppendLine("FIELDS x y z");
//         sb.AppendLine("SIZE 4 4 4");
//         sb.AppendLine("TYPE F F F");
//         sb.AppendLine("COUNT 1 1 1");
//         sb.AppendLine($"WIDTH {n}");
//         sb.AppendLine("HEIGHT 1");
//         sb.AppendLine("VIEWPOINT 0 0 0 1 0 0 0");
//         sb.AppendLine($"POINTS {n}");
//         sb.AppendLine("DATA ascii");
//         foreach (var p in originalPoints)
//             sb.AppendLine($"{p.x:F6} {p.y:F6} {p.z:F6}");

//         string pcdContent = sb.ToString();
//         byte[] pcdBytes = System.Text.Encoding.UTF8.GetBytes(pcdContent);
//         string base64 = Convert.ToBase64String(pcdBytes);

//         Debug.Log($"Uploading {n} points to Google Drive...");

//         byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(base64);

//         using (UnityWebRequest www = new UnityWebRequest(appsScriptUrl, "POST"))
//         {
//             www.uploadHandler = new UploadHandlerRaw(bodyBytes);
//             www.downloadHandler = new DownloadHandlerBuffer();
//             www.SetRequestHeader("Content-Type", "text/plain");
//             yield return www.SendWebRequest();

//             if (www.result == UnityWebRequest.Result.Success)
//             {
//                 Debug.Log("PCD uploaded to Drive: " + www.downloadHandler.text);
//                 exportLabel.text = "Processing on Colab...";
//                 StartCoroutine(PollForResult());
//             }
//             else
//             {
//                 Debug.LogError($"Upload failed: {www.error}");
//                 exportLabel.text = "Upload failed";
//                 exportButton.interactable = true;
//             }
//         }
//     }

//     IEnumerator PollForResult()
//     {
//         string pollUrl = appsScriptUrl + "?check=true";
//         Debug.Log("Polling for Point2CAD result...");

//         while (true)
//         {
//             yield return new WaitForSeconds(10f);

//             using (UnityWebRequest www = UnityWebRequest.Get(pollUrl))
//             {
//                 yield return www.SendWebRequest();

//                 if (www.result == UnityWebRequest.Result.Success)
//                 {
//                     string response = www.downloadHandler.text;
//                     Debug.Log($"Poll response: {response}");

//                     if (response == "READY")
//                     {
//                         exportLabel.text = "Done! CAD model ready.";
//                         Debug.Log("CAD model ready on Google Drive!");
//                         yield break;
//                     }
//                 }
//                 else
//                 {
//                     Debug.LogWarning($"Poll failed: {www.error}");
//                 }
//             }
//         }
//     }

//     void Update()
//     {
//         if (voxelsUpdated)
//         {
//             var arr = particleList.ToArray();
//             system.SetParticles(arr, arr.Length);
//             voxelsUpdated = false;
//         }
//     }

//     public static Vector3[] ExtractPoints(PointCloud2Msg msg, out Color[] colors)
//     {
//         int pointCount = (int)msg.width * (int)msg.height;
//         Vector3[] positions = new Vector3[pointCount];
//         colors = new Color[pointCount];

//         int offsetX = -1, offsetY = -1, offsetZ = -1;
//         int offsetR = -1, offsetG = -1, offsetB = -1;

//         foreach (var field in msg.fields)
//         {
//             switch (field.name)
//             {
//                 case "x": offsetX = (int)field.offset; break;
//                 case "y": offsetY = (int)field.offset; break;
//                 case "z": offsetZ = (int)field.offset; break;
//                 case "r": offsetR = (int)field.offset; break;
//                 case "g": offsetG = (int)field.offset; break;
//                 case "b": offsetB = (int)field.offset; break;
//             }
//         }

//         byte[] raw = msg.data;
//         int step = (int)msg.point_step;

//         for (int i = 0; i < pointCount; i++)
//         {
//             int baseIndex = i * step;
//             float x = BitConverter.ToSingle(raw, baseIndex + offsetX);
//             float y = BitConverter.ToSingle(raw, baseIndex + offsetY);
//             float z = BitConverter.ToSingle(raw, baseIndex + offsetZ);
//             positions[i] = new Vector3(x, y, z);

//             if (offsetR >= 0 && offsetG >= 0 && offsetB >= 0)
//             {
//                 colors[i] = new Color(
//                     raw[baseIndex + offsetR] / 255f,
//                     raw[baseIndex + offsetG] / 255f,
//                     raw[baseIndex + offsetB] / 255f
//                 );
//             }
//             else
//             {
//                 colors[i] = Color.white;
//             }
//         }
//         return positions;
//     }

//     void OnPointCloudReceived(PointCloud2Msg msg)
//     {
//         Vector3[] positions = ExtractPoints(msg, out Color[] colors);
//         for (int i = 0; i < positions.Length; i++)
//         {
//             originalPoints.Add(positions[i]);

//             ParticleSystem.Particle p = new ParticleSystem.Particle();
//             p.position = new Vector3(
//                 -positions[i].y,
//                  positions[i].z,
//                  positions[i].x
//             ) * scale;
//             p.startColor = Color.white;
//             p.startSize = voxelScale;
//             p.remainingLifetime = float.MaxValue;
//             p.startLifetime = float.MaxValue;
//             particleList.Add(p);
//         }
//         voxelsUpdated = true;
//     }

//     public void ClearPointCloud()
//     {
//         particleList.Clear();
//         originalPoints.Clear();
//         system.Clear();
//         voxelsUpdated = false;
//     }
// }