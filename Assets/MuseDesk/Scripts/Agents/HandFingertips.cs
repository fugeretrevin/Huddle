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

        static void Resolve()
        {
            _tips.Clear();
            try
            {
                var hvType = FindType(T_HandVisual);
                var jointType = FindType(T_HandJointId);
                if (hvType == null || jointType == null) return;

                var getT = hvType.GetMethod("GetTransformByHandJointId",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { jointType }, null);
                if (getT == null) return;

                var ids = TipJoints
                    .Select(n => { try { return Enum.Parse(jointType, n); } catch { return null; } })
                    .Where(v => v != null).ToArray();

                foreach (var visual in UnityEngine.Object.FindObjectsByType(hvType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Cast<Component>())
                {
                    if (!visual.gameObject.name.StartsWith(HandVisualPrefix)) continue;
                    foreach (var id in ids)
                    {
                        if (getT.Invoke(visual, new[] { id }) is Transform t && t != null) _tips.Add(t);
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
