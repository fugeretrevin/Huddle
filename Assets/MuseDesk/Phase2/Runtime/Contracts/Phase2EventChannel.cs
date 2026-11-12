using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// WebSocket client for the Phase 2 (v2) event channel: WS /v2/sessions/{sid}/events?after_seq=N.
    ///
    /// This is a v2-only mirror of the existing Phase 1 EventDispatcher (same ConcurrentQueue +
    /// Update()-drain main-thread pump, same exponential-backoff reconnect, same after_seq resume).
    /// A separate channel is required because the Phase 1 EventDispatcher is hard-wired to the v1
    /// path and parses the v1 envelope; v1 and v2 have SEPARATE seq spaces and logs, so they cannot
    /// share one socket. The Phase 1 dispatcher is NOT modified (additive, invariant #5 scope).
    ///
    /// The receive loop runs off the main thread and only enqueues parsed envelopes; Update() drains
    /// them on the main thread and raises EventReceived in arrival (seq) order. GameObject mutation
    /// therefore always happens on the main thread (master §2).
    /// </summary>
    public class Phase2EventChannel : MonoBehaviour
    {
        public event Action<EventEnvelopeV2> EventReceived;
        public event Action<bool> ConnectionChanged;   // true = connected

        readonly ConcurrentQueue<EventEnvelopeV2> _queue = new ConcurrentQueue<EventEnvelopeV2>();
        ClientWebSocket _ws;
        CancellationTokenSource _cts;
        string _wsBase, _sessionId, _token;
        long _afterSeq;
        bool _connected;

        public bool Connected => _connected;
        public long AfterSeq => _afterSeq;

        /// <summary>Open the v2 socket. afterSeq is a v2 seq (the snapshot's last_seq on first
        /// connect, or the reducer's lastAppliedSeq on a resync).</summary>
        public void Connect(string wsBaseUrl, string sessionId, string token, long afterSeq)
        {
            Disconnect();
            _wsBase = (wsBaseUrl ?? "").TrimEnd('/');
            _sessionId = sessionId;
            _token = token;
            _afterSeq = afterSeq;
            _cts = new CancellationTokenSource();
            _ = RunAsync(_cts.Token);
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            SetConnected(false);
        }

        void Update()
        {
            while (_queue.TryDequeue(out var e))
            {
                try { EventReceived?.Invoke(e); }
                catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] event handler error: {ex.Message}"); }
            }
        }

        void OnDestroy() => Disconnect();

        async Task RunAsync(CancellationToken ct)
        {
            int attempt = 0;
            float[] backoff = { 0.5f, 1f, 2f, 4f, 8f };
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    var url = $"{_wsBase}/v2/sessions/{_sessionId}/events" +
                              $"?after_seq={_afterSeq}&token={Uri.EscapeDataString(_token ?? "")}";
                    await _ws.ConnectAsync(new Uri(url), ct);
                    SetConnected(true);
                    attempt = 0;
                    await ReceiveLoop(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MuseDesk.P2] WS error: {ex.Message}");
                }
                SetConnected(false);
                if (ct.IsCancellationRequested) break;
                float wait = backoff[Mathf.Min(attempt, backoff.Length - 1)];
                attempt++;
                try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                EventEnvelopeV2 env = null;
                try { env = JsonConvert.DeserializeObject<EventEnvelopeV2>(sb.ToString()); }
                catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] bad v2 event json: {ex.Message}"); }
                if (env != null && env.Seq > _afterSeq)
                {
                    _afterSeq = env.Seq;      // resume point for reconnect
                    _queue.Enqueue(env);
                }
            }
        }

        void SetConnected(bool value)
        {
            if (_connected == value) return;
            _connected = value;
            // raised on a background thread; consumers must marshal if touching Unity objects
            ConnectionChanged?.Invoke(value);
        }
    }
}
