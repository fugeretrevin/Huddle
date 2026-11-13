import bpy
import math
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import spatial_common as sc


# ==============================================================================
# 1. WORKSPACE ATTENTION BEACON (7 States, Accessibility Shapes + MPB Lights)
# ==============================================================================

def build_attention_beacon():
    name = "WorkspaceAttentionBeacon"
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

    b_base = add_b("Bone_BeaconBase",  (0.0, 0.0, 0.000), (0.0, 0.0, 0.030))
    add_b("Bone_BeaconRing",           (0.0, 0.0, 0.035), (0.0, 0.0, 0.055), b_base)
    add_b("Bone_FlagArm",              (-0.022, 0.0, 0.042), (-0.032, 0.0, 0.068), b_base)
    add_b("Bone_IconCrest",            (0.0, 0.0, 0.072), (0.0, 0.0, 0.102), b_base)

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    # Base pedestal (bottom at Z=0)
    sc.make_cylinder(f"{name}_Pedestal", 0.026, 0.010, (0, 0, 0.005), "Mat_CharcoalVisor",
                     vertices=22, bevel_caps=0.0015, bone_name="Bone_BeaconBase", parts_list=parts)
    sc.make_cylinder(f"{name}_BrassCollar", 0.021, 0.006, (0, 0, 0.012), "Mat_Brass",
                     vertices=22, bone_name="Bone_BeaconBase", parts_list=parts)
    # Fluted CreamCeramic lantern waist
    sc.make_cone(f"{name}_BodyStem", 0.018, 0.014, 0.024, (0, 0, 0.026), "Mat_CreamCeramic",
                 vertices=20, bone_name="Bone_BeaconBase", parts_list=parts)

    # Runtime-tintable status light ring & crown lens (separate sub-meshes for MaterialPropertyBlock!)
    # Note: we keep Beacon_LightRing & Beacon_CrownOrb as separate child objects parented to armature
    # so Unity can drive their MaterialPropertyBlock independently without touching icon meshes!
    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Chassis")

    def make_submesh_part(part_obj, bone_name):
        part_obj.parent = arm_obj
        sc.bind_weight(part_obj, bone_name)
        mod = part_obj.modifiers.new("Armature", 'ARMATURE')
        mod.object = arm_obj
        return part_obj

    light_ring = sc.make_cylinder("Beacon_LightRing", 0.0165, 0.014, (0, 0, 0.045),
                                  "Mat_StatusCyan", vertices=22, bevel_caps=0.002)
    make_submesh_part(light_ring, "Bone_BeaconRing")

    crown_cap = sc.make_cylinder("Beacon_CrownCap", 0.015, 0.005, (0, 0, 0.055),
                                 "Mat_BrushedMetal", vertices=20, bevel_caps=0.001)
    make_submesh_part(crown_cap, "Bone_BeaconBase")

    crown_orb = sc.make_sphere("Beacon_CrownOrb", (0.011, 0.011, 0.011), (0, 0, 0.064),
                               "Mat_StatusCyan", segments=16, rings=10)
    make_submesh_part(crown_orb, "Bone_BeaconRing")

    # Mechanical indicator flag arm (swings out/up when attention or blocking decision needed)
    flag_arm = sc.make_beveled_box("Beacon_MechanicalFlag", (0.004, 0.005, 0.026),
                                   (-0.022, 0.0, 0.054), "Mat_Brass", bevel_width=0.0008)
    make_submesh_part(flag_arm, "Bone_FlagArm")
    flag_tip = sc.make_beveled_box("Beacon_MechanicalFlagTip", (0.012, 0.005, 0.010),
                                   (-0.028, 0.0, 0.068), "Mat_StatusAmber", bevel_width=0.001)
    make_submesh_part(flag_tip, "Bone_FlagArm")

    # 4 Distinct Symbolic 3D Icon Meshes for Accessibility (toggled independently at runtime!)
    # Centered at (0, 0, 0.088) above crown orb so silhouette is unmistakable from across the room
    # A. Question mark badge (Optional Suggestion)
    q_parts = []
    q_parts.append(sc.make_torus("IconQ_Arc", 0.0075, 0.0022, (0, 0, 0.094), "Mat_Researcher_Lavender",
                                 maj_seg=14, min_seg=6, rot_deg=(90, 0, 0), bone_name="Bone_IconCrest"))
    q_parts.append(sc.make_sphere("IconQ_Dot", (0.0026, 0.0026, 0.0026), (0, 0, 0.081), "Mat_CreamCeramic",
                                  segments=10, rings=6, bone_name="Bone_IconCrest"))
    icon_q = sc.join_parts_to_armature(q_parts, arm_obj, "Icon_Question")

    # B. Decision Document badge (Blocking Decision — distinct rectangular sheet + amber seal)
    d_parts = []
    d_parts.append(sc.make_beveled_box("IconDoc_Sheet", (0.016, 0.0035, 0.020), (0, 0, 0.090),
                                       "Mat_PaperWarm", bevel_width=0.0008, bone_name="Bone_IconCrest"))
    d_parts.append(sc.make_beveled_box("IconDoc_Band", (0.0165, 0.0040, 0.0045), (0, 0, 0.097),
                                       "Mat_StatusAmber", bevel_width=0.0005, bone_name="Bone_IconCrest"))
    icon_doc = sc.join_parts_to_armature(d_parts, arm_obj, "Icon_DecisionDoc")

    # C. Warning Triangle badge (Error — unmistakable triangle + exclamation)
    w_parts = []
    w_parts.append(sc.make_cone("IconWarn_Tri", 0.012, 0.001, 0.020, (0, 0, 0.090), "Mat_StatusRed",
                                vertices=3, rot_deg=(0, 0, 180), bone_name="Bone_IconCrest"))
    w_parts.append(sc.make_beveled_box("IconWarn_Bar", (0.0028, 0.006, 0.008), (0, 0.002, 0.091),
                                       "Mat_CreamCeramic", bevel_width=0.0005, bone_name="Bone_IconCrest"))
    icon_warn = sc.join_parts_to_armature(w_parts, arm_obj, "Icon_WarningTri")

    # D. Checkmark Medallion (Completed Work — ring + V check)
    c_parts = []
    c_parts.append(sc.make_cylinder("IconCheck_Disc", 0.011, 0.0035, (0, 0, 0.090), "Mat_StatusGreen",
                                    vertices=18, rot_deg=(90, 0, 0), bone_name="Bone_IconCrest"))
    c_parts.append(sc.make_beveled_box("IconCheck_Leg1", (0.0028, 0.0045, 0.007), (0.003, 0, 0.088),
                                       "Mat_CreamCeramic", bevel_width=0.0005, rot_deg=(0, 38, 0), bone_name="Bone_IconCrest"))
    c_parts.append(sc.make_beveled_box("IconCheck_Leg2", (0.0028, 0.0045, 0.011), (-0.002, 0, 0.090),
                                       "Mat_CreamCeramic", bevel_width=0.0005, rot_deg=(0, -40, 0), bone_name="Bone_IconCrest"))
    icon_check = sc.join_parts_to_armature(c_parts, arm_obj, "Icon_CheckBadge")

    # Animations (calm, peripheral-friendly!)
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
            pb.keyframe_insert(data_path="scale", frame=f)

    def add_clip(cname, end_f, fn):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    def b_idle():
        key_all(1)
        key_all(48)

    def b_working():
        key_all(1)
        arm_obj.pose.bones["Bone_BeaconRing"].rotation_euler = (0, 0, math.radians(180))
        key_all(36)
        arm_obj.pose.bones["Bone_BeaconRing"].rotation_euler = (0, 0, math.radians(360))
        key_all(72)

    def b_completed():
        key_all(1)
        arm_obj.pose.bones["Bone_IconCrest"].location = (0, 0, 0.006)
        arm_obj.pose.bones["Bone_IconCrest"].scale = (1.14, 1.14, 1.14)
        key_all(10)
        reset_bones()
        key_all(22)

    def b_suggestion():
        key_all(1)
        arm_obj.pose.bones["Bone_IconCrest"].rotation_euler = (0, math.radians(6), 0)
        arm_obj.pose.bones["Bone_IconCrest"].location = (0, 0, 0.003)
        key_all(24)
        arm_obj.pose.bones["Bone_IconCrest"].rotation_euler = (0, math.radians(-6), 0)
        key_all(48)
        reset_bones()
        key_all(64)

    def b_blocking():
        arm_obj.pose.bones["Bone_FlagArm"].rotation_euler = (0, math.radians(-38), 0)
        key_all(1)
        arm_obj.pose.bones["Bone_IconCrest"].location = (0, 0, 0.004)
        arm_obj.pose.bones["Bone_IconCrest"].scale = (1.08, 1.08, 1.08)
        key_all(20)
        arm_obj.pose.bones["Bone_IconCrest"].location = (0, 0, 0.0)
        arm_obj.pose.bones["Bone_IconCrest"].scale = (1.0, 1.0, 1.0)
        key_all(40)

    def b_error():
        arm_obj.pose.bones["Bone_FlagArm"].rotation_euler = (0, math.radians(-48), 0)
        key_all(1)
        arm_obj.pose.bones["Bone_IconCrest"].rotation_euler = (0, math.radians(8), 0)
        key_all(6)
        arm_obj.pose.bones["Bone_IconCrest"].rotation_euler = (0, math.radians(-8), 0)
        key_all(12)
        arm_obj.pose.bones["Bone_IconCrest"].rotation_euler = (0, 0, 0)
        key_all(32)

    def b_incoming():
        key_all(1)
        arm_obj.pose.bones["Bone_BeaconBase"].scale = (1.06, 1.06, 0.94)
        key_all(5)
        arm_obj.pose.bones["Bone_BeaconBase"].scale = (0.98, 0.98, 1.04)
        arm_obj.pose.bones["Bone_IconCrest"].location = (0, 0, 0.007)
        key_all(12)
        reset_bones()
        key_all(20)

    clips = [
        ("Beacon_Idle",              48, b_idle),
        ("Beacon_Working",           72, b_working),
        ("Beacon_Completed",         22, b_completed),
        ("Beacon_Suggestion",        64, b_suggestion),
        ("Beacon_Blocking_Decision", 40, b_blocking),
        ("Beacon_Error",             32, b_error),
        ("Beacon_Incoming",          20, b_incoming),
    ]
    for cname, end_f, fn in clips:
        add_clip(cname, end_f, fn)

    sc.make_anchor("BeaconRoot",  (0, 0, 0.000), arm_obj)
    sc.make_anchor("IconSocket",  (0, 0, 0.090), arm_obj)
    sc.make_anchor("LabelSocket", (0, 0.028, 0.022), arm_obj)

    return sc.export_animated_asset(
        name, [c[0] for c in clips],
        preview_cam_pos=(-0.15, 0.24, 0.14),
        preview_target=(0.0, 0.0, 0.05),
    )


