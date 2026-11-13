import bpy
import math
import mathutils
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import spatial_common as sc


# ==============================================================================
# 1. CEILING MOUNT (CeilingAnchor -> CeilingMountRoot -> SuspensionTop / Bottom)
#    Local mechanical latch + iris + spool clips (NO fixed vertical trajectory!)
# ==============================================================================

def build_ceiling_mount():
    name = "CeilingMount"
    sc.reset_scene()

    arm_data = bpy.data.armatures.new(f"{name}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{name}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add_b(bname, head, tail, parent=None):
        b = eb.new(bname)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = parent
        return b

    # CeilingAnchor is at (0,0,0); geometry hangs downward in -Z (Blender) -> -Y (Unity)
    b_root   = add_b("Bone_MountRoot",   (0.0, 0.0,  0.000), (0.0, 0.0, -0.030))
    add_b("Bone_SpoolDrum",              (0.0, 0.0, -0.024), (0.0, 0.0, -0.044), b_root)
    add_b("Bone_CollarLatch_L",          (-0.075, 0.0, -0.036), (-0.095, 0.0, -0.056), b_root)
    add_b("Bone_CollarLatch_R",          ( 0.075, 0.0, -0.036), ( 0.095, 0.0, -0.056), b_root)
    add_b("Bone_SuspCollar",             (0.0, 0.0, -0.055), (0.0, 0.0, -0.085), b_root)

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    # 1. Rounded ceiling mounting plate flush at Z=0 (extends Z in [-0.014, 0.0])
    sc.make_cylinder(f"{name}_PlateOuter", 0.145, 0.014, (0, 0, -0.007), "Mat_CreamCeramic",
                     vertices=32, bevel_caps=0.003, bone_name="Bone_MountRoot", parts_list=parts)
    sc.make_cylinder(f"{name}_PlateTrim", 0.125, 0.005, (0, 0, -0.015), "Mat_WarmWood",
                     vertices=28, bevel_caps=0.001, bone_name="Bone_MountRoot", parts_list=parts)

    # 2. Structural suspension housing dome + subtle perimeter status ring
    sc.make_cylinder(f"{name}_HousingCup", 0.086, 0.032, (0, 0, -0.032), "Mat_CharcoalVisor",
                     vertices=26, bevel_caps=0.004, bone_name="Bone_MountRoot", parts_list=parts)
    sc.make_torus(f"{name}_StatusRing", 0.087, 0.0032, (0, 0, -0.030), "Mat_Researcher_Teal",
                  maj_seg=28, min_seg=8, bone_name="Bone_MountRoot", parts_list=parts)

    # 3. Precision lift spool drum + decorative side brackets
    sc.make_cylinder(f"{name}_SpoolDrum", 0.032, 0.036, (0, 0, -0.034), "Mat_Brass",
                     vertices=20, rot_deg=(0, 90, 0), bone_name="Bone_SpoolDrum", parts_list=parts)
    for side, sx in [("L", -0.036), ("R", 0.036)]:
        sc.make_beveled_box(f"{name}_HousingFin_{side}", (0.012, 0.044, 0.022), (sx, 0, -0.038),
                            "Mat_BrushedMetal", bevel_width=0.002, bone_name="Bone_MountRoot", parts_list=parts)

    # 4. Articulated docking collar latches (open/close during Unlock & DockLock)
    for side, sx, bname in [("L", -0.072, "Bone_CollarLatch_L"), ("R", 0.072, "Bone_CollarLatch_R")]:
        sc.make_beveled_box(f"{name}_LatchArm_{side}", (0.016, 0.030, 0.034), (sx, 0, -0.052),
                            "Mat_BrushedMetal", bevel_width=0.002, bone_name=bname, parts_list=parts)
        sc.make_beveled_box(f"{name}_LatchTip_{side}", (0.018, 0.020, 0.010), (sx * 0.86, 0, -0.068),
                            "Mat_Brass", bevel_width=0.0015, bone_name=bname, parts_list=parts)

    # 5. Upper telescoping guide sleeve + lower attachment ring (runtime stretches tether between Top & Bottom!)
    sc.make_cylinder(f"{name}_GuideSleeve", 0.018, 0.040, (0, 0, -0.068), "Mat_BrushedMetal",
                     vertices=18, bevel_caps=0.002, bone_name="Bone_SuspCollar", parts_list=parts)
    sc.make_cylinder(f"{name}_TetherCore", 0.0055, 0.048, (0, 0, -0.088), "Mat_CharcoalVisor",
                     vertices=14, bone_name="Bone_SuspCollar", parts_list=parts)
    sc.make_cylinder(f"{name}_PodAttachCollar", 0.040, 0.012, (0, 0, -0.112), "Mat_Brass",
                     vertices=22, bevel_caps=0.002, bone_name="Bone_SuspCollar", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # Animations on CeilingMount (4 clean mechanical clips matching pod lifecycle)
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)

    def key_all(f):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="rotation_euler", frame=f)

    def add_clip(cname, key_fn, end_f):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        key_fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    def kf_mount_idle():
        key_all(1)
        key_all(48)

    def kf_mount_unlock():
        key_all(1)
        arm_obj.pose.bones["Bone_CollarLatch_L"].rotation_euler = (0, math.radians(-32), 0)
        arm_obj.pose.bones["Bone_CollarLatch_R"].rotation_euler = (0, math.radians(32), 0)
        arm_obj.pose.bones["Bone_SuspCollar"].location = (0, 0, -0.015)
        arm_obj.pose.bones["Bone_SpoolDrum"].rotation_euler = (math.radians(45), 0, 0)
        key_all(18)

    def kf_mount_spool():
        key_all(1)
        arm_obj.pose.bones["Bone_CollarLatch_L"].rotation_euler = (0, math.radians(-32), 0)
        arm_obj.pose.bones["Bone_CollarLatch_R"].rotation_euler = (0, math.radians(32), 0)
        key_all(1)
        arm_obj.pose.bones["Bone_SpoolDrum"].rotation_euler = (math.radians(360), 0, 0)
        key_all(24)

    def kf_mount_lock():
        arm_obj.pose.bones["Bone_CollarLatch_L"].rotation_euler = (0, math.radians(-32), 0)
        arm_obj.pose.bones["Bone_CollarLatch_R"].rotation_euler = (0, math.radians(32), 0)
        arm_obj.pose.bones["Bone_SuspCollar"].location = (0, 0, -0.015)
        key_all(1)
        reset_bones()
        key_all(18)

    add_clip("Mount_Docked_Idle", kf_mount_idle, 48)
    add_clip("Mount_Unlock", kf_mount_unlock, 18)
    add_clip("Mount_Spool_Spin", kf_mount_spool, 24)
    add_clip("Mount_Lock", kf_mount_lock, 18)

    # Required sockets on CeilingMount
    sc.make_anchor("CeilingAnchor",    (0, 0,  0.000), arm_obj)
    sc.make_anchor("CeilingMountRoot", (0, 0,  0.000), arm_obj)
    sc.make_anchor("SuspensionTop",    (0, 0, -0.048), arm_obj)
    sc.make_anchor("SuspensionBottom", (0, 0, -0.118), arm_obj)
    sc.make_anchor("DockedPose",       (0, 0, -0.130), arm_obj)
    sc.make_anchor("InspectionPose",   (0, 0, -1.250), arm_obj)

    clips = ["Mount_Docked_Idle", "Mount_Unlock", "Mount_Spool_Spin", "Mount_Lock"]
    return sc.export_animated_asset(
        name, clips,
        preview_cam_pos=(-0.38, 0.48, -0.26),
        preview_target=(0.0, 0.0, -0.06),
    )


