using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// What each hand's Interaction SDK interactors are doing, for <see cref="HandOwnershipArbiter"/>.
    /// READ ONLY: it finds the interactors the rig already has (no rig, no interactor, no collider is
    /// added; no second interaction framework) and reads their hover and select state.
    ///
    ///   holding ... a grab-type interactor (HandGrab, DistanceHandGrab, TouchHandGrab, Grab, ...) has
    ///               something selected: a paper, a stamp, a card, a panel grab bar;
    ///   nearHover . a grab-type interactor hovers something (near, or a distance grab's target);
    ///   uiHover ... a PokeInteractor or a RayInteractor hovers or selects something (keys, cards,
    ///               canvases, the agents' ray targets). Any other interactor that SELECTS counts here too.
    ///
    /// Interactor groups are skipped (their members are read). The hand of an interactor comes from its
    /// IHand (grab interactors), else a HandRef above it, else "Left"/"Right" in a parent's name; an
    /// interactor whose hand cannot be told counts for BOTH hands (fail-closed: it can only block).
    /// Found once, inactive ones included (again at most every 5 s while none were found, or when one was destroyed); one
    /// reading per frame is cached, so both gestures share it. Allocation-free after the search.
    /// </summary>
    public static class HandInteractionProbe
    {
        enum Kind { Grab = 0, Poke = 1, Ray = 2, Other = 3 }

        struct Entry
        {
            public IInteractor interactor;
            public Object owner;               // the MonoBehaviour, for Unity's destroyed check
            public int side;                   // 0 left, 1 right, -1 unknown (both)
            public Kind kind;
        }

        public struct Reading
        {
            public bool holding, nearHover, uiHover;
        }

        static readonly List<Entry> _entries = new List<Entry>(32);
        static bool _found;
        static float _nextSearch;
        static int _frame = -1;
        static Reading _left, _right;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _entries.Clear(); _found = false; _nextSearch = 0f; _frame = -1;
            _left = default(Reading); _right = default(Reading);
        }

        /// <summary>How many interactors are being read (for a driver's report).</summary>
        public static int InteractorCount { get { Refresh(); return _entries.Count; } }

        /// <summary>This frame's reading for a hand (0 left, 1 right).</summary>
        public static Reading Read(int hand)
        {
            Refresh();
            return hand == 0 ? _left : _right;
        }

        /// <summary>True when anything is held: a grab on either hand, or a paper's transition hold.</summary>
        public static bool AnyHeld
        {
            get
            {
                if (SpatialWorkspaceState.TransitionHeld) return true;
                Refresh();
                return _left.holding || _right.holding;
            }
        }

        /// <summary>Fill a hand sample with this frame's reading (OR-ed onto what it already says).</summary>
        public static AimHandSample Apply(AimHandSample s, int hand)
        {
            Reading r = Read(hand);
            return s.With(s.holding || r.holding, s.nearHover || r.nearHover, s.uiHover || r.uiHover);
        }

        static void Refresh()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            _left = default(Reading);
            _right = default(Reading);

            if (!_found)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 5f;
                Find();
                if (!_found) return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.owner == null) { _found = false; _nextSearch = 0f; continue; }   // destroyed: search again next frame
                IInteractor it = e.interactor;
                bool selected = it.HasSelectedInteractable;
                bool hovering = it.HasInteractable;
                if (!selected && !hovering) continue;
                bool holding = false, near = false, ui = false;
                switch (e.kind)
                {
                    case Kind.Grab: holding = selected; near = hovering && !selected; break;
                    case Kind.Poke:
                    case Kind.Ray: ui = true; break;
                    default: ui = selected; break;
                }
                if (e.side != 1) { _left.holding |= holding; _left.nearHover |= near; _left.uiHover |= ui; }
                if (e.side != 0) { _right.holding |= holding; _right.nearHover |= near; _right.uiHover |= ui; }
            }
        }

        static void Find()
        {
            _entries.Clear();
            // Inactive too: a hand that is not tracked at search time has its interactor objects switched off,
            // and would otherwise never be read. An inactive interactor reports no hover and no selection.
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                IInteractor it = mb as IInteractor;
                if (it == null || mb is InteractorGroup) continue;
                Entry e;
                e.interactor = it;
                e.owner = mb;
                e.kind = mb is PokeInteractor ? Kind.Poke
                       : mb is RayInteractor ? Kind.Ray
                       : mb.GetType().Name.IndexOf("Grab", System.StringComparison.Ordinal) >= 0 ? Kind.Grab
                       : Kind.Other;
                e.side = SideOf(mb);
                _entries.Add(e);
            }
            _found = _entries.Count > 0;
        }

        static int SideOf(MonoBehaviour mb)
        {
            IHand hand = null;
            HandGrabInteractor hg = mb as HandGrabInteractor;
            if (hg != null) hand = hg.Hand;
            DistanceHandGrabInteractor dg = mb as DistanceHandGrabInteractor;
            if (hand == null && dg != null) hand = dg.Hand;
            if (hand == null)
            {
                HandRef r = mb.GetComponentInParent<HandRef>(true);
                if (r != null && r.Hand != null) hand = r.Hand;
            }
            if (hand != null) return hand.Handedness == Handedness.Left ? 0 : 1;
            for (Transform t = mb.transform; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            }
            return -1;
        }

        /// <summary>One line per interactor (side, kind, hover, select) for a Play Mode driver.</summary>
        public static string Describe()
        {
            Refresh();
            var sb = new System.Text.StringBuilder(256);
            sb.Append("probe: ").Append(_entries.Count).Append(" interactors; L(hold=").Append(_left.holding).Append(" near=").Append(_left.nearHover)
              .Append(" ui=").Append(_left.uiHover).Append(") R(hold=").Append(_right.holding).Append(" near=").Append(_right.nearHover)
              .Append(" ui=").Append(_right.uiHover).Append(") transitionHeld=").Append(SpatialWorkspaceState.TransitionHeld);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.owner == null) continue;
                sb.Append("\n  ").Append(e.side == 0 ? "L " : e.side == 1 ? "R " : "? ").Append(e.kind).Append(' ')
                  .Append(e.owner.GetType().Name).Append(" '").Append(((MonoBehaviour)e.owner).name).Append("'");
            }
            return sb.ToString();
        }
    }
}
