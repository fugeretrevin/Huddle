import bpy
import math
import mathutils
import os

# ==============================================================================
# MuseDesk — Complete Miniature Office Diorama Studio Renderer
# Assembles the 3 workstations, 3 rigged Pipkin Sprite characters, Kanban task
# board, miniature software factory, document inbox, completed-project shelf,
# and spatial diagram platform in a single Blender scene and renders a high-res
# overview image for visual review.
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
BLEND_ROOT = os.path.join(PROJECT_ROOT, "ArtSource", "Blender")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")


def append_all_objects_from_blend(blend_path, offset_xyz=(0, 0, 0), rot_z_deg=0.0):
    if not os.path.exists(blend_path):
        return []
    with bpy.data.libraries.load(blend_path, link=False) as (data_from, data_to):
        data_to.objects = list(data_from.objects)

    parent_emp = bpy.data.objects.new(os.path.basename(blend_path) + "_Grp", None)
    bpy.context.collection.objects.link(parent_emp)
    parent_emp.location = offset_xyz
    parent_emp.rotation_euler = (0, 0, math.radians(rot_z_deg))

    linked = []
    for obj in data_to.objects:
        if obj is not None:
            if obj.name.startswith(("Preview", "Key", "Fill", "Rim")):
                continue
            bpy.context.collection.objects.link(obj)
            linked.append(obj)

    for obj in linked:
        if obj.parent is None:
            obj.parent = parent_emp
    return linked


def render_complete_office_diorama():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # 1. Three Workstations + Three Mascots (Research on Left +X=0.34, Dev Center 0, Testing Right -X=-0.34)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Workstations", "Workstation_Research.blend"),  ( 0.34,  0.00, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Characters",   "Agent_Researcher.blend"),      ( 0.34,  0.03, 0.0))

    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Workstations", "Workstation_Developer.blend"), ( 0.00, -0.02, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Characters",   "Agent_Developer.blend"),       ( 0.00,  0.01, 0.0))

    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Workstations", "Workstation_Testing.blend"),   (-0.34,  0.00, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Characters",   "Agent_Tester.blend"),          (-0.34,  0.03, 0.0))

    # 2. Kanban Task Board behind the stations (+Y = 0.24)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "TaskBoard", "TaskBoard_KanbanFrame.blend"),    ( 0.00,  0.24, 0.0))

    # 3. Miniature Software Factory in front/connecting the stations (-Y = -0.18)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Factory", "SoftwareFactory_Complete.blend"),   ( 0.00, -0.18, 0.0))

    # 4. User Document Inbox & Expanded Frame (+X = 0.24, -Y = -0.28)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Documents", "Doc_InboxTray.blend"),            ( 0.22, -0.28, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Documents", "Doc_ApprovalStamp.blend"),        ( 0.16, -0.28, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Documents", "Doc_SignaturePen.blend"),         ( 0.13, -0.28, 0.0))

    # 5. Completed Project Shelf & Archive behind Testing Station (-X = -0.36, +Y = 0.22)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Decorations", "Decor_ProjectDisplayShelf.blend"), (-0.36, 0.22, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Decorations", "Decor_Trophy.blend"),              (-0.36, 0.22, 0.088))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "Documents",   "Doc_DocumentArchive.blend"),       (-0.48, 0.22, 0.0))

    # 6. Spatial Diagram Platform (-X = -0.30, -Y = -0.28)
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "SpatialDiagrams", "Diagram_DisplayPlatform.blend"), (-0.30, -0.28, 0.0))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "SpatialDiagrams", "Diagram_FrontendNode.blend"),    (-0.18, -0.28, 0.016))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "SpatialDiagrams", "Diagram_ApiGatewayNode.blend"),  (-0.26, -0.28, 0.016))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "SpatialDiagrams", "Diagram_AuthNode.blend"),        (-0.34, -0.28, 0.016))
    append_all_objects_from_blend(os.path.join(BLEND_ROOT, "SpatialDiagrams", "Diagram_DatabaseNode.blend"),    (-0.42, -0.28, 0.016))

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720

    world = bpy.data.worlds.new("DioramaWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.13, 0.15, 0.19, 1.0)
        bg.inputs[1].default_value = 1.15

    for lname, loc, energy in [
        ("MainKey",  ( 0.65, -0.85, 0.85), 95.0),
        ("MainFill", (-0.65, -0.75, 0.65), 55.0),
        ("BackRim",  ( 0.00,  0.75, 0.70), 65.0),
    ]:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)

    cam_d = bpy.data.cameras.new("DioramaCam")
    cam_d.lens = 42
    cam_o = bpy.data.objects.new("DioramaCam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o

    cam_pos = (0.18, -0.92, 0.48)
    target  = (0.00, -0.02, 0.10)
    cam_o.location = cam_pos
    cam_o.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_DIR, "CompleteOffice_Diorama_preview.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    render_complete_office_diorama()

