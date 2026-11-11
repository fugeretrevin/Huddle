using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MuseDesk.Networking
{
    public class RestException : Exception
    {
        public long StatusCode { get; }
        public string Body { get; }
        public RestException(long status, string body)
            : base($"HTTP {status}: {body}") { StatusCode = status; Body = body; }
    }

    /// <summary>
    /// Minimal UnityWebRequest wrapper exposing async GET/POST with a per-session bearer
    /// token. Must be invoked from the Unity main thread (UnityWebRequest requirement);
    /// the gateway calls it from main-thread async methods.
    /// </summary>
    public class RestClient
    {
        readonly string _baseUrl;
        public string BearerToken { get; set; }

        public RestClient(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');

        public Task<string> GetAsync(string path, CancellationToken ct)
            => SendAsync(UnityWebRequest.kHttpVerbGET, path, null, ct);

        public Task<string> PostJsonAsync(string path, string json, CancellationToken ct)
            => SendAsync(UnityWebRequest.kHttpVerbPOST, path, json, ct);

        async Task<string> SendAsync(string verb, string path, string json, CancellationToken ct)
        {
            using var req = new UnityWebRequest(_baseUrl + path, verb);
            if (json != null)
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(BearerToken))
                req.SetRequestHeader("Authorization", "Bearer " + BearerToken);

            var tcs = new TaskCompletionSource<bool>();
            var op = req.SendWebRequest();
            op.completed += _ => tcs.TrySetResult(true);
            using (ct.Register(() => { try { req.Abort(); } catch { } tcs.TrySetCanceled(); }))
                await tcs.Task;

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.ProtocolError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new RestException(req.responseCode,
                    req.downloadHandler != null ? req.downloadHandler.text : req.error);
            }
            return req.downloadHandler.text;
        }
    }
}
