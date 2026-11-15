import bpy
import math
import mathutils
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import wall_habitat_common as wh
import spatial_common as sc


def _make_armature_bone(arm_data, name, head, tail, parent_bone=None):
    b = arm_data.edit_bones.new(name)
    b.head = head
    b.tail = tail
    if parent_bone is not None:
        b.parent = parent_bone
    return b


def _setup_anim(arm_obj):
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'


def _key_pose_map(arm_obj, clip_name, duration, frames_spec):
    if clip_name in bpy.data.actions:
        bpy.data.actions.remove(bpy.data.actions[clip_name])
    act = bpy.data.actions.new(name=clip_name)
    act.use_fake_user = True
    arm_obj.animation_data.action = act

    for frame, bone_dict in frames_spec:
        for pb in arm_obj.pose.bones:
            pb.location = (0.0, 0.0, 0.0)
            pb.rotation_euler = (0.0, 0.0, 0.0)
            pb.scale = (1.0, 1.0, 1.0)
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=frame)
            pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            pb.keyframe_insert(data_path="scale", frame=frame)
        for bname, xf in bone_dict.items():
            pb = arm_obj.pose.bones.get(bname)
            if not pb:
                continue
            if "loc" in xf:
                pb.location = xf["loc"]
                pb.keyframe_insert(data_path="location", frame=frame)
            if "rot" in xf:
                pb.rotation_euler = tuple(math.radians(a) for a in xf["rot"])
                pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            if "scale" in xf:
                pb.scale = xf["scale"]
                pb.keyframe_insert(data_path="scale", frame=frame)

    sc.push_action_to_nla(arm_obj, clip_name, 1, duration)
    return clip_name