# ==============================================================================
# 2. SPATIAL MAILBOX (3 Distinct Slots: Completed / Suggestion / PendingDecision)
# ==============================================================================

def build_spatial_mailbox():
    name = "SpatialMailbox"
    sc.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    # Bottom center at (0,0,0)
    # Warm wood base + cream ceramic compartment housing (13.5 cm wide x 8.5 cm deep x 8.8 cm tall)
    sc.make_rounded_slab(f"{name}_BasePlinth", 0.140, 0.088, 0.010, 0.016,
                         (0.0, 0.0, 0.005), "Mat_DarkWood", bevel=0.002, parent=root)
    sc.make_rounded_slab(f"{name}_BodyShell", 0.132, 0.080, 0.056, 0.014,
                         (0.0, -0.002, 0.038), "Mat_CreamCeramic", bevel=0.003, parent=root)

    # Three distinct color+shape-coded slot hoods across X:
    #   Left (+X Blender -> +X? Wait: Unity -X is user's right, +X is user's left)
    #   Slot 1: Completed (Mint Green, +0.042 Blender X)
    #   Slot 2: Suggestion (Lavender, 0.000 Blender X)
    #   Slot 3: Pending Decision (Amber, -0.042 Blender X)
    slots = [
        ("Completed",       +0.041, "Mat_Tester_Mint",         "Mat_StatusGreen"),
        ("Suggestion",       0.000, "Mat_Researcher_Lavender", "Mat_Researcher_Teal"),
        ("PendingDecision", -0.041, "Mat_Developer_Amber",     "Mat_StatusAmber"),
    ]
    for slbl, sx, hood_mat, pip_mat in slots:
        # Slot divider wall + colored top visor + recessed dark slot opening
        sc.make_beveled_box(f"{name}_SlotHood_{slbl}", (0.036, 0.072, 0.016),
                            (sx, 0.002, 0.068), hood_mat, bevel_width=0.002, parent=root)
        sc.make_beveled_box(f"{name}_SlotWell_{slbl}", (0.030, 0.010, 0.024),
                            (sx, 0.036, 0.048), "Mat_CharcoalVisor", bevel_width=0.001, parent=root)
        sc.make_cylinder(f"{name}_SlotPip_{slbl}", 0.0045, 0.004,
                         (sx, 0.039, 0.068), pip_mat, vertices=14, rot_deg=(90, 0, 0), parent=root)
        # Tiny physical miniature envelope tab sticking halfway out so each slot reads immediately
        sc.make_beveled_box(f"{name}_SampleTab_{slbl}", (0.024, 0.018, 0.002),
                            (sx, 0.042, 0.054), "Mat_PaperWarm", bevel_width=0.0004,
                            rot_deg=(-25, 0, 0), parent=root)

    # Front number indicator plaque (angled slightly upward toward user)
    sc.make_beveled_box(f"{name}_CounterBezel", (0.112, 0.012, 0.020),
                        (0.0, 0.042, 0.020), "Mat_Brass", bevel_width=0.0015,
                        rot_deg=(-20, 0, 0), parent=root)
    sc.make_beveled_box(f"{name}_CounterFace", (0.104, 0.013, 0.015),
                        (0.0, 0.043, 0.020), "Mat_CharcoalVisor", bevel_width=0.0006,
                        rot_deg=(-20, 0, 0), parent=root)

    # Named sockets for runtime document spawning & counter TMP text
    sc.make_anchor("MailboxRoot",           ( 0.000, 0.000, 0.000), root)
    sc.make_anchor("CompletedDocSocket",    (+0.041, 0.046, 0.058), root)
    sc.make_anchor("SuggestionDocSocket",   ( 0.000, 0.046, 0.058), root)
    sc.make_anchor("PendingDecisionSocket", (-0.041, 0.046, 0.058), root)
    sc.make_anchor("DocumentPickupPoint",   ( 0.000, 0.068, 0.064), root)
    sc.make_anchor("CounterPlateSocket",    ( 0.000, 0.049, 0.020), root, rot_deg=(-20, 0, 0))

    static_meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    sc.join_static_parts(static_meshes, f"{name}_Mesh", parent=root)

    return sc.export_static_asset(
        name,
        preview_cam_pos=(-0.18, 0.28, 0.16),
        preview_target=(0.0, 0.01, 0.04),
    )


def build_all():
    report = [
        build_attention_beacon(),
        build_spatial_mailbox(),
    ]
    out_json = os.path.join(sc.PREVIEW_SPATIAL_DIR, "beacon_and_mailbox_report.json")
    with open(out_json, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[SpatialBeaconMailbox] Built {len(report)} assets -> {out_json}")


if __name__ == "__main__":
    build_all()
