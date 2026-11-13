import bpy
import math
import mathutils
import os
import sys
import json

# ==============================================================================
# MuseDesk — Deliverable 5: Refined Miniature Software Factory
# Imports generate_factory.py's exact 14-bone Armature + 10 NLA Clips, and
# refines belt guide rails, gantry brackets, printer feed guides, scanner hood
# collars, light tower dividers, and socket anchors while keeping 100% bone
# and clip compatibility.
# Exports:
#   - ArtSource/Blender/Factory/SoftwareFactory_Complete_Polished.blend
#   - Assets/MuseDesk/Art/Models/Factory/SoftwareFactory_Complete_Polished.fbx
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
SCRIPTS_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Scripts")
if SCRIPTS_DIR not in sys.path:
    sys.path.append(SCRIPTS_DIR)

import generate_factory as gf

BLEND_OUT = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "Factory", "SoftwareFactory_Complete_Polished.blend")
FBX_OUT   = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models", "Factory", "SoftwareFactory_Complete_Polished.fbx")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "FinalRefinement")
os.makedirs(os.path.dirname(BLEND_OUT), exist_ok=True)
os.makedirs(os.path.dirname(FBX_OUT), exist_ok=True)
os.makedirs(PREVIEW_DIR, exist_ok=True)


def add_refined_factory_details(arm_obj, joined_mesh):
    """Adds subtle mechanical craftsmanship pieces bound safely to the factory bones."""
    extra_parts = []

    def add_box(name, size_xyz, loc, mat_name, bone_name="Bone_FactoryRoot", bevel_w=0.0012, rot_deg=(0, 0, 0)):
        return gf.make_box(name, size_xyz, loc, mat_name, bone_name=bone_name,
                           bevel_width=bevel_w, rot_deg=rot_deg, parts_list=extra_parts)

    def add_cyl(name, r, d, loc, mat_name, bone_name="Bone_FactoryRoot", verts=16, rot_deg=(0, 0, 0)):
        return gf.make_cyl(name, r, d, loc, mat_name, bone_name=bone_name,
                           vertices=verts, rot_deg=rot_deg, parts_list=extra_parts)

    def add_torus(name, maj_r, min_r, loc, mat_name, bone_name="Bone_FactoryRoot", rot_deg=(0, 0, 0)):
        return gf.make_torus(name, maj_r, min_r, loc, mat_name, bone_name=bone_name,
                             maj_seg=22, min_seg=8, rot_deg=rot_deg, parts_list=extra_parts)

    # 1. Conveyor Belt Guide Rails + Trestle Footpads & Brass Fasteners
    for sy in (-0.024, 0.024):
        add_box(f"Factory_BeltGuideRail_{int(sy*1000)}", (0.562, 0.004, 0.009),
                (-0.010, sy, 0.056), "Mat_BrushedMetal", bevel_w=0.0008)
        for rx in (-0.27, -0.18, -0.09, 0.0, 0.09, 0.18, 0.27):
            add_cyl(f"Factory_RailBolt_{int(rx*100)}_{int(sy*1000)}", 0.0022, 0.003,
                    (rx, sy * 1.15, 0.056), "Mat_Brass", verts=10, rot_deg=(90, 0, 0))
    for tx in (-0.26, -0.09, 0.09, 0.26):
        add_box(f"Factory_TrestleFoot_{int(tx*100)}", (0.030, 0.074, 0.004),
                (tx, 0.0, 0.002), "Mat_Brass", bevel_w=0.001)

    # 2. Stage 1 Printer Feed Slot Lip & Paper Guide Rails (around Bone_BlueprintSheet)
    add_box("Factory_PrinterFeedBezel", (0.010, 0.050, 0.014), (0.214, -0.014, 0.068), "Mat_Brass", bevel_w=0.001)
    add_box("Factory_PrinterCatchLip",  (0.044, 0.036, 0.004), (0.208, -0.024, 0.055), "Mat_CharcoalVisor", bevel_w=0.0008)

    # 3. Stage 2 Developer Build Gantry Gussets & Carrier Locator Rim
    for gx in (0.072, 0.038):
        add_box(f"Factory_GantryGusset_{int(gx*1000)}", (0.008, 0.056, 0.010),
                (gx, 0.0, 0.114), "Mat_Brass", bevel_w=0.001)
    # Recessed guide rail on Bone_Carrier tray so carried artifact sits securely
    add_torus("Factory_CarrierLocator", 0.017, 0.0016, (0.120, 0.0, 0.0615),
              "Mat_CharcoalVisor", bone_name="Bone_Carrier")

    # 4. Stage 3 Scanner Framing Collar & Status Tower Separating Rings
    add_torus("Factory_ChamberTopCollar", 0.029, 0.0028, (-0.210, 0.0, 0.114), "Mat_CharcoalVisor")
    for lz in (0.132, 0.145, 0.158):
        add_cyl(f"Factory_LightSpacer_{int(lz*1000)}", 0.0072, 0.0025,
                (-0.210, -0.024, lz), "Mat_CharcoalVisor", verts=14)

    # 5. Stage 4 Delivery Tray Rim & End Bumper
    add_torus("Factory_DeliveryLiftRim", 0.032, 0.0020, (-0.330, 0.0, 0.056),
              "Mat_Brass", bone_name="Bone_DeliveryLift")
    add_box("Factory_ConveyorEndStop", (0.006, 0.052, 0.012), (-0.295, 0.0, 0.058),
            "Mat_Developer_Coral", bevel_w=0.001)

    # Join refined parts into existing SoftwareFactory_Mesh
    bpy.ops.object.select_all(action='DESELECT')
    joined_mesh.select_set(True)
    for p in extra_parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = joined_mesh
    bpy.ops.object.join()

    # Crisp flat-vs-smooth edge polish on combined mesh
    for poly in joined_mesh.data.polygons:
        n = poly.normal
        poly.use_smooth = abs(n.x) < 0.92 and abs(n.y) < 0.92 and abs(n.z) < 0.92

    # Additional helpful status anchor without removing any existing sockets
    gf.make_anchor("Anchor_FactoryStatus", (0.0, -0.058, 0.135), arm_obj)
    return joined_mesh


