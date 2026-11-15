import bpy
import math
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import wall_habitat_common as wh


def get_layout_spec(compact=False):
    """Exact mirror of HabitatLayoutBuilder.Build(Full | Compact) in HabitatLayout.cs.
    Coordinate convention verified from Unity prefabs:
      Blender +X -> Unity +X
      Blender +Y -> Unity +Z (out from wall)
      Blender +Z -> Unity +Y (vertical up)
    """
    if not compact:
        W, H, D = 1.050, 0.900, 0.260
        slab, base_frame, back = 0.020, 0.050, 0.015
        elev_w, elev_margin = 0.130, 0.020
        agent_r, agent_h, clear = 0.032, 0.170, 0.005
        corridor_depth, railing = 0.080, 0.010
        desk_depth, desk_w_frac, partition = 0.045, 0.50, 0.012
        story_clear = [0.270, 0.240, 0.240]
        deck_xmin_from_left = [0.020, 0.475, 0.225]
    else:
        W, H, D = 0.740, 0.620, 0.240
        slab, base_frame, back = 0.020, 0.050, 0.015
        elev_w, elev_margin = 0.110, 0.020
        agent_r, agent_h, clear = 0.028, 0.150, 0.005
        corridor_depth, railing = 0.080, 0.010
        desk_depth, desk_w_frac, partition = 0.040, 0.42, 0.012
        story_clear = [0.240, 0.220]
        deck_xmin_from_left = [0.020, 0.170]

    hw, hh = W * 0.5, H * 0.5
    shaft_x1 = hw - elev_margin
    shaft_x0 = shaft_x1 - elev_w
    elev_x = (shaft_x0 + shaft_x1) * 0.5
    door_w = elev_w - 0.030

    z_c1 = D - railing
    z_c0 = z_c1 - corridor_depth
    zc = (z_c0 + z_c1) * 0.5
    cabin_d = z_c0 - back - 0.010
    cabin_half = (elev_w * 0.5 - 0.010, cabin_d * 0.5)
    cabin_z = back + 0.005 + cabin_half[1]

    floors = []
    cur_z = -hh + base_frame
    for i, sc_h in enumerate(story_clear):
        xmin = -hw + deck_xmin_from_left[i]
        floors.append({
            "index": i,
            "floorY": cur_z,
            "ceilingY": cur_z + sc_h,
            "clearH": sc_h,
            "xMin": xmin,
            "xMax": shaft_x0,
            "width": shaft_x0 - xmin,
        })
        cur_z += sc_h + slab
    roof_top = cur_z

    office_y0, office_y1 = back, z_c0
    office_yc = (office_y0 + office_y1) * 0.5
    zd1 = z_c0 - 0.005
    zd0 = zd1 - desk_depth
    work_y = (back + agent_r + clear + zd0 - agent_r - clear) * 0.5
    entry_y = (zd0 + zd1) * 0.5

    f0 = floors[0]
    x_l0 = f0["xMin"] + 0.010
    x_l1 = shaft_x0 - 0.010
    cw = (x_l1 - x_l0) / 3.0
    cx_res = x_l0 + 0.5 * cw
    cx_dev = x_l0 + 1.5 * cw
    cx_tst = x_l0 + 2.5 * cw

    mgr = floors[-1]
    mgr_off_x0 = mgr["xMin"] + 0.010
    mgr_off_x1 = shaft_x0 - 0.010
    printer_w = 0.100
    printer_x = mgr_off_x0 + printer_w * 0.5 + 0.010
    mcw = min(0.300, mgr_off_x1 - mgr_off_x0 - printer_w - 0.020)
    mcx = mgr_off_x1 - mcw * 0.5
    mgr_part_x = mcx - mcw * 0.5

    mid = floors[1] if not compact else None
    mx = (mid["xMin"] + shaft_x0) * 0.5 if mid else 0.0

    def role_workstation(role_name, floor_z, cx, bay_w):
        inner_r = cx + bay_w * 0.5 - partition * 0.5
        desk_w = bay_w * desk_w_frac
        dx0 = cx - bay_w * 0.5 + 0.020
        dx1 = dx0 + desk_w
        wx = (dx0 + dx1) * 0.5
        gap_x = (dx1 + inner_r) * 0.5
        qx = shaft_x0 - agent_r - 0.020
        return {
            "role": role_name,
            "cx": cx,
            "bay_w": bay_w,
            "desk_x0": dx0,
            "desk_x1": dx1,
            "desk_w": desk_w,
            "desk_y0": zd0,
            "desk_y1": zd1,
            "work_x": wx,
            "work_y": work_y,
            "gap_x": gap_x,
            "entry_y": entry_y,
            "floor_z": floor_z,
            "qx": qx,
        }

    role_stations = [
        role_workstation("Researcher", f0["floorY"], cx_res, cw),
        role_workstation("Developer",  f0["floorY"], cx_dev, cw),
        role_workstation("Tester",     f0["floorY"], cx_tst, cw),
        role_workstation("Manager",    mgr["floorY"], mcx, mcw),
    ]

    beacon_pos = (-hw + deck_xmin_from_left[-1] + 0.050, D * 0.5, roof_top)
    courier_pos = (f0["xMin"] + 0.050, zc, f0["floorY"])
    portal_pos = (0.0, D + 0.020, f0["floorY"])
    printer_pos = (printer_x, office_yc, mgr["floorY"] + 0.050)

    return {
        "compact": compact,
        "W": W, "H": H, "D": D, "hw": hw, "hh": hh,
        "slab": slab, "base_frame": base_frame, "back": back,
        "elev_w": elev_w, "elev_margin": elev_margin,
        "agent_r": agent_r, "agent_h": agent_h, "clear": clear,
        "shaft_x0": shaft_x0, "shaft_x1": shaft_x1, "elev_x": elev_x, "door_w": door_w,
        "corridor_depth": corridor_depth, "railing": railing,
        "z_c0": z_c0, "z_c1": z_c1, "zc": zc,
        "cabin_half": cabin_half, "cabin_z": cabin_z,
        "floors": floors, "f0": f0, "mid": mid, "mgr": mgr, "roof_top": roof_top,
        "office_y0": office_y0, "office_y1": office_y1, "office_yc": office_yc,
        "desk_y0": zd0, "desk_y1": zd1, "desk_depth": desk_depth, "desk_w_frac": desk_w_frac,
        "work_y": work_y, "entry_y": entry_y, "partition": partition,
        "x_l0": x_l0, "x_l1": x_l1, "cw": cw,
        "cx_res": cx_res, "cx_dev": cx_dev, "cx_tst": cx_tst,
        "mx": mx,
        "mgr_off_x0": mgr_off_x0, "mgr_off_x1": mgr_off_x1,
        "printer_w": printer_w, "printer_x": printer_x,
        "mcw": mcw, "mcx": mcx, "mgr_part_x": mgr_part_x,
        "role_stations": role_stations,
        "beacon_pos": beacon_pos,
        "courier_pos": courier_pos,
        "portal_pos": portal_pos,
        "printer_pos": printer_pos,
    }


