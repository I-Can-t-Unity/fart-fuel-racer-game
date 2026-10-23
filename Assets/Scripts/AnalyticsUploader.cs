using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Sends analytics events to a web endpoint in the background. It survives scene
// loads, so a race's last events still go out after Race Again reloads the scene.
//
// Events are posted as a JSON array in a text/plain body. That keeps it a "simple"
// request, which browsers (the WebGL build) send without a CORS preflight; Google
// Apps Script can't answer a preflight, so a JSON content type would be blocked.
public class AnalyticsUploader : MonoBehaviour
{
    static AnalyticsUploader instance;
    static bool quitting;

    readonly Queue<string> pending = new Queue<string>();
    string endpoint;
    bool sending;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        quitting = false;
        Application.quitting += () => quitting = true;
    }

    public static void Send(string url, string eventJson)
    {
        // Nothing could finish sending once the game (or play mode) is shutting down.
        if (quitting)
            return;
        if (instance == null)
        {
            instance = new GameObject("AnalyticsUploader").AddComponent<AnalyticsUploader>();
            DontDestroyOnLoad(instance.gameObject);
        }
        instance.endpoint = url;
        instance.pending.Enqueue(eventJson);
        if (!instance.sending)
            instance.StartCoroutine(instance.Flush());
    }

    // Everything queued while one request is in flight goes out together in the next.
    IEnumerator Flush()
    {
        sending = true;
        while (pending.Count > 0)
        {
            var batch = new StringBuilder("[");
            for (int i = 0; pending.Count > 0; i++)
            {
                if (i > 0)
                    batch.Append(',');
                batch.Append(pending.Dequeue());
            }
            batch.Append(']');

            using (var request = UnityWebRequest.Post(endpoint, batch.ToString(), "text/plain;charset=utf-8"))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("Analytics upload failed: " + request.error);
            }
        }
        sending = false;
    }
}
