using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Wall
{
    /// <summary>
    /// rc18 (owner, worn rc17): "we should be able to move the manager paper as well" and "the plan summary should be movable".
    /// The Manager's plan paper / status page and the plan summary receipt each get a small grab handle under their bottom
    /// edge. Pinch near the handle and the card follows the hand (smoothed, always turned to face the eyes); let go and it
    /// stays. Once moved, the card opens there again (remembered in the near-user rig's space, so it follows the desk).
    /// Presentation only: moving a card never presses a key, approves or changes anything.
    /// </summary>
    public partial class WallHabitatController
    {
        [Header("Movable cards (rc18)")]
        public bool movableCards = true;
        [Tooltip("How close (m) the pinching hand must be to a card's handle to pick the card up.")]
        public float cardGrabReach = 0.075f;

        sealed class CardDrag
        {
            public WallCard card;
            public Transform handle;
            public Renderer handleR;
            public int hand = -1;
            public Vector3 offset;
            public bool userMoved;
            public Vector3 userLocal;
            public int hoverState = -1;
        }

        readonly List<CardDrag> _cardDrags = new List<CardDrag>(2);
        readonly bool[] _cardPrevPinch = new bool[2];
        static readonly Color HandleIdle = new Color(0.62f, 0.66f, 0.72f), HandleHover = new Color(0.86f, 0.95f, 0.95f);

        /// <summary>For drivers: the card being dragged, if any (its name), else null.</summary>
        public string DraggedCard { get { for (int i = 0; i < _cardDrags.Count; i++) if (_cardDrags[i].hand >= 0) return _cardDrags[i].card.root.name; return null; } }

        CardDrag DragFor(WallCard c)
        {
            for (int i = 0; i < _cardDrags.Count; i++) if (_cardDrags[i].card == c) return _cardDrags[i];
            var d = new CardDrag { card = c };
            Vector2 size = c.Size;
            d.handleR = WallUi.Plate(c.root, "GrabHandle", new Vector3(0f, -size.y * 0.5f - 0.012f, 0.002f), 0.06f, 0.0075f, 0.0037f,
                                     HandleIdle, WallUi.PlateMaterial, ArtHelper());
            d.handle = d.handleR.transform;
            _cardDrags.Add(d);
            return d;
        }

        /// <summary>Where a card opens: where the user last left it, else its usual spot.</summary>
        Vector3 CardSpot(WallCard c, Vector3 usualWorld)
        {
            if (!movableCards || c == null || nearUserRig == null) return usualWorld;
            for (int i = 0; i < _cardDrags.Count; i++)
                if (_cardDrags[i].card == c && _cardDrags[i].userMoved) return nearUserRig.TransformPoint(_cardDrags[i].userLocal);
            return usualWorld;
        }

        void TickCardDrag(float dt)
        {
            TickScreen(dt);                                      // rc20: the PC screen panel (WallHabitatController.Screen.cs)
            if (!movableCards || nearUserRig == null) return;
            if (_planCard != null) DragFor(_planCard);
            if (_receiptCard != null) DragFor(_receiptCard);
            if (_visitCard != null) DragFor(_visitCard);       // rc20 (worn rc19): "this card should be movable"
            if (_cardDrags.Count == 0) return;

            Transform h = ResolveHead();
            Vector3 eye = h != null ? h.position : nearUserRig.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
            for (int i = 0; i < 2; i++)
            {
                AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                bool pinch = s.tracked && s.pinching;
                bool began = pinch && !_cardPrevPinch[i];
                _cardPrevPinch[i] = pinch;
                for (int k = 0; k < _cardDrags.Count; k++)
                {
                    CardDrag d = _cardDrags[k];
                    if (d.card.root == null) continue;
                    if (!d.card.Visible) { if (d.hand == i) EndCardDrag(d); continue; }
                    if (d.hand == i)
                    {
                        if (!pinch) { EndCardDrag(d); continue; }
                        Vector3 target = s.origin + d.offset;
                        Vector3 cur = d.card.root.position;
                        Vector3 pos = Vector3.Lerp(cur, target, 1f - Mathf.Exp(-dt * 18f));
                        d.card.root.SetPositionAndRotation(pos, WallUi.FacingEye(pos, eye, 12f));
                        continue;
                    }
                    if (d.hand >= 0 || !s.tracked) continue;
                    bool near = (s.origin - d.handle.position).sqrMagnitude <= cardGrabReach * cardGrabReach;
                    if (began && near && i != _carryHand && i != _aimHand && !HandBusyWithCard(i))
                    {
                        d.hand = i;
                        d.offset = d.card.root.position - s.origin;
                        Note("Moving the " + (d.card == _receiptCard ? "summary" : d.card == _visitCard ? "card" : "plan"));
                    }
                }
            }
            // Handle colour: brighter while a free hand is close or while held.
            for (int k = 0; k < _cardDrags.Count; k++)
            {
                CardDrag d = _cardDrags[k];
                if (d.handleR == null || !d.card.Visible) continue;
                bool close = d.hand >= 0;
                for (int i = 0; i < 2 && !close; i++)
                {
                    AimHandSample s = _simOn[i] ? _sim[i] : ReadHand(i);
                    close = s.tracked && (s.origin - d.handle.position).sqrMagnitude <= cardGrabReach * cardGrabReach;
                }
                int state = d.hand >= 0 ? 2 : close ? 1 : 0;
                if (state == d.hoverState) continue;
                d.hoverState = state;
                ArtHelper().Tint(d.handleR, state == 2 ? WallHabitatView.Teal : state == 1 ? HandleHover : HandleIdle);
            }
        }

        /// <summary>rc20: the voice readout's way out of the Wall's cards (see VoiceHud.Clearance). For each visible card
        /// the readout overlaps (in the card's plane, within 6 cm of it), the offset that puts the readout 5 cm in front of
        /// the card, toward the eyes. Zero when it is clear.</summary>
        Vector3 ReadoutClearance(Bounds b)
        {
            // rc21: measured in metres along the card's own (unit) axes, so a card mid-pop (scaled) is judged at its real
            // size, and the readout's own depth counts (it is now tilted toward the eyes, rc21 VoiceHud.PlaceForEye): the
            // readout's back must end up 3 cm in front of the card.
            Transform h = ResolveHead();
            if (h == null) return Vector3.zero;
            Vector3 best = Vector3.zero;
            Vector3 ext = b.extents;
            WallCard[] cards = { _planCard, _visitCard, _receiptCard };
            for (int i = 0; i < cards.Length; i++)
            {
                WallCard c = cards[i];
                if (c == null || c.root == null || !c.Visible) continue;
                Transform t = c.root;
                Vector3 toEye = h.position - t.position;
                if (toEye.sqrMagnitude < 1e-6f) continue;
                toEye.Normalize();
                Vector3 rel = b.center - t.position;
                Vector3 right = t.right, up = t.up;
                // The readout box's half size along each card axis.
                float hx = Mathf.Abs(right.x) * ext.x + Mathf.Abs(right.y) * ext.y + Mathf.Abs(right.z) * ext.z;
                float hy = Mathf.Abs(up.x) * ext.x + Mathf.Abs(up.y) * ext.y + Mathf.Abs(up.z) * ext.z;
                float hd = Mathf.Abs(toEye.x) * ext.x + Mathf.Abs(toEye.y) * ext.y + Mathf.Abs(toEye.z) * ext.z;
                Vector2 half = c.Size * 0.5f;
                if (Mathf.Abs(Vector3.Dot(rel, right)) > half.x + hx || Mathf.Abs(Vector3.Dot(rel, up)) > half.y + hy) continue;
                float ahead = Vector3.Dot(rel, toEye);          // how far in front of the card the readout's centre is
                const float Gap = 0.03f;
                if (ahead - hd >= Gap) continue;                 // already clear in front
                if (ahead + hd < -0.06f) continue;               // well behind the card: not in the way
                Vector3 need = toEye * Mathf.Min(0.15f, Gap + hd - ahead);
                if (need.sqrMagnitude > best.sqrMagnitude) best = need;
            }
            return best;
        }

        bool HandBusyWithCard(int hand)
        {
            for (int k = 0; k < _cardDrags.Count; k++) if (_cardDrags[k].hand == hand) return true;
            return false;
        }

        void EndCardDrag(CardDrag d)
        {
            d.hand = -1;
            d.userMoved = true;
            d.userLocal = nearUserRig.InverseTransformPoint(d.card.root.position);
        }

        /// <summary>For drivers: move a card by a simulated pinch from its handle by <paramref name="delta"/> (world metres).</summary>
        public bool DriverDragCard(bool receipt, Vector3 delta) => DriverDragCard(receipt ? _receiptCard : _planCard, delta);

        /// <summary>For drivers: the same for the visit card (rc20).</summary>
        public bool DriverDragVisitCard(Vector3 delta) => DriverDragCard(_visitCard, delta);

        bool DriverDragCard(WallCard c, Vector3 delta)
        {
            if (c == null || !c.Visible) return false;
            CardDrag d = DragFor(c);
            Transform h = ResolveHead();
            Vector3 pos = c.root.position + delta;
            c.root.SetPositionAndRotation(pos, WallUi.FacingEye(pos, h != null ? h.position : pos + Vector3.back, 12f));
            EndCardDrag(d);
            return true;
        }
    }
}