def _build_wall_backplate(name="WallHabitat_Backplate_Full", compact=False):
    """
    Wall-flush architectural Workshop Treehouse backplate matching HabitatLayoutBuilder envelope:
      Full:    1.050 x 0.900 x 0.260 m
      Compact: 0.740 x 0.620 x 0.240 m
    Pivot (0, 0, 0) = WallContact (center X/Y, Y=0 flush against wall).
    """
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(compact)
    W, H, hw, hh, back = s["W"], s["H"], s["hw"], s["hh"], s["back"]
    f0, mid, mgr = s["f0"], s["mid"], s["mgr"]

    static_parts = []
    trim_parts = []

    # 1. Soft charcoal/brass wall-contact pads strictly within W x H
    pad_xs = [-W * 0.43, 0.0, +W * 0.43]
    pad_zs = [-H * 0.42, +H * 0.42]
    for xi, px in enumerate(pad_xs):
        for zi, pz in enumerate(pad_zs):
            wh.make_cylinder(f"{name}_WallPad_{xi}_{zi}", 0.014, 0.004,
                             (px, 0.002, pz), "Mat_CharcoalVisor", vertices=14,
                             rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)
            wh.make_cylinder(f"{name}_WallBrassPin_{xi}_{zi}", 0.007, 0.005,
                             (px, 0.003, pz), "Mat_Brass", vertices=12,
                             rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)

    # 2. Main contoured ivory backplate spanning W x H, front face strictly at Blender Y = back (0.015)
    wh.make_rounded_slab(f"{name}_IvoryPlate", W, H, 0.012, 0.028,
                         (0.0, 0.009, 0.0), "Mat_CreamCeramic", bevel=0.0018,
                         corner_segs=5, rot_deg=(90, 0, 0), parent=root, parts_list=static_parts)

    # 3. Warm-wood outer perimeter timber rails (1.5mm proud of IvoryPlate to eliminate coplanar z-fighting)
    frame_d = 0.014
    frame_y = 0.0095
    for lbl, sz in [("Bottom", -hh + 0.009), ("Top", +hh - 0.009)]:
        wh.make_beveled_box(f"{name}_TimberBeam_{lbl}", (W - 0.036, frame_d, 0.016),
                            (0.0, frame_y, sz), "Mat_WarmWood", bevel_width=0.0018,
                            parent=root, parts_list=static_parts)
    for lbl, sx in [("Left", -hw + 0.009), ("Right", +hw - 0.009)]:
        wh.make_beveled_box(f"{name}_TimberStile_{lbl}", (0.016, frame_d, H - 0.044),
                            (sx, frame_y, 0.0), "Mat_WarmWood", bevel_width=0.0018,
                            parent=root, parts_list=static_parts)

    # Decorative vertical wood louvers & floor ledgers flush on backplate
    slat_count = 13 if not compact else 9
    for idx in range(slat_count):
        t = (idx / (slat_count - 1)) - 0.5
        sx = t * (W - 0.110)
        wh.make_beveled_box(f"{name}_BackSlat_{idx}", (0.009, 0.003, H - 0.070),
                            (sx, 0.014, 0.0), "Mat_WarmBeige", bevel_width=0.0006,
                            parent=root, parts_list=static_parts)

    for fl in s["floors"]:
        fw = fl["width"]
        fcx = (fl["xMin"] + fl["xMax"]) * 0.5
        fz = fl["floorY"]
        wh.make_beveled_box(f"{name}_LedgerRail_{fl['index']}", (fw, 0.005, 0.010),
                            (fcx, 0.015, fz - 0.012), "Mat_DarkWood", bevel_width=0.001,
                            parent=root, parts_list=static_parts)
        wh.make_beveled_box(f"{name}_CoveTrim_{fl['index']}", (fw * 0.94, 0.004, 0.004),
                            (fcx, 0.0165, fz + fl["clearH"] - 0.006), "Mat_Researcher_Teal",
                            bevel_width=0.0006, parent=root, parts_list=trim_parts)

    # 4. Under-corbel brackets below Floor 0 & Leftmost EndWall filler [f0.xMin, x_l0]
    skirt_z0 = -hh + 0.010
    skirt_z1 = f0["floorY"] - s["slab"]
    if skirt_z1 > skirt_z0:
        skirt_w = s["shaft_x1"] - f0["xMin"]
        skirt_cx = (f0["xMin"] + s["shaft_x1"]) * 0.5
        for c_off in (-skirt_w * 0.36, 0.0, +skirt_w * 0.36):
            wh.make_beveled_box(f"{name}_UnderCorbel_{c_off:+.2f}", (0.022, 0.095, skirt_z1 - skirt_z0),
                                (skirt_cx + c_off, back + 0.048, (skirt_z0 + skirt_z1) * 0.5),
                                "Mat_DarkWood", bevel_width=0.002, parent=root, parts_list=static_parts)

    # Small left edge floor strip closing the 1cm margin [f0.xMin, x_l0] & right 1cm strip [x_l1, shaft_x0]
    for slbl, sx0, sx1 in [("LowerMarginL", f0["xMin"], s["x_l0"]), ("LowerMarginR", s["x_l1"], s["shaft_x0"])]:
        mw = sx1 - sx0
        mc = (sx0 + sx1) * 0.5
        wh.make_beveled_box(f"{name}_{slbl}", (mw, s["D"] - back, s["slab"]),
                            (mc, (back + s["D"]) * 0.5, f0["floorY"] - s["slab"] * 0.5),
                            "Mat_WarmWood", bevel_width=0.0008, parent=root, parts_list=static_parts)

    # 5. Grounded Beacon Perch Plinth directly atop Manager Roof Deck at AttentionBeaconSocket
    bx, by, bz = s["beacon_pos"]
    wh.make_cylinder(f"{name}_BeaconPerchPad", 0.024, 0.004,
                     (bx, by, bz + 0.002), "Mat_CreamCeramic", vertices=18,
                     parent=root, parts_list=static_parts)
    wh.make_torus(f"{name}_BeaconBrassRim", 0.025, 0.0018,
                  (bx, by, bz + 0.003), "Mat_Brass", maj_seg=20, min_seg=6,
                  parent=root, parts_list=trim_parts)

    # 6. Whimsical Botanical Wall Sconce in clear negative-X upper void (strictly outside all floor decks!)
    planter_x = (-0.415 if not compact else -0.285)
    planter_z = (+0.040 if not compact else +0.080)
    wh.make_rounded_slab(f"{name}_SidePlanterShelf", 0.072, 0.058, 0.009, 0.014,
                         (planter_x, back + 0.029, planter_z), "Mat_WarmWood",
                         bevel=0.0015, parent=root, parts_list=static_parts)
    wh.make_cylinder(f"{name}_PlanterPot", 0.018, 0.022,
                     (planter_x, back + 0.030, planter_z + 0.015),
                     "Mat_Terracotta", vertices=14, parent=root, parts_list=static_parts)
    for lx, ly, lz, sc_xyz in [
        (-0.007, +0.003, 0.030, (0.011, 0.008, 0.008)),
        (+0.008, -0.002, 0.032, (0.010, 0.008, 0.007)),
        ( 0.000, +0.001, 0.039, (0.009, 0.009, 0.011)),
    ]:
        wh.make_sphere(f"{name}_Leaf_{lz:.3f}", sc_xyz,
                       (planter_x + lx, back + 0.030 + ly, planter_z + lz),
                       "Mat_PlantGreen", segments=10, rings=8, parent=root, parts_list=static_parts)

    # Courier Dispatch landing pad at CourierDispatchSocket
    cx_d, cy_d, cz_d = s["courier_pos"]
    wh.make_cylinder(f"{name}_CourierPad", 0.022, 0.002,
                     (cx_d, cy_d, cz_d + 0.001),
                     "Mat_FeltSlate", vertices=16, parent=root, parts_list=static_parts)

    wh.join_static_parts(static_parts, f"{name}_Chassis", parent=root)
    wh.join_static_parts(trim_parts, f"{name}_StatusTrim", parent=root)

    # Exact Contract Habitat Sockets (Blender (x, y_out, z_up) == Unity (x, z_up, y_out))
    wh.make_anchor("WallContact",             (0.0, 0.0, 0.0), root)
    wh.make_anchor("ResearchCubicleSocket",   (s["cx_res"], s["office_yc"], f0["floorY"]), root)
    wh.make_anchor("DeveloperCubicleSocket",  (s["cx_dev"], s["office_yc"], f0["floorY"]), root)
    wh.make_anchor("TesterCubicleSocket",     (s["cx_tst"], s["office_yc"], f0["floorY"]), root)
    if mid is not None:
        wh.make_anchor("SharedMeetingSocket", (s["mx"], s["office_yc"], mid["floorY"]), root)
        wh.make_anchor("ElevatorStop_Mid",    (s["elev_x"], s["zc"], mid["floorY"]), root)
    wh.make_anchor("ManagerOfficeSocket",     (s["mcx"], s["office_yc"], mgr["floorY"]), root)
    wh.make_anchor("TaskPrinterSocket",       s["printer_pos"], root)
    wh.make_anchor("AttentionBeaconSocket",   s["beacon_pos"], root)
    wh.make_anchor("CourierDispatchSocket",   s["courier_pos"], root)
    wh.make_anchor("PortalDepartureSocket",   s["portal_pos"], root)

    # Elevator spine mount & exact contract elevator sockets
    wh.make_anchor("ElevatorSpineSocket",     (s["elev_x"], 0.0, f0["floorY"]), root)
    wh.make_anchor("ElevatorCarSocket",       (s["elev_x"], s["cabin_z"], f0["floorY"]), root)
    wh.make_anchor("ElevatorStop_Lower",      (s["elev_x"], s["zc"], f0["floorY"]), root)
    wh.make_anchor("ElevatorStop_Manager",    (s["elev_x"], s["zc"], mgr["floorY"]), root)

    # Exact Per-Role Agent Sockets from HabitatLayoutBuilder.AddWorkstation + AddElevatorStop
    ah = s["agent_h"]
    for rs in s["role_stations"]:
        r = rs["role"]
        fz = rs["floor_z"]
        wx, wy = rs["work_x"], rs["work_y"]
        gx, ey = rs["gap_x"], rs["entry_y"]
        zc, qx = s["zc"], rs["qx"]
        wh.make_anchor(f"{r}.AgentHomePose",     (wx, wy, fz), root)
        wh.make_anchor(f"{r}.WorkPose",          (wx, wy, fz), root)
        wh.make_anchor(f"{r}.SeatPose",          (wx, wy, fz), root)
        wh.make_anchor(f"{r}.EntryPose",         (gx, ey, fz), root)
        wh.make_anchor(f"{r}.HandoffPose",       (gx, zc, fz), root)
        wh.make_anchor(f"{r}.ElevatorQueuePose", (qx, zc, fz), root)
        wh.make_anchor(f"{r}.DocumentAttach",    (wx, wy + 0.030, fz + ah * 0.55), root)
        wh.make_anchor(f"{r}.HeadLookTarget",    (wx, wy, fz + ah * 0.90), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.35, 1.45, 0.08),
        target=(0.0, 0.05, 0.0),
        extra_cam=((-0.55, 1.20, 0.30), (0.0, 0.06, 0.0)),
    )


