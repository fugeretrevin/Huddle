import bpy
import math
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import wall_habitat_common as wh


def _build_wall_backplate(name="WallHabitat_Backplate_Full", compact=False):
    """
    Shallow architectural wall backplate & mounting chassis for The Workshop Treehouse.
    - Pivot = WallContact at (0, 0, 0) flush on wall.
    - Blender +Y -> Unity +Z (outward into room); Blender +Z -> Unity +Y (up).
    - Very shallow thickness (0.016m) so cubicles sit cleanly in front without clipping into wall.
    """
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    if not compact:
        w, h = 0.960, 0.740
        deck_ys = {"Lower": -0.345, "Mid": -0.115, "Manager": +0.115}
        # Blender X = -Unity X
        bx_res, bx_dev, bx_tst = +0.345, +0.115, -0.115
        bx_elev = -0.345
        bx_stop = -0.255
        bx_mid = +0.040
        bx_mgr = +0.050
        bx_printer = -0.135
        bx_wing = -0.440
    else:
        w, h = 0.740, 0.510
        deck_ys = {"Lower": -0.230, "Manager": +0.000}
        bx_res, bx_dev, bx_tst = +0.235, +0.055, -0.125
        bx_elev = -0.285
        bx_stop = -0.210
        bx_mid = 0.0
        bx_mgr = +0.040
        bx_printer = -0.125
        bx_wing = -0.352

    static_parts = []
    trim_parts = []

    # 1. Soft charcoal rubber wall-contact pads at 6 anchor points (flush at Blender Y = 0)
    pad_xs = [-w * 0.43, 0.0, +w * 0.43]
    pad_zs = [-h * 0.42, +h * 0.42]
    for xi, px in enumerate(pad_xs):
        for zi, pz in enumerate(pad_zs):
            wh.make_cylinder(f"{name}_WallPad_{xi}_{zi}", 0.014, 0.004,
                             (px, 0.002, pz), "Mat_CharcoalVisor", vertices=14,
                             rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)
            wh.make_cylinder(f"{name}_WallBrassPin_{xi}_{zi}", 0.007, 0.005,
                             (px, 0.003, pz), "Mat_Brass", vertices=12,
                             rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)

    # 2. Main soft ivory contoured wall backing plate (Y in [0.003, 0.015])
    # Rotated slab in XZ plane: use make_rounded_slab extruded along Z then rotated 90 deg X
    wh.make_rounded_slab(f"{name}_IvoryPlate", w, h, 0.012, 0.048,
                         (0.0, 0.009, 0.0), "Mat_CreamCeramic", bevel=0.0025,
                         corner_segs=5, rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)

    # 3. Warm-wood outer perimeter timber frame & horizontal deck ledger strips
    frame_d = 0.014
    frame_y = 0.011
    # Top & bottom horizontal stringers
    for lbl, sz in [("Bottom", -h * 0.5 + 0.012), ("Top", +h * 0.5 - 0.012)]:
        wh.make_beveled_box(f"{name}_TimberBeam_{lbl}", (w - 0.040, frame_d, 0.018),
                            (0.0, frame_y, sz), "Mat_WarmWood", bevel_width=0.002,
                            parent=root, parts_list=static_parts)
    # Left & right vertical stiles
    for lbl, sx in [("Right", +w * 0.5 - 0.016), ("Left", -w * 0.5 + 0.016)]:
        wh.make_beveled_box(f"{name}_TimberStile_{lbl}", (0.018, frame_d, h - 0.060),
                            (sx, frame_y, 0.0), "Mat_WarmWood", bevel_width=0.002,
                            parent=root, parts_list=static_parts)

    # Decorative vertical wood louvers along backplate behind cubicles for cozy treehouse rhythm
    slat_count = 11 if not compact else 9
    for idx in range(slat_count):
        t = (idx / (slat_count - 1)) - 0.5
        sx = t * (w - 0.120)
        wh.make_beveled_box(f"{name}_BackSlat_{idx}", (0.010, 0.004, h - 0.095),
                            (sx, 0.0155, 0.0), "Mat_WarmBeige", bevel_width=0.0008,
                            parent=root, parts_list=static_parts)

    # 4. Warm recessed shelf cove-light accent bar & muted teal architectural trim band
    for tier_lbl, tz in deck_ys.items():
        bar_w = w - 0.090 if tier_lbl == "Lower" else (0.520 if not compact else 0.420)
        bar_x = 0.0 if tier_lbl == "Lower" else (0.065 if not compact else 0.055)
        wh.make_beveled_box(f"{name}_LedgerRail_{tier_lbl}", (bar_w, 0.016, 0.010),
                            (bar_x, 0.014, tz - 0.006), "Mat_DarkWood", bevel_width=0.0015,
                            parent=root, parts_list=static_parts)
        wh.make_beveled_box(f"{name}_CoveTrim_{tier_lbl}", (bar_w * 0.90, 0.005, 0.005),
                            (bar_x, 0.0175, tz + 0.002), "Mat_Researcher_Teal", bevel_width=0.0008,
                            parent=root, parts_list=trim_parts)

    # 5. Right-side Cantilevered Beacon & Courier Porch Sill (Flush on Lower tier right wing)
    low_z = deck_ys["Lower"]
    porch_w = 0.110 if not compact else 0.088
    wh.make_rounded_slab(f"{name}_BeaconPorchSlab", porch_w, 0.195, 0.014, 0.024,
                         (bx_wing, 0.112, low_z - 0.007), "Mat_WarmWood",
                         bevel=0.002, parent=root, parts_list=static_parts)
    # Curved wood corbel bracket under right wing porch
    wh.make_beveled_box(f"{name}_BeaconCorbel", (0.018, 0.110, 0.032),
                        (bx_wing, 0.068, low_z - 0.030), "Mat_DarkWood", bevel_width=0.0025,
                        rot_deg=(-18, 0, 0), parent=root, parts_list=static_parts)
    # Cozy beacon plinth pad + brass rim on porch
    wh.make_cylinder(f"{name}_BeaconPad", 0.034, 0.008, (bx_wing, 0.110, low_z + 0.004),
                     "Mat_CreamCeramic", vertices=18, parent=root, parts_list=static_parts)
    wh.make_torus(f"{name}_BeaconBrassRim", 0.035, 0.0025, (bx_wing, 0.110, low_z + 0.006),
                  "Mat_Brass", maj_seg=20, min_seg=6, parent=root, parts_list=static_parts)
    # Courier landing disk right in front on the wing porch
    wh.make_cylinder(f"{name}_CourierPad", 0.038, 0.006, (bx_wing, 0.175, low_z + 0.003),
                     "Mat_FeltSlate", vertices=18, parent=root, parts_list=static_parts)

    # 6. Left-side small botanical planter bracket for Workshop Treehouse whimsy
    left_bx = +w * 0.445
    wh.make_rounded_slab(f"{name}_LeftPlanterShelf", 0.085, 0.110, 0.012, 0.018,
                         (left_bx, 0.068, deck_ys["Manager"] - 0.006), "Mat_WarmWood",
                         bevel=0.0018, parent=root, parts_list=static_parts)
    wh.make_cylinder(f"{name}_PlanterPot", 0.022, 0.028, (left_bx, 0.072, deck_ys["Manager"] + 0.014),
                     "Mat_Terracotta", vertices=16, parent=root, parts_list=static_parts)
    for lx, ly, lz, sc_xyz in [
        (-0.008,  0.004, 0.034, (0.013, 0.010, 0.009)),
        ( 0.009, -0.003, 0.036, (0.012, 0.009, 0.008)),
        ( 0.000,  0.002, 0.044, (0.010, 0.010, 0.012)),
    ]:
        wh.make_sphere(f"{name}_Leaf_{lz:.3f}", sc_xyz,
                       (left_bx + lx, 0.072 + ly, deck_ys["Manager"] + lz),
                       "Mat_PlantGreen", segments=10, rings=8, parent=root, parts_list=static_parts)

    wh.join_static_parts(static_parts, f"{name}_Chassis", parent=root)
    wh.join_static_parts(trim_parts, f"{name}_StatusTrim", parent=root)

    # Required Habitat & Deck Mounting Sockets (all directly under root at unit scale!)
    wh.make_anchor("WallContact",             (0.0, 0.0, 0.0), root)
    wh.make_anchor("ResearchCubicleSocket",   (bx_res, 0.015, low_z), root)
    wh.make_anchor("DeveloperCubicleSocket",  (bx_dev, 0.015, low_z), root)
    wh.make_anchor("TesterCubicleSocket",     (bx_tst, 0.015, low_z), root)
    wh.make_anchor("ElevatorSpineSocket",     (bx_elev, 0.015, low_z), root)
    wh.make_anchor("ElevatorStop_Lower",      (bx_stop, 0.196, low_z), root)
    if "Mid" in deck_ys:
        wh.make_anchor("SharedMeetingSocket", (bx_mid, 0.015, deck_ys["Mid"]), root)
        wh.make_anchor("ElevatorStop_Mid",    (bx_stop, 0.196, deck_ys["Mid"]), root)
    wh.make_anchor("ManagerOfficeSocket",     (bx_mgr, 0.015, deck_ys["Manager"]), root)
    wh.make_anchor("TaskPrinterSocket",       (bx_printer, 0.105, deck_ys["Manager"] + 0.012), root)
    wh.make_anchor("ElevatorStop_Manager",    (bx_stop, 0.196, deck_ys["Manager"]), root)
    wh.make_anchor("AttentionBeaconSocket",   (bx_wing, 0.110, low_z + 0.008), root)
    wh.make_anchor("CourierDispatchSocket",   (bx_wing, 0.175, low_z + 0.006), root)
    wh.make_anchor("PortalDepartureSocket",   (0.0, 0.255, low_z), root)

    # Priority B Navigation, Elevator Threshold, Handoff & Interaction Sockets
    wh.make_anchor("PathNode_Lower_L",        (bx_res, 0.196, low_z + 0.014), root)
    wh.make_anchor("PathNode_Lower_C",        (bx_dev, 0.196, low_z + 0.014), root)
    wh.make_anchor("PathNode_Lower_R",        (bx_tst, 0.196, low_z + 0.014), root)
    wh.make_anchor("PathNode_Lower_Elev",     (bx_stop + 0.045, 0.196, low_z + 0.014), root)
    wh.make_anchor("ElevatorEntry_Lower",     (bx_stop + 0.025, 0.196, low_z + 0.014), root)
    wh.make_anchor("ElevatorExit_Lower",      (bx_stop + 0.040, 0.196, low_z + 0.014), root)
    if "Mid" in deck_ys:
        wh.make_anchor("PathNode_Mid_C",      (bx_mid, 0.196, deck_ys["Mid"] + 0.014), root)
        wh.make_anchor("PathNode_Mid_Elev",   (bx_stop + 0.045, 0.196, deck_ys["Mid"] + 0.014), root)
        wh.make_anchor("ElevatorEntry_Mid",   (bx_stop + 0.025, 0.196, deck_ys["Mid"] + 0.014), root)
        wh.make_anchor("ElevatorExit_Mid",    (bx_stop + 0.040, 0.196, deck_ys["Mid"] + 0.014), root)
    wh.make_anchor("PathNode_Manager_C",      (bx_mgr, 0.196, deck_ys["Manager"] + 0.014), root)
    wh.make_anchor("PathNode_Manager_Printer",(bx_printer, 0.170, deck_ys["Manager"] + 0.014), root)
    wh.make_anchor("ElevatorEntry_Manager",   (bx_stop + 0.025, 0.196, deck_ys["Manager"] + 0.014), root)
    wh.make_anchor("ElevatorExit_Manager",    (bx_stop + 0.040, 0.196, deck_ys["Manager"] + 0.014), root)
    wh.make_anchor("DoorClearance",           (bx_stop, 0.210, low_z + 0.095), root)
    wh.make_anchor("PortalSource_Wall",       (0.0, 0.240, low_z + 0.080), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.38, 1.35, 0.08),
        target=(0.0, 0.05, 0.0),
        extra_cam=(-0.55, 1.10, 0.30) and ((-0.55, 1.15, 0.30), (0.0, 0.06, 0.0)),
    )