def _build_elevator_spine(tiers=3):
    """
    Miniature Enclosed Treehouse Elevator Spine (3-Tier = 0.72m tall, 2-Tier = 0.49m tall).
    Pivot (0, 0, 0) at bottom center at spine socket (Blender Y=0 is backplate+0.015m).
    Shaft cab chamber centered at Blender Y=+0.170 (aligns directly with front corridor promenade!).
    Landings face Blender +X (Unity -X into Lower/Mid/Manager decks!).
    """
    name = f"WallHabitat_ElevatorSpine_{tiers}Tier"
    wh.reset_scene()

    story_h = 0.230
    total_h = (tiers - 1) * story_h + 0.255  # roof winch housing above top deck
    shaft_y = 0.170
    shaft_w = 0.124
    shaft_d = 0.124

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_root = _make_armature_bone(arm_data, "Bone_SpineRoot", (0, 0, 0), (0, 0, 0.08))
    b_winch = _make_armature_bone(arm_data, "Bone_WinchDrum", (0, shaft_y, total_h - 0.022), (0, shaft_y + 0.04, total_h - 0.022), b_root)

    floor_defs = [("Lower", 0.012)]
    if tiers == 3:
        floor_defs.append(("Mid", story_h + 0.012))
    mgr_z = (tiers - 1) * story_h + 0.014
    floor_defs.append(("Manager", mgr_z))

    door_bones = {}
    for flbl, fz in floor_defs:
        door_x = +shaft_w * 0.5 - 0.004
        bf = _make_armature_bone(arm_data, f"Bone_Door{flbl}_F", (door_x, shaft_y + 0.020, fz + 0.08), (door_x, shaft_y + 0.05, fz + 0.08), b_root)
        bb = _make_armature_bone(arm_data, f"Bone_Door{flbl}_B", (door_x, shaft_y - 0.020, fz + 0.08), (door_x, shaft_y - 0.05, fz + 0.08), b_root)
        bi = _make_armature_bone(arm_data, f"Bone_Ind{flbl}", (door_x + 0.004, shaft_y, fz + 0.186), (door_x + 0.004, shaft_y, fz + 0.206), b_root)
        door_bones[flbl] = (bf, bb, bi)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # Base plinth & rear structural column attached to wall backing
    wh.make_rounded_slab(f"{name}_BasePlinth", shaft_w + 0.022, shaft_d + 0.095, 0.014, 0.018,
                         (0.0, shaft_y - 0.032, 0.007), "Mat_DarkWood", bevel=0.0018,
                         bone_name="Bone_SpineRoot", parts_list=parts)
    # Four slender brass/wood vertical shaft corner columns
    hx = shaft_w * 0.5
    hy_f = shaft_y + shaft_d * 0.5
    hy_b = shaft_y - shaft_d * 0.5
    for cx_s in (-1, 1):
        for cy_s in (-1, 1):
            cy = shaft_y + cy_s * (shaft_d * 0.5 - 0.005)
            cx = cx_s * (hx - 0.005)
            wh.make_cylinder(f"{name}_Col_{cx_s:+.0f}_{cy_s:+.0f}", 0.0055, total_h - 0.025,
                             (cx, cy, (total_h - 0.025) * 0.5), "Mat_WarmWood", vertices=12,
                             bone_name="Bone_SpineRoot", parts_list=parts)
    # Machined metal guide rails along front/back center of inner shaft (-X / rear side)
    wh.make_beveled_box(f"{name}_GuideRail_L", (0.007, 0.010, total_h - 0.035),
                        (-hx + 0.006, shaft_y, (total_h - 0.035) * 0.5), "Mat_BrushedMetal",
                        bevel_width=0.0008, bone_name="Bone_SpineRoot", parts_list=parts)
    wh.make_beveled_box(f"{name}_RearPanelGlass", (shaft_w - 0.018, 0.004, total_h - 0.040),
                        (0.0, hy_b + 0.006, (total_h - 0.035) * 0.5), "Mat_ChamberGlass",
                        bevel_width=0.0005, bone_name="Bone_SpineRoot", parts_list=parts)
    # Front subtle translucent glass fascia slats (keeps cabin silhouette crystal clear)
    wh.make_beveled_box(f"{name}_FrontTopBand", (shaft_w - 0.012, 0.006, 0.014),
                        (0.0, hy_f - 0.004, total_h - 0.032), "Mat_CreamCeramic",
                        bevel_width=0.0012, bone_name="Bone_SpineRoot", parts_list=parts)
    # Right outside structural ribs
    for ti in range(tiers):
        tz = ti * story_h + 0.010
        wh.make_beveled_box(f"{name}_OuterGirt_{ti}", (0.010, shaft_d - 0.006, 0.014),
                            (-hx + 0.005, shaft_y, tz), "Mat_CreamCeramic", bevel_width=0.001,
                            bone_name="Bone_SpineRoot", parts_list=parts)
        # Floor deck landing sill projecting slightly toward deck (+X Blender)
        wh.make_beveled_box(f"{name}_LandingSill_{ti}", (0.036, shaft_d - 0.008, 0.012),
                            (+hx + 0.012, shaft_y, tz - 0.004), "Mat_WarmWood", bevel_width=0.0015,
                            bone_name="Bone_SpineRoot", parts_list=parts)
        # Overhead portal header per floor facing +X deck
        wh.make_beveled_box(f"{name}_PortalHeader_{ti}", (0.012, shaft_d - 0.008, 0.032),
                            (+hx - 0.004, shaft_y, tz + 0.186), "Mat_CreamCeramic", bevel_width=0.0015,
                            bone_name="Bone_SpineRoot", parts_list=parts)

    # Top Winch Canopy & Spinning Brass Drum
    wh.make_rounded_slab(f"{name}_CrownCap", shaft_w + 0.018, shaft_d + 0.018, 0.024, 0.022,
                         (0.0, shaft_y, total_h - 0.008), "Mat_WarmWood", bevel=0.0025,
                         bone_name="Bone_SpineRoot", parts_list=parts)
    wh.make_cylinder(f"{name}_CrownFinial", 0.016, 0.014, (0.0, shaft_y, total_h + 0.011),
                     "Mat_Brass", vertices=18, bone_name="Bone_SpineRoot", parts_list=parts)
    wh.make_cylinder(f"{name}_WinchWheel", 0.024, 0.026, (0.0, shaft_y, total_h - 0.022),
                     "Mat_Brass", vertices=20, rot_deg=(0, 90, 0),
                     bone_name="Bone_WinchDrum", parts_list=parts)

    # Parting sliding brass-framed doors + floor call indicator gems per stop
    for flbl, fz in floor_defs:
        door_x = +hx - 0.004
        door_h = 0.168
        door_w_y = 0.044
        # Front half door (+Y half) & Back half door (-Y half)
        for half_lbl, dy, b_suffix in [("F", +door_w_y * 0.5, "F"), ("B", -door_w_y * 0.5, "B")]:
            wh.make_beveled_box(f"{name}_Door{flbl}_{half_lbl}_Frame", (0.006, door_w_y, door_h),
                                (door_x, shaft_y + dy, fz + door_h * 0.5), "Mat_Brass",
                                bevel_width=0.001, bone_name=f"Bone_Door{flbl}_{b_suffix}", parts_list=parts)
            wh.make_beveled_box(f"{name}_Door{flbl}_{half_lbl}_Pane", (0.007, door_w_y - 0.012, door_h - 0.028),
                                (door_x, shaft_y + dy, fz + door_h * 0.5 + 0.006), "Mat_ChamberGlass",
                                bevel_width=0.0004, bone_name=f"Bone_Door{flbl}_{b_suffix}", parts_list=parts)

        # Call/Stop Indicator gem mounted above portal header on deck side
        ind_mat = "Mat_StatusCyan" if flbl == "Lower" else ("Mat_Developer_Amber" if flbl == "Mid" else "Mat_StatusGreen")
        wh.make_beveled_box(f"{name}_IndBezel_{flbl}", (0.008, 0.026, 0.016),
                            (door_x + 0.005, shaft_y, fz + 0.186), "Mat_CharcoalVisor",
                            bevel_width=0.001, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_sphere(f"{name}_IndGem_{flbl}", (0.005, 0.008, 0.006),
                       (door_x + 0.009, shaft_y, fz + 0.186), ind_mat,
                       segments=12, rings=8, bone_name=f"Bone_Ind{flbl}", parts_list=parts)

    # Dual vertical tether cables running shaft height
    for cy_off in (-0.018, +0.018):
        wh.make_cylinder(f"{name}_Cable_{cy_off:+.2f}", 0.0016, total_h - 0.030,
                         (0.0, shaft_y + cy_off, (total_h - 0.020) * 0.5), "Mat_BrushedMetal",
                         vertices=8, bone_name="Bone_SpineRoot", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # Keyframe all Spine animation clips
    clips = []
    clips.append(_key_pose_map(arm_obj, "ElevatorDoors_Idle_Closed", 24, [
        (1, {}),
        (24, {}),
    ]))
    for flbl, _ in floor_defs:
        open_d = {
            f"Bone_Door{flbl}_F": {"loc": (0.0, +0.044, 0.0)},
            f"Bone_Door{flbl}_B": {"loc": (0.0, -0.044, 0.0)},
            f"Bone_Ind{flbl}":    {"scale": (1.25, 1.25, 1.25)},
        }
        clips.append(_key_pose_map(arm_obj, f"ElevatorDoors_Open_{flbl}", 18, [
            (1, {}),
            (18, open_d),
        ]))
        clips.append(_key_pose_map(arm_obj, f"ElevatorDoors_Close_{flbl}", 18, [
            (1, open_d),
            (18, {}),
        ]))
    clips.append(_key_pose_map(arm_obj, "ElevatorWinch_Spin", 24, [
        (1,  {"Bone_WinchDrum": {"rot": (0, 0, 0)}}),
        (12, {"Bone_WinchDrum": {"rot": (0, 180, 0)}}),
        (24, {"Bone_WinchDrum": {"rot": (0, 360, 0)}}),
    ]))

    arm_obj.animation_data.action = bpy.data.actions.get("ElevatorDoors_Idle_Closed")
    bpy.context.scene.frame_set(1)

    # Named Sockets
    wh.make_anchor("ElevatorCarSocket", (0.0, shaft_y, 0.012), arm_obj)
    for flbl, fz in floor_defs:
        wh.make_anchor(f"ElevatorStop_{flbl}",      (+hx + 0.015, shaft_y, fz), arm_obj)
        wh.make_anchor(f"ElevatorQueuePose_{flbl}", (+hx + 0.055, shaft_y, fz), arm_obj)
        wh.make_anchor(f"EntryPose_{flbl}",         (+hx, shaft_y, fz), arm_obj)
        wh.make_anchor(f"ElevatorEntry_{flbl}",     (+hx + 0.018, shaft_y, fz), arm_obj)
        wh.make_anchor(f"ElevatorExit_{flbl}",      (+hx + 0.042, shaft_y, fz), arm_obj)
    wh.make_anchor("AgentHomePose",     (+hx + 0.055, shaft_y, 0.012), arm_obj)
    wh.make_anchor("WorkPose",          (0.0, shaft_y, 0.012), arm_obj)
    wh.make_anchor("SeatPose",          (0.0, shaft_y, 0.012), arm_obj)
    wh.make_anchor("EntryPose",         (+hx, shaft_y, 0.012), arm_obj)
    wh.make_anchor("HandoffPose",       (+hx + 0.032, shaft_y, 0.012), arm_obj)
    wh.make_anchor("ElevatorQueuePose", (+hx + 0.055, shaft_y, 0.012), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, shaft_y, 0.072), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, shaft_y, 0.115), arm_obj)
    wh.make_anchor("DoorClearance",     (+hx + 0.010, shaft_y, 0.092), arm_obj)
    wh.make_anchor("PathNode_ShaftFront",(0.0, shaft_y + 0.068, 0.012), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.35, 1.05, total_h * 0.55), target=(0.0, shaft_y, total_h * 0.45))


