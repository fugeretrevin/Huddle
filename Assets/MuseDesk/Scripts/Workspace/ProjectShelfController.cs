using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// The completed-project display shelf. Each REAL backend TaskCompleted event places one
    /// artifact token on the next shelf slot, and reaching a milestone count reveals a
    /// progress-based award (trophy/plaque). Nothing here is produced by a timer or a guess —
    /// only confirmed completions advance the shelf. Idempotent-friendly: tokens are capped to
    /// the available slots. Cosmetic only; it authorizes nothing.
    /// </summary>
    public class ProjectShelfController : MonoBehaviour
    {
        public SessionController controller;

        [Tooltip("Shelf positions a completed-artifact token is placed on, left to right.")]
        public List<Transform> slots = new List<Transform>();
        [Tooltip("Prefab dropped on a slot for each completed task (e.g. Collab_ArtifactToken).")]
        public GameObject completedTokenPrefab;
        public float tokenScale = 1f;

        [Header("Progress awards")]
        [Tooltip("Award objects revealed when the completed count reaches the matching threshold.")]
        public List<GameObject> awards = new List<GameObject>();
        public List<int> awardThresholds = new List<int>();

        int _completed;
        bool _bound;

        void Awake()
        {
            foreach (var a in awards) if (a != null) a.SetActive(false);
        }

        void Update() { if (!_bound && controller != null) Bind(); }

        void Bind()
        {
            controller.Reducer.TaskCompleted += OnTaskCompleted;
            controller.Reducer.SessionReset += OnReset;
            _bound = true;
        }

        void OnDisable()
        {
            if (_bound && controller != null)
            {
                controller.Reducer.TaskCompleted -= OnTaskCompleted;
                controller.Reducer.SessionReset -= OnReset;
            }
            _bound = false;
        }

        void OnTaskCompleted(EventEnvelope e)
        {
            if (_completed < slots.Count && slots[_completed] != null && completedTokenPrefab != null)
            {
                var slot = slots[_completed];
                var token = Instantiate(completedTokenPrefab, slot);
                token.name = "CompletedArtifact_" + _completed;
                token.transform.localPosition = Vector3.zero;
                token.transform.localRotation = Quaternion.identity;
                token.transform.localScale = Vector3.one * tokenScale;
            }
            _completed++;
            RevealAwards();
        }

        void RevealAwards()
        {
            for (int i = 0; i < awards.Count && i < awardThresholds.Count; i++)
                if (awards[i] != null && _completed >= awardThresholds[i])
                    awards[i].SetActive(true);
        }

        void OnReset()
        {
            _completed = 0;
            foreach (var a in awards) if (a != null) a.SetActive(false);
            foreach (var s in slots)
                if (s != null)
                    for (int i = s.childCount - 1; i >= 0; i--)
                    {
                        var c = s.GetChild(i);
                        if (c.name.StartsWith("CompletedArtifact_")) Destroy(c.gameObject);
                    }
        }
    }
}
