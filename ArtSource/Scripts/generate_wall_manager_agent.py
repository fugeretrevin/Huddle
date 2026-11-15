import bpy
import bmesh
import math
import mathutils
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import wall_habitat_common as wh
import generate_characters as gc

# Point generate_characters previews/exports safely to worktree WallHabitat dirs
gc.PROJECT_ROOT = wh.WORKTREE_ROOT
gc.BLEND_DIR = wh.BLEND_WALL_DIR
gc.FBX_DIR = wh.FBX_WALL_DIR
gc.PREVIEW_DIR = wh.PREVIEW_WALL_DIR
gc.PALETTE.setdefault("Mat_BlueprintBlue", {"color": (0.175, 0.385, 0.610, 1.0), "roughness": 0.45, "metallic": 0.0, "emission": None})


def build_manager_accessories():
    """
    Distinctive "Workshop Overseer / Coordinator" silhouette for Agent_Manager
    in the exact Pipkin Sprite aesthetic and scale:
      1. Tailored Architectural Slate-Blue Waistcoat + Cream Lapels + Brass Buttons
      2. Brass Wrist Cuff Signets + Coordinator Waistbelt & Cyan Compass Dial
      3. Peaked Conductor / Architect Visor Cap + Brass Brim Band + Drafting Beacon Crest
      4. Single Brass Architect Monocle over Right Eye (-X)
      5. Leather Architectural Portfolio Case + Brass Buckle + Blueprint Tube on Back (+Y)
    """
    parts = []

    # 1. Tailored Slate-Blue Waistcoat Torso Shell + Lapel Trim
    vest_profile = [
        (0.000, 0.036),
        (0.043, 0.038),
        (0.046, 0.056),
        (0.044, 0.078),
        (0.038, 0.098),
        (0.029, 0.114),
        (0.000, 0.116),
    ]
    vest = gc.create_lathe_mesh("Manager_Waistcoat", vest_profile, segments=18, scale_xy=(1.04, 0.94))
    gc.assign_material(vest, "Mat_Tester_SlateBlue")
    gc.assign_z_gradient_weights(vest, [(0.050, "Hips"), (0.078, "Spine"), (0.106, "Chest")])
    parts.append(vest)

    # Cozy brass waist sash belt & dual front brass waistcoat buttons
    bpy.ops.mesh.primitive_torus_add(
        major_segments=18, minor_segments=8,
        major_radius=0.044, minor_radius=0.0038,
        location=(0.0, 0.0, 0.058),
    )
    belt = bpy.context.active_object
    belt.name = "Manager_BrassSash"
    belt.scale = (1.04, 0.94, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(belt)
    gc.assign_material(belt, "Mat_Brass")
    gc.assign_single_bone_weight(belt, "Spine")
    parts.append(belt)

    for bidx, bz in enumerate((0.088, 0.072)):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=0.0038,
                                             location=(0.0, -0.042, bz))
        btn = bpy.context.active_object
        btn.name = f"Manager_Button_{bidx+1}"
        btn.scale = (1.0, 0.55, 1.0)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        gc.shade_smooth(btn)
        gc.assign_material(btn, "Mat_Brass")
        gc.assign_single_bone_weight(btn, "Chest")
        parts.append(btn)

    # Crisp Cream Lapel V-Collar around neck
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * 0.015, -0.025, 0.111))
        lap = bpy.context.active_object
        lap.name = f"Manager_Lapel_{side}"
        lap.scale = (0.013, 0.018, 0.006)
        lap.rotation_euler = (math.radians(30), math.radians(sx * -20), math.radians(sx * 18))
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bev = lap.modifiers.new("Bevel", 'BEVEL')
        bev.width = 0.002
        bev.segments = 2
        bpy.ops.object.modifier_apply(modifier="Bevel")
        gc.shade_smooth(lap)
        gc.assign_material(lap, "Mat_CreamCeramic")
        gc.assign_single_bone_weight(lap, "Chest")
        parts.append(lap)

    # Coordinator Pocket Compass Badge at hip belt
    bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.009, depth=0.005,
                                        location=(0.022, -0.041, 0.056),
                                        rotation=(math.radians(82), 0, math.radians(-18)))
    comp_rim = bpy.context.active_object
    comp_rim.name = "Manager_CompassRim"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(comp_rim)
    gc.assign_material(comp_rim, "Mat_Brass")
    gc.assign_single_bone_weight(comp_rim, "Spine")
    parts.append(comp_rim)

    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.0065, depth=0.0055,
                                        location=(0.022, -0.0418, 0.056),
                                        rotation=(math.radians(82), 0, math.radians(-18)))
    comp_dial = bpy.context.active_object
    comp_dial.name = "Manager_CompassDial"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(comp_dial)
    gc.assign_material(comp_dial, "Mat_Tester_LensGlow")
    gc.assign_single_bone_weight(comp_dial, "Spine")
    parts.append(comp_dial)

    # 2. Brass Architect Monocle on Right Eye (-X) + delicate ear stem
    bpy.ops.mesh.primitive_torus_add(
        major_segments=16, minor_segments=8,
        major_radius=0.0125, minor_radius=0.0018,
        location=(-0.0165, -0.042, 0.152),
        rotation=(math.radians(90), 0, 0),
    )
    monocle = bpy.context.active_object
    monocle.name = "Manager_BrassMonocle"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(monocle)
    gc.assign_material(monocle, "Mat_Brass")
    gc.assign_single_bone_weight(monocle, "Head")
    parts.append(monocle)

    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.0014, depth=0.036,
                                        location=(-0.035, -0.024, 0.152),
                                        rotation=(math.radians(90), 0, math.radians(32)))
    stem = bpy.context.active_object
    stem.name = "Manager_MonocleStem"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(stem)
    gc.assign_material(stem, "Mat_Brass")
    gc.assign_single_bone_weight(stem, "Head")
    parts.append(stem)

    # 3. Sculpted Architect Cap Crown + Peaked Brim + Brass Band + Glowing Crest Finial
    cap_profile = [
        (0.041, 0.178),
        (0.044, 0.184),
        (0.042, 0.194),
        (0.030, 0.204),
        (0.014, 0.209),
        (0.000, 0.210),
    ]
    cap = gc.create_lathe_mesh("Manager_ArchitectCap", cap_profile, segments=20, scale_xy=(1.04, 0.96))
    gc.assign_material(cap, "Mat_Tester_SlateBlue")
    gc.assign_single_bone_weight(cap, "Head")
    parts.append(cap)

    # Cap front peak visor
    bpy.ops.mesh.primitive_cylinder_add(vertices=18, radius=0.040, depth=0.005,
                                        location=(0.0, -0.022, 0.178),
                                        rotation=(math.radians(10), 0, 0))
    brim = bpy.context.active_object
    brim.name = "Manager_CapBrim"
    brim.scale = (0.96, 0.72, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(brim)
    gc.assign_material(brim, "Mat_LeatherSatchel")
    gc.assign_single_bone_weight(brim, "Head")
    parts.append(brim)

    # Brass hatband + kinetic Architect Drafting Antenna Finial (Accessory_Root -> Accessory_Tip)
    bpy.ops.mesh.primitive_torus_add(
        major_segments=20, minor_segments=8,
        major_radius=0.043, minor_radius=0.0028,
        location=(0.0, -0.001, 0.181),
    )
    hband = bpy.context.active_object
    hband.name = "Manager_CapBand"
    hband.scale = (1.04, 0.96, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(hband)
    gc.assign_material(hband, "Mat_Brass")
    gc.assign_single_bone_weight(hband, "Head")
    parts.append(hband)

    crest_profile = [
        (0.006, 0.207),
        (0.004, 0.217),
        (0.0025, 0.228),
        (0.000, 0.231),
    ]
    crest = gc.create_lathe_mesh("Manager_CrestSpire", crest_profile, segments=12)
    gc.assign_material(crest, "Mat_Brass")
    gc.assign_z_gradient_weights(crest, [(0.207, "Head"), (0.215, "Accessory_Root"), (0.228, "Accessory_Tip")])
    parts.append(crest)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.0062,
                                         location=(0.0, 0.0, 0.232))
    beacon_orb = bpy.context.active_object
    beacon_orb.name = "Manager_CrestBeaconOrb"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(beacon_orb)
    gc.assign_material(beacon_orb, "Mat_Developer_Amber")
    gc.assign_single_bone_weight(beacon_orb, "Accessory_Tip")
    parts.append(beacon_orb)

    # 4. Leather Architectural Portfolio Case + Brass Latch on Back (+Y)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.040, 0.084))
    folio = bpy.context.active_object
    folio.name = "Manager_PortfolioPack"
    folio.scale = (0.048, 0.018, 0.046)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bev = folio.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.004
    bev.segments = 2
    bpy.ops.object.modifier_apply(modifier="Bevel")
    gc.shade_smooth(folio)
    gc.assign_material(folio, "Mat_LeatherSatchel")
    gc.assign_single_bone_weight(folio, "Chest")
    parts.append(folio)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.050, 0.088))
    buckle = bpy.context.active_object
    buckle.name = "Manager_PortfolioClasp"
    buckle.scale = (0.016, 0.004, 0.010)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(buckle)
    gc.assign_material(buckle, "Mat_Brass")
    gc.assign_single_bone_weight(buckle, "Chest")
    parts.append(buckle)

    # Rolled blueprint scroll tucked diagonally above portfolio
    bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.0075, depth=0.056,
                                        location=(0.0, 0.036, 0.112),
                                        rotation=(0, math.radians(78), 0))
    bscroll = bpy.context.active_object
    bscroll.name = "Manager_BlueprintRoll"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    gc.shade_smooth(bscroll)
    gc.assign_material(bscroll, "Mat_BlueprintBlue")
    gc.assign_single_bone_weight(bscroll, "Chest")
    parts.append(bscroll)

    return parts


