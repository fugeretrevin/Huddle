using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Art
{
    /// <summary>
    /// Modular art-layer bridge for the rigged Pipkin Sprite characters
    /// (Agent_Researcher, Agent_Developer, Agent_Tester).
    /// Exposes the 5 standard attachment sockets and drives the 39-clip
    /// Animator Controller from canonical agent state without owning task authority.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentAnimatorBridge : MonoBehaviour
    {
        [Header("Animator & Rig References")]
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private Transform rightHandAttach;
        [SerializeField] private Transform leftHandAttach;
        [SerializeField] private Transform headAttach;
        [SerializeField] private Transform backAttach;
        [SerializeField] private Transform documentAttach;

        private readonly Dictionary<string, Transform> _sockets = new Dictionary<string, Transform>();
        private Vector3 _lastPosition;
        private string _currentState = "Idle_Breathing";

        public Animator CharacterAnimator => characterAnimator;
        public Transform RightHandAttach => rightHandAttach;
        public Transform LeftHandAttach => leftHandAttach;
        public Transform HeadAttach => headAttach;
        public Transform BackAttach => backAttach;
        public Transform DocumentAttach => documentAttach;
        public string CurrentAnimationState => _currentState;

        private void Awake()
        {
            ResolveSockets();
            _lastPosition = transform.position;
        }

        public void ResolveSockets()
        {
            if (characterAnimator == null)
            {
                characterAnimator = GetComponentInChildren<Animator>();
            }

            _sockets.Clear();
            rightHandAttach = FindSocket("RightHand_Attach", rightHandAttach);
            leftHandAttach  = FindSocket("LeftHand_Attach",  leftHandAttach);
            headAttach      = FindSocket("Head_Attach",      headAttach);
            backAttach      = FindSocket("Back_Attach",      backAttach);
            documentAttach  = FindSocket("Document_Attach",  documentAttach);
        }

        private Transform FindSocket(string socketName, Transform existing)
        {
            if (existing != null)
            {
                _sockets[socketName] = existing;
                return existing;
            }

            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == socketName)
                {
                    _sockets[socketName] = all[i];
                    return all[i];
                }
            }
            return null;
        }

        private void Update()
        {
            if (characterAnimator == null) return;

            float dt = Time.deltaTime;
            if (dt > 0.0001f)
            {
                Vector3 delta = transform.position - _lastPosition;
                delta.y = 0f;
                float speed = delta.magnitude / dt;
                _lastPosition = transform.position;

                // Update the MoveSpeed float parameter if the Animator has one. Looked up once (the
                // per-frame parameter scan allocated), and left alone when the backend-driven
                // MuseDesk.Agents.AgentAnimatorBridge is on this character: that bridge owns MoveSpeed,
                // and two writers made the controller's own idle/walk transitions fire under it.
                if (!_moveSpeedResolved)
                {
                    if (!characterAnimator.isInitialized || characterAnimator.runtimeAnimatorController == null) return;
                    _moveSpeedResolved = true;
                    _hasMoveSpeed = false;
                    int n = characterAnimator.parameterCount;
                    for (int i = 0; i < n; i++)
                    {
                        AnimatorControllerParameter p = characterAnimator.GetParameter(i);
                        if (p.name == "MoveSpeed" && p.type == AnimatorControllerParameterType.Float)
                        {
                            _hasMoveSpeed = true;
                            break;
                        }
                    }
                    if (GetComponentInParent<MuseDesk.Agents.AgentAnimatorBridge>() != null
                        || GetComponentInChildren<MuseDesk.Agents.AgentAnimatorBridge>() != null)
                        _hasMoveSpeed = false;
                }
                if (_hasMoveSpeed) characterAnimator.SetFloat(MoveSpeedId, speed);
            }
        }

        private static readonly int MoveSpeedId = Animator.StringToHash("MoveSpeed");
        private bool _moveSpeedResolved;
        private bool _hasMoveSpeed;

        /// <summary>
        /// Crossfades to any of the 39 shared character animation clips by name
        /// (e.g., "Idle_Breathing", "Walk_Loop", "Typing", "Reading", "Inspecting",
        /// "Offer_Document", "Carry_Package", "Success", "Failure").
        /// </summary>
        public void PlayAnimationState(string clipStateName, float crossfadeSeconds = 0.2f)
        {
            if (string.IsNullOrEmpty(clipStateName)) return;
            _currentState = clipStateName;

            if (characterAnimator == null)
            {
                characterAnimator = GetComponentInChildren<Animator>();
            }
            if (characterAnimator != null && characterAnimator.runtimeAnimatorController != null)
            {
                int stateHash = Animator.StringToHash(clipStateName);
                if (characterAnimator.HasState(0, stateHash))
                {
                    characterAnimator.CrossFadeInFixedTime(stateHash, crossfadeSeconds, 0);
                }
            }
        }

        /// <summary>
        /// Attaches a prop Transform to one of the 5 named character sockets:
        /// "RightHand_Attach", "LeftHand_Attach", "Head_Attach", "Back_Attach", "Document_Attach".
        /// </summary>
        public bool AttachPropToSocket(Transform prop, string socketName, bool resetLocalTransform = true)
        {
            if (prop == null) return false;
            if (_sockets.Count == 0) ResolveSockets();

            Transform targetSocket;
            if (!_sockets.TryGetValue(socketName, out targetSocket) || targetSocket == null)
            {
                targetSocket = FindSocket(socketName, null);
            }
            if (targetSocket == null) return false;

            prop.SetParent(targetSocket, !resetLocalTransform);
            if (resetLocalTransform)
            {
                prop.localPosition = Vector3.zero;
                prop.localRotation = Quaternion.identity;
            }
            return true;
        }
    }
}

