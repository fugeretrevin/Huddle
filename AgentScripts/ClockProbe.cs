using UnityEngine;

/// <summary>Is Play Mode time advancing? (The Editor has had a stall where unscaledDeltaTime stays 0.)</summary>
public static class ClockProbe
{
    public static string Run()
    {
        return "frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("F2") + " dt=" + Time.unscaledDeltaTime.ToString("F4")
               + " timeScale=" + Time.timeScale;
    }
}