def _build_cubicle_bay(name="WallHabitat_CubicleBay", compact=False):
    """
    Modular Treehouse Cubicle Shell matching exact HabitatLayoutBuilder bay geometry.
    - Mounted at {Research|Developer|Tester}CubicleSocket = (cx, office_yc, f0.floorY).
    - Local origin (0, 0, 0) sits at cubicle center X, office_yc, floor top Z=0.000.
    - Partition stays inside office zone [back, z_c0 - 0.005] so corridor [z_c0, z_c1] is 100% open!
    - WorkstationSocket placed at exact HabitatLayoutBuilder Desk_* footprint [deskX0..deskX1] x [zD0..zD1].
    - AgentHomePose placed at exact workX, workY behind desk with open aisle gapX on +X side!
    """
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(compact)
    width = s["cw"]
    ref_y = s["office_yc"]
    ly_back = s["back"] - ref_y
    ly_front = s["D"] - ref_y
    ly_c0 = s["z_c0"] - ref_y
    ly_c1 = s["z_c1"] - ref_y
    ly_corr = s["zc"] - ref_y
    depth = ly_front - ly_back
    clear_h = s["f0"]["clearH"]
    slab = s["slab"]
    part_t = s["partition"]

    # Sample offsets from Researcher station (identical for Researcher/Developer/Tester relative to cx)
    rs = s["role_stations"][0]
    lx_work = rs["work_x"] - rs["cx"]
    ly_work = rs["work_y"] - ref_y
    lx_gap = rs["gap_x"] - rs["cx"]
    ly_entry = rs["entry_y"] - ref_y
    ly_desk_c = ((rs["desk_y0"] + rs["desk_y1"]) * 0.5) - ref_y

    parts = []
    trim = []

    # 1. Floor deck slab (bottom at -0.020, walkable top flush at Z=0.000)
    wh.make_beveled_box(f"{name}_FloorDeck", (width - 0.001, depth, slab),
                        (0.0, (ly_back + ly_front) * 0.5, -slab * 0.5),
                        "Mat_WarmWood", bevel_width=0.0015, parent=root, parts_list=parts)

    # Flush felt corridor runner strictly inside corridor [z_c0, z_c1]
    wh.make_rounded_slab(f"{name}_WalkwayRug", width - 0.008, s["corridor_depth"] - 0.012, 0.0015, 0.006,
                         (0.0, ly_corr, 0.0008), "Mat_FeltSlate",
                         bevel=0.0003, parent=root, parts_list=parts)

    # Subtle work mat under agent standing zone (lx_work, ly_work)
    wh.make_cylinder(f"{name}_WorkRug", min(rs["desk_w"] * 0.45, 0.044), 0.0015,
                     (lx_work, ly_work, 0.0008), "Mat_WarmBeige",
                     vertices=20, parent=root, parts_list=parts)

    # 2. Rear wainscot back panel + cozy framed window against backplate
    panel_h = clear_h - 0.024
    wh.make_beveled_box(f"{name}_RearPanel", (width - 0.018, 0.005, panel_h),
                        (0.0, ly_back + 0.0025, panel_h * 0.5), "Mat_CreamCeramic",
                        bevel_width=0.001, parent=root, parts_list=parts)
    win_w = min(width * 0.42, 0.105)
    wh.make_beveled_box(f"{name}_RearWindowTrim", (win_w, 0.004, 0.068),
                        (0.0, ly_back + 0.005, 0.135), "Mat_WarmWood",
                        bevel_width=0.001, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RearWindowPane", (win_w - 0.010, 0.005, 0.054),
                        (0.0, ly_back + 0.0055, 0.135), "Mat_ChamberGlass",
                        bevel_width=0.0004, parent=root, parts_list=parts)

    # 3. Left (-X) Half-Height Partition matching HabitatLayoutBuilder Partition_0..2 [back, z_c0 - 0.005]
    part_y0 = ly_back + 0.002
    part_y1 = ly_c0 - 0.008
    part_len = max(0.040, part_y1 - part_y0)
    part_yc = (part_y0 + part_y1) * 0.5
    part_h = 0.110
    wh.make_beveled_box(f"{name}_LeftPartition", (part_t, part_len, part_h),
                        (-width * 0.5, part_yc, part_h * 0.5),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_LeftPartitionCap", (part_t + 0.003, part_len + 0.002, 0.007),
                        (-width * 0.5, part_yc, part_h + 0.0035),
                        "Mat_WarmWood", bevel_width=0.0008, parent=root, parts_list=parts)

    wh.make_cylinder(f"{name}_RearCornerPost", 0.0050, clear_h - 0.008,
                     (-width * 0.5 + 0.005, ly_back + 0.008, (clear_h - 0.008) * 0.5),
                     "Mat_WarmWood", vertices=10, parent=root, parts_list=parts)

    # 4. Warm Overhead Valance Rib strictly along rear wall
    wh.make_beveled_box(f"{name}_RearValance", (width - 0.010, 0.032, 0.010),
                        (0.0, ly_back + 0.018, clear_h - 0.007), "Mat_WarmWood",
                        bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RoleNameplateTrim", (width * 0.36, 0.006, 0.009),
                        (0.0, ly_back + 0.035, clear_h - 0.007), "Mat_Brass",
                        bevel_width=0.0008, parent=root, parts_list=trim)

    wh.make_cylinder(f"{name}_LampCanopy", 0.011, 0.004, (0.0, ly_back + 0.028, clear_h - 0.013),
                     "Mat_Brass", vertices=12, parent=root, parts_list=parts)
    wh.make_sphere(f"{name}_CeilingLightBulb", (0.008, 0.008, 0.005),
                   (0.0, ly_back + 0.028, clear_h - 0.017), "Mat_LampGlow",
                   segments=10, rings=8, parent=root, parts_list=trim)

    # 5. Front Balcony Safety Railing strictly inside [z_c1, D]
    rail_y = (ly_c1 + ly_front) * 0.5
    wh.make_railing(f"{name}_FrontRail", (-width * 0.46, rail_y), (+width * 0.46, rail_y),
                    0.0, rail_h=0.028, posts=3, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(trim, f"{name}_Accents", parent=root)

    # Workstation inside Desk_* box + Agent poses matching HabitatLayoutBuilder AddWorkstation!
    fz = 0.001
    ah = s["agent_h"]
    wh.make_anchor("WorkstationSocket",  (lx_work, ly_desk_c, fz), root)
    wh.make_anchor("WorkPose",           (lx_work, ly_work, fz), root)
    wh.make_anchor("SeatPose",           (lx_work, ly_work, fz), root)
    wh.make_anchor("AgentHomePose",      (lx_work, ly_work, fz), root)
    wh.make_anchor("EntryPose",          (lx_gap, ly_entry, fz), root)
    wh.make_anchor("HandoffPose",        (lx_gap, ly_corr, fz), root)
    wh.make_anchor("ElevatorQueuePose",  (width * 0.45, ly_corr, fz), root)
    wh.make_anchor("DocumentAttach",     (lx_work, ly_work + 0.030, fz + ah * 0.55), root)
    wh.make_anchor("HeadLookTarget",     (lx_work, ly_work, fz + ah * 0.90), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.24, 0.48, 0.24),
        target=(0.0, 0.0, 0.10),
        extra_cam=((-0.26, 0.45, 0.24), (0.0, 0.0, 0.10)),
    )


