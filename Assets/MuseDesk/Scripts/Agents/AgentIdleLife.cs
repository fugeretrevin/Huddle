using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Idle life on the torso: slow breathing (a small chest swell and spine rock) and a rare small
    /// weight shift, layered on the Spine and Chest bones in LateUpdate after the Animator. Each
    /// agent gets its own phase and timing so the three never move together.
    ///
    /// It follows the acknowledged state and never contradicts it:
    ///   walking or a reaction clip playing  : off (the clip owns the body);
    ///   working at the desk                 : breathing reduced, no fidgets (the clip is the motion);
    ///   waiting / error                     : calm breathing only, no fidgets (not busy, not happy);
    ///   idle / complete                     : full breathing, occasional weight shift.
    /// Focus mode makes the fidgets rarer. While the agent is listening (AgentGaze.SetListening) it
    /// leans slightly toward the user and holds still; while speaking, a smaller lean.
    /// Presentation only. With no Spine and no Chest bone it switches itself off.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public class AgentIdleLife : MonoBehaviour
    {
        [Header("Rig (assigned by AgentScripts/BuildAgentLiveliness; found by name if left empty)")]
        public Transform spine;
        public Transform chest;

        [Header("Wiring (optional)")]
        public AgentView view;
        public AgentMovement movement;
        public AgentAnimatorBridge bridge;
        public AgentGaze gaze;

        [Header("Breathing")]
        [Tooltip("Seconds per breath.")]
        public float breathPeriod = 3.5f;
        [Tooltip("Chest swell at the top of a breath (0.016 = 1.6%).")]
        public float breathScale = 0.016f;
        [Tooltip("Spine rock with each breath, degrees.")]
        public float breathPitch = 0.7f;
        [Range(0f, 1f)]
        [Tooltip("Breathing amount while a working clip plays.")]
        public float workingAmount = 0.4f;

        [Header("Fidgets (small weight shifts)")]
        public float fidgetIntervalMin = 9f;
        public float fidgetIntervalMax = 20f;
        [Tooltip("Size of a weight shift, degrees.")]
        public float fidgetDegrees = 2.5f;
        [Tooltip("In focus mode fidgets come this many times less often.")]
        public float focusFidgetSlowdown = 2f;

        [Header("Voice visuals")]
        [Tooltip("Forward lean toward the user while listening, degrees.")]
        public float listenLean = 4f;

        BoneRotationLayer _spineLayer, _chestLayer;
        BoneScaleLayer _chestScale;

        float _phase;
        float _amount;          // smoothed breathing amount, 0..1
        float _lean;            // smoothed forward lean, degrees
        float _nextFidget;
        float _fidgetStart = -100f, _fidgetLength = 1f, _fidgetSign = 1f;
        bool _fidgetYaw;

        void Awake()
        {
            if (view == null) view = GetComponent<AgentView>();
            if (movement == null) movement = GetComponent<AgentMovement>();
            if (bridge == null) bridge = GetComponent<AgentAnimatorBridge>();
            if (gaze == null) gaze = GetComponent<AgentGaze>();
            if (spine == null || chest == null)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (spine == null && all[i].name == "Spine") spine = all[i];
                    else if (chest == null && all[i].name == "Chest") chest = all[i];
                }
            }
            if (spine == null && chest == null) { enabled = false; return; }

            _phase = Random.value;
            _nextFidget = Time.time + Random.Range(fidgetIntervalMin * 0.5f, fidgetIntervalMax);
        }

        void OnDisable()
        {
            _spineLayer.Release(spine);
            _chestLayer.Release(chest);
            _chestScale.Release(chest);
            _amount = 0f;
            _lean = 0f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float now = Time.time;

            bool moving = movement != null && movement.IsMoving;
            bool reacting = bridge != null && bridge.ReactionPlaying;
            bool working = bridge != null && bridge.IsWorkingState;
            bool listening = gaze != null && gaze.IsListening;
            bool speaking = gaze != null && gaze.IsSpeaking;
            string state = view != null ? view.State : AgentStates.Idle;
            bool subdued = state == AgentStates.Waiting || state == AgentStates.Error;

            float wantAmount;
            if (moving || reacting) wantAmount = 0f;
            else if (working) wantAmount = workingAmount;
            else if (listening) wantAmount = 0.5f;
            else if (subdued) wantAmount = 0.7f;
            else wantAmount = 1f;

            float wantLean = (moving || reacting) ? 0f : (listening ? listenLean : (speaking ? listenLean * 0.35f : 0f));

            float k = 1f - Mathf.Exp(-dt / 0.3f);
            _amount = Mathf.Lerp(_amount, wantAmount, k);
            _lean = Mathf.Lerp(_lean, wantLean, k);

            // Breathing.
            float breath = Mathf.Sin((now / Mathf.Max(0.5f, breathPeriod) + _phase) * 6.2831853f);
            float swell = breathScale * _amount * (0.5f + 0.5f * breath);
            float rock = -breathPitch * _amount * breath;        // chest lifts on the in-breath

            // Fidget: one small weight shift, eased in and out. Only when truly at rest.
            bool restful = !moving && !reacting && !working && !subdued && !listening && !speaking;
            if (now >= _nextFidget)
            {
                float slow = MuseDesk.Workspace.FocusModeController.Active ? Mathf.Max(1f, focusFidgetSlowdown) : 1f;
                _nextFidget = now + Random.Range(fidgetIntervalMin, fidgetIntervalMax) * slow;
                if (restful)
                {
                    _fidgetStart = now;
                    _fidgetLength = Random.Range(1.2f, 1.9f);
                    _fidgetSign = Random.value < 0.5f ? -1f : 1f;
                    _fidgetYaw = Random.value < 0.4f;
                }
            }
            float fidget = 0f;
            float ft = (now - _fidgetStart) / _fidgetLength;
            if (ft >= 0f && ft < 1f)
            {
                float e = Mathf.Sin(ft * Mathf.PI);
                // Fades with the breathing amount, so it dies away if the agent starts doing something.
                fidget = _fidgetSign * fidgetDegrees * e * e * _amount;
            }

            Quaternion rootRot = transform.rotation;
            Quaternion rootInv = Quaternion.Inverse(rootRot);

            if (spine != null)
            {
                if (Mathf.Abs(rock) < 0.005f && Mathf.Abs(fidget) < 0.005f) _spineLayer.Release(spine);
                else
                {
                    Quaternion d = rootRot * Quaternion.Euler(rock, _fidgetYaw ? fidget : 0f, _fidgetYaw ? 0f : fidget) * rootInv;
                    _spineLayer.Apply(spine, d);
                }
            }

            if (chest != null)
            {
                if (Mathf.Abs(_lean) < 0.01f) _chestLayer.Release(chest);
                else _chestLayer.Apply(chest, rootRot * Quaternion.Euler(_lean, 0f, 0f) * rootInv);

                if (swell < 0.0002f) _chestScale.Release(chest);
                else _chestScale.Apply(chest, new Vector3(1f + swell, 1f + swell * 0.35f, 1f + swell));
            }
        }
    }
}
