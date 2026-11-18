using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// rc25 (owner: "make an app that does X and then we can see it in MuseDesk"): while the screen panel shows the program
    /// the agents built, a fingertip pressed onto the picture is a tap on that program's window on the PC. Pure logic.
    ///
    /// Input per fingertip and frame: its position on the picture in the picture's own units (x, y in -0.5..0.5, x to the
    /// viewer's right, y up) and its depth in metres (negative = in front of the picture, toward the eye).
    /// A finger ARMS while it is over the picture and at least <see cref="ArmDepth"/> in front of it; an armed finger that
    /// reaches the surface (<see cref="TouchDepth"/>) over the picture TAPS. After a tap the same finger must pull back to
    /// <see cref="RearmDepth"/> before it can tap again (no double taps from one press). Leaving the picture, losing
    /// tracking or reaching far behind it disarms. Output (u, v): fractions of the window, top-left origin.
    /// </summary>
    public sealed class ScreenTapLogic
    {
        public const float TouchDepth = -0.004f;   // within 4 mm of the surface (or through it) is touching
        public const float ArmDepth = -0.012f;     // must come from at least 12 mm in front: sliding in along the surface is not a tap
        public const float RearmDepth = -0.025f;   // after a tap, pull back 2.5 cm before the next one
        public const float MaxThrough = 0.06f;     // more than 6 cm behind the picture is reaching around it, not touching

        readonly bool[] _armed, _spent;

        public ScreenTapLogic(int fingers = 2)
        {
            int n = Mathf.Max(1, fingers);
            _armed = new bool[n];
            _spent = new bool[n];
        }

        /// <summary>One fingertip sample. True (with u, v) on the frame a tap lands.</summary>
        public bool Step(int finger, bool tracked, float x, float y, float depth, out float u, out float v)
        {
            u = v = 0f;
            if (finger < 0 || finger >= _armed.Length) return false;
            bool over = x >= -0.5f && x <= 0.5f && y >= -0.5f && y <= 0.5f;
            if (!tracked || !over || depth > MaxThrough)
            {
                _armed[finger] = false;
                if (!tracked || depth <= RearmDepth) _spent[finger] = false;
                return false;
            }
            if (_spent[finger])
            {
                if (depth <= RearmDepth) { _spent[finger] = false; _armed[finger] = true; }
                return false;
            }
            if (depth <= ArmDepth) { _armed[finger] = true; return false; }
            if (_armed[finger] && depth >= TouchDepth)
            {
                _armed[finger] = false;
                _spent[finger] = true;
                u = Mathf.Clamp01(x + 0.5f);
                v = Mathf.Clamp01(0.5f - y);
                return true;
            }
            return false;
        }

        public void Reset()
        {
            for (int i = 0; i < _armed.Length; i++) _armed[i] = _spent[i] = false;
        }
    }
}
