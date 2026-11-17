using UnityEngine;
using MuseDesk.Workspace;
public static class FocusToggleDrive
{
    // A16: toggle Focus twice in the same frame; latest wins so at most one backend request goes out. Offline demo: checks state + no exceptions.
    public static string Twice()
    {
        var f = Object.FindFirstObjectByType<FocusModeController>();
        if (f == null) return "no FocusModeController";
        bool before = FocusModeController.Active;
        f.Toggle(); f.Toggle();
        return "before=" + before + " after=" + FocusModeController.Active + " (expect equal after two toggles)";
    }
}
