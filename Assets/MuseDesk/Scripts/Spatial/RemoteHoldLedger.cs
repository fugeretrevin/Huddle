using System;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Keeps the reach side's use of <see cref="SpatialWorkspaceState.AcquireTransitionHold"/>
    /// exactly balanced. It owns at most ONE outstanding hold: <see cref="Acquire"/> while already
    /// holding and <see cref="Release"/> while not holding do nothing, so however the grab,
    /// release, resolve and reset paths interleave, every acquire is matched by one release.
    /// Plain class (the two calls are injected) so the balance can be tested without a scene.
    /// </summary>
    public sealed class RemoteHoldLedger
    {
        readonly Action _acquire;
        readonly Action _release;

        /// <summary>True while this ledger has a hold outstanding.</summary>
        public bool Holding { get; private set; }
        /// <summary>How many times the acquire call was actually made.</summary>
        public int AcquireCount { get; private set; }
        /// <summary>How many times the release call was actually made.</summary>
        public int ReleaseCount { get; private set; }

        public RemoteHoldLedger(Action acquire, Action release)
        {
            _acquire = acquire;
            _release = release;
        }

        public void Acquire()
        {
            if (Holding) return;
            Holding = true;
            AcquireCount++;
            if (_acquire != null) _acquire();
        }

        public void Release()
        {
            if (!Holding) return;
            Holding = false;
            ReleaseCount++;
            if (_release != null) _release();
        }

        /// <summary>Acquire when <paramref name="held"/> is true, release when it is false.</summary>
        public void Set(bool held)
        {
            if (held) Acquire(); else Release();
        }
    }
}