# ==============================================================================
# 2. SUSPENDED OFFICE POD (with 13 Local Mechanical Animation Clips!)
# ==============================================================================

def build_suspended_office_pod():
    name = "SuspendedOfficePod"
    sc.reset_scene()

    arm_data = bpy.data.armatures.new(f"{name}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{name}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add_b(bname, head, tail, parent=None):
        b = eb.new(bname)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = parent
        return b

    # PodRoot at (0,0,0) = underside / base of suspended pod, deck surface at Z = +0.024
    b_pod   = add_b("Bone_PodRoot",          (0.000, 0.000, 0.000), (0.000, 0.000, 0.040))
    b_yoke  = add_b("Bone_DockYoke",         (0.000, -0.120, 0.220), (0.000, -0.120, 0.260), b_pod)
    add_b("Bone_PodLatch_L",                 ( 0.055, -0.120, 0.245), ( 0.075, -0.120, 0.265), b_yoke)
    add_b("Bone_PodLatch_R",                 (-0.055, -0.120, 0.245), (-0.075, -0.120, 0.265), b_yoke)
    add_b("Bone_Wing_L",                     ( 0.285, 0.000, 0.018), ( 0.335, 0.000, 0.028), b_pod)
    add_b("Bone_Wing_R",                     (-0.285, 0.000, 0.018), (-0.335, 0.000, 0.028), b_pod)
    b_shelf = add_b("Bone_InspectionShelf",  ( 0.000, 0.145, 0.008), ( 0.000, 0.195, 0.008), b_pod)
    add_b("Bone_ShelfRail_L",                ( 0.105, 0.185, 0.014), ( 0.105, 0.185, 0.032), b_shelf)
    add_b("Bone_ShelfRail_R",                (-0.105, 0.185, 0.014), (-0.105, 0.185, 0.032), b_shelf)
    add_b("Bone_InfoDisplay",                (-0.190, 0.140, 0.030), (-0.190, 0.140, 0.070), b_pod)
    add_b("Bone_BeaconMount",                (-0.255, 0.095, 0.026), (-0.255, 0.095, 0.050), b_pod)

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    w, d = 0.60, 0.38
    deck_z = 0.024

    # 1. Sleek rounded capsule hull base + lower acoustic/hover cradle ribs
    sc.make_rounded_slab(f"{name}_LowerBelly", w - 0.06, d - 0.05, 0.012, 0.095,
                         (0.0, 0.0, 0.006), "Mat_CharcoalVisor", bevel=0.003,
                         corner_segs=6, bone_name="Bone_PodRoot", parts_list=parts)
    sc.make_rounded_slab(f"{name}_MainHull", w, d, 0.016, 0.105,
                         (0.0, 0.0, 0.016), "Mat_CreamCeramic", bevel=0.004,
                         corner_segs=6, bone_name="Bone_PodRoot", parts_list=parts)
    sc.make_rounded_slab(f"{name}_WoodDeck", w - 0.044, d - 0.044, 0.003, 0.088,
                         (0.0, 0.0, deck_z - 0.001), "Mat_WarmWood", bevel=0.0008,
                         corner_segs=5, bone_name="Bone_PodRoot", parts_list=parts)

    # Subtle underside perimeter accent ring (Teal)
    sc.make_rounded_frame_outline(f"{name}_BellyGlowRing", w - 0.036, d - 0.036, 0.005, 0.003, 0.092,
                                  (0.0, 0.0, 0.009), "Mat_Researcher_Teal", corner_segs=5,
                                  bone_name="Bone_PodRoot", parts_list=parts)

    # 2. Gentle protective safety rim (rear & side arc, keeping front +Y open for inspection shelf & sightlines)
    rim_z = deck_z + 0.014
    sc.make_beveled_box(f"{name}_SafetyRim_Back", (0.42, 0.009, 0.014), (0.0, -d * 0.5 + 0.014, rim_z),
                        "Mat_BrushedMetal", bevel_width=0.002, bone_name="Bone_PodRoot", parts_list=parts)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        sc.make_beveled_box(f"{name}_SafetyRim_Side_{side}", (0.009, 0.18, 0.012),
                            (sx * (w * 0.5 - 0.016), -0.05, rim_z),
                            "Mat_BrushedMetal", bevel_width=0.002, bone_name="Bone_PodRoot", parts_list=parts)
        sc.make_cylinder(f"{name}_RimPost_{side}", 0.005, 0.022,
                         (sx * (w * 0.5 - 0.016), 0.038, deck_z + 0.011),
                         "Mat_Brass", vertices=12, bone_name="Bone_PodRoot", parts_list=parts)

    # 3. Graceful rear suspension arch + top DockingConnector yoke (connects up to CeilingMount!)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        sc.make_beveled_box(f"{name}_ArchStrut_{side}", (0.014, 0.022, 0.228),
                            (sx * 0.130, -0.132, 0.125), "Mat_BrushedMetal", bevel_width=0.0025,
                            rot_deg=(-5.0, sx * -9.0, 0.0), bone_name="Bone_PodRoot", parts_list=parts)
        sc.make_cylinder(f"{name}_ArchBaseSocket_{side}", 0.015, 0.014,
                         (sx * 0.146, -0.141, deck_z + 0.006), "Mat_Brass", vertices=14,
                         bone_name="Bone_PodRoot", parts_list=parts)
        sc.make_cylinder(f"{name}_ArchTopCollar_{side}", 0.014, 0.014,
                         (sx * 0.112, -0.122, 0.222), "Mat_Brass", vertices=14,
                         bone_name="Bone_PodRoot", parts_list=parts)
    # Top arched crossbar + magnetic DockingConnector cup
    sc.make_rounded_slab(f"{name}_ArchCrown", 0.275, 0.048, 0.018, 0.020,
                         (0.0, -0.120, 0.232), "Mat_CreamCeramic", bevel=0.003,
                         bone_name="Bone_DockYoke", parts_list=parts)
    sc.make_cylinder(f"{name}_DockConnectorRing", 0.044, 0.016,
                     (0.0, -0.120, 0.248), "Mat_Brass", vertices=24, bevel_caps=0.002,
                     bone_name="Bone_DockYoke", parts_list=parts)
    sc.make_cylinder(f"{name}_DockConnectorCore", 0.028, 0.020,
                     (0.0, -0.120, 0.250), "Mat_CharcoalVisor", vertices=20,
                     bone_name="Bone_DockYoke", parts_list=parts)

    # Pod-side locking tabs on DockYoke
    for side, sx, bname in [("L", 0.050, "Bone_PodLatch_L"), ("R", -0.050, "Bone_PodLatch_R")]:
        sc.make_beveled_box(f"{name}_PodLatch_{side}", (0.014, 0.022, 0.024),
                            (sx, -0.120, 0.252), "Mat_Developer_Amber", bevel_width=0.002,
                            bone_name=bname, parts_list=parts)

    # 4. Folding side stabilizer wings (tucked slightly in Docked/Transit, open in Deployed)
    for side, sx, bname, mat_accent in [
        ("L",  1.0, "Bone_Wing_L", "Mat_Researcher_Lavender"),
        ("R", -1.0, "Bone_Wing_R", "Mat_Tester_Mint"),
    ]:
        sc.make_rounded_slab(f"{name}_WingPlate_{side}", 0.058, 0.165, 0.008, 0.022,
                             (sx * 0.312, 0.0, 0.018), "Mat_CreamCeramic", bevel=0.002,
                             bone_name=bname, parts_list=parts)
        sc.make_beveled_box(f"{name}_WingTrim_{side}", (0.008, 0.110, 0.005),
                            (sx * 0.335, 0.0, 0.022), mat_accent, bevel_width=0.001,
                            bone_name=bname, parts_list=parts)
        sc.make_cylinder(f"{name}_WingHinge_{side}", 0.007, 0.036,
                         (sx * 0.285, 0.0, 0.018), "Mat_Brass", vertices=12, rot_deg=(90, 0, 0),
                         bone_name=bname, parts_list=parts)

    # 5. Retractable front InspectionShelf (slides out from belly slot!)
    # Resting tucked position in edit pose: y = 0.135 (mostly tucked under front lip); Deployed slides to +0.105 in Y!
    shelf_y = 0.135
    sc.make_rounded_slab(f"{name}_InspectionShelf_Body", 0.240, 0.125, 0.009, 0.024,
                         (0.0, shelf_y, 0.010), "Mat_CreamCeramic", bevel=0.002,
                         bone_name="Bone_InspectionShelf", parts_list=parts)
    sc.make_rounded_slab(f"{name}_InspectionShelf_Pad", 0.216, 0.104, 0.003, 0.018,
                         (0.0, shelf_y, 0.015), "Mat_FeltSlate", bevel=0.0005,
                         bone_name="Bone_InspectionShelf", parts_list=parts)
    sc.make_beveled_box(f"{name}_ShelfFrontTrim", (0.140, 0.008, 0.006),
                        (0.0, shelf_y + 0.058, 0.014), "Mat_Brass", bevel_width=0.001,
                        bone_name="Bone_InspectionShelf", parts_list=parts)
    for side, sx, bname in [("L", 0.108, "Bone_ShelfRail_L"), ("R", -0.108, "Bone_ShelfRail_R")]:
        sc.make_beveled_box(f"{name}_ShelfRail_{side}", (0.006, 0.075, 0.012),
                            (sx, shelf_y + 0.005, 0.020), "Mat_BrushedMetal", bevel_width=0.001,
                            bone_name=bname, parts_list=parts)

    # 6. Small Task Information Display (front-right angled mini display bezel)
    sc.make_beveled_box(f"{name}_DisplayStand", (0.016, 0.014, 0.022), (-0.190, 0.140, 0.032),
                        "Mat_BrushedMetal", bevel_width=0.0015, bone_name="Bone_InfoDisplay", parts_list=parts)
    sc.make_beveled_box(f"{name}_DisplayFrame", (0.098, 0.008, 0.048), (-0.190, 0.144, 0.058),
                        "Mat_CharcoalVisor", bevel_width=0.002, rot_deg=(-18, 0, -14),
                        bone_name="Bone_InfoDisplay", parts_list=parts)
    sc.make_beveled_box(f"{name}_DisplayFace", (0.088, 0.009, 0.038), (-0.190, 0.145, 0.058),
                        "Mat_Spatial_SlateIcon", bevel_width=0.0005, rot_deg=(-18, 0, -14),
                        bone_name="Bone_InfoDisplay", parts_list=parts)

    # 7. Subtle Workstation Pad Rings on Pod Deck (Research / Dev / Test)
    for lbl, px, py, mat_p in [
        ("PodResearchPad",  0.185, -0.045, "Mat_Researcher_Lavender"),
        ("PodDeveloperPad", 0.000, -0.062, "Mat_Developer_Coral"),
        ("PodTesterPad",   -0.185, -0.045, "Mat_Tester_Mint"),
    ]:
        sc.make_rounded_slab(f"{name}_{lbl}", 0.135, 0.095, 0.002, 0.014,
                             (px, py, deck_z + 0.0005), mat_p, bevel=0.0004,
                             bone_name="Bone_PodRoot", parts_list=parts)

    # Beacon pedestal pad on front-right edge
    sc.make_cylinder(f"{name}_BeaconPad", 0.024, 0.006, (-0.255, 0.095, deck_z + 0.003),
                     "Mat_CharcoalVisor", vertices=18, bone_name="Bone_BeaconMount", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # ==========================================================================
    # Build ALL 13 Required Local Mechanical Animation Clips (24 FPS)
    # ZERO world-space trajectory baked! Local movement, unfolding, and settle only.
    # ==========================================================================
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)

    def set_bone(bname, loc=None, rot_deg=None, scale=None):
        pb = arm_obj.pose.bones.get(bname)
        if not pb:
            return
        if loc is not None:
            pb.location = loc
        if rot_deg is not None:
            pb.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if scale is not None:
            pb.scale = scale

    def key_all(f):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="rotation_euler", frame=f)
            pb.keyframe_insert(data_path="scale", frame=f)

    def pose_docked_closed():
        reset_bones()
        # Wings folded gently upward/inward, shelf tucked, rails flat, display folded back slightly
        set_bone("Bone_Wing_L", rot_deg=(0, -52, 0))
        set_bone("Bone_Wing_R", rot_deg=(0,  52, 0))
        set_bone("Bone_InspectionShelf", loc=(0, 0.0, 0.0))
        set_bone("Bone_ShelfRail_L", rot_deg=(0,  82, 0))
        set_bone("Bone_ShelfRail_R", rot_deg=(0, -82, 0))
        set_bone("Bone_InfoDisplay", rot_deg=(-35, 0, 0))

    def pose_transit_streamlined():
        pose_docked_closed()
        set_bone("Bone_PodLatch_L", rot_deg=(0, -34, 0))
        set_bone("Bone_PodLatch_R", rot_deg=(0,  34, 0))

    def pose_deployed_shelf_closed():
        reset_bones()
        set_bone("Bone_PodLatch_L", rot_deg=(0, -34, 0))
        set_bone("Bone_PodLatch_R", rot_deg=(0,  34, 0))
        set_bone("Bone_Wing_L", rot_deg=(0, 0, 0))
        set_bone("Bone_Wing_R", rot_deg=(0, 0, 0))
        set_bone("Bone_InspectionShelf", loc=(0, 0.0, 0.0))
        set_bone("Bone_ShelfRail_L", rot_deg=(0,  82, 0))
        set_bone("Bone_ShelfRail_R", rot_deg=(0, -82, 0))
        set_bone("Bone_InfoDisplay", rot_deg=(0, 0, 0))

    def pose_deployed_shelf_open():
        pose_deployed_shelf_closed()
        set_bone("Bone_InspectionShelf", loc=(0, 0.105, 0.004), rot_deg=(-4, 0, 0))
        set_bone("Bone_ShelfRail_L", rot_deg=(0, 0, 0))
        set_bone("Bone_ShelfRail_R", rot_deg=(0, 0, 0))

    def build_clip(cname, end_f, fn):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    # 1. Docked idle (calm subtle micro-breathe)
    def c1():
        pose_docked_closed()
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.002))
        key_all(30)
        pose_docked_closed()
        key_all(60)

    # 2. User attention acknowledgment (warm double-nod + wing shimmer)
    def c2():
        pose_docked_closed()
        key_all(1)
        set_bone("Bone_PodRoot", rot_deg=(-4, 0, 0))
        set_bone("Bone_Wing_L", rot_deg=(0, -40, 0))
        set_bone("Bone_Wing_R", rot_deg=(0,  40, 0))
        set_bone("Bone_BeaconMount", loc=(0, 0, 0.005))
        key_all(7)
        pose_docked_closed()
        set_bone("Bone_PodRoot", rot_deg=(2, 0, 0))
        key_all(13)
        pose_docked_closed()
        key_all(18)

    # 3. Unlocking the pod (latches open outward, local 1.5cm unseat)
    def c3():
        pose_docked_closed()
        key_all(1)
        set_bone("Bone_PodLatch_L", rot_deg=(0, -34, 0))
        set_bone("Bone_PodLatch_R", rot_deg=(0,  34, 0))
        set_bone("Bone_PodRoot", loc=(0, 0, 0.003))
        key_all(8)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.015))
        key_all(18)

    # 4. Beginning descent (gentle anticipation tilt & tuck)
    def c4():
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, -0.015))
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.006), rot_deg=(3.5, 0, 0))
        key_all(7)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.012), rot_deg=(-2.5, 0, 0))
        key_all(15)

    # 5. Smooth descending movement (looping subtle gimbal sway)
    def c5():
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, 0), rot_deg=(-1.5, 0, -0.8))
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.003), rot_deg=(-1.5, 0,  0.8))
        key_all(15)
        set_bone("Bone_PodRoot", loc=(0, 0, 0), rot_deg=(-1.5, 0, -0.8))
        key_all(30)

    # 6. Stabilization at inspection height (damped spring bounce + wings unfold!)
    def c6():
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, 0.010), rot_deg=(-2, 0, 0))
        key_all(1)
        pose_deployed_shelf_closed()
        set_bone("Bone_PodRoot", loc=(0, 0, -0.010), rot_deg=(1.5, 0, 0))
        key_all(10)
        pose_deployed_shelf_closed()
        set_bone("Bone_PodRoot", loc=(0, 0, 0.003))
        key_all(16)
        pose_deployed_shelf_closed()
        key_all(22)

    # 7. Opening the interaction shelf (shelf glides out, rails pop up)
    def c7():
        pose_deployed_shelf_closed()
        key_all(1)
        set_bone("Bone_InspectionShelf", loc=(0, 0.095, 0.004), rot_deg=(-3, 0, 0))
        key_all(12)
        pose_deployed_shelf_open()
        key_all(20)

    # 8. Fully deployed idle (calm restrained 1.5mm breathing)
    def c8():
        pose_deployed_shelf_open()
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, 0.0018))
        key_all(36)
        pose_deployed_shelf_open()
        key_all(72)

    # 9. Closing the interaction shelf (rails fold, shelf retracts smoothly)
    def c9():
        pose_deployed_shelf_open()
        key_all(1)
        set_bone("Bone_ShelfRail_L", rot_deg=(0,  82, 0))
        set_bone("Bone_ShelfRail_R", rot_deg=(0, -82, 0))
        set_bone("Bone_InspectionShelf", loc=(0, 0.080, 0.002))
        key_all(8)
        pose_deployed_shelf_closed()
        key_all(18)

    # 10. Beginning ascent (wings fold into streamlined dock silhouette)
    def c10():
        pose_deployed_shelf_closed()
        key_all(1)
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, -0.005), rot_deg=(2.5, 0, 0))
        key_all(15)

    # 11. Returning to dock (looping smooth ascent posture)
    def c11():
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, 0), rot_deg=(1.5, 0, 0.6))
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, 0.002), rot_deg=(1.5, 0, -0.6))
        key_all(15)
        set_bone("Bone_PodRoot", loc=(0, 0, 0), rot_deg=(1.5, 0, 0.6))
        key_all(30)

    # 12. Docking lock animation (seat upward + latches snap closed)
    def c12():
        pose_transit_streamlined()
        set_bone("Bone_PodRoot", loc=(0, 0, -0.012))
        key_all(1)
        pose_docked_closed()
        set_bone("Bone_PodRoot", loc=(0, 0, 0.003))
        key_all(11)
        pose_docked_closed()
        key_all(19)

    # 13. Subtle mechanical settling (2-cycle damped harmonic micro-settle)
    def c13():
        pose_docked_closed()
        key_all(1)
        set_bone("Bone_PodRoot", loc=(0, 0, -0.004), rot_deg=(-0.8, 0, 0))
        key_all(5)
        set_bone("Bone_PodRoot", loc=(0, 0,  0.002), rot_deg=(0.4, 0, 0))
        key_all(10)
        pose_docked_closed()
        key_all(16)

    clip_defs = [
        ("Pod_Docked_Idle",        60, c1),
        ("Pod_Attention_Ack",      18, c2),
        ("Pod_Unlock",             18, c3),
        ("Pod_Begin_Descent",      15, c4),
        ("Pod_Descending",         30, c5),
        ("Pod_Stabilize",          22, c6),
        ("Pod_Shelf_Open",         20, c7),
        ("Pod_Deployed_Idle",      72, c8),
        ("Pod_Shelf_Close",        18, c9),
        ("Pod_Begin_Ascent",       15, c10),
        ("Pod_Returning_Dock",     30, c11),
        ("Pod_Dock_Lock",          19, c12),
        ("Pod_Mechanical_Settle",  16, c13),
    ]
    for cname, end_f, fn in clip_defs:
        build_clip(cname, end_f, fn)

    # Required sockets on Pod (using exact names!)
    sc.make_anchor("PodRoot",                    ( 0.000,  0.000, 0.000), arm_obj)
    sc.make_anchor("DockingConnector",           ( 0.000, -0.120, 0.258), arm_obj)
    sc.make_anchor("ResearchStationSocket",      ( 0.185, -0.045, deck_z), arm_obj)
    sc.make_anchor("DeveloperStationSocket",     ( 0.000, -0.062, deck_z), arm_obj)
    sc.make_anchor("TesterStationSocket",        (-0.185, -0.045, deck_z), arm_obj)
    sc.make_anchor("FactorySocket",              ( 0.000,  0.070, deck_z), arm_obj)
    sc.make_anchor("InfoDisplaySocket",          (-0.190,  0.145, 0.058), arm_obj, rot_deg=(-18, 0, -14))
    sc.make_anchor("AttentionBeaconSocket",      (-0.255,  0.095, deck_z + 0.006), arm_obj)
    sc.make_anchor("MailboxSocket",              ( 0.250,  0.095, deck_z), arm_obj)

    # Shelf-mounted sockets (follow Bone_InspectionShelf cleanly!)
    shelf_emp = sc.make_anchor("InspectionShelf", (0.0, 0.135, 0.015), arm_obj)
    shelf_emp.parent = arm_obj
    shelf_emp.parent_type = 'BONE'
    shelf_emp.parent_bone = "Bone_InspectionShelf"
    # Note: in Blender, bone-parented empties sit relative to bone tail; we'll place explicit clean
    # sockets on arm_obj too and have Unity prefab normalize/follow Bone_InspectionShelf cleanly!
    sc.make_anchor("AgentInteractionSocket",     ( 0.060,  0.240, 0.020), arm_obj)
    sc.make_anchor("DocumentPresentationSocket", (-0.055,  0.240, 0.032), arm_obj)

    return sc.export_animated_asset(
        name, [c[0] for c in clip_defs],
        preview_cam_pos=(-0.55, 0.78, 0.42),
        preview_target=(0.0, 0.0, 0.08),
    )


