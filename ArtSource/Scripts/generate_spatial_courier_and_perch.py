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
# 1. FLOOR COURIER STATION (Dispatch pad, loading tray, lift, signal light)
# ==============================================================================

def build_floor_courier_station():
    name = "FloorCourierStation"
    sc.reset_scene()

    arm_data = bpy.data.armatures.new(f"{name}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{name}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    b_base = eb.new("Bone_StationBase")
    b_base.head = (0, 0, 0)
    b_base.tail = (0, 0, 0.025)

    b_lift = eb.new("Bone_StationLift")
    b_lift.head = (0, 0, 0.014)
    b_lift.tail = (0, 0, 0.036)
    b_lift.parent = b_base

    b_arm = eb.new("Bone_LoaderGate")
    b_arm.head = (0, -0.048, 0.022)
    b_arm.tail = (0, -0.048, 0.046)
    b_arm.parent = b_base

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    # Base chassis (bottom center at Z=0)
    sc.make_rounded_slab(f"{name}_Base", 0.128, 0.128, 0.012, 0.026,
                         (0, 0, 0.006), "Mat_CreamCeramic", bevel=0.0025,
                         bone_name="Bone_StationBase", parts_list=parts)
    # Agent-accessible sloped document loading ramp (-Y side toward agents on platform)
    sc.make_beveled_box(f"{name}_LoadingRamp", (0.078, 0.036, 0.007),
                        (0, -0.056, 0.008), "Mat_WarmWood", bevel_width=0.0015,
                        rot_deg=(12, 0, 0), bone_name="Bone_StationBase", parts_list=parts)
    # Document receiver tray beside ramp
    sc.make_beveled_box(f"{name}_DocReceiverTray", (0.054, 0.044, 0.006),
                        (-0.048, -0.022, 0.015), "Mat_DarkWood", bevel_width=0.0015,
                        bone_name="Bone_StationBase", parts_list=parts)
    # Compact lift cradle (rises smoothly when loading/receiving courier)
    sc.make_cylinder(f"{name}_LiftPillar", 0.016, 0.016, (0, 0.004, 0.014),
                     "Mat_BrushedMetal", vertices=18, bone_name="Bone_StationLift", parts_list=parts)
    sc.make_cylinder(f"{name}_DockCradle", 0.044, 0.007, (0, 0.004, 0.021),
                     "Mat_CharcoalVisor", vertices=24, bevel_caps=0.0015,
                     bone_name="Bone_StationLift", parts_list=parts)
    sc.make_torus(f"{name}_DockRing", 0.042, 0.0028, (0, 0.004, 0.024),
                  "Mat_Brass", maj_seg=24, min_seg=8,
                  bone_name="Bone_StationLift", parts_list=parts)
    # Small articulated loader guide gate
    sc.make_beveled_box(f"{name}_LoaderGate", (0.046, 0.005, 0.014),
                        (0, -0.042, 0.024), "Mat_Brass", bevel_width=0.001,
                        bone_name="Bone_LoaderGate", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Chassis")

    # Separate controllable signal light strip (MaterialPropertyBlock ready)
    sig_lamp = sc.make_beveled_box("Station_SignalLight", (0.042, 0.008, 0.007),
                                   (0, 0.056, 0.014), "Mat_Researcher_Teal", bevel_width=0.001)
    sig_lamp.parent = arm_obj
    sc.bind_weight(sig_lamp, "Bone_StationBase")
    sig_lamp.modifiers.new("Armature", 'ARMATURE').object = arm_obj

    # 4 Local Station Clips
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)

    def key_all(f):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="rotation_euler", frame=f)

    def add_clip(cname, end_f, fn):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    def s_idle():
        key_all(1)
        key_all(24)

    def s_load_lift():
        key_all(1)
        arm_obj.pose.bones["Bone_StationLift"].location = (0, 0, 0.018)
        arm_obj.pose.bones["Bone_LoaderGate"].rotation_euler = (math.radians(-65), 0, 0)
        key_all(14)
        key_all(22)

    def s_dispatch():
        arm_obj.pose.bones["Bone_StationLift"].location = (0, 0, 0.018)
        arm_obj.pose.bones["Bone_LoaderGate"].rotation_euler = (math.radians(-65), 0, 0)
        key_all(1)
        reset_bones()
        key_all(16)

    def s_dock_receive():
        arm_obj.pose.bones["Bone_StationLift"].location = (0, 0, 0.014)
        key_all(1)
        arm_obj.pose.bones["Bone_StationLift"].location = (0, 0, -0.002)
        key_all(10)
        reset_bones()
        key_all(18)

    clips = [
        ("Station_Idle",             24, s_idle),
        ("Station_Load_Lift",        22, s_load_lift),
        ("Station_Dispatch_Release", 16, s_dispatch),
        ("Station_Dock_Receive",     18, s_dock_receive),
    ]
    for cname, end_f, fn in clips:
        add_clip(cname, end_f, fn)

    sc.make_anchor("StationRoot",           ( 0.000,  0.000, 0.000), arm_obj)
    sc.make_anchor("CourierDockPoint",      ( 0.000,  0.004, 0.025), arm_obj)
    sc.make_anchor("AgentLoadPoint",        ( 0.000, -0.056, 0.015), arm_obj)
    sc.make_anchor("DocumentReceiverPoint", (-0.048, -0.022, 0.022), arm_obj)

    return sc.export_animated_asset(
        name, [c[0] for c in clips],
        preview_cam_pos=(-0.18, 0.28, 0.18),
        preview_target=(0.0, 0.0, 0.02),
    )


