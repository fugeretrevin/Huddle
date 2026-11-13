using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the ceiling pod audio. Report() only reads. WalkLower / WalkRaise ask the
/// director for the same moves as a hand would (SpatialDrive.PodDown / PodUp); call Report between
/// them, a few seconds apart, to see the unlock cue, the travel loop (pitch) and the lock cue.
///
/// Typical: SpatialDrive.Enable, Ceiling, Confirm (wait for the dock); CeilingAudioDrive.Report;
/// WalkLower; Report (loop playing, pitch about 0.9); wait; Report (last cue Lock);
/// WalkRaise; Report (pitch about 1.1); wait; Report (last cue Lock at Mount, loop faded out).
/// </summary>
public static class CeilingAudioDrive
{
    static CeilingPodAudio One() => Object.FindAnyObjectByType<CeilingPodAudio>(FindObjectsInactive.Include);

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var a = One();
        if (a == null) return "no CeilingPodAudio (run BuildCeilingAudioV1.Apply)";
        var sb = new StringBuilder();
        sb.Append("bound=").Append(a.Bound)
          .Append(" podState=").Append(a.AudioState)
          .Append(" lastCue=").Append(a.LastCue).Append('@').Append(a.LastCueSite)
          .Append(" cuesPlayed=").Append(a.CuesPlayed)
          .Append(" loopPlaying=").Append(a.LoopPlaying)
          .Append(" pitch=").Append(a.LoopPitch.ToString("F2"))
          .Append(" loopLevel=").Append(a.LoopLevel.ToString("F2"))
          .Append(" muted=").Append(a.muted)
          .Append(" master=").Append(a.masterVolume.ToString("F2"))
          .Append(" spatialized=").Append(a.Spatialized)
          .Append(" mode=").Append(SpatialWorkspaceState.Mode);
        return sb.ToString();
    }

    public static string WalkLower()
    {
        var d = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (d == null) return "no SpatialWorkspaceDirector";
        d.BringPodDown();
        return "lowering. " + Report();
    }

    public static string WalkRaise()
    {
        var d = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (d == null) return "no SpatialWorkspaceDirector";
        d.SendPodUp();
        return "raising. " + Report();
    }

    public static string Mute(bool on)
    {
        var a = One();
        if (a == null) return "no CeilingPodAudio";
        a.muted = on;
        return Report();
    }
}