# ==============================================================================
# 3. STANDALONE CEILING INSPECTION SHELF (Modular Prefab Variant)
# ==============================================================================

def build_ceiling_inspection_shelf():
    name = "CeilingInspectionShelf"
    sc.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    sc.make_rounded_slab(f"{name}_Deck", 0.240, 0.125, 0.009, 0.024,
                         (0.0, 0.0, 0.0045), "Mat_CreamCeramic", bevel=0.002, parent=root)
    sc.make_rounded_slab(f"{name}_Mat", 0.216, 0.104, 0.003, 0.018,
                         (0.0, 0.0, 0.0095), "Mat_FeltSlate", bevel=0.0005, parent=root)
    sc.make_beveled_box(f"{name}_FrontTrim", (0.140, 0.008, 0.006),
                        (0.0, 0.058, 0.008), "Mat_Brass", bevel_width=0.001, parent=root)
    for side, sx in [("L", 0.108), ("R", -0.108)]:
        sc.make_beveled_box(f"{name}_SideRail_{side}", (0.006, 0.075, 0.012),
                            (sx, 0.005, 0.015), "Mat_BrushedMetal", bevel_width=0.001, parent=root)

    sc.make_anchor("ShelfRoot",                  ( 0.000, 0.0, 0.011), root)
    sc.make_anchor("AgentInteractionSocket",     ( 0.055, 0.0, 0.011), root)
    sc.make_anchor("DocumentPresentationSocket", (-0.055, 0.0, 0.022), root)

    static_meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    sc.join_static_parts(static_meshes, f"{name}_Mesh", parent=root)

    return sc.export_static_asset(
        name,
        preview_cam_pos=(-0.22, 0.30, 0.18),
        preview_target=(0.0, 0.0, 0.01),
    )


def build_all():
    report = [
        build_ceiling_mount(),
        build_suspended_office_pod(),
        build_ceiling_inspection_shelf(),
    ]
    out_json = os.path.join(sc.PREVIEW_SPATIAL_DIR, "ceiling_pod_report.json")
    with open(out_json, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[SpatialCeiling] Built {len(report)} assets -> {out_json}")


if __name__ == "__main__":
    build_all()