def render_factory_polished_preview():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720

    world = bpy.data.worlds.new("FactoryPolishWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.12, 0.14, 0.18, 1.0)
        bg.inputs[1].default_value = 1.2

    temp_objs = []
    for lname, loc, energy in [
        ("Key",  ( 0.45, -0.65, 0.65), 70.0),
        ("Fill", (-0.55, -0.55, 0.50), 40.0),
        ("Rim",  ( 0.00,  0.55, 0.50), 48.0),
    ]:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)
        temp_objs.append(lo)

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 45
    cam_o = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o
    temp_objs.append(cam_o)

    cam_pos = mathutils.Vector((0.04, -0.86, 0.35))
    target  = mathutils.Vector((-0.04, 0.00, 0.075))
    cam_o.location = cam_pos
    cam_o.rotation_euler = (target - cam_pos).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_DIR, "Blender_SoftwareFactory_Polished.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    for o in temp_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return out_png


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)

    arm_obj = gf.create_factory_armature()
    mesh_obj = gf.build_factory_geometry(arm_obj)
    add_refined_factory_details(arm_obj, mesh_obj)
    clip_count = gf.build_factory_animations(arm_obj)
    preview_png = render_factory_polished_preview()

    arm_obj.animation_data.action = None
    for pb in arm_obj.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.scene.frame_set(1)

    deps = bpy.context.evaluated_depsgraph_get()
    em = mesh_obj.evaluated_get(deps).to_mesh()
    em.calc_loop_triangles()
    tris = len(em.loop_triangles)
    verts = len(em.vertices)
    mesh_obj.evaluated_get(deps).to_mesh_clear()

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    bpy.ops.export_scene.fbx(
        filepath=FBX_OUT,
        use_selection=False,
        object_types={'ARMATURE', 'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
    )

    report = {
        "asset": "SoftwareFactory_Complete_Polished",
        "blend": BLEND_OUT,
        "fbx": FBX_OUT,
        "fbx_bytes": os.path.getsize(FBX_OUT),
        "vertices": verts,
        "triangles": tris,
        "bones": len(arm_obj.data.bones),
        "clips": clip_count,
        "preview": preview_png,
    }
    report_path = os.path.join(PREVIEW_DIR, "factory_polished_report.json")
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[FinalRefinement] Exported SoftwareFactory_Complete_Polished ({tris} tris, {clip_count} clips)")


if __name__ == "__main__":
    main()
