using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// DEBUG (remove for submission): writes live grab diagnostics onto the on-screen DiagnosticsText
    /// so the paper-grab failure can be measured on-head instead of guessed. Shows whether the paper's
    /// HandGrabInteractable is active/registered, its interactable state, how many hands are selecting
    /// it, the resolved head camera, and FPS. All via reflection — no hard ISDK dependency.
    /// </summary>
    public class GrabReadout : MonoBehaviour
    {
        public TMP_Text target;              // the DiagnosticsText
        public float interval = 0.5f;

        float _t;
        float _fps;
        Component _grabbable;                // paper Grabbable
        MonoBehaviour _interactable;         // paper HandGrabInteractable
        PropertyInfo _sel;
        FieldInfo _state;

        void Awake() { if (target == null) target = GetComponent<TMP_Text>(); }

        void Update()
        {
            // Inert in a release build (no per-0.5 s string work or logcat spam); runs in the Editor and development builds.
            if (!Debug.isDebugBuild) return;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.1f);
            _t += Time.unscaledDeltaTime;
            if (_t < interval || target == null) return;
            _t = 0f;

            Resolve();

            string line;
            if (_interactable == null)
            {
                line = "paper: <not present>";
            }
            else
            {
                bool active = _interactable.isActiveAndEnabled;
                string st = "?";
                if (_state == null)
                    _state = _interactable.GetType().GetField("_state",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                        ?? FindUp(_interactable.GetType(), "_state");
                if (_state != null) { try { st = _state.GetValue(_interactable)?.ToString() ?? "?"; } catch { } }

                int sel = -1;
                if (_grabbable != null)
                {
                    if (_sel == null) _sel = _grabbable.GetType().GetProperty("SelectingPointsCount");
                    if (_sel != null) { try { sel = (int)_sel.GetValue(_grabbable); } catch { } }
                }
                float sc = _interactable != null ? _interactable.transform.localScale.x : -1f;
                line = $"paper: active={active} state={st} grabbedBy={sel} scale={sc:F2}";
            }

            string cam = Camera.main != null ? Camera.main.name : "NULL";
            target.text = $"{line}\ncam={cam}  fps={_fps:F0}";
            // Also emit to Android logcat so grab state can be read over adb (tag MDGRAB).
            Debug.Log($"MDGRAB {line} cam={cam}");
        }

        void Resolve()
        {
            if (_interactable != null && _interactable) return;
            var paper = GameObject.Find("DecisionDocument");
            if (paper == null) { _interactable = null; _grabbable = null; return; }
            _grabbable = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Grabbable");
            _interactable = paper.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().Name == "HandGrabInteractable");
            _state = null; _sel = null;
        }

        static FieldInfo FindUp(System.Type t, string name)
        {
            for (var c = t; c != null; c = c.BaseType)
            {
                var f = c.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }
    }
}
