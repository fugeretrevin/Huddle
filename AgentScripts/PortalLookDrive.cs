using UnityEngine;

/// <summary>Look at a prefab in Play Mode: spawn it on the floor in front of the eye and aim __ShotCam at it.
///   scripts\drv.ps1 PortalLookDrive Spawn Portal_Open_Expand      (state to play; the prefab is the large portal)
///   scripts\drv.ps1 PortalLookDrive Aim 1.2                       (camera distance)
///   then: unity command capture game_view --camera __ShotCam --output-path ...
///   scripts\drv.ps1 PortalLookDrive Clear</summary>
public static class PortalLookDrive
{
    const string Path = "Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_LargeAgentPortal.prefab";

    public static string Spawn(string state)
    {
#if UNITY_EDITOR
        Clear();
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (prefab == null) return "no prefab";
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : new Vector3(0, 1.2f, 0);
        Vector3 fwd = main != null ? main.transform.forward : Vector3.forward; fwd.y = 0; fwd.Normalize();
        var go = Object.Instantiate(prefab);
        go.name = "__PortalLook";
        go.transform.position = eye + fwd * 1.4f + Vector3.down * 1.15f;
        go.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        var an = go.GetComponentInChildren<Animator>();
        if (an != null && !string.IsNullOrEmpty(state)) an.Play(state, 0, 0f);
        return "spawned at " + go.transform.position.ToString("F2") + " playing " + state;
#else
        return "editor only";
#endif
    }

    public static string Aim(string distance)
    {
        var go = GameObject.Find("__PortalLook");
        if (go == null) return "spawn first";
        float d = 1.2f; float.TryParse(distance, out d);
        var cam = GameObject.Find("__ShotCam");
        if (cam == null) cam = new GameObject("__ShotCam");
        var c = cam.GetComponent<Camera>(); if (c == null) c = cam.AddComponent<Camera>();
        c.enabled = true; c.depth = -100f; c.fieldOfView = 40f; c.nearClipPlane = 0.01f;
        c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        Vector3 look = go.transform.position + Vector3.up * 0.35f;
        cam.transform.position = look + go.transform.forward * d + Vector3.up * 0.45f;
        cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
        var r = go.GetComponentInChildren<SkinnedMeshRenderer>();
        return "aimed; mesh bounds " + (r != null ? r.bounds.size.ToString("F3") : "none");
    }

    public static string Clear()
    {
        var go = GameObject.Find("__PortalLook");
        if (go != null) Object.Destroy(go);
        return "cleared";
    }
}