def _build_cubicle_bay(name="WallHabitat_CubicleBay", width=0.228, compact=False):
    """
    Modular Treehouse Cubicle Shell:
    - Pivot = (0, 0, 0) at wall-side bottom center (Y=0 rests against backplate+0.015m; Z=0 is floor).
    - Local +Y extent [0, 0.232m] (interior floor Z=0.012m top; clear ceiling to 0.222m).
    - Front promenade walkway unobstructed along Y in [0.155, 0.225] through 0.094m side arches!
    - Exposes WorkstationSocket + ALL 8 required_agent_sockets.
    """
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    depth = 0.232 if not compact else 0.224
    height = 0.224
    floor_h = 0.012
    wall_t = 0.010

    parts = []
    trim = []

    # 1. Floor deck slab (bottom at Z=0, top walkable floor at Z=0.012)
    wh.make_rounded_slab(f"{name}_FloorDeck", width, depth, floor_h, 0.016,
                         (0.0, depth * 0.5, floor_h * 0.5), "Mat_WarmWood",
                         bevel=0.0018, parent=root, parts_list=parts)

    # Walkway runner rug in front promenade (Y centered at 0.184m, flush top at 0.013m)
    walk_y = 0.184 if not compact else 0.178
    wh.make_rounded_slab(f"{name}_WalkwayRug", width - 0.010, 0.064, 0.002, 0.008,
                         (0.0, walk_y, floor_h + 0.001), "Mat_FeltSlate",
                         bevel=0.0004, parent=root, parts_list=parts)

    # Cozy round station mat under desk/chair zone
    wh.make_cylinder(f"{name}_WorkRug", 0.068 if not compact else 0.056, 0.002,
                     (0.0, 0.088, floor_h + 0.001), "Mat_WarmBeige",
                     vertices=22, parent=root, parts_list=parts)

    # 2. Rear cozy wainscot back panel (Y = 0.005, stops nicely with window niche cutout silhouette)
    wh.make_beveled_box(f"{name}_RearPanel", (width - 0.020, 0.008, height - 0.036),
                        (0.0, 0.006, floor_h + (height - 0.036) * 0.5), "Mat_CreamCeramic",
                        bevel_width=0.0015, parent=root, parts_list=parts)
    # Recessed window frame ornament on rear wall for depth & warmth
    win_w = width * 0.44
    wh.make_beveled_box(f"{name}_RearWindowTrim", (win_w, 0.006, 0.072),
                        (0.0, 0.011, floor_h + 0.118), "Mat_WarmWood",
                        bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RearWindowPane", (win_w - 0.012, 0.007, 0.058),
                        (0.0, 0.0115, floor_h + 0.118), "Mat_ChamberGlass",
                        bevel_width=0.0005, parent=root, parts_list=parts)

    # 3. Left & Right Arched Side Partition Walls (centered at Y = walk_y? No: pass-through arch centered at Y = walk_y!)
    # wall_depth covers rear & corridor: center wall at Y = 0.122m, depth 0.210m -> Y in [0.017, 0.227].
    # Relative to wall center Y=0.122, walk_y=0.184 is offset +0.062m!
    # Cleanest solution: build rear wing solid [0.015, 0.128] + arched corridor frame [0.128, 0.228] centered on walk_y!
    rear_len = walk_y - 0.052 - 0.012
    arch_depth = 0.104 if not compact else 0.098
    arch_open_w = 0.090 if not compact else 0.084
    arch_open_h = 0.168
    hx_side = width * 0.5 - wall_t * 0.5
    for side_lbl, sx in [("R", +hx_side), ("L", -hx_side)]:
        wh.make_beveled_box(f"{name}_SideRearWall_{side_lbl}", (wall_t, rear_len, height - 0.020),
                            (sx, 0.012 + rear_len * 0.5, floor_h + (height - 0.020) * 0.5),
                            "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
        wh.make_arch_wall(f"{name}_SideArch_{side_lbl}", arch_depth, height - 0.018, wall_t,
                          arch_open_w, arch_open_h,
                          (sx, walk_y, floor_h), "Mat_CreamCeramic", arch_segs=10,
                          parent=root, parts_list=parts)
        # Warm brass arch keystone post cap at front corner
        wh.make_cylinder(f"{name}_CornerPillar_{side_lbl}", 0.0065, height - 0.006,
                         (sx, depth - 0.010, floor_h + (height - 0.006) * 0.5),
                         "Mat_WarmWood", vertices=12, parent=root, parts_list=parts)

    # 4. Gently curved overhead ceiling rib & warm interior ceiling sconce light
    wh.make_curved_canopy(f"{name}_CeilingRib", width - 0.004, depth - 0.012, 0.010, 0.016,
                          (0.0, 0.006, height - 0.010), "Mat_CreamCeramic", segs=10,
                          parent=root, parts_list=parts)
    # Front fascia header beam with role nameplate slot
    wh.make_beveled_box(f"{name}_FrontHeader", (width - 0.008, 0.014, 0.018),
                        (0.0, depth - 0.010, height - 0.006), "Mat_WarmWood",
                        bevel_width=0.002, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RoleNameplateTrim", (width * 0.36, 0.006, 0.012),
                        (0.0, depth - 0.002, height - 0.006), "Mat_Brass",
                        bevel_width=0.001, parent=root, parts_list=trim)

    # Cozy dome pendant ceiling bulb inside top rear
    wh.make_cylinder(f"{name}_LampCanopy", 0.014, 0.005, (0.0, 0.095, height - 0.006),
                     "Mat_Brass", vertices=14, parent=root, parts_list=parts)
    wh.make_sphere(f"{name}_CeilingLightBulb", (0.010, 0.010, 0.006),
                   (0.0, 0.095, height - 0.011), "Mat_LampGlow", segments=12, rings=8,
                   parent=root, parts_list=trim)

    # 5. Front balcony safety railing with open center step-out apron so view of agent/desk stays wide open
    gap_half = 0.044 if not compact else 0.036
    side_rail_end = width * 0.5 - 0.014
    wh.make_railing(f"{name}_FrontRail_R", (gap_half, depth - 0.008), (side_rail_end, depth - 0.008),
                    floor_h, rail_h=0.026, posts=2, parent=root, parts_list=parts)
    wh.make_railing(f"{name}_FrontRail_L", (-side_rail_end, depth - 0.008), (-gap_half, depth - 0.008),
                    floor_h, rail_h=0.026, posts=2, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(trim, f"{name}_Accents", parent=root)

    # Interaction & Agent Sockets (floor surface Z = 0.013m)
    fz = floor_h + 0.001
    # Workstation mounted toward rear-center so front promenade walk_y is 100% collision-free!
    wh.make_anchor("WorkstationSocket",  (0.0, 0.088, fz), root)
    wh.make_anchor("WorkPose",           (0.0, 0.062, fz), root)
    wh.make_anchor("SeatPose",           (0.0, 0.072, fz), root)
    wh.make_anchor("AgentHomePose",      (0.0, walk_y, fz), root)
    wh.make_anchor("EntryPose",          (-width * 0.5, walk_y, fz), root)
    wh.make_anchor("HandoffPose",        (0.038, walk_y, fz), root)
    wh.make_anchor("ElevatorQueuePose",  (-width * 0.5 + 0.022, walk_y, fz), root)
    wh.make_anchor("DocumentAttach",     (0.038, 0.092, fz + 0.056), root)
    wh.make_anchor("HeadLookTarget",     (0.0, 0.075, fz + 0.095), root)
    wh.make_anchor("AgentStand_Cubicle", (0.0, walk_y, fz), root)
    wh.make_anchor("PathNode_Entry",     (-width * 0.45, walk_y, fz), root)
    wh.make_anchor("Handoff_In",         (+0.045, walk_y, fz), root)
    wh.make_anchor("Handoff_Out",        (-0.045, walk_y, fz), root)
    wh.make_anchor("DoorClearance",      (-width * 0.5 + 0.025, walk_y, fz + 0.085), root)
    wh.make_anchor("RaySelectCollider",  (0.0, 0.120, fz + 0.095), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.24, 0.48, 0.24),
        target=(0.0, 0.11, 0.10),
        extra_cam=((-0.26, 0.45, 0.24), (0.0, 0.11, 0.10)),
    )


def _build_hallway_segment():
    name = "WallHabitat_HallwaySegment"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    parts = []
    # Modular connecting skybridge/corridor (0.140m W x 0.088m D corridor)
    w, d = 0.140, 0.086
    floor_h = 0.012
    wh.make_rounded_slab(f"{name}_Deck", w, d, floor_h, 0.012, (0.0, 0.184, floor_h * 0.5),
                         "Mat_WarmWood", bevel=0.0015, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_Runner", w - 0.006, 0.058, 0.002, 0.006, (0.0, 0.184, floor_h + 0.001),
                         "Mat_FeltSlate", bevel=0.0003, parent=root, parts_list=parts)
    wh.make_railing(f"{name}_FrontRail", (-w * 0.48, 0.222), (w * 0.48, 0.222), floor_h,
                    rail_h=0.028, posts=3, parent=root, parts_list=parts)
    # Rear wall mount brace
    wh.make_beveled_box(f"{name}_BackBrace", (w - 0.016, 0.008, 0.048), (0.0, 0.144, floor_h + 0.024),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Mesh", parent=root)

    fz = floor_h + 0.001
    wh.make_anchor("AgentHomePose",     (0.0, 0.184, fz), root)
    wh.make_anchor("WorkPose",          (0.0, 0.184, fz), root)
    wh.make_anchor("SeatPose",          (0.0, 0.184, fz), root)
    wh.make_anchor("EntryPose",         (w * 0.5, 0.184, fz), root)
    wh.make_anchor("HandoffPose",       (-w * 0.5, 0.184, fz), root)
    wh.make_anchor("ElevatorQueuePose", (-w * 0.5 + 0.02, 0.184, fz), root)
    wh.make_anchor("DocumentAttach",    (0.0, 0.184, fz + 0.055), root)
    wh.make_anchor("HeadLookTarget",    (0.0, 0.184, fz + 0.095), root)

    return wh.export_wall_static(name, cam_pos=(0.20, 0.46, 0.18), target=(0.0, 0.18, 0.03))


def _build_mid_planning_nook():
    """
    Middle Work Deck: Shared Blueprint Balcony, Meeting Nook & Brass Task-Card Rail.
    Spans 0.456m wide (over Developer + Tester bays), connecting straight to ElevatorStop_Mid.
    """
    name = "WallHabitat_MidPlanningNook"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    w, d, h = 0.456, 0.232, 0.218
    floor_h = 0.012
    parts = []
    accents = []

    # 1. Floor deck & corridor strip
    wh.make_rounded_slab(f"{name}_Deck", w, d, floor_h, 0.020, (0.0, d * 0.5, floor_h * 0.5),
                         "Mat_WarmWood", bevel=0.002, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_CorridorPad", w - 0.024, 0.066, 0.002, 0.010, (0.0, 0.184, floor_h + 0.001),
                         "Mat_FeltSlate", bevel=0.0004, parent=root, parts_list=parts)
    # Recessed teal brass-trimmed meeting rug under shared round blueprint table
    wh.make_cylinder(f"{name}_MeetingRug", 0.088, 0.002, (0.040, 0.088, floor_h + 0.001),
                     "Mat_Researcher_Teal", vertices=24, parent=root, parts_list=parts)

    # 2. Curved Pergola Arched Frame (open, airy middle deck silhouette)
    for rx in (-0.218, -0.020, +0.218):
        wh.make_cylinder(f"{name}_RearPost_{rx:+.2f}", 0.008, h - floor_h, (rx, 0.018, floor_h + (h - floor_h) * 0.5),
                         "Mat_WarmWood", vertices=12, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_BackHeader", (w - 0.016, 0.016, 0.014),
                        (0.0, 0.018, h - 0.008), "Mat_CreamCeramic", bevel_width=0.0015,
                        parent=root, parts_list=parts)

    # Right end arch leading into ElevatorStop_Mid (-X in Blender -> +X Unity elevator)
    wh.make_arch_wall(f"{name}_ElevSideArch", 0.104, h - 0.014, 0.010, 0.090, 0.168,
                      (-w * 0.5 + 0.005, 0.184, floor_h), "Mat_CreamCeramic", arch_segs=10,
                      parent=root, parts_list=parts)
    # Left end decorative wooden pergola rail + wall trellis
    wh.make_railing(f"{name}_LeftSideRail", (w * 0.5 - 0.008, 0.030), (w * 0.5 - 0.008, d - 0.012),
                    floor_h, rail_h=0.030, posts=3, parent=root, parts_list=parts)

    # 3. Shared Round Blueprint Table & Dual Minimalist Stools
    tx, ty = 0.040, 0.088
    wh.make_cylinder(f"{name}_TablePedestal", 0.012, 0.042, (tx, ty, floor_h + 0.021),
                     "Mat_DarkWood", vertices=14, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_TableFoot", 0.032, 0.005, (tx, ty, floor_h + 0.003),
                     "Mat_Brass", vertices=18, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_TableTop", 0.056, 0.007, (tx, ty, floor_h + 0.044),
                     "Mat_CreamCeramic", vertices=24, bevel_caps=0.001, parent=root, parts_list=parts)
    # Unrolled Blueprint sheet & miniature pin markers on table
    wh.make_beveled_box(f"{name}_SharedBlueprint", (0.064, 0.044, 0.002), (tx, ty, floor_h + 0.049),
                        "Mat_BlueprintBlue", bevel_width=0.0004, rot_deg=(0, 0, -6),
                        parent=root, parts_list=parts)
    for px, py, pmat in [(-0.014, 0.006, "Mat_Developer_Coral"), (0.016, -0.008, "Mat_Researcher_Lavender"), (0.002, 0.010, "Mat_Tester_Mint")]:
        wh.make_cylinder(f"{name}_PlanPin_{px:+.3f}", 0.0035, 0.008, (tx + px, ty + py, floor_h + 0.054),
                         pmat, vertices=10, parent=root, parts_list=accents)

    # Two miniature perch stools on left & right of meeting table
    for side_lbl, sx in [("L", tx + 0.068), ("R", tx - 0.068)]:
        wh.make_cylinder(f"{name}_StoolLeg_{side_lbl}", 0.007, 0.024, (sx, ty, floor_h + 0.012),
                         "Mat_DarkWood", vertices=12, parent=root, parts_list=parts)
        wh.make_cylinder(f"{name}_StoolCushion_{side_lbl}", 0.018, 0.006, (sx, ty, floor_h + 0.025),
                         "Mat_WarmBeige", vertices=16, parent=root, parts_list=parts)

    # 4. Horizontal Task-Card Distribution Rail mounted cleanly above WallBoard (level & bracketed)
    rail_x0, rail_x1 = -0.165, +0.165
    rail_z = floor_h + 0.162
    wh.make_beveled_box(f"{name}_CardRailBack", (rail_x1 - rail_x0, 0.010, 0.012),
                        ((rail_x0 + rail_x1) * 0.5, 0.022, rail_z), "Mat_DarkWood",
                        bevel_width=0.0015, rot_deg=(0, 0, 0), parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_CardRailBar", 0.0032, rail_x1 - rail_x0,
                     ((rail_x0 + rail_x1) * 0.5, 0.030, rail_z), "Mat_Brass",
                     vertices=12, rot_deg=(0, 90, 0), parent=root, parts_list=parts)
    for bkx in (rail_x0 + 0.018, rail_x1 - 0.018):
        wh.make_beveled_box(f"{name}_RailBracket_{bkx:+.2f}", (0.008, 0.016, 0.010),
                            (bkx, 0.024, rail_z), "Mat_Brass", bevel_width=0.0008,
                            parent=root, parts_list=accents)
    for tok_lbl, tx_tok, tok_mat in [("L", -0.105, "Mat_Researcher_Lavender"), ("R", +0.115, "Mat_Developer_Coral")]:
        wh.make_beveled_box(f"{name}_RailToken_{tok_lbl}", (0.018, 0.004, 0.022),
                            (tx_tok, 0.033, rail_z - 0.010), tok_mat, bevel_width=0.0006,
                            parent=root, parts_list=accents)
    # Miniature pinned Kanban diagram board behind meeting table
    wh.make_beveled_box(f"{name}_WallBoard", (0.115, 0.008, 0.072), (tx, 0.018, floor_h + 0.104),
                        "Mat_WarmWood", bevel_width=0.0018, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_WallBoardFace", (0.102, 0.009, 0.058), (tx, 0.019, floor_h + 0.104),
                        "Mat_PaperWarm", bevel_width=0.0005, parent=root, parts_list=parts)

    # Front safety balcony railings with open promenade center
    wh.make_railing(f"{name}_FrontRail_L", (-w * 0.47, d - 0.008), (-0.060, d - 0.008),
                    floor_h, rail_h=0.028, posts=3, parent=root, parts_list=parts)
    wh.make_railing(f"{name}_FrontRail_R", (0.060, d - 0.008), (w * 0.47, d - 0.008),
                    floor_h, rail_h=0.028, posts=3, parent=root, parts_list=parts)

    # Cozy overhead pendant lamp
    wh.make_cylinder(f"{name}_PendantCord", 0.002, 0.042, (tx, ty, h - 0.024),
                     "Mat_Brass", vertices=10, parent=root, parts_list=parts)
    wh.make_cone(f"{name}_PendantShade", 0.024, 0.008, 0.016, (tx, ty, h - 0.048),
                 "Mat_Researcher_Teal", vertices=18, parent=root, parts_list=parts)
    wh.make_sphere(f"{name}_PendantBulb", (0.008, 0.008, 0.006), (tx, ty, h - 0.056),
                   "Mat_LampGlow", segments=10, rings=8, parent=root, parts_list=accents)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(accents, f"{name}_Accents", parent=root)

    fz = floor_h + 0.001
    wh.make_anchor("SharedMeetingSocket", (tx, ty, fz), root)
    wh.make_anchor("DeliverySocket",      (tx - 0.040, 0.184, fz), root)
    wh.make_anchor("AgentHomePose",       (tx + 0.068, ty, fz), root)
    wh.make_anchor("WorkPose",            (tx - 0.068, ty, fz), root)
    wh.make_anchor("SeatPose",            (tx + 0.068, ty, fz), root)
    wh.make_anchor("EntryPose",           (-w * 0.5, 0.184, fz), root)
    wh.make_anchor("HandoffPose",         (0.0, 0.184, fz), root)
    wh.make_anchor("ElevatorQueuePose",   (-w * 0.5 + 0.026, 0.184, fz), root)
    wh.make_anchor("DocumentAttach",      (tx, ty, fz + 0.052), root)
    wh.make_anchor("HeadLookTarget",      (tx, ty, fz + 0.095), root)
    wh.make_anchor("AgentStand_Nook_L",   (tx + 0.068, 0.184, fz), root)
    wh.make_anchor("AgentStand_Nook_R",   (tx - 0.068, 0.184, fz), root)
    wh.make_anchor("Handoff_Mid",         (tx, 0.184, fz), root)
    wh.make_anchor("DoorClearance",       (-w * 0.5 + 0.025, 0.184, fz + 0.085), root)
    wh.make_anchor("PathNode_MidDeck",    (tx, 0.184, fz), root)
    wh.make_anchor("RaySelectCollider",   (tx, 0.120, fz + 0.090), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.30, 0.65, 0.22),
        target=(0.0, 0.11, 0.09),
        extra_cam=((-0.34, 0.62, 0.24), (0.0, 0.11, 0.09)),
    )


def _build_manager_lookout():
    """
    Top-Tier Elevated Manager Deck & Architect Study:
    - Whimsical vaulted canopy roof with warm brass rib trim (Workshop Treehouse crown).
    - Central Manager executive curved drafting desk, glowing tactical map board,
      inspectable physical plan receipt tray, brass banker lamp, plush executive seat,
      and dedicated right-wing Printer Alcove (`TaskPrinterSocket`).
    """
    name = "WallHabitat_ManagerLookout"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    w, d, h = 0.495, 0.238, 0.250
    floor_h = 0.014
    parts = []
    accents = []

    # 1. Floor deck & corridor
    wh.make_rounded_slab(f"{name}_Deck", w, d, floor_h, 0.024, (0.0, d * 0.5, floor_h * 0.5),
                         "Mat_WarmWood", bevel=0.0022, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_Carpet", 0.210, 0.135, 0.002, 0.025, (+0.035, 0.112, floor_h + 0.001),
                         "Mat_Researcher_Lavender", bevel=0.0005, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_CorridorStrip", 0.220, 0.064, 0.002, 0.010, (-0.125, 0.184, floor_h + 0.001),
                         "Mat_FeltSlate", bevel=0.0004, parent=root, parts_list=parts)

    # 2. Architectural Treehouse Vaulted Canopy Roof & Corner Columns
    vault_base_z = 0.195
    wh.make_curved_canopy(f"{name}_RoofOuterShell", w + 0.022, d + 0.014, 0.014, 0.042,
                          (0.0, 0.0, vault_base_z), "Mat_WarmWood", segs=16,
                          parent=root, parts_list=parts)
    wh.make_curved_canopy(f"{name}_RoofInnerCrown", w - 0.020, d + 0.004, 0.009, 0.038,
                          (0.0, 0.004, vault_base_z - 0.006), "Mat_CreamCeramic", segs=14,
                          parent=root, parts_list=parts)
    # Brass roof ridge finial crest
    wh.make_beveled_box(f"{name}_RoofFinialBar", (0.140, d - 0.020, 0.010),
                        (0.0, d * 0.5 + 0.002, vault_base_z + 0.056), "Mat_Brass",
                        bevel_width=0.002, parent=root, parts_list=parts)

    # Columns & rear curved study window wall
    wh.make_beveled_box(f"{name}_RearStudyWall", (w - 0.028, 0.010, vault_base_z - floor_h + 0.015),
                        (0.0, 0.010, floor_h + (vault_base_z - floor_h) * 0.5),
                        "Mat_CreamCeramic", bevel_width=0.0015, parent=root, parts_list=parts)
    for cx in (-w * 0.47, +w * 0.47):
        wh.make_cylinder(f"{name}_FrontPillar_{cx:+.2f}", 0.0085, vault_base_z - floor_h,
                         (cx, d - 0.014, floor_h + (vault_base_z - floor_h) * 0.5),
                         "Mat_WarmWood", vertices=12, parent=root, parts_list=parts)

    # Right pass-through arch connecting to ElevatorStop_Manager
    wh.make_arch_wall(f"{name}_ElevSideArch", 0.104, vault_base_z - floor_h, 0.010, 0.090, 0.168,
                      (-w * 0.5 + 0.005, 0.184, floor_h), "Mat_CreamCeramic", arch_segs=10,
                      parent=root, parts_list=parts)

    # 3. Manager Curved Executive Desk (Blender X = +0.035 -> Unity -0.035; slightly left-of-center lookout)
    dx, dy = +0.035, 0.125
    desk_h = 0.052
    wh.make_rounded_slab(f"{name}_ExecDeskTop", 0.165, 0.078, 0.009, 0.022,
                         (dx, dy, floor_h + desk_h), "Mat_WarmWood", bevel=0.0018,
                         parent=root, parts_list=parts)
    for lx in (dx - 0.062, dx + 0.062):
        wh.make_cylinder(f"{name}_DeskLeg_{lx:+.2f}", 0.006, desk_h, (lx, dy, floor_h + desk_h * 0.5),
                         "Mat_DarkWood", vertices=12, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_DeskFrontModesty", (0.124, 0.007, desk_h * 0.68),
                        (dx, dy + 0.028, floor_h + desk_h * 0.52), "Mat_CreamCeramic",
                        bevel_width=0.0012, parent=root, parts_list=parts)
    # Brass badge emblem on front of Manager desk
    wh.make_beveled_box(f"{name}_DeskBrassBadge", (0.042, 0.004, 0.014),
                        (dx, dy + 0.033, floor_h + desk_h * 0.56), "Mat_Brass",
                        bevel_width=0.0008, parent=root, parts_list=accents)

    # Desk props: Overview project map/slate, inspectable Plan Receipt Tray, Brass Banker Lamp
    wh.make_beveled_box(f"{name}_OverviewMapPad", (0.068, 0.046, 0.004),
                        (dx + 0.010, dy, floor_h + desk_h + 0.006), "Mat_BlueprintBlue",
                        bevel_width=0.0008, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_PlanReceiptTray", (0.044, 0.036, 0.006),
                        (dx - 0.050, dy - 0.004, floor_h + desk_h + 0.007), "Mat_DarkWood",
                        bevel_width=0.001, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_PlanReceiptSheet", (0.034, 0.026, 0.002),
                        (dx - 0.050, dy - 0.004, floor_h + desk_h + 0.011), "Mat_PaperWarm",
                        bevel_width=0.0004, rot_deg=(0, 0, 5), parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_BankerLampBase", 0.010, 0.004, (dx + 0.062, dy - 0.018, floor_h + desk_h + 0.006),
                     "Mat_Brass", vertices=14, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_BankerLampNeck", 0.0025, 0.034, (dx + 0.062, dy - 0.018, floor_h + desk_h + 0.023),
                     "Mat_Brass", vertices=10, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_BankerLampShade", (0.024, 0.014, 0.010), (dx + 0.058, dy - 0.012, floor_h + desk_h + 0.040),
                        "Mat_Tester_Mint", bevel_width=0.002, rot_deg=(12, 0, -18), parent=root, parts_list=accents)

    # Rear panoramic overview project board on Manager rear wall
    wh.make_beveled_box(f"{name}_OverviewWallFrame", (0.140, 0.010, 0.082),
                        (dx, 0.020, floor_h + 0.118), "Mat_WarmWood", bevel_width=0.0018,
                        parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_OverviewScreen", (0.128, 0.011, 0.070),
                        (dx, 0.021, floor_h + 0.118), "Mat_ScreenCode", bevel_width=0.0008,
                        parent=root, parts_list=accents)

    # Executive Plush Swivel Chair behind desk facing +Z room (tucked neatly back for standing Manager clearance)
    wh.make_cylinder(f"{name}_ChairPedestal", 0.006, 0.022, (dx, dy - 0.072, floor_h + 0.011),
                     "Mat_Brass", vertices=12, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_ChairSeat", 0.022, 0.008, (dx, dy - 0.072, floor_h + 0.026),
                     "Mat_Researcher_Teal", vertices=18, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_ChairBack", (0.038, 0.009, 0.044), (dx, dy - 0.088, floor_h + 0.052),
                        "Mat_Researcher_Teal", bevel_width=0.003, parent=root, parts_list=parts)

    # 4. Printer Alcove Plinth on right side near elevator (Blender X = -0.145, Y = 0.100)
    px, py = -0.145, 0.095
    wh.make_rounded_slab(f"{name}_PrinterStand", 0.096, 0.082, 0.042, 0.014,
                         (px, py, floor_h + 0.021), "Mat_WarmWood", bevel=0.0018,
                         parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_PrinterCardTray", (0.064, 0.040, 0.005),
                        (px, py + 0.046, floor_h + 0.036), "Mat_Brass", bevel_width=0.001,
                        rot_deg=(15, 0, 0), parent=root, parts_list=parts)

    # Balcony lookout safety railings at front
    wh.make_railing(f"{name}_LookoutRail_L", (-w * 0.46, d - 0.010), (-0.060, d - 0.010),
                    floor_h, rail_h=0.030, posts=3, parent=root, parts_list=parts)
    wh.make_railing(f"{name}_LookoutRail_R", (-0.015, d - 0.010), (+w * 0.46, d - 0.010),
                    floor_h, rail_h=0.030, posts=4, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(accents, f"{name}_Accents", parent=root)

    fz = floor_h + 0.001
    wh.make_anchor("ManagerOfficeSocket", (dx, dy, fz), root)
    wh.make_anchor("ManagerDeskSocket",   (dx, dy - 0.048, fz), root)
    wh.make_anchor("TaskPrinterSocket",   (px, py, floor_h + 0.042), root)
    wh.make_anchor("DeliverySocket",      (dx - 0.050, dy + 0.036, fz + desk_h + 0.012), root)
    wh.make_anchor("AgentHomePose",       (dx, dy - 0.048, fz), root)
    wh.make_anchor("WorkPose",            (dx, dy - 0.048, fz), root)
    wh.make_anchor("SeatPose",            (dx, dy - 0.072, fz + 0.018), root)
    wh.make_anchor("EntryPose",           (-w * 0.5, 0.184, fz), root)
    wh.make_anchor("HandoffPose",         (-0.080, 0.184, fz), root)
    wh.make_anchor("ElevatorQueuePose",   (-w * 0.5 + 0.026, 0.184, fz), root)
    wh.make_anchor("DocumentAttach",      (dx - 0.045, dy + 0.010, fz + desk_h + 0.015), root)
    wh.make_anchor("HeadLookTarget",      (dx, dy - 0.048, fz + 0.105), root)
    wh.make_anchor("AgentStand_Manager",  (dx, dy - 0.048, fz), root)
    wh.make_anchor("CardOutput_Manager",  (px, py + 0.046, fz + 0.040), root)
    wh.make_anchor("Handoff_Manager",     (-0.080, 0.184, fz), root)
    wh.make_anchor("DoorClearance",       (-w * 0.5 + 0.025, 0.184, fz + 0.085), root)
    wh.make_anchor("RaySelectCollider",   (dx, 0.110, fz + 0.095), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.35, 0.72, 0.25),
        target=(0.0, 0.11, 0.11),
        extra_cam=((-0.38, 0.68, 0.26), (0.0, 0.11, 0.11)),
    )


def _build_wall_placement_ghost():
    """
    Lightweight Wall Placement Preview Ghost:
    - Rounded flush back perimeter outline + 4 crisp L-corner depth brackets showing full 0.245m swept volume
    - Bold outward (+Y Blender -> +Z Unity wall_outward) facing indicator chevron
    - Compatible with MaterialPropertyBlock Red (Invalid) / Amber (Review) / Teal (Valid) tinting!
    """
    name = "WallHabitat_PlacementGhost"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    w, h, d = 0.980, 0.760, 0.245
    hw, hh = w * 0.5, h * 0.5
    bar_t = 0.012
    ghost_parts = []
    arrow_parts = []

    # 1. Perimeter wall frame (Blender Y = 0.004)
    for lbl, loc, sz in [
        ("Top",    (0, 0.006, +hh), (w, 0.008, bar_t)),
        ("Bottom", (0, 0.006, -hh), (w, 0.008, bar_t)),
        ("Right",  (+hw, 0.006, 0), (bar_t, 0.008, h)),
        ("Left",   (-hw, 0.006, 0), (bar_t, 0.008, h)),
    ]:
        wh.make_beveled_box(f"{name}_WallBar_{lbl}", sz, loc, "Mat_Spatial_Ghost",
                            bevel_width=0.001, parent=root, parts_list=ghost_parts)

    # 2. 4 Front Swept-Volume Corner L-Brackets showing exact depth clearance (+0.245m out from wall)
    arm = 0.085
    for cx_s in (-1.0, +1.0):
        for cz_s in (-1.0, +1.0):
            cx, cz = cx_s * hw, cz_s * hh
            # Depth strut
            wh.make_beveled_box(f"{name}_Strut_{cx_s:+.0f}_{cz_s:+.0f}", (bar_t * 0.85, d, bar_t * 0.85),
                                (cx, d * 0.5, cz), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)
            # Horizontal L-tip
            wh.make_beveled_box(f"{name}_LTipH_{cx_s:+.0f}_{cz_s:+.0f}", (arm, bar_t, bar_t),
                                (cx - cx_s * arm * 0.5, d, cz), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)
            # Vertical L-tip
            wh.make_beveled_box(f"{name}_LTipV_{cx_s:+.0f}_{cz_s:+.0f}", (bar_t, bar_t, arm),
                                (cx, d, cz - cz_s * arm * 0.5), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)

    # 3. Bold outward wall-facing chevron indicator at front-bottom center (+Y Blender = +Z Unity wall_outward)
    wh.make_cylinder(f"{name}_FacingStem", 0.010, 0.095, (0.0, 0.140, -hh + 0.020),
                     "Mat_Researcher_Teal", vertices=14, rot_deg=(90, 0, 0),
                     parent=root, parts_list=arrow_parts)
    wh.make_cone(f"{name}_FacingTip", 0.028, 0.002, 0.048, (0.0, 0.210, -hh + 0.020),
                 "Mat_Researcher_Teal", vertices=16, rot_deg=(-90, 0, 0),
                 parent=root, parts_list=arrow_parts)

    wh.join_static_parts(ghost_parts, f"{name}_VolumeFrame", parent=root)
    wh.join_static_parts(arrow_parts, f"{name}_FacingArrow", parent=root)

    wh.make_anchor("WallContact", (0.0, 0.0, 0.0), root)
    wh.make_anchor("ConfirmationBadgeSocket", (0.0, 0.250, -hh + 0.025), root)

    return wh.export_wall_static(name, cam_pos=(0.55, 1.35, 0.15), target=(0.0, 0.10, 0.0))


def generate_all():
    results = [
        _build_wall_backplate("WallHabitat_Backplate_Full", compact=False),
        _build_wall_backplate("WallHabitat_Backplate_Compact", compact=True),
        _build_cubicle_bay("WallHabitat_CubicleBay", width=0.228, compact=False),
        _build_cubicle_bay("WallHabitat_CubicleBay_Compact", width=0.178, compact=True),
        _build_hallway_segment(),
        _build_mid_planning_nook(),
        _build_manager_lookout(),
        _build_wall_placement_ghost(),
    ]
    report_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_core_report.json")
    with open(report_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"[generate_wall_habitat_core] Generated {len(results)} modular core assets -> {report_path}")


if __name__ == "__main__":
    try:
        generate_all()
    except Exception as ex:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_core_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
