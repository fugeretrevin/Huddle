using TMPro;
using UnityEngine;
using MuseDesk.Documents;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// The poke target on the out-tray's stack of filed papers. Poking it opens the record of the
    /// newest filed decision and steps to the next one on each further poke
    /// (DecisionArchive.ShowNext, what the old "Filed" key did). Its cap reads "Filed" and the
    /// number of filed decisions.
    ///
    /// While nothing has been filed there is nothing to poke: the target is hidden by scale (it
    /// stays active so the Interaction SDK registers it at start-up) and a poke is ignored.
    /// </summary>
    public class ArchiveStackPoke : MonoBehaviour
    {
        public DecisionArchive archive;
        [Tooltip("Label on the cap: 'Filed 2'.")]
        public TMP_Text countLabel;
        [Tooltip("Scale of the target when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = new Vector3(0.04f, 0.04f, 0.04f);

        readonly ScaleEase _ease = new ScaleEase();
        DecisionArchive _boundArchive;
        int _count = -1;
        bool _wasAnimating;

        void Awake()
        {
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), 0.22f, false);
        }

        void OnEnable() { Refresh(); }

        void Update()
        {
            if (_boundArchive == null && archive != null)
            {
                _boundArchive = archive;
                _boundArchive.FiledCountChanged += Refresh;
                Refresh();
            }
            _ease.Tick(Time.deltaTime);
            bool animating = _ease.Animating;
            if (_wasAnimating && !animating && _ease.Shown)
                BroadcastMessage("SyncToCurrentScale", SendMessageOptions.DontRequireReceiver);
            _wasAnimating = animating;
        }

        void OnDisable()
        {
            if (_boundArchive != null) _boundArchive.FiledCountChanged -= Refresh;
            _boundArchive = null;
        }

        void Refresh()
        {
            int count = archive != null ? archive.FiledCount : 0;
            if (count == _count) return;
            bool grew = _count >= 0 && count > _count;
            _count = count;
            if (countLabel != null) countLabel.text = count > 0 ? "Filed " + count : "Filed";
            if (count > 0)
            {
                if (_ease.Shown) { if (grew) _ease.Pulse(0.06f, 0.3f); }
                else _ease.Show();
            }
            else _ease.Hide();
        }

        /// <summary>Poke target: open the filed record, or step to the next one. Ignored while
        /// nothing is filed.</summary>
        public void Poke()
        {
            if (archive == null || archive.FiledCount <= 0) return;
            archive.ShowNext();
        }
    }
}
