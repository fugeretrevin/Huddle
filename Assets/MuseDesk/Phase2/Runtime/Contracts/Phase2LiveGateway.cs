using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using MuseDesk.Networking;   // reuse the project's RestClient + RestException

namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// Live v2 gateway: talks to the Windows companion backend over the additive /v2 REST surface
    /// and the v2 WebSocket. Reuses the project's RestClient (HTTP) and the SAME per-session bearer
    /// token issued by POST /v1/sessions (master §3 — "v2 uses the same per-session auth as v1").
    ///
    /// The backend is the source of truth: this gateway never fabricates an acknowledgement. A
    /// command returns the canonical TaskCardV2 the server computed; a 409 is returned as a parsed
    /// conflict body (Ok=false) so the caller reconciles rather than synthesizing success.
    ///
    /// Session acquisition is intentionally NOT done here: the integrator passes the sessionId +
    /// token already obtained by the Phase 1 live handshake (so we do not double-create sessions).
    /// Call Connect(...) once that handshake has completed.
    /// </summary>
    public class Phase2LiveGateway : IPhase2Gateway
    {
        public string ModeLabel => "LIVE · v2 (desktop)";
        public string SourceMode => SourceModes.Live;
        public bool IsLive => true;
        public event Action<EventEnvelopeV2> EventReceived;

        readonly RestClient _rest;
        readonly string _wsBase;
        readonly Phase2EventChannel _channel;
        string _sessionId;

        public string SessionId => _sessionId;

        /// <param name="apiBaseUrl">e.g. http://127.0.0.1:8765 (same host as v1).</param>
        /// <param name="wsBaseUrl">e.g. ws://127.0.0.1:8765 (same host as v1).</param>
        /// <param name="channel">The v2 WebSocket pump (a MonoBehaviour in the scene).</param>
        public Phase2LiveGateway(string apiBaseUrl, string wsBaseUrl, Phase2EventChannel channel)
        {
            _rest = new RestClient(apiBaseUrl);
            _wsBase = wsBaseUrl;
            _channel = channel;
            if (_channel != null)
                _channel.EventReceived += e => EventReceived?.Invoke(e);
        }

        /// <summary>
        /// Attach the already-authenticated session and open the v2 event socket resuming at
        /// afterSeq (the snapshot's last_seq on first connect). Reuses the v1 bearer token.
        /// </summary>
        public void Connect(string sessionId, string bearerToken, long afterSeq)
        {
            _sessionId = sessionId;
            _rest.BearerToken = bearerToken;
            if (_channel != null)
                _channel.Connect(_wsBase, sessionId, bearerToken, afterSeq);
        }

        public async Task<SnapshotV2> GetSnapshotAsync(CancellationToken ct)
        {
            var json = await _rest.GetAsync($"/v2/sessions/{_sessionId}/snapshot", ct);
            return JsonConvert.DeserializeObject<SnapshotV2>(json);
        }

        public async Task<TaskCardV2> CreateTaskAsync(TaskCreateV2 cmd, CancellationToken ct)
        {
            var json = await _rest.PostJsonAsync($"/v2/sessions/{_sessionId}/tasks",
                JsonConvert.SerializeObject(cmd), ct);
            return JsonConvert.DeserializeObject<TaskCardV2>(json);
        }

        public Task<Phase2CommandResult<TaskCardV2>> AssignAsync(string taskId, AssignCommand cmd, CancellationToken ct)
            => CommandAsync($"/v2/sessions/{_sessionId}/tasks/{taskId}/assign", cmd, ct);

        public Task<Phase2CommandResult<TaskCardV2>> SetPriorityAsync(string taskId, PriorityCommand cmd, CancellationToken ct)
            => CommandAsync($"/v2/sessions/{_sessionId}/tasks/{taskId}/priority", cmd, ct);

        public Task<Phase2CommandResult<TaskCardV2>> PauseAsync(string taskId, PauseCommand cmd, CancellationToken ct)
            => CommandAsync($"/v2/sessions/{_sessionId}/tasks/{taskId}/pause", cmd, ct);

        public Task<Phase2CommandResult<TaskCardV2>> ResumeAsync(string taskId, ResumeCommand cmd, CancellationToken ct)
            => CommandAsync($"/v2/sessions/{_sessionId}/tasks/{taskId}/resume", cmd, ct);

        public async Task<FocusPolicyV2> SetFocusAsync(FocusCommand cmd, CancellationToken ct)
        {
            var json = await _rest.PostJsonAsync($"/v2/sessions/{_sessionId}/focus",
                JsonConvert.SerializeObject(cmd), ct);
            return JsonConvert.DeserializeObject<FocusPolicyV2>(json);
        }

        public async Task<ArtifactPageResponse> GetArtifactPageAsync(string artifactId, int page, int pageSize, CancellationToken ct)
        {
            var json = await _rest.GetAsync(
                $"/v2/sessions/{_sessionId}/artifacts/{artifactId}?page={page}&page_size={pageSize}", ct);
            return JsonConvert.DeserializeObject<ArtifactPageResponse>(json);
        }

        public async Task<DiagramV2> CreateDiagramAsync(DiagramCommand cmd, CancellationToken ct)
        {
            var json = await _rest.PostJsonAsync($"/v2/sessions/{_sessionId}/diagrams",
                JsonConvert.SerializeObject(cmd), ct);
            return JsonConvert.DeserializeObject<DiagramV2>(json);
        }

        /// <summary>
        /// POST a task command. Success → canonical card. HTTP 409 → parsed conflict body (Ok=false);
        /// the RestClient throws RestException on any non-2xx, carrying the response body, so we can
        /// recover the { code, message, canonical_task } JSON. Any other status (or an unparseable
        /// 409 body) is rethrown for the caller's generic error path.
        /// </summary>
        async Task<Phase2CommandResult<TaskCardV2>> CommandAsync<TCmd>(string path, TCmd cmd, CancellationToken ct)
        {
            try
            {
                var json = await _rest.PostJsonAsync(path, JsonConvert.SerializeObject(cmd), ct);
                var card = JsonConvert.DeserializeObject<TaskCardV2>(json);
                return Phase2CommandResult<TaskCardV2>.Success(card);
            }
            catch (RestException rex) when (rex.StatusCode == 409)
            {
                CommandErrorBody body = null;
                try { body = JsonConvert.DeserializeObject<CommandErrorBody>(rex.Body); }
                catch { /* fall through to rethrow below */ }
                if (body != null && !string.IsNullOrEmpty(body.Code))
                    return Phase2CommandResult<TaskCardV2>.Conflict(body);
                throw;
            }
        }
    }
}