def _build_elevator_car():
    """
    Miniature Panorama Treehouse Elevator Car:
    - Pivot (0, 0, 0) at cab bottom center (floor top Z = 0.008m).
    - Clear internal space: 0.098m x 0.100m x 0.164m (fits agent 0.064m D x 0.139m H with zero clipping!).
    - Articulated brass sliding gates on deck-entry side (+X Blender = -X Unity).
    """
    name = "WallHabitat_ElevatorCar"
    wh.reset_scene()

    w, d, h = 0.108, 0.108, 0.180
    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_cab = _make_armature_bone(arm_data, "Bone_CabBody", (0, 0, 0), (0, 0, 0.09))
    door_x = +w * 0.5 - 0.003
    _make_armature_bone(arm_data, "Bone_CabGate_F", (door_x, +0.020, 0.06), (door_x, +0.045, 0.06), b_cab)
    _make_armature_bone(arm_data, "Bone_CabGate_B", (door_x, -0.020, 0.06), (door_x, -0.045, 0.06), b_cab)
    _make_armature_bone(arm_data, "Bone_CabLamp",   (0, 0, h - 0.015), (0, 0, h - 0.028), b_cab)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # Cab floor base + soft FeltSlate floor pad
    wh.make_rounded_slab(f"{name}_Floor", w, d, 0.008, 0.014, (0.0, 0.0, 0.004),
                         "Mat_WarmWood", bevel=0.0012, bone_name="Bone_CabBody", parts_list=parts)
    wh.make_rounded_slab(f"{name}_FloorPad", w - 0.016, d - 0.016, 0.002, 0.010, (0.0, 0.0, 0.009),
                         "Mat_FeltSlate", bevel=0.0003, bone_name="Bone_CabBody", parts_list=parts)

    # Four curved brass corner posts
    hx, hy = w * 0.5 - 0.004, d * 0.5 - 0.004
    for sx in (-1, 1):
        for sy in (-1, 1):
            wh.make_cylinder(f"{name}_Post_{sx:+.0f}_{sy:+.0f}", 0.0038, h - 0.014,
                             (sx * hx, sy * hy, h * 0.5), "Mat_Brass", vertices=12,
                             bone_name="Bone_CabBody", parts_list=parts)

    # Low waist guardrails on closed sides (-X, -Y, +Y stays open panorama frame)
    wh.make_beveled_box(f"{name}_BackWainscot", (0.006, d - 0.014, 0.048),
                        (-hx, 0.0, 0.032), "Mat_CreamCeramic", bevel_width=0.001,
                        bone_name="Bone_CabBody", parts_list=parts)
    wh.make_beveled_box(f"{name}_BackWindow", (0.003, d - 0.020, 0.112),
                        (-hx, 0.0, 0.112), "Mat_ChamberGlass", bevel_width=0.0004,
                        bone_name="Bone_CabBody", parts_list=parts)
    wh.make_beveled_box(f"{name}_RearRail", (w - 0.014, 0.004, 0.036),
                        (0.0, -hy, 0.026), "Mat_WarmWood", bevel_width=0.0008,
                        bone_name="Bone_CabBody", parts_list=parts)
    # Front subtle bottom brass curb + upper canopy valance (front face open glass-free so character reads clearly!)
    wh.make_beveled_box(f"{name}_FrontCurb", (w - 0.014, 0.005, 0.010),
                        (0.0, +hy, 0.012), "Mat_Brass", bevel_width=0.0008,
                        bone_name="Bone_CabBody", parts_list=parts)

    # Domed CreamCeramic canopy roof + Brass suspension yoke + Interior warm lamp
    wh.make_rounded_slab(f"{name}_RoofDeck", w + 0.004, d + 0.004, 0.012, 0.016,
                         (0.0, 0.0, h - 0.008), "Mat_CreamCeramic", bevel=0.002,
                         bone_name="Bone_CabBody", parts_list=parts)
    wh.make_cylinder(f"{name}_SuspYoke", 0.014, 0.012, (0.0, 0.0, h + 0.004),
                     "Mat_Brass", vertices=16, bone_name="Bone_CabBody", parts_list=parts)
    wh.make_sphere(f"{name}_CabCeilingLamp", (0.012, 0.012, 0.006), (0.0, 0.0, h - 0.015),
                   "Mat_LampGlow", segments=12, rings=8, bone_name="Bone_CabLamp", parts_list=parts)

    # Sliding brass safety gates on entry side (+X Blender)
    for glbl, gy, b_suf in [("F", +0.022, "F"), ("B", -0.022, "B")]:
        wh.make_beveled_box(f"{name}_Gate_{glbl}", (0.004, 0.040, 0.118),
                            (door_x, gy, 0.072), "Mat_Brass", bevel_width=0.0008,
                            bone_name=f"Bone_CabGate_{b_suf}", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    open_gate = {
        "Bone_CabGate_F": {"loc": (0.0, +0.042, 0.0)},
        "Bone_CabGate_B": {"loc": (0.0, -0.042, 0.0)},
    }
    clips = [
        _key_pose_map(arm_obj, "Cabin_Idle_Closed", 24, [(1, {}), (24, {})]),
        _key_pose_map(arm_obj, "Cabin_Gate_Open", 16, [(1, {}), (16, open_gate)]),
        _key_pose_map(arm_obj, "Cabin_Gate_Close", 16, [(1, open_gate), (16, {})]),
        _key_pose_map(arm_obj, "Cabin_Travel_Bob", 36, [
            (1,  {}),
            (9,  {"Bone_CabBody": {"loc": (0, 0, +0.0025)}, "Bone_CabLamp": {"scale": (1.12, 1.12, 1.12)}}),
            (18, {"Bone_CabBody": {"loc": (0, 0, -0.0020)}}),
            (27, {"Bone_CabBody": {"loc": (0, 0, +0.0020)}, "Bone_CabLamp": {"scale": (1.12, 1.12, 1.12)}}),
            (36, {}),
        ]),
        _key_pose_map(arm_obj, "Cabin_Arrive_Chime", 20, [
            (1,  {}),
            (6,  {"Bone_CabBody": {"loc": (0, 0, -0.0045)}, "Bone_CabLamp": {"scale": (1.28, 1.28, 1.28)}}),
            (13, {"Bone_CabBody": {"loc": (0, 0, +0.0020)}}),
            (20, {}),
        ]),
    ]
    arm_obj.animation_data.action = bpy.data.actions.get("Cabin_Idle_Closed")
    bpy.context.scene.frame_set(1)

    wh.make_anchor("ElevatorCabinPose", (0.0, 0.0, 0.010), arm_obj)
    wh.make_anchor("AgentHomePose",     (0.0, 0.0, 0.010), arm_obj)
    wh.make_anchor("WorkPose",          (0.0, 0.0, 0.010), arm_obj)
    wh.make_anchor("SeatPose",          (0.0, 0.0, 0.010), arm_obj)
    wh.make_anchor("EntryPose",         (+w * 0.5, 0.0, 0.010), arm_obj)
    wh.make_anchor("HandoffPose",       (+w * 0.25, 0.0, 0.010), arm_obj)
    wh.make_anchor("ElevatorQueuePose", (+w * 0.72, 0.0, 0.010), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, +0.024, 0.068), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, 0.0, 0.110), arm_obj)
    wh.make_anchor("DoorClearance",     (+w * 0.52, 0.0, 0.088), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.24, 0.36, 0.14), target=(0.0, 0.0, 0.09))


