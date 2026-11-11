# Architecture, scene ownership and implementation notes

## Guiding architecture rules

1. Backend is source of truth for real tasks, approvals and artifacts. Quest owns only visual/interaction transients.
2. Offline mode follows the same interface and reports demo provenance prominently.
3. UI should never depend on a long-running LLM call in Unity's main thread. All network and decode work is asynchronous, with bounded updates on main thread.
4. Character art and interactions are separate prefabs. Agents may edit their owned C# scripts without modifying the central Unity scene simultaneously.
5. The interaction rig is added once by XR lead. All other systems consume it, do not spawn another rig.

## Proposed Unity code structure

```text
quest/Assets/MuseDesk/
├── Scenes/MainMRScene.unity
├── Prefabs/
│   ├── WorkspaceRoot.prefab
│   ├── ResearchStation.prefab
│   ├── DevStation.prefab
│   ├── QAStation.prefab
│   ├── AgentResearcher.prefab
│   ├── AgentDeveloper.prefab
│   ├── AgentTester.prefab
│   ├── DecisionPaper.prefab
│   └── TaskBoard.prefab
├── Scripts/
│   ├── Bootstrap/AppBootstrap.cs
│   ├── Config/RuntimeSettings.cs
│   ├── Networking/IAgentGateway.cs
│   ├── Networking/LiveAgentGateway.cs
│   ├── Networking/DemoAgentGateway.cs
│   ├── Networking/EventDispatcher.cs
│   ├── Networking/SessionReducer.cs
│   ├── Models/EventEnvelope.cs
│   ├── Models/TaskModel.cs
│   ├── Models/DecisionModel.cs
│   ├── Workspace/WorkspacePlacement.cs
│   ├── Workspace/WorkstationAnchor.cs
│   ├── Agents/AgentView.cs
│   ├── Agents/AgentAnimator.cs
│   ├── Agents/AgentMovement.cs
│   ├── Documents/DecisionPaper.cs
│   ├── Documents/DocumentDeliveryController.cs
│   ├── Documents/DocumentInteractionController.cs
│   ├── UI/TaskBoardView.cs
│   ├── UI/ConversationPanel.cs
│   └── Debug/DiagnosticsOverlay.cs
├── Animations/
├── Materials/
├── Audio/
└── Demo/fixtures.json
```

### `IAgentGateway` contract (illustrative C#)

```csharp
public interface IAgentGateway
{
    Task<SessionSnapshot> StartSessionAsync(CancellationToken ct);
    Task<TaskRecord> StartTaskAsync(string templateId, string idempotencyKey, CancellationToken ct);
    Task<DecisionResult> RespondToDecisionAsync(
        string decisionId, string action, string idempotencyKey,
        string comment, CancellationToken ct);
    Task<QuestionResult> AskAsync(string agentId, string decisionId,
        string question, CancellationToken ct);
    Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct);
    event Action<EventEnvelope> EventReceived;
}
```

Implement this exact surface in demo/live variants. Decide the concrete serialization library with XR lead before coding; current Unity supports JsonUtility but nested dictionaries/unions are limited, so use a proven supported JSON package (e.g. Newtonsoft Json) with version pinned.

### Threading

WebSocket callbacks must marshal event application to Unity main thread. One sequential dispatcher prevents event `seq` reordering. Update visuals after state reducer, not directly in transport callbacks. Cap queue and log overflow; snapshots recover from event loss.

## MR workspace dimensions and anchor rules

- `WorkspaceRoot` is an anchor-relative tray rather than a room-size simulation.
- Proposed visual footprint: 0.75–0.95 m across, 0.25–0.40 m deep; user may scale to desk size.
- Three stations arranged left to right; place delivery zone closest to the user in the center.
- Retain at least 0.15 m vertical clearance for props; avoid objects clipping into scene mesh.
- If MRUK scene provides table plane, place tray slightly above it (small offset) and orient front edge toward user's current forward vector projected to table plane.
- If no data, spawn a floating horizontal placement guide around 0.5 m in front of user's torso; require explicit hand confirmation, then allow dragging/repositioning.
- Limit mandatory controls to near reach; don't send papers outside ~0.65 m.
- Use scene anchors/world locking after permission; if pose drifts, allow **Reset Workspace** button.

## Agent animation projection

| Backend state | Primary animation | Station effect | Delivery behavior |
|---|---|---|---|
| `idle` | breathing/looking around | low neutral light | none |
| `researching` | flipping pages | moving notes | none |
| `coding` | typing | active laptop | none |
| `testing` | examining clipboard | test LEDs | none |
| `reporting` | note-taking | report icon | completion inbox |
| `waiting` | checking wrist/clock | dim pending | optional quiet notification |
| `error` | startled then neutral | warning icon | paper only if actionable |
| `complete` | brief celebration, then idle | green complete icon | completion report |

The client-only approach animation begins only after `decision.requested`, and **has no authority to change task state**. On release/resolve/timeout, return path should be safe even if user grabs paper during motion.

### Animation event implementation

A central queue accepts an `ApproachRequest` containing `agent_id`, `decision_id`, `priority` and expiry. It assigns exactly one DeliveryZone reservation at a time. Additional requests become physical papers in nearby inbox rather than overlapping the user's hands. Once an agent reaches delivery point, `DecisionPaper.SetOffered()` enables grab. After 30 seconds idle, gently move paper to Inbox while preserving decision state.

### Content legibility

The paper is a stylized 3D object while carried. Once expanded it is a stable, head-readable 2D world-space panel with fixed recommended physical dimensions and content scaling, not arbitrary tiny TextMeshPro text. Arrange concise summary at top, then rationale, alternatives, impact and actions. Use a dark/light high-contrast theme; reduce background transparency for text readability.

### Interaction bug-prevention

- Only register approval control events when the document is currently `expanded` and not `responding/resolved`.
- Use explicit visual selected state and send command on finger-release/poke confirmation, not while finger merely hovers.
- Debounce repeated presses and disable buttons while awaiting acknowledgement; retry with same idempotency key.
- Detach paper from agent hand when grabbed, preserving world transform and making object `held`.
- On session reset, destroy prior event-derived objects; do not spawn duplicates from replayed events.
- Provide a **Return to Desk** or **Reset Paper** action if the user drops a document out of reach.

## Performance budget (initial targets, profile on Quest)

- Three characters, roughly under 10–20k triangles each (design target, not device limit), one small rig each.
- Avoid real-time shadows for miniature props unless on-device headroom allows; bake/simple blob shadows instead.
- Prefer single material atlas per character; minimize transparent layers and dynamic UI canvases.
- Throttle low-value screen effects and status label redraws; animate through Animator rather than allocations every Update.
- Pool documents and effect objects; cap particle counts. Track draw calls, CPU/GPU frame time, memory and thermal throttling with MVDH.
- Favor 72Hz on Quest 3/3S passthrough when compatible; release minimum ≥60 fps per competition rules.

## Desktop companion deployment modes

**Developer mode:** tethered Quest (`adb reverse`) ↔ `127.0.0.1:8765` PC backend. Simple and reliable.

**Untethered live LAN:** Quest ↔ PC private IP via wireless; token auth and restricted firewall.

**Standalone reviewer mode:** Quest local `DemoAgentGateway`; no external PC or keys. Give judges task sample and labeled visual proofs of interaction.

**Hosted live mode:** cloud API with actual isolated sample workers and secrets, if time permits. Do not build it before standalone mode is stable.