def _build_hallway_segment():
    name = "WallHabitat_HallwaySegment"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(False)
    w = 0.140
    d = s["corridor_depth"] + s["railing"]
    slab = s["slab"]
    yc = (s["z_c0"] + s["D"]) * 0.5 - s["office_yc"]
    y_rail = s["D"] - 0.005 - s["office_yc"]

    parts = []
    wh.make_beveled_box(f"{name}_Deck", (w, d, slab), (0.0, yc, -slab * 0.5),
                        "Mat_WarmWood", bevel_width=0.0015, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_Runner", w - 0.006, s["corridor_depth"] - 0.010, 0.0015, 0.005,
                         (0.0, s["zc"] - s["office_yc"], 0.0008), "Mat_FeltSlate",
                         bevel=0.0003, parent=root, parts_list=parts)
    wh.make_railing(f"{name}_FrontRail", (-w * 0.46, y_rail), (w * 0.46, y_rail), 0.0,
                    rail_h=0.028, posts=3, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Mesh", parent=root)

    yc_corr = s["zc"] - s["office_yc"]
    wh.make_anchor("AgentHomePose",     (0.0, yc_corr, 0.001), root)
    wh.make_anchor("WorkPose",          (0.0, yc_corr, 0.001), root)
    wh.make_anchor("SeatPose",          (0.0, yc_corr, 0.001), root)
    wh.make_anchor("EntryPose",         (-w * 0.5, yc_corr, 0.001), root)
    wh.make_anchor("HandoffPose",       (+w * 0.5, yc_corr, 0.001), root)
    wh.make_anchor("ElevatorQueuePose", (+w * 0.4, yc_corr, 0.001), root)
    wh.make_anchor("DocumentAttach",    (0.0, yc_corr, 0.056), root)
    wh.make_anchor("HeadLookTarget",    (0.0, yc_corr, 0.095), root)

    return wh.export_wall_static(name, cam_pos=(0.20, 0.46, 0.18), target=(0.0, yc, 0.03))


def _build_mid_planning_nook():
    """
    Full-Layout Middle Deck (`SharedMeetingSocket` = (+0.1625, 0.0925, -0.1100)):
      X span: [-0.050, +0.375] -> width 0.425m, centered at lx=0.
      MeetingTable strictly inside [mx-0.06..mx+0.06] x [back+0.02..zC0-0.01].
      Corridor [z_c0, z_c1] 100% open straight to ElevatorStop_Mid!
    """
    name = "WallHabitat_MidPlanningNook"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(False)
    mid = s["mid"]
    w = mid["width"]
    ref_y = s["office_yc"]
    ly_back = s["back"] - ref_y
    ly_front = s["D"] - ref_y
    ly_c0 = s["z_c0"] - ref_y
    ly_c1 = s["z_c1"] - ref_y
    ly_corr = s["zc"] - ref_y
    depth = ly_front - ly_back
    slab = s["slab"]
    clear_h = mid["clearH"]

    parts = []
    accents = []

    # 1. Floor deck slab + corridor strip + meeting mat
    wh.make_beveled_box(f"{name}_Deck", (w, depth, slab),
                        (0.0, (ly_back + ly_front) * 0.5, -slab * 0.5),
                        "Mat_WarmWood", bevel_width=0.0018, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_CorridorPad", w - 0.016, s["corridor_depth"] - 0.012, 0.0015, 0.006,
                         (0.0, ly_corr, 0.0008), "Mat_FeltSlate", bevel=0.0003,
                         parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_MeetingRug", 0.068, 0.0015,
                     (0.0, -0.005, 0.0008), "Mat_Researcher_Teal", vertices=24,
                     parent=root, parts_list=parts)

    # 2. Left (-X) End Wall & Rear Posts (keeps +X edge 100% OPEN to ElevatorStop_Mid!)
    wh.make_beveled_box(f"{name}_LeftEndWall", (s["partition"], depth - 0.012, clear_h * 0.52),
                        (-w * 0.5 + s["partition"] * 0.5, (ly_back + ly_front) * 0.5, clear_h * 0.26),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_LeftEndWallCap", (s["partition"] + 0.004, depth - 0.008, 0.008),
                        (-w * 0.5 + s["partition"] * 0.5, (ly_back + ly_front) * 0.5, clear_h * 0.52 + 0.004),
                        "Mat_WarmWood", bevel_width=0.001, parent=root, parts_list=parts)

    for rx in (-w * 0.44, +0.02, +w * 0.44):
        wh.make_cylinder(f"{name}_RearPost_{rx:+.2f}", 0.0065, clear_h - 0.008,
                         (rx, ly_back + 0.007, (clear_h - 0.008) * 0.5),
                         "Mat_WarmWood", vertices=12, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_BackHeader", (w - 0.016, 0.014, 0.012),
                        (0.0, ly_back + 0.009, clear_h - 0.008), "Mat_CreamCeramic",
                        bevel_width=0.0012, parent=root, parts_list=parts)

    # 3. Shared Round Blueprint Table strictly inside [mx-0.06..mx+0.06] x [back+0.02..zC0-0.01]
    tx, ty = 0.0, 0.0
    wh.make_cylinder(f"{name}_TablePedestal", 0.010, 0.044, (tx, ty, 0.022),
                     "Mat_DarkWood", vertices=12, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_TableFoot", 0.026, 0.004, (tx, ty, 0.002),
                     "Mat_Brass", vertices=16, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_TableTop", 0.048, 0.006, (tx, ty, 0.047),
                     "Mat_CreamCeramic", vertices=22, bevel_caps=0.0008, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_SharedBlueprint", (0.058, 0.038, 0.002), (tx, ty, 0.051),
                        "Mat_BlueprintBlue", bevel_width=0.0004, rot_deg=(0, 0, -5),
                        parent=root, parts_list=parts)
    for px, py, pmat in [(-0.012, 0.004, "Mat_Developer_Coral"), (0.013, -0.006, "Mat_Researcher_Lavender"), (0.001, 0.008, "Mat_Tester_Mint")]:
        wh.make_cylinder(f"{name}_PlanPin_{px:+.3f}", 0.003, 0.007, (tx + px, ty + py, 0.055),
                         pmat, vertices=10, parent=root, parts_list=accents)

    for side_lbl, sx in [("L", -0.072), ("R", +0.072)]:
        wh.make_cylinder(f"{name}_StoolLeg_{side_lbl}", 0.006, 0.022, (sx, ty, 0.011),
                         "Mat_DarkWood", vertices=12, parent=root, parts_list=parts)
        wh.make_cylinder(f"{name}_StoolCushion_{side_lbl}", 0.015, 0.005, (sx, ty, 0.024),
                         "Mat_WarmBeige", vertices=14, parent=root, parts_list=parts)

    # 4. Pinned Kanban Board & Task-Card Rail mounted flat along rear wall
    wh.make_beveled_box(f"{name}_WallBoard", (0.115, 0.006, 0.068),
                        (0.0, ly_back + 0.008, 0.112), "Mat_WarmWood",
                        bevel_width=0.0015, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_WallBoardFace", (0.102, 0.007, 0.054),
                        (0.0, ly_back + 0.009, 0.112), "Mat_PaperWarm",
                        bevel_width=0.0004, parent=root, parts_list=parts)
    rail_z = 0.172
    wh.make_beveled_box(f"{name}_CardRailBack", (0.280, 0.008, 0.010),
                        (0.0, ly_back + 0.010, rail_z), "Mat_DarkWood",
                        bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_CardRailBar", 0.0028, 0.275,
                     (0.0, ly_back + 0.017, rail_z), "Mat_Brass",
                     vertices=12, rot_deg=(0, 90, 0), parent=root, parts_list=parts)
    for tok_lbl, tx_tok, tok_mat in [("L", -0.095, "Mat_Researcher_Lavender"), ("R", +0.095, "Mat_Developer_Coral")]:
        wh.make_beveled_box(f"{name}_RailToken_{tok_lbl}", (0.016, 0.003, 0.020),
                            (tx_tok, ly_back + 0.019, rail_z - 0.009), tok_mat,
                            bevel_width=0.0005, parent=root, parts_list=accents)

    # 5. Front safety railing strictly inside [z_c1, D]
    rail_y = (ly_c1 + ly_front) * 0.5
    wh.make_railing(f"{name}_FrontRail", (-w * 0.45, rail_y), (+w * 0.45, rail_y),
                    0.0, rail_h=0.028, posts=4, parent=root, parts_list=parts)

    wh.make_cylinder(f"{name}_PendantCord", 0.0018, 0.024, (tx, ty, clear_h - 0.018),
                     "Mat_Brass", vertices=10, parent=root, parts_list=parts)
    wh.make_cone(f"{name}_PendantShade", 0.020, 0.007, 0.014, (tx, ty, clear_h - 0.032),
                 "Mat_Researcher_Teal", vertices=16, parent=root, parts_list=parts)
    wh.make_sphere(f"{name}_PendantBulb", (0.007, 0.007, 0.005), (tx, ty, clear_h - 0.040),
                   "Mat_LampGlow", segments=10, rings=8, parent=root, parts_list=accents)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(accents, f"{name}_Accents", parent=root)

    fz = 0.001
    wh.make_anchor("SharedMeetingSocket", (0.0, 0.0, fz), root)
    wh.make_anchor("AgentHomePose",       (0.0, ly_corr, fz), root)
    wh.make_anchor("WorkPose",            (0.0, ly_corr, fz), root)
    wh.make_anchor("SeatPose",            (-0.072, ty, fz), root)
    wh.make_anchor("EntryPose",           (+w * 0.46, ly_corr, fz), root)
    wh.make_anchor("HandoffPose",         (+0.080, ly_corr, fz), root)
    wh.make_anchor("ElevatorQueuePose",   (+w * 0.46, ly_corr, fz), root)
    wh.make_anchor("DocumentAttach",      (0.0, ty, fz + 0.055), root)
    wh.make_anchor("HeadLookTarget",      (0.0, ly_corr, fz + 0.095), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.30, 0.65, 0.22),
        target=(0.0, 0.02, 0.09),
        extra_cam=((-0.34, 0.62, 0.24), (0.0, 0.02, 0.09)),
    )