def _build_task_printer():
    """
    Miniature Manager Delegation Task Printer:
    Mounted at TaskPrinterSocket on the Manager Lookout deck.
    Articulated print carriage, brass roller drum, status orb, and animated emerging task card.
    """
    name = "WallHabitat_TaskPrinter"
    wh.reset_scene()

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_base = _make_armature_bone(arm_data, "Bone_PrinterBase", (0, 0, 0), (0, 0, 0.035))
    _make_armature_bone(arm_data, "Bone_PrintHead",   (0, +0.008, 0.050), (0, +0.008, 0.065), b_base)
    _make_armature_bone(arm_data, "Bone_FeedRoller",  (0, +0.022, 0.034), (+0.028, +0.022, 0.034), b_base)
    _make_armature_bone(arm_data, "Bone_StatusLight", (-0.026, +0.024, 0.048), (-0.026, +0.024, 0.060), b_base)
    _make_armature_bone(arm_data, "Bone_EjectCard",   (0, +0.016, 0.028), (0, +0.040, 0.022), b_base)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # Main cozy curved ceramic chassis + recessed front paper output throat & brass feed lip
    wh.make_rounded_slab(f"{name}_Body", 0.082, 0.058, 0.042, 0.014, (0.0, 0.0, 0.021),
                         "Mat_CreamCeramic", bevel=0.0025, bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_OutputThroat", (0.048, 0.006, 0.010), (0.0, +0.0285, 0.027),
                        "Mat_CharcoalVisor", bevel_width=0.001, bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_BrassLip", (0.052, 0.010, 0.0035), (0.0, +0.033, 0.020),
                        "Mat_Brass", bevel_width=0.0008, rot_deg=(12, 0, 0),
                        bone_name="Bone_PrinterBase", parts_list=parts)
    # Recessed dark wood output throat & top accent hood
    wh.make_rounded_slab(f"{name}_TealHood", 0.072, 0.044, 0.014, 0.012, (0.0, -0.004, 0.046),
                         "Mat_Researcher_Teal", bevel=0.002, bone_name="Bone_PrinterBase", parts_list=parts)
    # Paper stack magazine protruding slightly out top rear
    wh.make_beveled_box(f"{name}_PaperMagazine", (0.044, 0.010, 0.024), (0.0, -0.016, 0.058),
                        "Mat_PaperWarm", bevel_width=0.0008, rot_deg=(-12, 0, 0),
                        bone_name="Bone_PrinterBase", parts_list=parts)
    # Articulated print head carriage & brass roller
    wh.make_beveled_box(f"{name}_HeadCarriage", (0.018, 0.016, 0.012), (0.0, +0.010, 0.054),
                        "Mat_Brass", bevel_width=0.0015, bone_name="Bone_PrintHead", parts_list=parts)
    wh.make_cylinder(f"{name}_BrassRoller", 0.006, 0.054, (0.0, +0.022, 0.034),
                     "Mat_Brass", vertices=14, rot_deg=(0, 90, 0),
                     bone_name="Bone_FeedRoller", parts_list=parts)
    wh.make_sphere(f"{name}_StatusOrb", (0.0055, 0.0055, 0.0055), (-0.026, +0.024, 0.054),
                   "Mat_StatusCyan", segments=12, rings=8, bone_name="Bone_StatusLight", parts_list=parts)
    # Animated task card emerging cleanly out front slit onto Manager tray
    wh.make_beveled_box(f"{name}_PrintedCard", (0.036, 0.026, 0.0022), (0.0, +0.012, 0.028),
                        "Mat_PaperWarm", bevel_width=0.0004, bone_name="Bone_EjectCard", parts_list=parts)
    wh.make_beveled_box(f"{name}_PrintedCardStripe", (0.032, 0.006, 0.0026), (0.0, +0.019, 0.0285),
                        "Mat_Developer_Coral", bevel_width=0.0003, bone_name="Bone_EjectCard", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    clips = [
        _key_pose_map(arm_obj, "Printer_Idle", 36, [
            (1,  {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}}),
            (18, {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}, "Bone_StatusLight": {"scale": (0.85, 0.85, 0.85)}}),
            (36, {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}}),
        ]),
        _key_pose_map(arm_obj, "Printer_Print_TaskCard", 36, [
            (1,  {"Bone_PrintHead": {"loc": (-0.020, 0, 0)}, "Bone_EjectCard": {"scale": (0.1, 0.1, 0.1)}}),
            (8,  {"Bone_PrintHead": {"loc": (+0.020, 0, 0)}, "Bone_FeedRoller": {"rot": (120, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.010, 0), "scale": (1, 1, 1)}, "Bone_StatusLight": {"scale": (1.25, 1.25, 1.25)}}),
            (16, {"Bone_PrintHead": {"loc": (-0.018, 0, 0)}, "Bone_FeedRoller": {"rot": (240, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.022, -0.002), "scale": (1, 1, 1)}}),
            (26, {"Bone_PrintHead": {"loc": (+0.015, 0, 0)}, "Bone_FeedRoller": {"rot": (360, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.036, -0.005), "rot": (-14, 0, 0), "scale": (1, 1, 1)}}),
            (36, {"Bone_PrintHead": {"loc": (0, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.038, -0.006), "rot": (-16, 0, 0), "scale": (1, 1, 1)}}),
        ]),
        _key_pose_map(arm_obj, "Printer_Dispatch_Card", 24, [
            (1,  {"Bone_EjectCard": {"loc": (0, +0.038, -0.006), "rot": (-16, 0, 0)}}),
            (14, {"Bone_EjectCard": {"loc": (0, +0.068, -0.010), "rot": (-22, 0, 0)}}),
            (24, {"Bone_EjectCard": {"loc": (0, 0, 0), "scale": (0.1, 0.1, 0.1)}}),
        ]),
        _key_pose_map(arm_obj, "Printer_Queue_Pulse", 24, [
            (1,  {}),
            (12, {"Bone_PrinterBase": {"scale": (1.02, 1.02, 1.03)}, "Bone_StatusLight": {"scale": (1.30, 1.30, 1.30)}}),
            (24, {}),
        ]),
    ]
    arm_obj.animation_data.action = bpy.data.actions.get("Printer_Idle")
    bpy.context.scene.frame_set(20)

    wh.make_anchor("TaskCardSpawnPose", (0.0, +0.048, 0.026), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, +0.048, 0.026), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, 0.0, 0.045), arm_obj)
    wh.make_anchor("CardOutput_Tray",   (0.0, +0.052, 0.024), arm_obj)
    wh.make_anchor("Handoff_Printer",   (0.0, +0.075, 0.012), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.14, 0.22, 0.12), target=(0.0, 0.01, 0.03))