# ==============================================================================
# 2. DOCUMENT COURIER (Hovering Capsule / Soft Robotic Paper Carrier — NO face!)
# ==============================================================================

def build_document_courier():
    name = "DocumentCourier"
    sc.reset_scene()

    arm_data = bpy.data.armatures.new(f"{name}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{name}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    b_hull = eb.new("Bone_CourierHull")
    b_hull.head = (0, 0, 0)
    b_hull.tail = (0, 0, 0.035)

    b_tray = eb.new("Bone_DocTray")
    b_tray.head = (0, 0.005, 0.038)
    b_tray.tail = (0, 0.025, 0.055)
    b_tray.parent = b_hull

    b_cl = eb.new("Bone_Clamp_L")
    b_cl.head = ( 0.036, 0.005, 0.042)
    b_cl.tail = ( 0.024, 0.005, 0.054)
    b_cl.parent = b_tray

    b_cr = eb.new("Bone_Clamp_R")
    b_cr.head = (-0.036, 0.005, 0.042)
    b_cr.tail = (-0.024, 0.005, 0.054)
    b_cr.parent = b_tray

    b_fl = eb.new("Bone_Fin_L")
    b_fl.head = ( 0.046, 0.0, 0.022)
    b_fl.tail = ( 0.068, 0.0, 0.022)
    b_fl.parent = b_hull

    b_fr = eb.new("Bone_Fin_R")
    b_fr.head = (-0.046, 0.0, 0.022)
    b_fr.tail = (-0.068, 0.0, 0.022)
    b_fr.parent = b_hull

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    # Smooth toy-like rounded shuttle capsule (bottom dock ring at Z=0)
    sc.make_cylinder(f"{name}_DockSkid", 0.030, 0.006, (0, 0, 0.003), "Mat_CharcoalVisor",
                     vertices=22, bone_name="Bone_CourierHull", parts_list=parts)
    sc.make_rounded_slab(f"{name}_LowerHull", 0.094, 0.088, 0.018, 0.038,
                         (0, 0, 0.015), "Mat_CreamCeramic", bevel=0.005,
                         corner_segs=5, bone_name="Bone_CourierHull", parts_list=parts)
    sc.make_rounded_slab(f"{name}_MidCollar", 0.086, 0.080, 0.010, 0.034,
                         (0, 0, 0.026), "Mat_WarmWood", bevel=0.0025,
                         corner_segs=5, bone_name="Bone_CourierHull", parts_list=parts)

    # Twin side stabilizer fins (mechanical, clearly a courier pod, not an agent)
    for side, sx, bname in [("L", 1.0, "Bone_Fin_L"), ("R", -1.0, "Bone_Fin_R")]:
        sc.make_rounded_slab(f"{name}_StabFin_{side}", 0.026, 0.044, 0.006, 0.008,
                             (sx * 0.056, -0.004, 0.022), "Mat_BrushedMetal", bevel=0.0015,
                             bone_name=bname, parts_list=parts)
        sc.make_beveled_box(f"{name}_FinTip_{side}", (0.006, 0.028, 0.004),
                            (sx * 0.068, -0.004, 0.024), "Mat_Brass", bevel_width=0.0008,
                            bone_name=bname, parts_list=parts)

    # Angled upper document carrier cradle & felt cushion pad (+Y front tilted up 18 deg)
    sc.make_rounded_slab(f"{name}_DocCradle", 0.082, 0.066, 0.007, 0.016,
                         (0, 0.004, 0.038), "Mat_CharcoalVisor", bevel=0.0015,
                         rot_deg=(-18, 0, 0), bone_name="Bone_DocTray", parts_list=parts)
    sc.make_rounded_slab(f"{name}_DocCushion", 0.072, 0.056, 0.003, 0.012,
                         (0, 0.005, 0.042), "Mat_FeltSlate", bevel=0.0005,
                         rot_deg=(-18, 0, 0), bone_name="Bone_DocTray", parts_list=parts)
    # Front document stop lip
    sc.make_beveled_box(f"{name}_DocStopLip", (0.054, 0.005, 0.007),
                        (0, 0.030, 0.035), "Mat_Brass", bevel_width=0.001,
                        rot_deg=(-18, 0, 0), bone_name="Bone_DocTray", parts_list=parts)

    # Spring-loaded brass paper clamp arms
    for side, sx, bname in [("L", 1.0, "Bone_Clamp_L"), ("R", -1.0, "Bone_Clamp_R")]:
        sc.make_beveled_box(f"{name}_ClampArm_{side}", (0.018, 0.010, 0.005),
                            (sx * 0.032, 0.005, 0.048), "Mat_Brass", bevel_width=0.001,
                            rot_deg=(-18, 0, 0), bone_name=bname, parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # Separate glowing hover ring + front directional beacon (MPB tintable)
    hover_ring = sc.make_torus("Courier_HoverRing", 0.034, 0.0032, (0, 0, 0.005),
                               "Mat_StatusCyan", maj_seg=24, min_seg=8)
    hover_ring.parent = arm_obj
    sc.bind_weight(hover_ring, "Bone_CourierHull")
    hover_ring.modifiers.new("Armature", 'ARMATURE').object = arm_obj

    front_pip = sc.make_beveled_box("Courier_FrontLight", (0.026, 0.005, 0.005),
                                    (0, 0.044, 0.020), "Mat_Researcher_Teal", bevel_width=0.0008)
    front_pip.parent = arm_obj
    sc.bind_weight(front_pip, "Bone_CourierHull")
    front_pip.modifiers.new("Armature", 'ARMATURE').object = arm_obj

    # 6 Local Clips (NO world-space route baked!)
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)

    def key_all(f):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="rotation_euler", frame=f)

    def add_clip(cname, end_f, fn):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    def pose_clamps_open():
        arm_obj.pose.bones["Bone_Clamp_L"].rotation_euler = (0, math.radians(46), 0)
        arm_obj.pose.bones["Bone_Clamp_R"].rotation_euler = (0, math.radians(-46), 0)

    def c_docked_idle():
        pose_clamps_open()
        key_all(1)
        key_all(36)

    def c_load():
        pose_clamps_open()
        key_all(1)
        reset_bones()
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.012)
        key_all(16)

    def c_fly_loop():
        arm_obj.pose.bones["Bone_CourierHull"].rotation_euler = (math.radians(-6), 0, 0)
        arm_obj.pose.bones["Bone_Fin_L"].rotation_euler = (0, math.radians(6), 0)
        arm_obj.pose.bones["Bone_Fin_R"].rotation_euler = (0, math.radians(-6), 0)
        key_all(1)
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.005)
        key_all(15)
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.0)
        key_all(30)

    def c_arrive_present():
        arm_obj.pose.bones["Bone_CourierHull"].rotation_euler = (math.radians(8), 0, 0)
        key_all(1)
        reset_bones()
        arm_obj.pose.bones["Bone_DocTray"].rotation_euler = (math.radians(-14), 0, 0)
        pose_clamps_open()
        key_all(18)

    def c_wait_idle():
        arm_obj.pose.bones["Bone_DocTray"].rotation_euler = (math.radians(-14), 0, 0)
        pose_clamps_open()
        key_all(1)
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.004)
        key_all(24)
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.0)
        key_all(48)

    def c_return_dock():
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, 0.014)
        key_all(1)
        pose_clamps_open()
        arm_obj.pose.bones["Bone_CourierHull"].location = (0, 0, -0.002)
        key_all(14)
        pose_clamps_open()
        key_all(20)

    clips = [
        ("Courier_Docked_Idle",   36, c_docked_idle),
        ("Courier_Load_Document", 16, c_load),
        ("Courier_Fly_Loop",      30, c_fly_loop),
        ("Courier_Arrive_Present",18, c_arrive_present),
        ("Courier_Waiting_Idle",  48, c_wait_idle),
        ("Courier_Return_Dock",   20, c_return_dock),
    ]
    for cname, end_f, fn in clips:
        add_clip(cname, end_f, fn)

    sc.make_anchor("CourierRoot",                (0.0, 0.000, 0.000), arm_obj)
    sc.make_anchor("DockBaseSocket",             (0.0, 0.000, 0.000), arm_obj)
    sc.make_anchor("DocumentAttachSocket",       (0.0, 0.006, 0.056), arm_obj, rot_deg=(-28, 0, 0))
    sc.make_anchor("PresentationColliderCenter", (0.0, 0.006, 0.045), arm_obj)

    return sc.export_animated_asset(
        name, [c[0] for c in clips],
        preview_cam_pos=(-0.15, 0.22, 0.14),
        preview_target=(0.0, 0.0, 0.03),
    )