def _build_manager_lookout(name="WallHabitat_ManagerLookout", compact=False):
    """
    Top-Tier Elevated Manager Deck & Printer Alcove (`ManagerOfficeSocket` = (mcx, office_yc, mgr.floorY)).
      Exact match to HabitatLayoutBuilder Manager floor, PrinterTable, ManagerPartition, Desk_Manager & Roof:
      Full:    deck [-0.300, +0.375], mcx=+0.215, printerX=-0.230, ManagerPartition x=+0.065, roofTop=+0.260 local.
      Compact: deck [-0.200, +0.240], mcx=+0.080, printerX=-0.130, ManagerPartition x=-0.070, roofTop=+0.240 local.
    """
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(compact)
    mgr = s["mgr"]
    mcx = s["mcx"]
    ref_y = s["office_yc"]
    lx_min = mgr["xMin"] - mcx
    lx_max = mgr["xMax"] - mcx
    w = mgr["width"]
    deck_cx = (lx_min + lx_max) * 0.5

    lx_printer = s["printer_x"] - mcx
    lx_part = s["mgr_part_x"] - mcx

    ly_back = s["back"] - ref_y
    ly_front = s["D"] - ref_y
    ly_c0 = s["z_c0"] - ref_y
    ly_c1 = s["z_c1"] - ref_y
    ly_corr = s["zc"] - ref_y
    depth = ly_front - ly_back
    slab = s["slab"]
    clear_h = mgr["clearH"]

    mgr_rs = s["role_stations"][3]
    lx_work = mgr_rs["work_x"] - mcx
    ly_work = mgr_rs["work_y"] - ref_y
    lx_gap = mgr_rs["gap_x"] - mcx
    ly_entry = mgr_rs["entry_y"] - ref_y
    ly_desk = ((mgr_rs["desk_y0"] + mgr_rs["desk_y1"]) * 0.5) - ref_y

    parts = []
    accents = []

    # 1. Manager Floor Deck Slab
    wh.make_beveled_box(f"{name}_Deck", (w, depth, slab),
                        (deck_cx, (ly_back + ly_front) * 0.5, -slab * 0.5),
                        "Mat_WarmWood", bevel_width=0.0018, parent=root, parts_list=parts)
    # Executive rug under Manager office zone & corridor runner strip
    off_w = lx_max - lx_part
    wh.make_rounded_slab(f"{name}_Carpet", max(0.14, off_w - 0.04), 0.115, 0.0015, 0.016,
                         (0.0, -0.005, 0.0008), "Mat_Researcher_Lavender",
                         bevel=0.0004, parent=root, parts_list=parts)
    wh.make_rounded_slab(f"{name}_CorridorStrip", w - 0.020, s["corridor_depth"] - 0.012, 0.0015, 0.006,
                         (deck_cx, ly_corr, 0.0008), "Mat_FeltSlate",
                         bevel=0.0003, parent=root, parts_list=parts)

    # 2. Architectural Roof Slab strictly inside [clear_h, clear_h + slab]
    wh.make_beveled_box(f"{name}_RoofDeck", (w + 0.004, depth + 0.002, slab),
                        (deck_cx, (ly_back + ly_front) * 0.5, clear_h + slab * 0.5),
                        "Mat_WarmWood", bevel_width=0.0018, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RoofFasciaTrim", (w * 0.85, 0.006, 0.010),
                        (deck_cx, ly_front + 0.001, clear_h + slab * 0.5),
                        "Mat_Brass", bevel_width=0.0010, parent=root, parts_list=accents)

    # Left (-X) end wall at lx_min & rear study wall
    wh.make_beveled_box(f"{name}_LeftEndWall", (s["partition"], depth - 0.012, clear_h * 0.52),
                        (lx_min + s["partition"] * 0.5, (ly_back + ly_front) * 0.5, clear_h * 0.26),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_RearStudyWall", (w - 0.024, 0.006, clear_h - 0.016),
                        (deck_cx, ly_back + 0.003, (clear_h - 0.016) * 0.5),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
    for cx_col in (lx_min + 0.008, lx_part, lx_max - 0.008):
        wh.make_cylinder(f"{name}_RearPillar_{cx_col:+.2f}", 0.006, clear_h,
                         (cx_col, ly_back + 0.008, clear_h * 0.5),
                         "Mat_WarmWood", vertices=10, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_FrontLeftPillar", 0.0065, clear_h,
                     (lx_min + 0.008, ly_front - 0.010, clear_h * 0.5),
                     "Mat_WarmWood", vertices=10, parent=root, parts_list=parts)

    # 3. ManagerPartition between Printer Alcove & Manager Office
    part_y0 = ly_back + 0.002
    part_y1 = ly_c0 - 0.008
    part_len = max(0.040, part_y1 - part_y0)
    wh.make_beveled_box(f"{name}_PrinterPartition", (s["partition"], part_len, 0.110),
                        (lx_part, (part_y0 + part_y1) * 0.5, 0.055),
                        "Mat_CreamCeramic", bevel_width=0.0012, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_PrinterPartitionCap", (s["partition"] + 0.003, part_len + 0.002, 0.007),
                        (lx_part, (part_y0 + part_y1) * 0.5, 0.1135),
                        "Mat_WarmWood", bevel_width=0.0008, parent=root, parts_list=parts)

    # 4. PrinterStand strictly matching HabitatLayoutBuilder PrinterTable (h = 0.050 -> top at lz = +0.050!)
    stand_h = 0.050
    stand_w = 0.088
    stand_d = min(0.068, (ly_c0 - ly_back) - 0.025)
    wh.make_rounded_slab(f"{name}_PrinterStand", stand_w, stand_d, stand_h, 0.010,
                         (lx_printer, 0.0, stand_h * 0.5),
                         "Mat_WarmWood", bevel=0.0015, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_PrinterBaseTrim", (stand_w - 0.008, stand_d - 0.008, 0.004),
                        (lx_printer, 0.0, stand_h - 0.002),
                        "Mat_Brass", bevel_width=0.0006, parent=root, parts_list=accents)

    # 5. Executive Manager Desk strictly inside Desk_Manager bounds (workX, deskZ)
    desk_w = min(mgr_rs["desk_w"], 0.145)
    desk_d = s["desk_depth"]
    desk_h = 0.046 if not compact else 0.042
    wh.make_rounded_slab(f"{name}_ExecDeskTop", desk_w, desk_d, 0.007, 0.012,
                         (lx_work, ly_desk, desk_h), "Mat_WarmWood", bevel=0.0012,
                         parent=root, parts_list=parts)
    for lx_leg in (-desk_w * 0.40, +desk_w * 0.40):
        wh.make_cylinder(f"{name}_DeskLeg_{lx_leg:+.2f}", 0.005, desk_h,
                         (lx_work + lx_leg, ly_desk, desk_h * 0.5), "Mat_DarkWood",
                         vertices=10, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_DeskModesty", (desk_w * 0.78, 0.005, desk_h * 0.65),
                        (lx_work, ly_desk + desk_d * 0.35, desk_h * 0.5),
                        "Mat_CreamCeramic", bevel_width=0.0008, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_DeskBrassBadge", (0.036, 0.003, 0.010),
                        (lx_work, ly_desk + desk_d * 0.38, desk_h * 0.55),
                        "Mat_Brass", bevel_width=0.0006, parent=root, parts_list=accents)
    wh.make_beveled_box(f"{name}_OverviewMapPad", (desk_w * 0.44, desk_d * 0.62, 0.003),
                        (lx_work, ly_desk, desk_h + 0.0045), "Mat_BlueprintBlue",
                        bevel_width=0.0005, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_BankerLampBase", 0.008, 0.003,
                     (lx_work + desk_w * 0.34, ly_desk - 0.006, desk_h + 0.005),
                     "Mat_Brass", vertices=12, parent=root, parts_list=parts)
    wh.make_cylinder(f"{name}_BankerLampNeck", 0.002, 0.024,
                     (lx_work + desk_w * 0.34, ly_desk - 0.006, desk_h + 0.018),
                     "Mat_Brass", vertices=8, parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_BankerLampShade", (0.018, 0.010, 0.007),
                        (lx_work + desk_w * 0.31, ly_desk - 0.003, desk_h + 0.030),
                        "Mat_Tester_Mint", bevel_width=0.0012, rot_deg=(10, 0, -14),
                        parent=root, parts_list=accents)

    # Rear panoramic overview project screen flat against rear wall behind Manager
    scr_w = 0.130 if not compact else 0.105
    scr_h = 0.072 if not compact else 0.060
    wh.make_beveled_box(f"{name}_OverviewWallFrame", (scr_w, 0.006, scr_h),
                        (lx_work, ly_back + 0.009, 0.125), "Mat_WarmWood", bevel_width=0.0012,
                        parent=root, parts_list=parts)
    wh.make_beveled_box(f"{name}_OverviewScreen", (scr_w - 0.012, 0.007, scr_h - 0.012),
                        (lx_work, ly_back + 0.010, 0.125), "Mat_ScreenCode", bevel_width=0.0005,
                        parent=root, parts_list=accents)

    # 6. Front lookout railing strictly inside [z_c1, D]
    rail_y = (ly_c1 + ly_front) * 0.5
    wh.make_railing(f"{name}_LookoutRail", (lx_min + 0.018, rail_y), (lx_max - 0.012, rail_y),
                    0.0, rail_h=0.028, posts=5 if not compact else 4, parent=root, parts_list=parts)

    wh.join_static_parts(parts, f"{name}_Structure", parent=root)
    wh.join_static_parts(accents, f"{name}_Accents", parent=root)

    fz = 0.001
    ah = s["agent_h"]
    wh.make_anchor("ManagerOfficeSocket", (0.0, 0.0, fz), root)
    wh.make_anchor("WorkstationSocket",   (lx_work, ly_desk, fz), root)
    wh.make_anchor("ManagerDeskSocket",   (lx_work, ly_work, fz), root)
    wh.make_anchor("TaskPrinterSocket",   (lx_printer, 0.0, stand_h), root)
    wh.make_anchor("AgentHomePose",       (lx_work, ly_work, fz), root)
    wh.make_anchor("WorkPose",            (lx_work, ly_work, fz), root)
    wh.make_anchor("SeatPose",            (lx_work, ly_work, fz), root)
    wh.make_anchor("EntryPose",           (lx_gap, ly_entry, fz), root)
    wh.make_anchor("HandoffPose",         (lx_gap, ly_corr, fz), root)
    wh.make_anchor("ElevatorQueuePose",   (lx_max - 0.015, ly_corr, fz), root)
    wh.make_anchor("DocumentAttach",      (lx_work, ly_work + 0.030, fz + ah * 0.55), root)
    wh.make_anchor("HeadLookTarget",      (lx_work, ly_work, fz + ah * 0.90), root)

    return wh.export_wall_static(
        name,
        cam_pos=(0.35, 0.72, 0.25),
        target=(deck_cx, 0.02, 0.10),
        extra_cam=((-0.38, 0.68, 0.26), (deck_cx, 0.02, 0.10)),
    )


def _build_wall_placement_ghost():
    """
    Lightweight Wall Placement Preview Ghost sized to Full HabitatLayout envelope (1.050 x 0.900 x 0.260 m).
    Pivot = WallContact (0, 0, 0).
    """
    name = "WallHabitat_PlacementGhost"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    s = get_layout_spec(False)
    w, h, d = s["W"], s["H"], s["D"]
    hw, hh = w * 0.5, h * 0.5
    bar_t = 0.012
    ghost_parts = []
    arrow_parts = []

    for lbl, loc, sz in [
        ("Top",    (0, 0.006, +hh), (w, 0.008, bar_t)),
        ("Bottom", (0, 0.006, -hh), (w, 0.008, bar_t)),
        ("Right",  (+hw, 0.006, 0), (bar_t, 0.008, h)),
        ("Left",   (-hw, 0.006, 0), (bar_t, 0.008, h)),
    ]:
        wh.make_beveled_box(f"{name}_WallBar_{lbl}", sz, loc, "Mat_Spatial_Ghost",
                            bevel_width=0.001, parent=root, parts_list=ghost_parts)

    arm = 0.085
    for cx_s in (-1.0, +1.0):
        for cz_s in (-1.0, +1.0):
            cx, cz = cx_s * hw, cz_s * hh
            wh.make_beveled_box(f"{name}_Strut_{cx_s:+.0f}_{cz_s:+.0f}", (bar_t * 0.85, d, bar_t * 0.85),
                                (cx, d * 0.5, cz), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)
            wh.make_beveled_box(f"{name}_LTipH_{cx_s:+.0f}_{cz_s:+.0f}", (arm, bar_t, bar_t),
                                (cx - cx_s * arm * 0.5, d, cz), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)
            wh.make_beveled_box(f"{name}_LTipV_{cx_s:+.0f}_{cz_s:+.0f}", (bar_t, bar_t, arm),
                                (cx, d, cz - cz_s * arm * 0.5), "Mat_Spatial_Ghost", bevel_width=0.001,
                                parent=root, parts_list=ghost_parts)

    wh.make_cylinder(f"{name}_FacingStem", 0.010, 0.095, (0.0, 0.145, -hh + 0.025),
                     "Mat_Researcher_Teal", vertices=14, rot_deg=(90, 0, 0),
                     parent=root, parts_list=arrow_parts)
    wh.make_cone(f"{name}_FacingTip", 0.028, 0.002, 0.048, (0.0, 0.215, -hh + 0.025),
                 "Mat_Researcher_Teal", vertices=16, rot_deg=(-90, 0, 0),
                 parent=root, parts_list=arrow_parts)

    wh.join_static_parts(ghost_parts, f"{name}_VolumeFrame", parent=root)
    wh.join_static_parts(arrow_parts, f"{name}_FacingArrow", parent=root)

    wh.make_anchor("WallContact", (0.0, 0.0, 0.0), root)
    wh.make_anchor("ConfirmationBadgeSocket", (0.0, d + 0.010, -hh + 0.025), root)

    return wh.export_wall_static(name, cam_pos=(0.55, 1.45, 0.15), target=(0.0, 0.10, 0.0))


def generate_all():
    results = [
        _build_wall_backplate("WallHabitat_Backplate_Full", compact=False),
        _build_wall_backplate("WallHabitat_Backplate_Compact", compact=True),
        _build_cubicle_bay("WallHabitat_CubicleBay", compact=False),
        _build_cubicle_bay("WallHabitat_CubicleBay_Compact", compact=True),
        _build_hallway_segment(),
        _build_mid_planning_nook(),
        _build_manager_lookout("WallHabitat_ManagerLookout", compact=False),
        _build_manager_lookout("WallHabitat_ManagerLookout_Compact", compact=True),
        _build_wall_placement_ghost(),
    ]
    report_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_core_report.json")
    with open(report_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"[generate_wall_habitat_core] Generated {len(results)} modular core assets -> {report_path}")


if __name__ == "__main__":
    try:
        generate_all()
    except Exception:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_core_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