def append_wall_habitat_and_manager_clips(arm_obj):
    """Appends 16 Phase 3 Wall Habitat, Elevator, DeskVisit, Portal, and Manager clips on the shared 26-bone rig."""
    phase3_specs = [
        # --- Shared Elevator Choreography (compatible with ALL agents!) ---
        ("Elevator_Queue_Idle", 48, [
            (1,  {}),
            (12, {"Chest": {"rot": (2, 4, 0)}, "Head": {"rot": (4, -10, 4)}, "UpperArm_R": {"rot": (12, 0, -6)}}),
            (24, {"Hips": {"loc": (0, -0.0015, 0)}, "Head": {"rot": (6, 8, -4)}, "Accessory_Root": {"rot": (-4, 0, 0)}}),
            (36, {"Head": {"rot": (-2, 4, 2)}}),
            (48, {}),
        ]),
        ("Elevator_Step_Board", 24, [
            (1,  {}),
            (7,  {"Hips": {"loc": (0, 0.004, 0)}, "Chest": {"rot": (6, 0, 0)}, "Thigh_L": {"rot": (-28, 0, 0)}, "Shin_L": {"rot": (22, 0, 0)}, "Thigh_R": {"rot": (22, 0, 0)}, "UpperArm_R": {"rot": (-14, 0, -10)}}),
            (15, {"Hips": {"loc": (0, 0.002, 0)}, "Head": {"rot": (-8, 0, 0)}, "Thigh_L": {"rot": (20, 0, 0)}, "Thigh_R": {"rot": (-26, 0, 0)}, "Shin_R": {"rot": (20, 0, 0)}}),
            (24, {}),
        ]),
        ("Elevator_Ride_Idle", 48, [
            (1,  {"UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (14, {"Hips": {"loc": (0, 0.002, 0)}, "Head": {"rot": (6, 12, 4)}, "UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (30, {"Hips": {"loc": (0, -0.002, 0)}, "Head": {"rot": (4, -12, -4)}, "UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (48, {"UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
        ]),
        ("Elevator_Step_Exit", 24, [
            (1,  {"Head": {"rot": (4, 0, 0)}}),
            (8,  {"Hips": {"loc": (0, 0.005, 0)}, "Chest": {"rot": (8, 0, 0)}, "Thigh_R": {"rot": (-30, 0, 0)}, "Thigh_L": {"rot": (20, 0, 0)}, "UpperArm_L": {"rot": (16, 0, 12)}}),
            (16, {"Hips": {"loc": (0, 0.002, 0)}, "Thigh_L": {"rot": (-24, 0, 0)}, "Thigh_R": {"rot": (16, 0, 0)}}),
            (24, {}),
        ]),
        # --- Desk Visit (/btw) & Distance Selection ---
        ("Select_Highlighted_Wave", 32, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0, 0.008, 0)}, "Head": {"rot": (8, -6, -6)}, "UpperArm_R": {"rot": (118, 0, -28)}, "Forearm_R": {"rot": (25, 0, 0)}}),
            (16, {"Head": {"rot": (10, 4, 4)}, "UpperArm_R": {"rot": (115, 0, -10)}, "Forearm_R": {"rot": (12, 0, 22)}}),
            (24, {"UpperArm_R": {"rot": (118, 0, -28)}, "Forearm_R": {"rot": (25, 0, 0)}}),
            (32, {}),
        ]),
        ("DeskVisit_Perch_Idle", 48, [
            (1,  {"Head": {"rot": (4, 0, 4)}}),
            (16, {"Hips": {"loc": (0, -0.002, 0)}, "Chest": {"rot": (4, 4, 0)}, "Head": {"rot": (6, 8, 6)}, "UpperArm_R": {"rot": (16, 0, -8)}, "Brow_L": {"loc": (0, 0.001, 0)}}),
            (32, {"Hips": {"loc": (0, 0.001, 0)}, "Chest": {"rot": (2, -4, 0)}, "Head": {"rot": (4, -8, -4)}, "UpperArm_L": {"rot": (14, 0, 8)}}),
            (48, {"Head": {"rot": (4, 0, 4)}}),
        ]),
        ("DeskVisit_Listening", 40, [
            (1,  {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (6, 0, 8)}}),
            (12, {"Chest": {"rot": (10, 0, 0)}, "Head": {"rot": (10, 6, 10)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (-8, 0, 0)}}),
            (26, {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (-2, 4, 10)}}),
            (40, {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (6, 0, 8)}}),
        ]),
        ("DeskVisit_Explain", 48, [
            (1,  {}),
            (10, {"Chest": {"rot": (6, 8, 0)}, "Head": {"rot": (6, -6, 4)}, "UpperArm_R": {"rot": (52, 0, -24)}, "Forearm_R": {"rot": (48, 0, 0)}}),
            (22, {"Chest": {"rot": (8, -6, 0)}, "Head": {"rot": (8, 6, -4)}, "UpperArm_L": {"rot": (48, 0, 22)}, "Forearm_L": {"rot": (45, 0, 0)}, "UpperArm_R": {"rot": (32, 0, -14)}}),
            (35, {"Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (-4, 0, 0)}, "UpperArm_L": {"rot": (38, 0, 16)}, "UpperArm_R": {"rot": (42, 0, -16)}}),
            (48, {}),
        ]),
        # --- Enlarged Portal Emergence / Conversation / Return ---
        ("Portal_Emerge_Step", 32, [
            (1,  {"Hips": {"loc": (0, -0.008, 0)}, "Chest": {"rot": (10, 0, 0)}, "UpperArm_L": {"rot": (18, 0, 16)}, "UpperArm_R": {"rot": (18, 0, -16)}}),
            (12, {"Hips": {"loc": (0, 0.010, 0)}, "Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (8, 0, 0)}, "Thigh_L": {"rot": (-26, 0, 0)}, "Thigh_R": {"rot": (18, 0, 0)}, "UpperArm_L": {"rot": (42, 0, 22)}, "UpperArm_R": {"rot": (65, 0, -24)}}),
            (22, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (-2, 0, 0)}, "Head": {"rot": (10, 6, 0)}, "Thigh_L": {"rot": (10, 0, 0)}, "Thigh_R": {"rot": (-14, 0, 0)}, "UpperArm_R": {"rot": (92, 0, -22)}}),
            (32, {}),
        ]),
        ("Portal_Converse_Loop", 48, [
            (1,  {}),
            (12, {"Chest": {"rot": (4, 5, 0)}, "Head": {"rot": (6, -4, 3)}, "UpperArm_R": {"rot": (36, 0, -14)}, "Forearm_R": {"rot": (42, 0, 0)}}),
            (24, {"Hips": {"loc": (0, -0.002, 0)}, "Head": {"rot": (4, 6, -3)}, "UpperArm_L": {"rot": (24, 0, 12)}, "Forearm_L": {"rot": (32, 0, 0)}}),
            (36, {"Chest": {"rot": (2, -4, 0)}, "Head": {"rot": (8, 0, 4)}, "Eye_L": {"scale": (1, 0.15, 1)}, "Eye_R": {"scale": (1, 0.15, 1)}}),
            (48, {}),
        ]),
        ("Portal_Return_Step", 28, [
            (1,  {}),
            (8,  {"Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (-6, 0, 0)}, "UpperArm_R": {"rot": (78, 0, -18)}, "Forearm_R": {"rot": (35, 0, 0)}}),
            (18, {"Hips": {"loc": (0, 0.004, 0)}, "Chest": {"rot": (0, 165, 0)}, "Thigh_L": {"rot": (-24, 0, 0)}, "Thigh_R": {"rot": (22, 0, 0)}}),
            (28, {"Chest": {"rot": (0, 180, 0)}}),
        ]),
        # --- Manager Role Signature Clips ---
        ("Manager_Lookout_Idle", 48, [
            (1,  {}),
            (14, {"Chest": {"rot": (4, -12, 0)}, "Head": {"rot": (-6, -16, 4)}, "UpperArm_L": {"rot": (28, 0, 14)}, "Forearm_L": {"rot": (46, 0, 0)}}),
            (28, {"Chest": {"rot": (2, 12, 0)}, "Head": {"rot": (-4, 16, -4)}, "UpperArm_R": {"rot": (22, 0, -10)}}),
            (48, {}),
        ]),
        ("Manager_Review_Plan", 48, [
            (1,  {"UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
            (16, {"Chest": {"rot": (8, 4, 0)}, "Head": {"rot": (-10, 4, 6)}, "UpperArm_L": {"rot": (45, 0, 16)}, "Forearm_L": {"rot": (56, 0, 12)}, "UpperArm_R": {"rot": (58, 0, -12)}, "Forearm_R": {"rot": (78, 0, 18)}}),
            (32, {"Chest": {"rot": (6, -4, 0)}, "Head": {"rot": (-6, -4, -4)}, "UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
            (48, {"UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
        ]),
        ("Manager_Operate_Printer", 36, [
            (1,  {}),
            (8,  {"Chest": {"rot": (10, -28, 0)}, "Head": {"rot": (-8, -22, 0)}, "UpperArm_R": {"rot": (72, 0, -18)}, "Forearm_R": {"rot": (35, 0, 0)}}),
            (16, {"Chest": {"rot": (12, -28, 0)}, "Head": {"rot": (-10, -20, 0)}, "UpperArm_R": {"rot": (58, 0, -16)}, "Forearm_R": {"rot": (28, 0, 0)}}),
            (26, {"Chest": {"rot": (4, -10, 0)}, "Head": {"rot": (4, -8, 0)}, "UpperArm_R": {"rot": (62, 0, 8)}, "UpperArm_L": {"rot": (56, 0, -8)}}),
            (36, {}),
        ]),
        ("Manager_Delegate_Point", 36, [
            (1,  {}),
            (10, {"Chest": {"rot": (6, -18, 0)}, "Head": {"rot": (-6, -18, 4)}, "UpperArm_R": {"rot": (88, 0, -24)}, "Forearm_R": {"rot": (12, 0, 0)}}),
            (22, {"Chest": {"rot": (4, 18, 0)}, "Head": {"rot": (-6, 18, -4)}, "UpperArm_L": {"rot": (88, 0, 24)}, "Forearm_L": {"rot": (12, 0, 0)}}),
            (36, {}),
        ]),
        ("Manager_Receive_Report", 32, [
            (1,  {}),
            (10, {"Chest": {"rot": (8, 0, 0)}, "UpperArm_L": {"rot": (54, 0, 14)}, "Forearm_L": {"rot": (35, 0, 10)}, "UpperArm_R": {"rot": (54, 0, -14)}, "Forearm_R": {"rot": (35, 0, -10)}}),
            (20, {"Head": {"rot": (-12, 0, 0)}, "UpperArm_L": {"rot": (42, 0, 10)}, "UpperArm_R": {"rot": (42, 0, -10)}}),
            (32, {}),
        ]),
    ]

    added = []
    for clip_name, duration, keyframes in phase3_specs:
        if clip_name in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[clip_name])
        act = bpy.data.actions.new(name=clip_name)
        act.use_fake_user = True
        arm_obj.animation_data.action = act

        for frame, bone_map in keyframes:
            gc.reset_pose(arm_obj)
            gc.key_all_rest(arm_obj, frame)
            for bname, xform in bone_map.items():
                gc.key_bone(
                    arm_obj,
                    bname,
                    frame,
                    loc=xform.get("loc"),
                    rot_deg=xform.get("rot"),
                    scale=xform.get("scale"),
                )

        track = arm_obj.animation_data.nla_tracks.new()
        track.name = clip_name
        strip = track.strips.new(clip_name, 1, act)
        strip.name = clip_name
        track.mute = False
        added.append(act)
    return added


def generate_manager():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)

    cname = "Agent_Manager"
    arm_obj = gc.create_character_armature(cname)
    shared_parts = gc.build_shared_body_parts("Mat_Tester_SlateBlue", "Mat_Brass")
    mgr_parts = build_manager_accessories()
    mesh_obj = gc.join_and_bind_to_armature(shared_parts + mgr_parts, arm_obj, f"{cname}_Body")

    depsgraph = bpy.context.evaluated_depsgraph_get()
    eval_obj = mesh_obj.evaluated_get(depsgraph)
    eval_mesh = eval_obj.to_mesh()
    eval_mesh.calc_loop_triangles()
    tri_count = len(eval_mesh.loop_triangles)
    vert_count = len(eval_mesh.vertices)
    eval_obj.to_mesh_clear()

    base_actions = gc.build_shared_animation_library(arm_obj)
    all_clips = [a.name for a in base_actions]

    renders = gc.render_character_views(cname, arm_obj)

    blend_path = os.path.join(wh.BLEND_WALL_DIR, f"{cname}.blend")
    fbx_path = os.path.join(wh.FBX_WALL_DIR, f"{cname}.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        object_types={'ARMATURE', 'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        armature_nodetype='NULL',
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode='AUTO',
        axis_forward='-Z',
        axis_up='Y',
    )

    report = {
        "character": cname,
        "blend": blend_path,
        "fbx": fbx_path,
        "fbx_bytes": os.path.getsize(fbx_path),
        "vertices": vert_count,
        "triangles": tri_count,
        "bones": len(arm_obj.data.bones),
        "shared_clips": len(base_actions),
        "total_clips": len(all_clips),
        "clip_names": all_clips,
        "renders": renders,
    }
    report_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_manager_agent_report.json")
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[generate_wall_manager_agent] Built Agent_Manager ({tri_count} tris, {len(all_clips)} clips) -> {report_path}")


if __name__ == "__main__":
    try:
        generate_manager()
    except Exception as ex:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_manager_agent_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
