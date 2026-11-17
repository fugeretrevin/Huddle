using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Networking
{
    /// <summary>
    /// Talks to the Windows companion backend over REST + WebSocket. The backend is the
    /// source of truth: this gateway never fabricates approvals, it waits for the server's
    /// decision.resolved. Implements the shared IAgentGateway surface.
    /// </summary>
    public class LiveAgentGateway : IAgentGateway
    {
        public string ModeLabel => "LIVE · Connected to desktop";
        public event Action<EventEnvelope> EventReceived;

        readonly RestClient _rest;
        readonly string _wsBase;
        readonly EventDispatcher _dispatcher;
        readonly string _mode;

        string _sessionId;
        string _token;

        public string SessionId => _sessionId;
        /// <summary>The per-session bearer token (so the additive v2 surface can reuse the same auth).</summary>
        public string Token => _token;

        public LiveAgentGateway(string apiBaseUrl, string wsBaseUrl, EventDispatcher dispatcher,
                                string mode = "live_local")
        {
            _rest = new RestClient(apiBaseUrl);
            _wsBase = wsBaseUrl;
            _dispatcher = dispatcher;
            _mode = mode;
            _dispatcher.EventReceived += e => EventReceived?.Invoke(e);
        }

        public async Task<SessionSnapshot> StartSessionAsync(CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                schema_version = 1, mode = _mode, client = "quest"
            });
            var created = JsonConvert.DeserializeObject<SessionCreatedDto>(
                await _rest.PostJsonAsync("/v1/sessions", body, ct));
            _sessionId = created.SessionId;
            _token = created.Token;   // exposed via Token so the v2 board gateway can share this session
            _rest.BearerToken = created.Token;

            var snap = await GetSnapshotAsync(ct);
            _dispatcher.Connect(_wsBase, _sessionId, created.Token, snap?.LastSeq ?? created.LastSeq);
            return snap;
        }

        public async Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct)
        {
            var json = await _rest.GetAsync($"/v1/sessions/{_sessionId}/snapshot", ct);
            return JsonConvert.DeserializeObject<SessionSnapshot>(json);
        }

        public async Task<TaskView> StartTaskAsync(string templateId, string idempotencyKey,
                                                   CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                schema_version = 1, task_template = templateId, idempotency_key = idempotencyKey
            });
            var json = await _rest.PostJsonAsync($"/v1/sessions/{_sessionId}/tasks", body, ct);
            return JsonConvert.DeserializeObject<TaskView>(json);
        }

        public async Task<DecisionResolutionDto> RespondToDecisionAsync(string decisionId, string action,
            string idempotencyKey, string comment, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                schema_version = 1, action, idempotency_key = idempotencyKey, comment
            });
            var json = await _rest.PostJsonAsync(
                $"/v1/sessions/{_sessionId}/decisions/{decisionId}/respond", body, ct);
            return JsonConvert.DeserializeObject<DecisionResolutionDto>(json);
        }

        public async Task<QuestionAnswerDto> AskAsync(string agentId, string decisionId, string question,
            CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                schema_version = 1, agent_id = agentId, decision_id = decisionId, question
            });
            var json = await _rest.PostJsonAsync($"/v1/sessions/{_sessionId}/questions", body, ct);
            return JsonConvert.DeserializeObject<QuestionAnswerDto>(json);
        }
    }
}