def _build_role_task_card(role_label, accent_mat):
    name = f"WallHabitat_TaskCard_{role_label}"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    parts = []
    # Compact legible Wall Habitat task card (0.042m x 0.056m x 0.003m upright token)
    wh.make_rounded_slab(f"{name}_CardBase", 0.042, 0.054, 0.003, 0.005,
                         (0.0, 0.0, 0.0), "Mat_PaperWarm", bevel=0.0005,
                         parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RoleHeader", (0.036, 0.010, 0.0036),
                        (0.0, +0.018, 0.0005), accent_mat, bevel_width=0.0004,
                        parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_BrassClip", (0.016, 0.005, 0.005),
                        (0.0, +0.026, 0.001), "Mat_Brass", bevel_width=0.0006,
                        parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_Line1", (0.028, 0.003, 0.0034), (0.0, +0.004, 0.0003),
                        "Mat_CharcoalVisor", bevel_width=0.0003, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_Line2", (0.022, 0.003, 0.0034), (-0.003, -0.006, 0.0003),
                        "Mat_CharcoalVisor", bevel_width=0.0003, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Mesh", parent=root)
    wh.make_anchor("DocumentAttach", (0.0, 0.0, 0.002), root)
    return wh.export_wall_static(name, cam_pos=(0.06, 0.09, 0.08), target=(0.0, 0.0, 0.0))


def generate_all():
    results = [
        _build_elevator_spine(tiers=3),
        _build_elevator_spine(tiers=2),
        _build_elevator_car(),
        _build_task_printer(),
        _build_role_task_card("Researcher", "Mat_Researcher_Lavender"),
        _build_role_task_card("Developer",  "Mat_Developer_Coral"),
        _build_role_task_card("Tester",     "Mat_Tester_Mint"),
        _build_role_task_card("Manager",    "Mat_Brass"),
    ]
    report_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_mechanical_report.json")
    with open(report_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"[generate_wall_habitat_mechanical] Generated {len(results)} mechanical assets -> {report_path}")


if __name__ == "__main__":
    try:
        generate_all()
    except Exception as ex:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_mechanical_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
