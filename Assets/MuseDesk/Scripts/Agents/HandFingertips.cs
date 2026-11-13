using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Resolves the five fingertip bone transforms of each rendered hand (via the ISDK HandVisual,
    /// reflected so we keep no hard compile dependency), cached and shared across all callers. These
    /// move with the real tracked fingers, so proximity checks against them give finger-level
    /// contact instead of wrist-level. Returns an empty list until the hands are tracked; callers
    /// re-query cheaply each frame and it self-heals once hands appear.
    /// </summary>
    public static class HandFingertips
    {
        static readonly string[] TipJoints =
            { "HandThumbTip", "HandIndexTip", "HandMiddleTip", "HandRingTip", "HandPinkyTip" };
        const string T_HandVisual = "Oculus.Interaction.HandVisual";
        const string T_HandJointId = "Oculus.Interaction.Input.HandJointId";
        const string HandVisualPrefix = "OVRHandVisual";

        static readonly List<Transform> _tips = new List<Transform>();
        static float _nextTry;

        // Reflection results, looked up once. (They used to be looked up again on every retry, twice
        // a second for as long as no hand was tracked: an assembly scan, LINQ and enum parsing.)
        static bool _reflected;
        static Type _hvType;
        static MethodInfo _getT;
        static object[] _ids;
        static readonly object[] _arg = new object[1];

        /// <summary>The fingertip transforms (both hands). Empty until hands are tracked.</summary>
        public static IReadOnlyList<Transform> Get()
        {
            // Still valid? (transforms get destroyed/recreated when hands drop in and out.)
            if (_tips.Count > 0 && _tips[0] != null) return _tips;
            if (Time.unscaledTime < _nextTry) return _tips;
            _nextTry = Time.unscaledTime + 0.5f;
            Resolve();
            return _tips;
        }

        static void Reflect()
        {
            _reflected = true;
            try
            {
                var hvType = FindType(T_HandVisual);
                var jointType = FindType(T_HandJointId);
                if (hvType == null || jointType == null) return;

                var getT = hvType.GetMethod("GetTransformByHandJointId",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { jointType }, null);
                if (getT == null) return;

                _ids = TipJoints
                    .Select(n => { try { return Enum.Parse(jointType, n); } catch { return null; } })
                    .Where(v => v != null).ToArray();
                _hvType = hvType;
                _getT = getT;
            }
            catch { _hvType = null; _getT = null; _ids = null; }
        }

        static void Resolve()
        {
            _tips.Clear();
            if (!_reflected) Reflect();
            if (_hvType == null || _getT == null || _ids == null) return;
            try
            {
                UnityEngine.Object[] visuals = UnityEngine.Object.FindObjectsByType(_hvType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int v = 0; v < visuals.Length; v++)
                {
                    var visual = visuals[v] as Component;
                    if (visual == null) continue;
                    if (!visual.gameObject.name.StartsWith(HandVisualPrefix)) continue;
                    for (int i = 0; i < _ids.Length; i++)
                    {
                        _arg[0] = _ids[i];
                        if (_getT.Invoke(visual, _arg) is Transform t && t != null) _tips.Add(t);
                    }
                }
            }
            catch { _tips.Clear(); }
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
