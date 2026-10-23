using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Persist before sending; remove only after the backend acknowledges the batch.
// Stable event IDs let the backend safely ignore retries of an accepted request.
public class AnalyticsUploader : MonoBehaviour
{
    [Serializable] public class PendingEvent { public string url; public string json; }
    [Serializable] public class Outbox { public List<PendingEvent> events = new List<PendingEvent>(); }
    [Serializable] class Acknowledgement { public bool ok = false; }
    const string StorageKey = "fuel_analytics_outbox_v2";
    static AnalyticsUploader instance;
    static bool quitting;
    Outbox outbox;
    bool sending;

    [RuntimeInitializeOnLoadMethod]
    static void Init() { quitting = false; }

    void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject);
        try { outbox = JsonUtility.FromJson<Outbox>(PlayerPrefs.GetString(StorageKey, "")); }
        catch (Exception) { outbox = null; }
        if (outbox == null || outbox.events == null) outbox = new Outbox();
    }

    void Start() { if (!sending && outbox.events.Count > 0) StartCoroutine(Flush()); }
    void OnApplicationQuit() { Save(); quitting = true; }

    public static void Send(string url, string eventJson)
    {
        if (quitting) return;
        if (instance == null) new GameObject("AnalyticsUploader").AddComponent<AnalyticsUploader>();
        instance.outbox.events.Add(new PendingEvent { url = url, json = eventJson });
        instance.Save();
        if (!instance.sending) instance.StartCoroutine(instance.Flush());
    }

    void Save()
    {
        PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(outbox));
        PlayerPrefs.Save();
    }

    IEnumerator Flush()
    {
        sending = true;
        float retryDelay = 1;
        while (outbox.events.Count > 0)
        {
            string endpoint = outbox.events[0].url;
            int count = 0;
            var batch = new StringBuilder("[");
            foreach (PendingEvent item in outbox.events)
            {
                if (item.url != endpoint || count == 50) break;
                if (count++ > 0) batch.Append(',');
                batch.Append(item.json);
            }
            batch.Append(']');
            bool accepted = false;
            // text/plain avoids the browser's JSON CORS preflight.
            using (var request = UnityWebRequest.Post(endpoint, batch.ToString(), "text/plain;charset=utf-8"))
            {
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { accepted = JsonUtility.FromJson<Acknowledgement>(request.downloadHandler.text)?.ok == true; }
                    catch (Exception) { accepted = false; }
                }
                if (!accepted) Debug.LogWarning("Analytics upload not acknowledged; retained for retry.");
            }
            if (accepted)
            {
                outbox.events.RemoveRange(0, count);
                Save();
                retryDelay = 1;
            }
            else
            {
                yield return new WaitForSecondsRealtime(retryDelay);
                retryDelay = Mathf.Min(30, retryDelay * 2);
            }
        }
        sending = false;
    }
}
