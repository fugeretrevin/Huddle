using System;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Meet (rc9 owner report: "the agents never used the portal and there was no way to access that as an option").
    /// One poke on the Spaces dock while the habitat is on the wall opens a small chooser card (Researcher / Developer /
    /// Tester / Manager / Cancel); choosing an agent goes straight to the large-portal preview for that agent. Voice does the
    /// same ("Researcher, come here", "Tester, meet me", "meet the developer") through
    /// <see cref="MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler"/>, registered only while Wall is active with the
    /// portal flag on.
    ///
    /// Nothing new decides anything: the agent goes through <see cref="AgentVisitFlow.PrepareMeet"/> (Select Agents off, a
    /// perched visit of the same agent hands over, anything else in flight refuses) and then the ONE portal entry
    /// <c>BeginPortalPreview</c> that Meet larger uses: <see cref="PortalFlow.BeginPreview"/> reserves the agent in the registry
    /// (one presentation per agent, one away at a time, Away/Home exactly once), the PresentationMirror follows the flow, the
    /// comfort guard keeps the spot clear of the user, screen, desk and doors (and closes the portal when it stops being
    /// clear), the small-visitor fallback ("Not enough space here") still applies, and only Open here opens it.
    /// The backend task is never touched.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("Meet (MeetKeyV1)")]
        [Tooltip("Seconds the Meet chooser waits for a choice before it closes by itself.")]
        public float meetChooserTimeout = 30f;
        [Tooltip("Seconds the 'Press Meet...' line stays on the dock's status after the habitat is placed.")]
        public float meetHintSeconds = 10f;

        WallCard _meetCard;
        float _meetOpenedAt;
        bool _meetHintShown, _wasAtRest;
        int _meetOpens, _meetStarts, _meetRefusals;
        Func<MuseDesk.Voice.VoiceAgentTarget, string> _meetVoice;

        /// <summary>The chooser card (drivers press its keys: 0 Researcher, 1 Developer, 2 Tester, 3 Manager, 4 Cancel).</summary>
        public WallCard MeetCard => _meetCard;
        public bool MeetChoosing => _meetCard != null && _meetCard.Visible;
        /// <summary>The portal keep-out guard (drivers read its last check, checks and closures).</summary>
        public PortalComfortGuard ComfortGuard => _comfortGuard;
        /// <summary>The last reason Meet was refused (drivers and the log).</summary>
        public string LastMeetRefusal { get; private set; }

        // ── IWallModeHost ──
        public bool WallMeetEnabled => flags.largeAgentPortalEnabled;
        public void WallMeet() { PressMeet(); }

        // ───────────────────────── the Meet key ─────────────────────────

        /// <summary>The dock's Meet key (also the driver API): open / close the chooser, or let the perched agent step out.</summary>
        public void PressMeet()
        {
            string why;
            MeetKeyAction a = MeetEntry.Decide(flags.largeAgentPortalEnabled, WallActive, WallPreviewing, MeetChoosing,
                                               _portal.State, _visit.State, out why);
            switch (a)
            {
                case MeetKeyAction.CloseChooser:
                    CloseMeetChooser();
                    Note("");
                    return;
                case MeetKeyAction.MeetPerched:
                    MeetAgent(_visit.Agent);
                    return;
                case MeetKeyAction.OpenChooser:
                    OpenMeetChooser();
                    return;
                default:
                    RefuseMeet(why);
                    return;
            }
        }

        /// <summary>
        /// Straight to the portal preview for <paramref name="agent"/> ("researcher", "developer", "tester", "manager"): the
        /// chooser's keys, voice and drivers. False (with words on the status line) when it cannot start now.
        /// </summary>
        public bool MeetAgent(string agent)
        {
            CloseMeetChooser();
            if (!flags.largeAgentPortalEnabled) { RefuseMeet(MeetCopy.PortalOff); return false; }
            if (!WallActive || WallPreviewing) { RefuseMeet(MeetCopy.PlaceFirst); return false; }
            HabitatRole role;
            if (string.IsNullOrEmpty(agent) || !HabitatStage.TryRole(agent, out role)) { RefuseMeet("Unknown agent"); return false; }
            agent = AgentId(role);
            if (_portal.State != PortalState.Hidden)
            {
                RefuseMeet(_portal.Agent == agent ? AgentPresentationRegistry.Pretty(agent) + " is already here" : MeetCopy.SendBackFirst);
                return false;
            }
            bool handedOver;
            if (!_visit.PrepareMeet(agent, out handedOver)) { RefuseMeet(_visit.Message); return false; }
            SideConversationSession keep = null;
            if (handedOver)
            {
                // The perched projection leaves without flying home (exactly what Meet larger does): the portal takes over.
                DestroyProxy();
                keep = _side;
            }
            ReleaseHands();                                  // Select Agents may have been armed: no hand stays claimed for it
            if (!BeginPortalPreview(agent, keep)) { LastMeetRefusal = _portal.Message; _meetRefusals++; return false; }
            _meetStarts++;
            Note("");
            Debug.Log("[MuseDesk.Wall] Meet " + agent + ": portal preview at " + _portal.Spot.position.ToString("F2") + " h=" + _portal.Spot.avatarHeight.ToString("F2")
                      + (PortalSpotIsFallback ? " (small visitor: not enough space)" : "") + (handedOver ? " (from the desk perch)" : ""));
            return true;
        }

        void RefuseMeet(string why)
        {
            _meetRefusals++;
            LastMeetRefusal = why;
            if (!string.IsNullOrEmpty(why)) Note(why);
            RefreshDock();
        }

        // ───────────────────────── the chooser card ─────────────────────────

        void OpenMeetChooser()
        {
            if (nearUserRig == null) { RefuseMeet("No place for the card"); return; }
            if (_visit.IsArmed) { _visit.Disarm(); ReleaseHands(); }   // one way in at a time
            if (_meetCard == null)
                _meetCard = new WallCard(nearUserRig, "WallMeetCard", keyTemplate, font, ArtHelper(), new Vector2(0.16f, 0.13f), keyScale);
            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            _meetCard.Place(nearUserRig.TransformPoint(visitCardLocal), eye, 12f);   // where the portal card will appear
            _meetCard.SetText(MeetCopy.ChooserTitle, MeetCopy.ChooserBody, MeetCopy.ChooserFoot);
            _meetCard.Show(true);
            _meetCard.SetKeys(ChooserKeys());
            _meetOpenedAt = Time.unscaledTime;
            _meetOpens++;
            Note(MeetCopy.ChooseOnCard);
            RefreshDock();
        }

        List<KeyValuePair<string, Action>> ChooserKeys()
        {
            var keys = new List<KeyValuePair<string, Action>>(5);
            for (int i = 0; i < MeetCopy.Agents.Length; i++)
            {
                string a = MeetCopy.Agents[i];
                keys.Add(new KeyValuePair<string, Action>(AgentPresentationRegistry.Pretty(a), () => MeetAgent(a)));
            }
            // Cancel bottom left: where Return sits on the visit card.
            keys.Add(new KeyValuePair<string, Action>(MeetCopy.Cancel, () => { CloseMeetChooser(); Note(""); }));
            return keys;
        }

        void CloseMeetChooser()
        {
            if (_meetCard == null || !_meetCard.Visible) return;
            _meetCard.Show(false);
            RefreshDock();
        }

        // ───────────────────────── per frame (from TickInteraction) ─────────────────────────

        void TickMeet(float dt)
        {
            SyncMeetVoiceHook();

            // The status line names Meet once each time the habitat comes to rest on a wall (placed, or restored).
            bool atRest = WallActive && !WallPreviewing;
            if (atRest && !_wasAtRest && !_meetHintShown && flags.largeAgentPortalEnabled)
            {
                _meetHintShown = true;
                _note = MeetCopy.PlacedHint;
                _noteUntil = Time.unscaledTime + Mathf.Max(4f, meetHintSeconds);
                RefreshDock();
            }
            if (!WallActive) _meetHintShown = false;
            _wasAtRest = atRest;

            if (!MeetChoosing) return;
            // The chooser only lives while nothing else is going on: anything that starts elsewhere closes it.
            if (!atRest || !flags.largeAgentPortalEnabled || _visit.Busy || _portal.State != PortalState.Hidden
                || Time.unscaledTime - _meetOpenedAt > Mathf.Max(5f, meetChooserTimeout))
                CloseMeetChooser();
        }

        // ───────────────────────── voice ─────────────────────────

        /// <summary>Registered only while the habitat is on the wall with the portal flag on; otherwise the voice path is
        /// exactly what it was before (the sentence falls through to the normal grammar).</summary>
        void SyncMeetVoiceHook()
        {
            if (_meetVoice == null) _meetVoice = MeetByVoice;
            bool want = isActiveAndEnabled && flags.wallHabitatEnabled && flags.largeAgentPortalEnabled && WallActive;
            if (want) { if (MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler != _meetVoice) MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler = _meetVoice; }
            else ClearMeetVoiceHook();
        }

        void ClearMeetVoiceHook()
        {
            if (_meetVoice != null && MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler == _meetVoice)
                MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler = null;
        }

        /// <summary>"Researcher, come here": the same preview the chooser opens. Null (not handled) outside Wall mode.</summary>
        string MeetByVoice(MuseDesk.Voice.VoiceAgentTarget t)
        {
            if (this == null || !isActiveAndEnabled || !flags.largeAgentPortalEnabled || !WallActive) return null;
            HabitatRole role;
            if (!TryVoiceRole(t, out role)) return null;
            string agent = AgentId(role);
            if (MeetAgent(agent)) return MeetCopy.VoiceOpened(AgentPresentationRegistry.Pretty(agent));
            return string.IsNullOrEmpty(LastMeetRefusal) ? "Not now." : LastMeetRefusal + ".";
        }

        /// <summary>One line for drivers and logs.</summary>
        public string DescribeMeet()
        {
            return "meet enabled=" + WallMeetEnabled + " choosing=" + MeetChoosing + " opens=" + _meetOpens + " starts=" + _meetStarts
                 + " refusals=" + _meetRefusals + (LastMeetRefusal != null ? " last='" + LastMeetRefusal + "'" : "")
                 + " | portal=" + _portal.State + (_portal.Agent != null ? ":" + _portal.Agent : "") + " spot=" + _portal.Spot.position.ToString("F2")
                 + " h=" + _portal.Spot.avatarHeight.ToString("F2") + " valid=" + _portal.Spot.valid + " checked=" + _portal.Spot.roomChecked
                 + (PortalSpotIsFallback ? " FALLBACK(small)" : "")
                 + " | visit=" + _visit.State + " away=" + _registry.AwayCount
                 + " | guard checks=" + _comfortGuard.Checks + " closures=" + _comfortGuard.Closures + " blocks=" + _comfortGuard.Blocks
                 + (string.IsNullOrEmpty(_comfortGuard.LastReason) ? "" : " reason='" + _comfortGuard.LastReason + "'")
                 + " | voiceHook=" + (_meetVoice != null && MuseDesk.Voice.VoiceCommandCenter.PortalRequestHandler == _meetVoice);
        }
    }
}
