using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the Meta XR Building Blocks into the open MainMRScene using the installed
/// Meta.XR.BuildingBlocks.Editor API (via reflection, since it is internal). This is
/// the documented, non-invented content path and auto-installs the single CameraRig
/// as a dependency, so no duplicate rig is created.
/// </summary>
public static class MetaBlocks
{
    const string ScenePath = "Assets/MuseDesk/Scenes/MainMRScene.unity";

    // GUIDs from Meta.XR.BuildingBlocks.Editor.BlockDataIds (com.meta.xr.sdk.core).
    const string CameraRig = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
    const string Passthrough = "f0540b20-dfd6-420e-b20d-c270f88dc77e";
    const string HandTracking = "8b26b298-7bf4-490e-b245-a039c0184303";
    const string MRUK = "d4413f4f-542f-451f-92ee-4801f27d676a";

    // Interaction category (com.meta.xr.sdk.interaction.ovr)
    const string PokeableItem = "5838e892-31a4-4f65-a415-dc108aefa14c";
    const string GrabbableItem = "5c5184f2-c2f5-4063-b14b-3b1264fb3c1a";

    public static Task<string> AddInteraction()
        => AddBlocks(new[] { (PokeableItem, "PokeableItem"), (GrabbableItem, "GrabbableItem") });

    // Core rig WITHOUT the standalone HandTracking block — hands (with ISDK interactors)
    // come from the interaction blocks, so there is exactly one set of hand visuals.
    public static Task<string> AddCoreBlocks()
        => AddBlocks(new[] { (CameraRig, "CameraRig"), (Passthrough, "Passthrough"),
                             (MRUK, "MRUK") });

    static async Task<string> AddBlocks((string id, string name)[] blocks)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetType("Meta.XR.BuildingBlocks.Editor.Utils") != null);
        if (asm == null) return "ERROR: Meta.XR.BuildingBlocks.Editor assembly not found";

        var utils = asm.GetType("Meta.XR.BuildingBlocks.Editor.Utils");
        var getBlockData = utils.GetMethod("GetBlockData",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

        var sb = new StringBuilder();
        foreach (var (id, name) in blocks)
        {
            try
            {
                var blockData = getBlockData.Invoke(null, new object[] { id });
                if (blockData == null) { sb.AppendLine($"{name}: block data NOT FOUND"); continue; }

                var addToProject = blockData.GetType().GetMethod("AddToProject",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new[] { typeof(GameObject), typeof(Action) }, null);
                if (addToProject == null) { sb.AppendLine($"{name}: AddToProject not found"); continue; }

                var task = (Task)addToProject.Invoke(blockData, new object[] { null, null });
                await task;
                sb.AppendLine($"{name}: added");
            }
            catch (Exception e)
            {
                sb.AppendLine($"{name}: EXCEPTION {e.GetBaseException().Message}");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        // Report the building blocks present (proves single rig, etc.).
        var bbType = asm.GetType("Meta.XR.BuildingBlocks.BuildingBlock")
                     ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Meta.XR.BuildingBlocks.BuildingBlock"))
                        .FirstOrDefault(t => t != null);
        if (bbType != null)
        {
            var found = UnityEngine.Object.FindObjectsByType(bbType, FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            sb.AppendLine($"BuildingBlock components in scene: {found.Length}");
            foreach (var f in found) sb.AppendLine($"  - {((Component)f).gameObject.name}");
        }
        return sb.ToString();
    }
}
