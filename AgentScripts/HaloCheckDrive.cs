using UnityEngine;
using MuseDesk.Phase2.Board;
public static class HaloCheckDrive
{
    // A16: drives SetHighlight valid/invalid on every zone and reports the shader plus the colour read back from the MPB.
    public static string Check()
    {
        var zones = Object.FindObjectsByType<StationDropZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new System.Text.StringBuilder("zones=" + zones.Length);
        foreach (var z in zones)
        {
            if (z.halo == null) { sb.Append(" | " + z.name + ": no halo"); continue; }
            var r = z.halo.GetComponent<Renderer>();
            if (r == null) { sb.Append(" | " + z.name + ": no renderer"); continue; }
            var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
            bool hasBase = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor");
            bool hasCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color");
            var mpb = new MaterialPropertyBlock();
            z.SetHighlight(true, true); r.GetPropertyBlock(mpb); var a = mpb.GetColor("_BaseColor");
            z.SetHighlight(true, false); r.GetPropertyBlock(mpb); var b = mpb.GetColor("_BaseColor");
            z.SetHighlight(false, true);
            sb.Append(" | " + z.name + ": shader=" + (sh != null ? sh.name : "null") + " _BaseColor=" + hasBase + " _Color=" + hasCol + " valid=" + a + " invalid=" + b);
        }
        return sb.ToString();
    }
}
