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
from generate_wall_habitat_core import get_layout_spec


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
    Miniature Enclosed Treehouse Elevator Spine aligned strictly to HabitatLayoutBuilder:
      - Mounted at ElevatorSpineSocket = (elev_x, 0.0, f0.floorY) in Blender
        (Unity: (elevX, f0.floorY, 0.0)).
      - Shaft occupies X in [-elev_w/2, +elev_w/2], Y in [back, z_c0].
      - Landing platforms per floor occupy Y in [z_c0, D], LEFT side (-X) 100% OPEN to corridor!
      - Front shaft doorway (width door_w) faces +Y Blender (+Z Unity corridor) at Y = z_c0.
      - Sliding doors part horizontally left/right along +-X (using bone local +Y axis along X!).
    """
    compact = (tiers == 2)
    name = f"WallHabitat_ElevatorSpine_{tiers}Tier"
    wh.reset_scene()

    s = get_layout_spec(compact)
    f0 = s["f0"]
    base_z = f0["floorY"]
    elev_w = s["elev_w"]                  # 0.130 Full / 0.110 Compact
    hx = elev_w * 0.5                     # 0.065 Full / 0.055 Compact
    back = s["back"]                      # 0.015
    z_c0 = s["z_c0"]                      # 0.170 Full / 0.150 Compact
    zc = s["zc"]                          # 0.210 Full / 0.190 Compact
    z_c1 = s["z_c1"]                      # 0.250 Full / 0.230 Compact
    D = s["D"]                            # 0.260 Full / 0.240 Compact
    slab = s["slab"]                      # 0.020
    door_w = s["door_w"]                  # 0.100 Full / 0.080 Compact
    cabin_z = s["cabin_z"]                # 0.0925 Full / 0.0825 Compact
    shaft_yc = (back + z_c0) * 0.5        # 0.0925 Full / 0.0825 Compact
    shaft_d = z_c0 - back                 # 0.155 Full / 0.135 Compact
    landing_d = D - z_c0                  # 0.090
    landing_yc = (z_c0 + D) * 0.5         # 0.215 Full / 0.195 Compact

    roof_bot_local = (s["roof_top"] - slab) - base_z   # 0.790 Full / 0.480 Compact
    roof_top_local = s["roof_top"] - base_z            # 0.810 Full / 0.500 Compact
    max_total_local = s["hh"] - base_z                 # 0.850 Full / 0.570 Compact

    floor_defs = [("Lower", 0.0)]
    if tiers == 3:
        floor_defs.append(("Mid", s["mid"]["floorY"] - base_z))
    floor_defs.append(("Manager", s["mgr"]["floorY"] - base_z))

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_root = _make_armature_bone(arm_data, "Bone_SpineRoot", (0, 0, 0), (0, 0, 0.08))
    _make_armature_bone(arm_data, "Bone_WinchDrum",
                        (0, shaft_yc, roof_bot_local - 0.018),
                        (0, shaft_yc + 0.035, roof_bot_local - 0.018), b_root)

    # Door bones oriented with tail along sliding direction (-X for Left/_F, +X for Right/_B)
    # so pose_bone.location = (0, +slide, 0) always slides outward along X regardless of bone roll!
    door_h = 0.180 if not compact else 0.165
    leaf_w = door_w * 0.5
    door_slide = leaf_w * 0.85
    for flbl, fz in floor_defs:
        _make_armature_bone(arm_data, f"Bone_Door{flbl}_F",
                            (-leaf_w * 0.5, z_c0, fz + door_h * 0.5),
                            (-leaf_w * 0.5 - 0.030, z_c0, fz + door_h * 0.5), b_root)
        _make_armature_bone(arm_data, f"Bone_Door{flbl}_B",
                            (+leaf_w * 0.5, z_c0, fz + door_h * 0.5),
                            (+leaf_w * 0.5 + 0.030, z_c0, fz + door_h * 0.5), b_root)
        _make_armature_bone(arm_data, f"Bone_Ind{flbl}",
                            (0.0, z_c0 + 0.004, fz + door_h + 0.018),
                            (0.0, z_c0 + 0.004, fz + door_h + 0.036), b_root)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []

    # 1. Bottom base plinth strictly below floor top (Z in [-slab, 0.000])
    wh.make_beveled_box(f"{name}_BasePlinth", (elev_w, D - back, slab),
                        (0.0, (back + D) * 0.5, -slab * 0.5), "Mat_TreeBark",
                        bevel_width=0.0015, bone_name="Bone_SpineRoot", parts_list=parts)

    # 2. Four sturdy Tree-Bark corner columns framing [back, z_c0] x [-hx, +hx]
    col_h = roof_bot_local
    for sx in (-1, 1):
        cx = sx * (hx - 0.004)
        for cy in (back + 0.004, z_c0 - 0.004):
            wh.make_cylinder(f"{name}_Col_{sx:+.0f}_{cy:.3f}", 0.0046, col_h,
                             (cx, cy, col_h * 0.5), "Mat_TreeBark", vertices=10,
                             bone_name="Bone_SpineRoot", parts_list=parts)

    # 3. Shaft Rear Wall & Side Shaft Walls (STRICTLY Y in [back, z_c0] — NEVER blocks corridor [z_c0, D]!)
    wh.make_beveled_box(f"{name}_ShaftBackGlass", (elev_w - 0.016, 0.004, col_h - 0.012),
                        (0.0, back + 0.003, col_h * 0.5), "Mat_ChamberGlass",
                        bevel_width=0.0004, bone_name="Bone_SpineRoot", parts_list=parts)
    for side_lbl, sx in [("L", -1), ("R", +1)]:
        wh.make_beveled_box(f"{name}_ShaftSidePanel_{side_lbl}", (0.004, shaft_d - 0.014, col_h - 0.012),
                            (sx * (hx - 0.003), shaft_yc, col_h * 0.5), "Mat_ChamberGlass",
                            bevel_width=0.0004, bone_name="Bone_SpineRoot", parts_list=parts)
    # Machined metal guide rails inside shaft left/right edges (clearance >= cabin_half[0] + 0.003)
    for side_lbl, sx in [("L", -1), ("R", +1)]:
        wh.make_beveled_box(f"{name}_GuideRail_{side_lbl}", (0.003, 0.006, col_h - 0.016),
                            (sx * (hx - 0.006), shaft_yc, col_h * 0.5), "Mat_BrushedMetal",
                            bevel_width=0.0005, bone_name="Bone_SpineRoot", parts_list=parts)

    # 4. Per-Floor Landing Slabs [z_c0, D] + Outer +X Landing Side + Front Railing [z_c1, D] + Shaft Front Jambs
    rail_y = (z_c1 + D) * 0.5
    jamb_w = (elev_w - door_w) * 0.5        # 0.015m wide each side
    for fi, (flbl, fz) in enumerate(floor_defs):
        # Landing deck slab (skip base slab duplicating plinth at fi==0 or overlap harmlessly; top at fz!)
        if fi > 0:
            wh.make_beveled_box(f"{name}_LandingDeck_{flbl}", (elev_w, landing_d, slab),
                                (0.0, landing_yc, fz - slab * 0.5), "Mat_WarmWood",
                                bevel_width=0.0012, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_rounded_slab(f"{name}_LandingPad_{flbl}", elev_w - 0.010, s["corridor_depth"] - 0.014, 0.0015, 0.005,
                             (0.0, zc, fz + 0.0008), "Mat_ForestMoss", bevel=0.0003,
                             bone_name="Bone_SpineRoot", parts_list=parts)

        # Front rustic treehouse landing rail & outer +X landing edge guard
        wh.make_beveled_box(f"{name}_LandingFrontBar_{flbl}", (elev_w - 0.008, 0.0045, 0.0045),
                            (0.0, rail_y, fz + 0.026), "Mat_WarmWood",
                            bevel_width=0.0009, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_beveled_box(f"{name}_LandingFrontMid_{flbl}", (elev_w - 0.010, 0.003, 0.003),
                            (0.0, rail_y, fz + 0.013), "Mat_LeatherSatchel",
                            bevel_width=0.0005, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_cylinder(f"{name}_LandingPostR_{flbl}", 0.0026, 0.028,
                         (+hx - 0.004, rail_y, fz + 0.014), "Mat_TreeBark", vertices=8,
                         bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_beveled_box(f"{name}_LandingEndBar_{flbl}", (0.0045, landing_d - 0.008, 0.0045),
                            (+hx - 0.004, landing_yc, fz + 0.026), "Mat_WarmWood",
                            bevel_width=0.0009, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_beveled_box(f"{name}_LandingEndMid_{flbl}", (0.003, landing_d - 0.010, 0.003),
                            (+hx - 0.004, landing_yc, fz + 0.013), "Mat_LeatherSatchel",
                            bevel_width=0.0005, bone_name="Bone_SpineRoot", parts_list=parts)

        # Front Shaft Timber Jambs & Bark Door Header along Y = z_c0
        for j_lbl, sx in [("L", -1), ("R", +1)]:
            jx = sx * (door_w * 0.5 + jamb_w * 0.5)
            wh.make_beveled_box(f"{name}_ShaftFrontJamb_{flbl}_{j_lbl}", (jamb_w, 0.006, door_h + 0.022),
                                (jx, z_c0, fz + (door_h + 0.022) * 0.5), "Mat_WarmWood",
                                bevel_width=0.0009, bone_name="Bone_SpineRoot", parts_list=parts)

        wh.make_beveled_box(f"{name}_PortalHeader_{flbl}", (elev_w - 0.006, 0.008, 0.026),
                            (0.0, z_c0, fz + door_h + 0.015), "Mat_TreeBark",
                            bevel_width=0.0014, bone_name="Bone_SpineRoot", parts_list=parts)

        # Sliding timber/lattice treehouse lift doors on +Y corridor front face
        for side_lbl, dx_s, b_suf in [("L", -leaf_w * 0.5, "F"), ("R", +leaf_w * 0.5, "B")]:
            wh.make_beveled_box(f"{name}_Door{flbl}_{b_suf}_Frame", (leaf_w - 0.001, 0.004, door_h - 0.004),
                                (dx_s, z_c0 - 0.002, fz + door_h * 0.5), "Mat_WarmWood",
                                bevel_width=0.0006, bone_name=f"Bone_Door{flbl}_{b_suf}", parts_list=parts)
            wh.make_beveled_box(f"{name}_Door{flbl}_{b_suf}_Pane", (leaf_w - 0.010, 0.005, door_h - 0.026),
                                (dx_s, z_c0 - 0.002, fz + door_h * 0.5 + 0.004), "Mat_ChamberGlass",
                                bevel_width=0.0003, bone_name=f"Bone_Door{flbl}_{b_suf}", parts_list=parts)

        # Call/Stop Indicator gem mounted centered on door header facing +Y corridor
        ind_mat = "Mat_StatusCyan" if flbl == "Lower" else ("Mat_Developer_Amber" if flbl == "Mid" else "Mat_StatusGreen")
        wh.make_beveled_box(f"{name}_IndBezel_{flbl}", (0.026, 0.006, 0.014),
                            (0.0, z_c0 + 0.003, fz + door_h + 0.016), "Mat_CharcoalVisor",
                            bevel_width=0.0008, bone_name="Bone_SpineRoot", parts_list=parts)
        wh.make_sphere(f"{name}_IndGem_{flbl}", (0.007, 0.004, 0.005),
                       (0.0, z_c0 + 0.006, fz + door_h + 0.016), ind_mat,
                       segments=10, rings=8, bone_name=f"Bone_Ind{flbl}", parts_list=parts)

    # 5. Treehouse timber pulley roof slab + mossy eave crown strictly within Roof elevation (<= max_total_local)
    wh.make_beveled_box(f"{name}_ShaftRoofSlab", (elev_w + 0.006, D - back + 0.004, slab),
                        (0.0, (back + D) * 0.5, roof_top_local - slab * 0.5), "Mat_TreeBark",
                        bevel_width=0.0018, bone_name="Bone_SpineRoot", parts_list=parts)
    wh.make_foliage_cluster(f"{name}_ShaftRoofMoss", (-hx * 0.45, landing_yc, roof_top_local + 0.010),
                            (0.028, 0.024, 0.014), rot_z=12, bone_name="Bone_SpineRoot", parts_list=parts)
    finial_h = min(0.018, max(0.008, (max_total_local - roof_top_local) * 0.6))
    wh.make_cylinder(f"{name}_CrownFinial", 0.014, finial_h,
                     (0.0, shaft_yc, roof_top_local + finial_h * 0.5), "Mat_Brass",
                     vertices=16, bone_name="Bone_SpineRoot", parts_list=parts)
    wh.make_cylinder(f"{name}_WinchWheel", 0.018, 0.022,
                     (0.0, shaft_yc, roof_bot_local - 0.018), "Mat_Brass",
                     vertices=16, rot_deg=(0, 90, 0), bone_name="Bone_WinchDrum", parts_list=parts)

    # Dual vertical tether cables running inside rear shaft
    for cx_off in (-0.016, +0.016):
        wh.make_cylinder(f"{name}_Cable_{cx_off:+.2f}", 0.0014, roof_bot_local - 0.020,
                         (cx_off, back + 0.012, (roof_bot_local - 0.020) * 0.5), "Mat_BrushedMetal",
                         vertices=8, bone_name="Bone_SpineRoot", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    # Keyframe all Spine animation clips (+Y bone axis slides outward horizontally in X!)
    clips = [
        _key_pose_map(arm_obj, "ElevatorDoors_Idle_Closed", 24, [(1, {}), (24, {})]),
    ]
    for flbl, _ in floor_defs:
        open_d = {
            f"Bone_Door{flbl}_F": {"loc": (0.0, +door_slide, 0.0)},
            f"Bone_Door{flbl}_B": {"loc": (0.0, +door_slide, 0.0)},
            f"Bone_Ind{flbl}":    {"scale": (1.25, 1.25, 1.25)},
        }
        clips.append(_key_pose_map(arm_obj, f"ElevatorDoors_Open_{flbl}", 18, [(1, {}), (18, open_d)]))
        clips.append(_key_pose_map(arm_obj, f"ElevatorDoors_Close_{flbl}", 18, [(1, open_d), (18, {})]))

    clips.append(_key_pose_map(arm_obj, "ElevatorWinch_Spin", 24, [
        (1,  {"Bone_WinchDrum": {"rot": (0, 0, 0)}}),
        (12, {"Bone_WinchDrum": {"rot": (0, 180, 0)}}),
        (24, {"Bone_WinchDrum": {"rot": (0, 360, 0)}}),
    ]))

    arm_obj.animation_data.action = bpy.data.actions.get("ElevatorDoors_Idle_Closed")
    bpy.context.scene.frame_set(1)

    # Exact Contract Sockets (local relative to ElevatorSpineSocket = (elev_x, 0.0, f0.floorY))
    wh.make_anchor("ElevatorCarSocket", (0.0, cabin_z, 0.0), arm_obj)
    for flbl, fz in floor_defs:
        wh.make_anchor(f"ElevatorStop_{flbl}",      (0.0, zc, fz), arm_obj)
        wh.make_anchor(f"ElevatorQueuePose_{flbl}", (-hx - 0.025, zc, fz), arm_obj)
        wh.make_anchor(f"EntryPose_{flbl}",         (0.0, z_c0, fz), arm_obj)
        wh.make_anchor(f"ElevatorEntry_{flbl}",     (0.0, z_c0, fz), arm_obj)
        wh.make_anchor(f"ElevatorExit_{flbl}",      (0.0, zc, fz), arm_obj)

    wh.make_anchor("AgentHomePose",     (0.0, zc, 0.0), arm_obj)
    wh.make_anchor("WorkPose",          (0.0, cabin_z, 0.0), arm_obj)
    wh.make_anchor("SeatPose",          (0.0, cabin_z, 0.0), arm_obj)
    wh.make_anchor("EntryPose",         (0.0, z_c0, 0.0), arm_obj)
    wh.make_anchor("HandoffPose",       (-hx, zc, 0.0), arm_obj)
    wh.make_anchor("ElevatorQueuePose", (-hx - 0.025, zc, 0.0), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, cabin_z, 0.068), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, cabin_z, 0.110), arm_obj)
    wh.make_anchor("DoorClearance",     (0.0, z_c0, 0.092), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.35, 1.05, roof_top_local * 0.55), target=(0.0, shaft_yc, roof_top_local * 0.45))


def _build_elevator_car(name="WallHabitat_ElevatorCar", compact=False):
    """
    Miniature Panorama Treehouse Elevator Car fitted cleanly inside HabitatLayout.cabinHalf:
      Full:    cabinHalf=(0.0550, 0.0725) -> outer 0.098 x 0.128 x 0.180 m
      Compact: cabinHalf=(0.0450, 0.0625) -> outer 0.080 x 0.110 x 0.168 m
    Open / sliding brass gates face +Y Blender (+Z Unity corridor) to match shaft doorway!
    """
    wh.reset_scene()

    if not compact:
        w, d, h = 0.098, 0.128, 0.180
        gate_w = 0.042
        gate_slide = 0.036
    else:
        w, d, h = 0.080, 0.110, 0.168
        gate_w = 0.034
        gate_slide = 0.028

    hx = w * 0.5 - 0.004
    hy = d * 0.5 - 0.004
    door_y = +d * 0.5 - 0.003

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_cab = _make_armature_bone(arm_data, "Bone_CabBody", (0, 0, 0), (0, 0, 0.09))
    _make_armature_bone(arm_data, "Bone_CabGate_F",
                        (-gate_w * 0.5, door_y, 0.06),
                        (-gate_w * 0.5 - 0.025, door_y, 0.06), b_cab)
    _make_armature_bone(arm_data, "Bone_CabGate_B",
                        (+gate_w * 0.5, door_y, 0.06),
                        (+gate_w * 0.5 + 0.025, door_y, 0.06), b_cab)
    _make_armature_bone(arm_data, "Bone_CabLamp", (0, 0, h - 0.014), (0, 0, h - 0.026), b_cab)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # Cab floor base (slab top flush at +0.002)
    wh.make_rounded_slab(f"{name}_Floor", w, d, 0.006, 0.010, (0.0, 0.0, -0.001),
                         "Mat_WarmWood", bevel=0.0008, bone_name="Bone_CabBody", parts_list=parts)
    wh.make_rounded_slab(f"{name}_FloorPad", w - 0.014, d - 0.014, 0.0015, 0.008, (0.0, 0.0, +0.0025),
                         "Mat_ForestMoss", bevel=0.0002, bone_name="Bone_CabBody", parts_list=parts)

    # Four timber-bark corner posts
    for sx in (-1, 1):
        for sy in (-1, 1):
            wh.make_cylinder(f"{name}_Post_{sx:+.0f}_{sy:+.0f}", 0.0036, h - 0.012,
                             (sx * hx, sy * hy, h * 0.5), "Mat_TreeBark", vertices=10,
                             bone_name="Bone_CabBody", parts_list=parts)

    # Enclosed rear (-Y wall side) & side (±X) warm wainscot + panorama glass
    wh.make_beveled_box(f"{name}_BackWainscot", (w - 0.012, 0.004, 0.044),
                        (0.0, -hy, 0.024), "Mat_WarmWood", bevel_width=0.0008,
                        bone_name="Bone_CabBody", parts_list=parts)
    wh.make_beveled_box(f"{name}_BackWindow", (w - 0.016, 0.0025, h - 0.060),
                        (0.0, -hy, 0.046 + (h - 0.060) * 0.5), "Mat_ChamberGlass",
                        bevel_width=0.0003, bone_name="Bone_CabBody", parts_list=parts)
    for side_lbl, sx in [("L", -1), ("R", +1)]:
        wh.make_beveled_box(f"{name}_SideRail_{side_lbl}", (0.004, d - 0.014, 0.036),
                            (sx * hx, 0.0, 0.020), "Mat_WarmWood", bevel_width=0.0006,
                            bone_name="Bone_CabBody", parts_list=parts)

    # Front (+Y) low threshold sill + openable timber pulley gates
    wh.make_beveled_box(f"{name}_FrontCurb", (w - 0.012, 0.004, 0.006),
                        (0.0, +hy, 0.005), "Mat_TreeBark", bevel_width=0.0006,
                        bone_name="Bone_CabBody", parts_list=parts)
    for glbl, gx, b_suf in [("L", -gate_w * 0.5, "F"), ("R", +gate_w * 0.5, "B")]:
        wh.make_beveled_box(f"{name}_GateFrame_{glbl}", (gate_w - 0.002, 0.003, 0.105),
                            (gx, door_y, 0.062), "Mat_WarmWood", bevel_width=0.0005,
                            bone_name=f"Bone_CabGate_{b_suf}", parts_list=parts)
        wh.make_beveled_box(f"{name}_GateGlass_{glbl}", (gate_w - 0.010, 0.0036, 0.088),
                            (gx, door_y, 0.064), "Mat_ChamberGlass", bevel_width=0.0003,
                            bone_name=f"Bone_CabGate_{b_suf}", parts_list=parts)

    # Treehouse canopy roof & warm acorn ceiling lamp
    wh.make_rounded_slab(f"{name}_RoofDeck", w + 0.002, d + 0.002, 0.010, 0.012,
                         (0.0, 0.0, h - 0.006), "Mat_TreeBark", bevel=0.0014,
                         bone_name="Bone_CabBody", parts_list=parts)
    wh.make_cylinder(f"{name}_SuspYoke", 0.012, 0.010, (0.0, 0.0, h + 0.004),
                     "Mat_Brass", vertices=14, bone_name="Bone_CabBody", parts_list=parts)
    wh.make_sphere(f"{name}_CabCeilingLamp", (0.010, 0.010, 0.005), (0.0, 0.0, h - 0.013),
                   "Mat_LampGlow", segments=10, rings=8, bone_name="Bone_CabLamp", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    open_gate = {
        "Bone_CabGate_F": {"loc": (0.0, +gate_slide, 0.0)},
        "Bone_CabGate_B": {"loc": (0.0, +gate_slide, 0.0)},
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

    wh.make_anchor("ElevatorCabinPose", (0.0, 0.0, 0.003), arm_obj)
    wh.make_anchor("AgentHomePose",     (0.0, 0.0, 0.003), arm_obj)
    wh.make_anchor("WorkPose",          (0.0, 0.0, 0.003), arm_obj)
    wh.make_anchor("SeatPose",          (0.0, 0.0, 0.003), arm_obj)
    wh.make_anchor("EntryPose",         (0.0, +hy, 0.003), arm_obj)
    wh.make_anchor("HandoffPose",       (0.0, +hy * 0.5, 0.003), arm_obj)
    wh.make_anchor("ElevatorQueuePose", (0.0, +hy + 0.025, 0.003), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, +0.022, 0.062), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, 0.0, 0.105), arm_obj)
    wh.make_anchor("DoorClearance",     (0.0, +hy, 0.088), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.22, 0.36, 0.14), target=(0.0, 0.0, 0.09))


def _build_task_printer():
    """
    Miniature Manager Delegation Task Printer:
    Mounted flush at TaskPrinterSocket atop ManagerLookout PrinterStand (compact-safe 0.064 x 0.044 m base).
    """
    name = "WallHabitat_TaskPrinter"
    wh.reset_scene()

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_base = _make_armature_bone(arm_data, "Bone_PrinterBase", (0, 0, 0), (0, 0, 0.032))
    _make_armature_bone(arm_data, "Bone_PrintHead",   (0, +0.006, 0.046), (0, +0.006, 0.058), b_base)
    _make_armature_bone(arm_data, "Bone_FeedRoller",  (0, +0.018, 0.031), (+0.024, +0.018, 0.031), b_base)
    _make_armature_bone(arm_data, "Bone_StatusLight", (-0.022, +0.020, 0.044), (-0.022, +0.020, 0.054), b_base)
    _make_armature_bone(arm_data, "Bone_EjectCard",   (0, +0.014, 0.025), (0, +0.036, 0.020), b_base)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    wh.make_rounded_slab(f"{name}_Body", 0.064, 0.044, 0.038, 0.010, (0.0, 0.0, 0.019),
                         "Mat_WarmBeige", bevel=0.0020, bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_OutputThroat", (0.040, 0.005, 0.009), (0.0, +0.0215, 0.025),
                        "Mat_CharcoalVisor", bevel_width=0.0008, bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_BrassLip", (0.044, 0.008, 0.003), (0.0, +0.025, 0.018),
                        "Mat_Brass", bevel_width=0.0006, rot_deg=(12, 0, 0),
                        bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_rounded_slab(f"{name}_TealHood", 0.056, 0.034, 0.012, 0.009, (0.0, -0.003, 0.042),
                         "Mat_ForestMoss", bevel=0.0016, bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_PaperMagazine", (0.036, 0.008, 0.020), (0.0, -0.012, 0.052),
                        "Mat_PaperWarm", bevel_width=0.0006, rot_deg=(-12, 0, 0),
                        bone_name="Bone_PrinterBase", parts_list=parts)
    wh.make_beveled_box(f"{name}_HeadCarriage", (0.015, 0.013, 0.010), (0.0, +0.008, 0.049),
                        "Mat_Brass", bevel_width=0.0012, bone_name="Bone_PrintHead", parts_list=parts)
    wh.make_cylinder(f"{name}_BrassRoller", 0.005, 0.044, (0.0, +0.018, 0.031),
                     "Mat_Brass", vertices=12, rot_deg=(0, 90, 0),
                     bone_name="Bone_FeedRoller", parts_list=parts)
    wh.make_sphere(f"{name}_StatusOrb", (0.0045, 0.0045, 0.0045), (-0.022, +0.020, 0.049),
                   "Mat_StatusCyan", segments=10, rings=8, bone_name="Bone_StatusLight", parts_list=parts)
    wh.make_beveled_box(f"{name}_PrintedCard", (0.032, 0.022, 0.002), (0.0, +0.010, 0.025),
                        "Mat_PaperWarm", bevel_width=0.0003, bone_name="Bone_EjectCard", parts_list=parts)
    wh.make_beveled_box(f"{name}_PrintedCardStripe", (0.028, 0.005, 0.0024), (0.0, +0.016, 0.0255),
                        "Mat_Developer_Coral", bevel_width=0.0003, bone_name="Bone_EjectCard", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    clips = [
        _key_pose_map(arm_obj, "Printer_Idle", 36, [
            (1,  {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}}),
            (18, {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}, "Bone_StatusLight": {"scale": (0.85, 0.85, 0.85)}}),
            (36, {"Bone_EjectCard": {"scale": (0.01, 0.01, 0.01)}}),
        ]),
        _key_pose_map(arm_obj, "Printer_Print_TaskCard", 36, [
            (1,  {"Bone_PrintHead": {"loc": (-0.016, 0, 0)}, "Bone_EjectCard": {"scale": (0.1, 0.1, 0.1)}}),
            (8,  {"Bone_PrintHead": {"loc": (+0.016, 0, 0)}, "Bone_FeedRoller": {"rot": (120, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.008, 0), "scale": (1, 1, 1)}, "Bone_StatusLight": {"scale": (1.25, 1.25, 1.25)}}),
            (16, {"Bone_PrintHead": {"loc": (-0.014, 0, 0)}, "Bone_FeedRoller": {"rot": (240, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.018, -0.002), "scale": (1, 1, 1)}}),
            (26, {"Bone_PrintHead": {"loc": (+0.012, 0, 0)}, "Bone_FeedRoller": {"rot": (360, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.028, -0.004), "rot": (-14, 0, 0), "scale": (1, 1, 1)}}),
            (36, {"Bone_PrintHead": {"loc": (0, 0, 0)}, "Bone_EjectCard": {"loc": (0, +0.030, -0.005), "rot": (-16, 0, 0), "scale": (1, 1, 1)}}),
        ]),
        _key_pose_map(arm_obj, "Printer_Dispatch_Card", 24, [
            (1,  {"Bone_EjectCard": {"loc": (0, +0.030, -0.005), "rot": (-16, 0, 0)}}),
            (14, {"Bone_EjectCard": {"loc": (0, +0.056, -0.008), "rot": (-22, 0, 0)}}),
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

    wh.make_anchor("TaskCardSpawnPose", (0.0, +0.038, 0.022), arm_obj)
    wh.make_anchor("DocumentAttach",    (0.0, +0.038, 0.022), arm_obj)
    wh.make_anchor("HeadLookTarget",    (0.0, 0.0, 0.042), arm_obj)
    wh.make_anchor("CardOutput_Tray",   (0.0, +0.042, 0.020), arm_obj)
    wh.make_anchor("Handoff_Printer",   (0.0, +0.062, 0.010), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.14, 0.22, 0.12), target=(0.0, 0.01, 0.03))


def _build_role_task_card(role_label, accent_mat):
    name = f"WallHabitat_TaskCard_{role_label}"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    parts = []
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
        _build_elevator_car("WallHabitat_ElevatorCar", compact=False),
        _build_elevator_car("WallHabitat_ElevatorCar_Compact", compact=True),
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
    except Exception:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_mechanical_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
