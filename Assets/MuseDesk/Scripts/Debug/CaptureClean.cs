using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace MuseDesk.DebugTools
{
    /// <summary>
    /// CAPTURE ONLY (owner, 2026-11-18, filming a hand-off shot in the offline demo): hides the "SCRIPTED DEMO" wording
    /// (paper, agent tags, cards, receipts, bubbles, plaque). It is OFF unless the file <see cref="FlagFile"/> exists in
    /// Application.persistentDataPath when the app starts, so the build the judges get always shows the labels.
    ///
    /// On the Quest:  adb shell touch /sdcard/Android/data/com.musedesk.quest/files/capture_clean.flag   (then restart the app)
    /// Off again:     adb shell rm /sdcard/Android/data/com.musedesk.quest/files/capture_clean.flag      (then restart the app)
    ///
    /// It does not add a LIVE label or change what runs: it only removes the demo wording from text that is shown.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CaptureClean : MonoBehaviour
    {
        public const string FlagFile = "capture_clean.flag";

        static bool? _on;

        /// <summary>True when the capture flag file was present at start-up (read once).</summary>
        public static bool On
        {
            get
            {
                if (_on == null)
                {
                    try { _on = File.Exists(Path.Combine(Application.persistentDataPath, FlagFile)); }
                    catch { _on = false; }
                }
                return _on.Value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _on = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!On) return;
            Debug.Log("[MuseDesk] CAPTURE CLEAN is ON: scripted-demo wording is hidden (capture only, " + FlagFile + ")");
            var go = new GameObject("CaptureClean");
            DontDestroyOnLoad(go);
            go.AddComponent<CaptureClean>();
        }

        // 1) the mode tags themselves
        static readonly Regex Tags = new Regex(
            @"[ \t]*[\[(](?:SCRIPTED DEMO|[Ss]cripted demo)[\])][ \t]*|SCRIPTED DEMO[ \t]*[:·|\-.][ \t]*|[ \t]*[·|][ \t]*SCRIPTED DEMO|SCRIPTED DEMO|Offline: demo",
            RegexOptions.CultureInvariant);   // case-sensitive: lower-case "scripted demo" inside a sentence goes with its sentence (2)
        // 2) whole sentences that only exist to say it is scripted
        static readonly Regex Sentences = new Regex(
            @"[^.!?\n<>]*\b(?:scripted|no model was used|nothing is running on a computer)\b[^.!?\n<>]*[.!?]?[ \t]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // 3) rich-text wrappers left empty (the bubble footer, the touch-summary tag)
        static readonly Regex EmptyWrappers = new Regex(
            @"\n?[ \t]*<size=[^>]*>\s*(?:<color=[^>]*>\s*</color>\s*)?</size>|<color=[^>]*>\s*</color>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex BlankLines = new Regex(@"\n[ \t]*\n[ \t]*\n+", RegexOptions.CultureInvariant);

        /// <summary>True if the text carries demo wording this switch removes.</summary>
        public static bool HasMarker(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("scripted", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("no model was used", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("nothing is running on a computer", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Offline: demo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The text with the demo wording removed (unchanged when it has none).</summary>
        public static string Clean(string s)
        {
            if (!HasMarker(s)) return s;
            string t = Tags.Replace(s.Replace("SCRIPTED DEMO plan", "Plan"), " ");
            t = Sentences.Replace(t, "");
            t = EmptyWrappers.Replace(t, "");
            t = BlankLines.Replace(t, "\n\n");
            t = Regex.Replace(t, @"[ \t]{2,}", " ");
            return t.Trim();
        }

        readonly List<TMP_Text> _texts = new List<TMP_Text>(512);
        int _nextScan;

        void OnEnable() { TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged); }
        void OnDisable() { TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged); }

        // A text that was just (re)generated: clean it and remember it, so new objects are caught at once.
        void OnTextChanged(UnityEngine.Object o)
        {
            var t = o as TMP_Text;
            if (t == null) return;
            string s = t.text;
            if (HasMarker(s)) { t.text = Clean(s); _texts.Add(t); }
        }

        void LateUpdate()
        {
            if (Time.frameCount >= _nextScan)
            {
                _nextScan = Time.frameCount + 30;
                _texts.Clear();
                _texts.AddRange(FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }
            for (int i = 0; i < _texts.Count; i++)
            {
                var t = _texts[i];
                if (t == null) continue;
                string s = t.text;
                if (!HasMarker(s)) continue;
                string c = Clean(s);
                if (!string.Equals(c, s, StringComparison.Ordinal)) t.text = c;
            }
        }
    }
}
