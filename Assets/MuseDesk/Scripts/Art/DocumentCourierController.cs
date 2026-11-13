using System;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Controls the FloorCourierStation and DocumentCourier 7-step delivery lifecycle.
    /// Calculates the flight arc dynamically in runtime world space between the live
    /// station dock pose and the user interaction pose (never uses baked world routes).
    /// </summary>
    [DisallowMultipleComponent]
    public class DocumentCourierController : MonoBehaviour
    {
        public enum CourierState
        {
            DockedIdle,
            LoadingDocument,
            Departing,
            TravelingToUser,
            Presenting,
            WaitingForInteraction,
            ReturningToStation
        }

        [Header("Animators")]
        [SerializeField] private Animator courierAnimator;
        [SerializeField] private Animator stationAnimator;

        [Header("Station & Courier Transforms")]
        [SerializeField] private Transform courierRoot;
        [SerializeField] private Transform courierDockPoint;
        [SerializeField] private Transform agentLoadPoint;
        [SerializeField] private Transform documentReceiverPoint;
        [SerializeField] private Transform documentAttachSocket;
        [SerializeField] private Transform presentationColliderCenter;

        [Header("Signal Renderers (MPB)")]
        [SerializeField] private Renderer stationSignalRenderer;
        [SerializeField] private Renderer courierHoverRingRenderer;
        [SerializeField] private Renderer courierFrontLightRenderer;

        [Header("Flight Tuning")]
        [SerializeField] private float loadSeconds = 0.65f;
        [SerializeField] private float travelSeconds = 1.45f;
        [SerializeField] private float arcHeightMeters = 0.28f;
        [SerializeField] private float presentSeconds = 0.60f;

        private CourierState _state = CourierState.DockedIdle;
        private Vector3 _routeStart;
        private Vector3 _routeControl;
        private Vector3 _routeEnd;
        private Quaternion _rotStart;
        private Quaternion _rotEnd;
        private float _elapsed;
        private Transform _attachedDocument;
        private Pose _userPresentationPose;
        private Action _onArrivedCallback;
        private Action _onReturnedCallback;

        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public CourierState CurrentState => _state;
        public Transform CourierRoot => courierRoot;
        public Transform CourierDockPoint => courierDockPoint;
        public Transform AgentLoadPoint => agentLoadPoint;
        public Transform DocumentReceiverPoint => documentReceiverPoint;
        public Transform DocumentAttachSocket => documentAttachSocket;
        public Transform PresentationColliderCenter => presentationColliderCenter;

        public event Action<CourierState> StateChanged;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                switch (t.name)
                {
                    case "CourierRoot":                courierRoot = t; break;
                    case "CourierDockPoint":           courierDockPoint = t; break;
                    case "AgentLoadPoint":             agentLoadPoint = t; break;
                    case "DocumentReceiverPoint":      documentReceiverPoint = t; break;
                    case "DocumentAttachSocket":       documentAttachSocket = t; break;
                    case "PresentationColliderCenter": presentationColliderCenter = t; break;
                    case "Station_SignalLight":        stationSignalRenderer = t.GetComponent<Renderer>(); break;
                    case "Courier_HoverRing":          courierHoverRingRenderer = t.GetComponent<Renderer>(); break;
                    case "Courier_FrontLight":         courierFrontLightRenderer = t.GetComponent<Renderer>(); break;
                }
            }

            if (courierAnimator == null && courierRoot != null)
                courierAnimator = courierRoot.GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Mounts a carried document onto DocumentAttachSocket while preserving its intended world scale.
        /// </summary>
        public void AttachDocument(Transform docTransform, Vector3 desiredWorldScale)
        {
            _attachedDocument = docTransform;
            if (docTransform != null && documentAttachSocket != null)
            {
                SpatialScaleUtility.MountPreservingWorldScale(docTransform, documentAttachSocket, desiredWorldScale, true);
            }
        }

        public void DetachDocument()
        {
            _attachedDocument = null;
        }

        /// <summary>
        /// Starts the 7-step delivery sequence from the station to a runtime user presentation pose.
        /// </summary>
        public void DispatchToUserPose(Pose targetPresentationPose, Action onArrived = null)
        {
            _userPresentationPose = targetPresentationPose;
            _onArrivedCallback = onArrived;
            _elapsed = 0f;
            SetState(CourierState.LoadingDocument, "Courier_Load_Document", "Station_Load_Lift",
                     new Color(0.878f, 0.627f, 0.188f, 1f), 0.45f);
        }

        /// <summary>
        /// Completes interaction and flies the courier back to the live station dock point.
        /// </summary>
        public void ReturnToStation(Action onReturned = null)
        {
            _onReturnedCallback = onReturned;
            Vector3 from = courierRoot != null ? courierRoot.position : transform.position;
            Vector3 to = courierDockPoint != null ? courierDockPoint.position : transform.position;
            Quaternion qFrom = courierRoot != null ? courierRoot.rotation : Quaternion.identity;
            Quaternion qTo = courierDockPoint != null ? courierDockPoint.rotation : Quaternion.identity;

            ConfigureRoute(from, to, qFrom, qTo);
            _elapsed = 0f;
            SetState(CourierState.ReturningToStation, "Courier_Fly_Loop", "Station_Idle",
                     new Color(0.184f, 0.639f, 0.604f, 1f), 0.30f);
        }

        public void SnapDockedImmediate()
        {
            if (courierRoot != null && courierDockPoint != null)
                courierRoot.SetPositionAndRotation(courierDockPoint.position, courierDockPoint.rotation);
            SetState(CourierState.DockedIdle, "Courier_Docked_Idle", "Station_Idle",
                     new Color(0.184f, 0.639f, 0.604f, 1f), 0.20f);
        }

        private void Update()
        {
            switch (_state)
            {
                case CourierState.DockedIdle:
                    if (courierRoot != null && courierDockPoint != null)
                        courierRoot.SetPositionAndRotation(courierDockPoint.position, courierDockPoint.rotation);
                    break;

                case CourierState.LoadingDocument:
                    _elapsed += Time.deltaTime;
                    if (_elapsed >= loadSeconds)
                    {
                        Vector3 from = courierRoot != null ? courierRoot.position : transform.position;
                        Quaternion qFrom = courierRoot != null ? courierRoot.rotation : Quaternion.identity;
                        ConfigureRoute(from, _userPresentationPose.position, qFrom, _userPresentationPose.rotation);
                        _elapsed = 0f;
                        SetState(CourierState.TravelingToUser, "Courier_Fly_Loop", "Station_Dispatch_Release",
                                 new Color(0.184f, 0.639f, 0.604f, 1f), 0.38f);
                    }
                    break;

                case CourierState.TravelingToUser:
                    _elapsed += Time.deltaTime;
                    float u = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, travelSeconds));
                    float k = Mathf.SmoothStep(0f, 1f, u);
                    if (courierRoot != null)
                    {
                        courierRoot.position = EvalQuadraticBezier(_routeStart, _routeControl, _routeEnd, k);
                        courierRoot.rotation = Quaternion.Slerp(_rotStart, _rotEnd, k);
                    }
                    if (u >= 1f)
                    {
                        _elapsed = 0f;
                        SetState(CourierState.Presenting, "Courier_Arrive_Present", "Station_Idle",
                                 new Color(0.180f, 0.608f, 0.373f, 1f), 0.45f);
                    }
                    break;

                case CourierState.Presenting:
                    _elapsed += Time.deltaTime;
                    if (_elapsed >= presentSeconds)
                    {
                        SetState(CourierState.WaitingForInteraction, "Courier_Waiting_Idle", "Station_Idle",
                                 new Color(0.878f, 0.627f, 0.188f, 1f), 0.35f);
                        var cb = _onArrivedCallback;
                        _onArrivedCallback = null;
                        cb?.Invoke();
                    }
                    break;

                case CourierState.ReturningToStation:
                    // Dynamically track live station dock position in case the floor platform moved!
                    if (courierDockPoint != null)
                    {
                        _routeEnd = courierDockPoint.position;
                        _rotEnd = courierDockPoint.rotation;
                        _routeControl = (_routeStart + _routeEnd) * 0.5f + Vector3.up * arcHeightMeters;
                    }
                    _elapsed += Time.deltaTime;
                    float ur = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, travelSeconds));
                    float kr = Mathf.SmoothStep(0f, 1f, ur);
                    if (courierRoot != null)
                    {
                        courierRoot.position = EvalQuadraticBezier(_routeStart, _routeControl, _routeEnd, kr);
                        courierRoot.rotation = Quaternion.Slerp(_rotStart, _rotEnd, kr);
                    }
                    if (ur >= 1f)
                    {
                        SetState(CourierState.DockedIdle, "Courier_Return_Dock", "Station_Dock_Receive",
                                 new Color(0.184f, 0.639f, 0.604f, 1f), 0.20f);
                        var cb = _onReturnedCallback;
                        _onReturnedCallback = null;
                        cb?.Invoke();
                    }
                    break;
            }
        }

        private void ConfigureRoute(Vector3 start, Vector3 end, Quaternion qStart, Quaternion qEnd)
        {
            _routeStart = start;
            _routeEnd = end;
            _routeControl = (start + end) * 0.5f + Vector3.up * arcHeightMeters;
            _rotStart = qStart;
            _rotEnd = qEnd;
        }

        private static Vector3 EvalQuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float omt = 1f - t;
            return omt * omt * a + 2f * omt * t * b + t * t * c;
        }

        private void SetState(CourierState next, string courierClip, string stationClip, Color signalColor, float emission)
        {
            _state = next;
            PlayClip(courierAnimator, courierClip, 0.14f);
            PlayClip(stationAnimator, stationClip, 0.14f);
            ApplySignalColor(stationSignalRenderer, signalColor, emission);
            ApplySignalColor(courierHoverRingRenderer, signalColor, emission);
            ApplySignalColor(courierFrontLightRenderer, signalColor, emission);
            StateChanged?.Invoke(_state);
        }

        private void PlayClip(Animator anim, string clipName, float fade)
        {
            if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(clipName)) return;
            int hash = Animator.StringToHash(clipName);
            if (anim.HasState(0, hash))
                anim.CrossFadeInFixedTime(hash, fade, 0);
        }

        private void ApplySignalColor(Renderer r, Color col, float emission)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(EmissionColorId, col * emission);
            r.SetPropertyBlock(_mpb);
        }
    }
}