# ==============================================================================
# 3. AGENT CONVERSATION PERCH (Lightweight single-agent floating presentation disc)
# ==============================================================================

def build_agent_conversation_perch():
    name = "AgentConversationPerch"
    sc.reset_scene()

    arm_data = bpy.data.armatures.new(f"{name}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{name}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    b_disc = eb.new("Bone_PerchDisc")
    b_disc.head = (0, 0, 0)
    b_disc.tail = (0, 0, 0.025)

    b_wl = eb.new("Bone_PerchWing_L")
    b_wl.head = ( 0.072, 0, 0.012)
    b_wl.tail = ( 0.105, 0, 0.012)
    b_wl.parent = b_disc

    b_wr = eb.new("Bone_PerchWing_R")
    b_wr.head = (-0.072, 0, 0.012)
    b_wr.tail = (-0.105, 0, 0.012)
    b_wr.parent = b_disc

    b_rail = eb.new("Bone_PerchBackRail")
    b_rail.head = (0, -0.068, 0.018)
    b_rail.tail = (0, -0.068, 0.044)
    b_rail.parent = b_disc

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    parts = []
    # Main circular platform sized for 1 mascot (15.6 cm diameter, 1.8 cm deck height)
    sc.make_cylinder(f"{name}_BaseRing", 0.078, 0.014, (0, 0, 0.011), "Mat_CreamCeramic",
                     vertices=28, bevel_caps=0.003, bone_name="Bone_PerchDisc", parts_list=parts)
    sc.make_cylinder(f"{name}_LowerBelly", 0.062, 0.008, (0, 0, 0.004), "Mat_CharcoalVisor",
                     vertices=24, bevel_caps=0.0015, bone_name="Bone_PerchDisc", parts_list=parts)
    sc.make_cylinder(f"{name}_StandingInlay", 0.064, 0.003, (0, 0, 0.017), "Mat_WarmWood",
                     vertices=26, bone_name="Bone_PerchDisc", parts_list=parts)
    sc.make_cylinder(f"{name}_AgentPad", 0.042, 0.0035, (0, -0.006, 0.0175), "Mat_FeltSlate",
                     vertices=22, bone_name="Bone_PerchDisc", parts_list=parts)

    # Unfolding side stabilizer petals (also serve as subtle resting surface for document shelf)
    for side, sx, bname in [("L", 1.0, "Bone_PerchWing_L"), ("R", -1.0, "Bone_PerchWing_R")]:
        sc.make_rounded_slab(f"{name}_Petal_{side}", 0.042, 0.076, 0.007, 0.016,
                             (sx * 0.088, 0.005, 0.012), "Mat_CreamCeramic", bevel=0.0018,
                             bone_name=bname, parts_list=parts)
        sc.make_beveled_box(f"{name}_PetalTrim_{side}", (0.006, 0.046, 0.004),
                            (sx * 0.104, 0.005, 0.015), "Mat_Brass", bevel_width=0.0008,
                            bone_name=bname, parts_list=parts)

    # Low curved back-guard rail (folds down flat when stowed, pops up gently for mascot safety)
    sc.make_beveled_box(f"{name}_BackRail", (0.076, 0.008, 0.014), (0, -0.068, 0.028),
                        "Mat_BrushedMetal", bevel_width=0.002,
                        bone_name="Bone_PerchBackRail", parts_list=parts)
    sc.make_beveled_box(f"{name}_BackRailTrim", (0.056, 0.009, 0.005), (0, -0.068, 0.036),
                        "Mat_Brass", bevel_width=0.001,
                        bone_name="Bone_PerchBackRail", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # Separate controllable front status & conversation indicator ring (MPB controllable)
    convo_ring = sc.make_torus("Perch_ConvoRing", 0.079, 0.0028, (0, 0, 0.011),
                               "Mat_Researcher_Teal", maj_seg=28, min_seg=8)
    convo_ring.parent = arm_obj
    sc.bind_weight(convo_ring, "Bone_PerchDisc")
    convo_ring.modifiers.new("Armature", 'ARMATURE').object = arm_obj

    status_led = sc.make_beveled_box("Perch_StatusLED", (0.036, 0.006, 0.006),
                                     (0, 0.077, 0.012), "Mat_StatusCyan", bevel_width=0.001)
    status_led.parent = arm_obj
    sc.bind_weight(status_led, "Bone_PerchDisc")
    status_led.modifiers.new("Armature", 'ARMATURE').object = arm_obj

    # 5 Local Clips
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)

    def key_all(f):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="rotation_euler", frame=f)

    def add_clip(cname, end_f, fn):
        act = bpy.data.actions.new(cname)
        arm_obj.animation_data.action = act
        reset_bones()
        fn()
        sc.push_action_to_nla(arm_obj, cname, 1, end_f)

    def pose_folded():
        arm_obj.pose.bones["Bone_PerchWing_L"].rotation_euler = (0, math.radians(-85), 0)
        arm_obj.pose.bones["Bone_PerchWing_R"].rotation_euler = (0, math.radians(85), 0)
        arm_obj.pose.bones["Bone_PerchBackRail"].rotation_euler = (math.radians(-80), 0, 0)

    def p_folded_idle():
        pose_folded()
        key_all(1)
        key_all(24)

    def p_unfold_arrive():
        pose_folded()
        arm_obj.pose.bones["Bone_PerchDisc"].location = (0, 0, -0.015)
        key_all(1)
        reset_bones()
        arm_obj.pose.bones["Bone_PerchDisc"].location = (0, 0, 0.004)
        key_all(14)
        reset_bones()
        key_all(22)

    def p_listening_idle():
        reset_bones()
        key_all(1)
        arm_obj.pose.bones["Bone_PerchDisc"].location = (0, 0, 0.0025)
        key_all(30)
        reset_bones()
        key_all(60)

    def p_speaking_pulse():
        reset_bones()
        key_all(1)
        arm_obj.pose.bones["Bone_PerchDisc"].location = (0, 0, 0.004)
        arm_obj.pose.bones["Bone_PerchWing_L"].rotation_euler = (0, math.radians(-4), 0)
        arm_obj.pose.bones["Bone_PerchWing_R"].rotation_euler = (0, math.radians(4), 0)
        key_all(10)
        reset_bones()
        key_all(20)

    def p_fold_return():
        reset_bones()
        key_all(1)
        pose_folded()
        arm_obj.pose.bones["Bone_PerchDisc"].location = (0, 0, -0.010)
        key_all(18)

    clips = [
        ("Perch_Folded_Idle",    24, p_folded_idle),
        ("Perch_Unfold_Arrive",  22, p_unfold_arrive),
        ("Perch_Listening_Idle", 60, p_listening_idle),
        ("Perch_Speaking_Pulse", 20, p_speaking_pulse),
        ("Perch_Fold_Return",    18, p_fold_return),
    ]
    for cname, end_f, fn in clips:
        add_clip(cname, end_f, fn)

    sc.make_anchor("PerchRoot",                  ( 0.000,  0.000, 0.000), arm_obj)
    sc.make_anchor("AgentStandSocket",           ( 0.000, -0.006, 0.019), arm_obj)
    sc.make_anchor("DocumentPresentationSocket", (-0.088,  0.032, 0.036), arm_obj)
    sc.make_anchor("ConversationIndicator",      ( 0.000,  0.078, 0.012), arm_obj)

    return sc.export_animated_asset(
        name, [c[0] for c in clips],
        preview_cam_pos=(-0.22, 0.30, 0.18),
        preview_target=(0.0, 0.0, 0.02),
    )


def build_all():
    report = [
        build_floor_courier_station(),
        build_document_courier(),
        build_agent_conversation_perch(),
    ]
    out_json = os.path.join(sc.PREVIEW_SPATIAL_DIR, "courier_and_perch_report.json")
    with open(out_json, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[SpatialCourierPerch] Built {len(report)} assets -> {out_json}")


if __name__ == "__main__":
    build_all()

