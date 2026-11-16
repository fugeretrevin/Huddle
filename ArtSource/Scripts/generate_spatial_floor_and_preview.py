import bpy
import math
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import spatial_common as sc


def _build_floor_platform(name="FloorOfficePlatform", width=0.72, depth=0.48, compact=False):
    """
    Modular Floor Office Platform:
    - Pivot at bottom center (0, 0, 0) [FloorContact].
    - Light, architectural miniature plinth with cushioned feet, beveled cream ceramic rim,
      warm wood inset deck, recessed slate agent navigation paths, subtle embedded status
      light rail, low side/back protective edge rails, front nameplate mark, and courier bay.
    - Front faces +Y in Blender (+Z in Unity toward user).
    """
    sc.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    deck_z = 0.022 if not compact else 0.020
    pad_h = 0.005
    slab_h = deck_z - pad_h  # 0.017m thick floating deck above 5mm pads
    slab_cz = pad_h + slab_h * 0.5
    corner_r = 0.055 if not compact else 0.045

    # 1. Four cushioned leveling pads (bottom flush at Z=0.0)
    fx = width * 0.5 - 0.055
    fy = depth * 0.5 - 0.050
    for idx, (px, py) in enumerate([(-fx, -fy), (fx, -fy), (-fx, fy), (fx, fy)]):
        sc.make_cylinder(f"{name}_FootPad_{idx+1}", 0.022, pad_h, (px, py, pad_h * 0.5),
                         "Mat_CharcoalVisor", vertices=20, bevel_caps=0.001, parent=root)
        sc.make_cylinder(f"{name}_FootCollar_{idx+1}", 0.018, 0.003, (px, py, pad_h + 0.001),
                         "Mat_Brass", vertices=20, parent=root)

    # 2. Lower shadow-gap chassis skirt (recessed slightly so the platform floats lightly over the floor)
    sc.make_rounded_slab(f"{name}_ChassisSkirt", width - 0.028, depth - 0.028, 0.006,
                         corner_r * 0.85, (0, 0, pad_h + 0.003), "Mat_CharcoalVisor",
                         bevel=0.0012, parent=root)

    # 3. Main rounded beveled CreamCeramic outer platform body
    sc.make_rounded_slab(f"{name}_BaseShell", width, depth, slab_h,
                         corner_r, (0, 0, slab_cz), "Mat_CreamCeramic",
                         bevel=0.0035, corner_segs=5, parent=root)

    # 4. Warm Wood inset studio floor deck (flush with top at deck_z)
    rim = 0.021 if compact else 0.014
    inlay_w = width - rim * 2.0
    inlay_d = depth - rim * 2.0
    sc.make_rounded_slab(f"{name}_WoodDeck", inlay_w, inlay_d, 0.003,
                         corner_r * 0.78, (0, 0, deck_z - 0.001), "Mat_WarmWood",
                         bevel=0.0008, corner_segs=4, parent=root)

    # 5. Subtle embedded perimeter status light strip (front & side arc, runtime MPB controllable)
    status_parts = []
    strip_w = width * 0.62
    sc.make_beveled_box(f"{name}_StatusLightStrip_Front", (strip_w, 0.0055, 0.0045),
                        (0, depth * 0.5 - 0.0045, slab_cz + 0.002), "Mat_Researcher_Teal",
                        bevel_width=0.0008, parent=root, parts_list=status_parts)
    for side_lbl, sx in [("R", -1.0), ("L", 1.0)]:
        sc.make_beveled_box(f"{name}_StatusLightStrip_{side_lbl}", (0.0055, depth * 0.44, 0.0045),
                            (sx * (width * 0.5 - 0.0045), 0.0, slab_cz + 0.002), "Mat_Researcher_Teal",
                            bevel_width=0.0008, parent=root, parts_list=status_parts)
    sc.join_static_parts(status_parts, f"{name}_StatusStrip", parent=root)

    # 6. Recessed FeltSlate agent navigation walkways linking the 3 stations & courier bay
    # Blender +X = Unity -X (User's Right: Research), Blender -X = Unity +X (User's Left: Tester)
    # Full layout matches MainMRScene V2 station centers (scale = 1.3529412, holder z = +0.135)
    if compact:
        res_bx, res_by =  0.172, -0.045
        dev_bx, dev_by =  0.000, -0.060
        tst_bx, tst_by = -0.172, -0.045
        fac_y = 0.070
        disp_bx, disp_y = 0.172 * 0.98, 0.105
        beacon_bx = -0.172 * 1.18
        nav_w = 0.172 * 2.05
        nav_y = 0.020
    else:
        res_bx, res_by = -0.2676,  0.0222   # Unity (-0.362, +0.165)
        dev_bx, dev_by =  0.0776, -0.0074   # Unity (+0.105, +0.125)
        tst_bx, tst_by =  0.2691, -0.0443   # Unity (+0.364, +0.075)
        fac_y = 0.085
        disp_bx, disp_y = -0.1478, 0.1441   # Unity (-0.200, +0.330)
        beacon_bx = 0.215 * 1.18
        nav_w = 0.555
        nav_y = -0.055                      # Clear behind Researcher/Developer/Tester desks

    path_z = deck_z + 0.0006
    # Spine path across stations
    sc.make_rounded_slab(f"{name}_NavPath_Main", nav_w, 0.042, 0.0014, 0.018,
                         (0.0, nav_y, path_z), "Mat_FeltSlate", bevel=0.0003, parent=root)
    # Branch path to Dispatch Station
    sc.make_rounded_slab(f"{name}_NavPath_Dispatch", 0.036, 0.130, 0.0014, 0.015,
                         (disp_bx, 0.095, path_z), "Mat_FeltSlate", bevel=0.0003, parent=root)

    # 7. Modular Workstation Pad Markers (centered under each V2 station desk + agent)
    pad_specs = [
        ("ResearchPad",  res_bx, res_by, 0.206 if not compact else 0.145, 0.155 if not compact else 0.100, "Mat_Researcher_Lavender"),
        ("DeveloperPad", dev_bx, dev_by, 0.190 if not compact else 0.145, 0.148 if not compact else 0.100, "Mat_Developer_Coral"),
        ("TesterPad",    tst_bx, tst_by, 0.190 if not compact else 0.145, 0.148 if not compact else 0.100, "Mat_Tester_Mint"),
    ]
    for pad_lbl, px, py, pad_w, pad_d, mat_pad in pad_specs:
        sc.make_rounded_slab(f"{name}_{pad_lbl}_Rim", pad_w, pad_d, 0.0022, 0.018,
                             (px, py, deck_z + 0.0005), mat_pad, bevel=0.0005, parent=root)
        sc.make_rounded_slab(f"{name}_{pad_lbl}_Mat", pad_w - 0.010, pad_d - 0.010, 0.0026, 0.014,
                             (px, py, deck_z + 0.0007), "Mat_FeltSlate", bevel=0.0004, parent=root)

    # 8. Integrated Dispatch Bay Pad + Subtle Flush Beacon Emitting Medallion (no bulky orphan puck when beacon hovers)
    sc.make_cylinder(f"{name}_DispatchBayRing", 0.044 if not compact else 0.048, 0.0028,
                     (disp_bx, disp_y, deck_z + 0.0010), "Mat_Brass", vertices=24, parent=root)
    sc.make_cylinder(f"{name}_BeaconInlayRing", 0.024, 0.0016,
                     (beacon_bx, disp_y + 0.010, deck_z + 0.0006), "Mat_Brass", vertices=20, parent=root)
    sc.make_cylinder(f"{name}_BeaconInlayCore", 0.018, 0.0018,
                     (beacon_bx, disp_y + 0.010, deck_z + 0.0008), "Mat_FeltSlate", vertices=18, parent=root)

    # 9. Low Sculpted Back & Side Corner Railings (keep front +Y completely open and clear of V2 desks/paths)
    rail_z = deck_z + 0.018
    post_h = 0.022
    back_ry = -depth * 0.455 if not compact else -depth * 0.44
    side_rx = width * 0.474 if not compact else width * 0.45
    side_end_y = -depth * 0.215 if not compact else -depth * 0.10
    rail_posts = [
        (-side_rx * 0.96, back_ry + 0.005),
        (-width * 0.16,   back_ry),
        ( width * 0.16,   back_ry),
        ( side_rx * 0.96, back_ry + 0.005),
        (-side_rx, side_end_y),
        ( side_rx, side_end_y),
    ]
    for i, (rx, ry) in enumerate(rail_posts):
        sc.make_cylinder(f"{name}_RailPost_{i+1}", 0.0042, post_h,
                         (rx, ry, deck_z + post_h * 0.5), "Mat_BrushedMetal", vertices=12, parent=root)
        sc.make_sphere(f"{name}_RailCap_{i+1}", (0.0052, 0.0052, 0.0052),
                       (rx, ry, deck_z + post_h), "Mat_Brass", segments=12, rings=8, parent=root)

    # Back top rail bar
    sc.make_rounded_slab(f"{name}_BackRailBar", side_rx * 1.92, 0.006, 0.005, 0.0025,
                         (0.0, back_ry, rail_z), "Mat_WarmWood", bevel=0.001, parent=root)
    # Left & Right short side rail bars (kept behind outer desk front edges)
    side_bar_len = abs(back_ry - side_end_y)
    side_bar_cy = 0.5 * (back_ry + side_end_y)
    for side_lbl, sx in [("R", -1.0), ("L", 1.0)]:
        sc.make_rounded_slab(f"{name}_SideRailBar_{side_lbl}", 0.006, side_bar_len, 0.005, 0.0025,
                             (sx * side_rx, side_bar_cy, rail_z), "Mat_WarmWood", bevel=0.001, parent=root)

    # 10. Front-Center Product Nameplate Mark + Minimal Decorative Planters on Back Corners
    sc.make_beveled_box(f"{name}_BrandPlaqueFrame", (0.110, 0.012, 0.011),
                        (0.0, depth * 0.5 - 0.002, slab_cz + 0.001), "Mat_Brass",
                        bevel_width=0.0015, rot_deg=(-18, 0, 0), parent=root)
    sc.make_beveled_box(f"{name}_BrandPlaqueFace", (0.098, 0.013, 0.007),
                        (0.0, depth * 0.5 - 0.0015, slab_cz + 0.001), "Mat_CharcoalVisor",
                        bevel_width=0.0008, rot_deg=(-18, 0, 0), parent=root)

    # Tiny cozy corner succulent pots at far back corners (out of navigation paths)
    for p_lbl, px in [("Planter_L", width * 0.42), ("Planter_R", -width * 0.42)]:
        py = -depth * 0.39
        sc.make_cone(f"{name}_{p_lbl}_Pot", 0.009, 0.012, 0.014, (px, py, deck_z + 0.007),
                     "Mat_CreamCeramic", vertices=14, parent=root)
        sc.make_sphere(f"{name}_{p_lbl}_Plant", (0.011, 0.011, 0.009), (px, py, deck_z + 0.018),
                       "Mat_Tester_Mint", segments=12, rings=8, parent=root)

    # 11. Required Sockets (exact names from specification)
    sc.make_anchor("FloorContact",                  (0.0, 0.0, 0.0), root)
    sc.make_anchor("WorkspaceRoot",                 (0.0, 0.0, deck_z), root)
    sc.make_anchor("ResearchStationSocket",         (res_bx, res_by, deck_z), root)
    sc.make_anchor("DeveloperStationSocket",        (dev_bx, dev_by, deck_z), root)
    sc.make_anchor("TesterStationSocket",           (tst_bx, tst_by, deck_z), root)
    sc.make_anchor("FactorySocket",                 (0.0,    fac_y, deck_z), root)
    sc.make_anchor("DispatchStationSocket",         (disp_bx, disp_y, deck_z), root)
    sc.make_anchor("StatusBeaconSocket",            (beacon_bx, disp_y + 0.010, deck_z + 0.002), root)
    sc.make_anchor("MailboxSocket",                 (beacon_bx + 0.090, disp_y + 0.035, deck_z), root)
    sc.make_anchor("NavigationBounds",              (0.0, 0.0, deck_z), root)
    sc.make_anchor("InteractionPresentationSocket", (0.0, depth * 0.5 + 0.04, deck_z + 0.22), root)

    # Weld static architectural parts into 1 clean multi-material mesh (keeping StatusStrip separate!)
    static_meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name != f"{name}_StatusStrip"]
    sc.join_static_parts(static_meshes, f"{name}_StaticBody", parent=root)

    return sc.export_static_asset(
        name,
        preview_cam_pos=(-0.55, 0.85, 0.46),
        preview_target=(0.0, 0.0, 0.05),
    )


def _build_placement_ghost_floor():
    """Lightweight translucent footprint outline + directional arrow + corner brackets + confirm ring."""
    name = "PlacementGhostPreview_Floor"
    sc.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    w, d = 0.788, 0.490
    parts = []
    # Outer thin translucent ring
    sc.make_rounded_frame_outline(f"{name}_FootprintRing", w, d, 0.016, 0.006, 0.055,
                                  (0, 0, 0.004), "Mat_Spatial_Ghost", corner_segs=5, parent=root, parts_list=parts)
    # Inner subtle guide ring
    sc.make_rounded_frame_outline(f"{name}_InnerRing", w - 0.06, d - 0.06, 0.006, 0.003, 0.040,
                                  (0, 0, 0.003), "Mat_Spatial_Ghost", corner_segs=4, parent=root, parts_list=parts)
    # 4 corner L-brackets (raised slightly so it reads clearly on passthrough)
    bx, by = w * 0.5 - 0.035, d * 0.5 - 0.035
    for idx, (cx, cy, sx, sy) in enumerate([
        (-bx, -by, -1, -1), (bx, -by, 1, -1), (-bx, by, -1, 1), (bx, by, 1, 1)
    ]):
        sc.make_beveled_box(f"{name}_BracketX_{idx+1}", (0.055, 0.010, 0.014),
                            (cx - sx * 0.008, cy + sy * 0.015, 0.010), "Mat_Spatial_Ghost",
                            bevel_width=0.001, parent=root, parts_list=parts)
        sc.make_beveled_box(f"{name}_BracketY_{idx+1}", (0.010, 0.055, 0.014),
                            (cx + sx * 0.015, cy - sy * 0.008, 0.010), "Mat_Spatial_Ghost",
                            bevel_width=0.001, parent=root, parts_list=parts)

    # Forward (+Y in Blender -> +Z in Unity) facing chevron arrow pointing OUTWARD toward user
    sc.make_beveled_box(f"{name}_ArrowWing_L", (0.055, 0.012, 0.008),
                        ( 0.019, d * 0.5 + 0.042, 0.006), "Mat_Spatial_Ghost",
                        bevel_width=0.001, rot_deg=(0, 0,  40), parent=root, parts_list=parts)
    sc.make_beveled_box(f"{name}_ArrowWing_R", (0.055, 0.012, 0.008),
                        (-0.019, d * 0.5 + 0.042, 0.006), "Mat_Spatial_Ghost",
                        bevel_width=0.001, rot_deg=(0, 0, -40), parent=root, parts_list=parts)

    # Center pivot & confirmation badge ring
    sc.make_rounded_frame_outline(f"{name}_CenterReticle", 0.09, 0.09, 0.008, 0.004, 0.044,
                                  (0, 0, 0.004), "Mat_Spatial_Ghost", corner_segs=6, parent=root, parts_list=parts)

    sc.join_static_parts(parts, f"{name}_Mesh", parent=root)

    sc.make_anchor("FloorContact", (0, 0, 0), root)
    sc.make_anchor("ConfirmationBadgeSocket", (0, d * 0.5 + 0.08, 0.08), root)
    sc.make_anchor("FacingDirection", (0, d * 0.5 + 0.12, 0.0), root)

    return sc.export_static_asset(
        name,
        preview_cam_pos=(-0.50, 0.80, 0.45),
        preview_target=(0.0, 0.02, 0.02),
    )


def _build_placement_ghost_ceiling():
    """Lightweight ceiling-mount ghost preview: top ceiling ring at Z=0 + faint drop-pod outline below."""
    name = "PlacementGhostPreview_Ceiling"
    sc.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    parts = []
    # Top ceiling-contact mount outline (flush at Z=0, extending downward in -Z)
    sc.make_rounded_frame_outline(f"{name}_CeilingRing", 0.30, 0.30, 0.014, 0.008, 0.145,
                                  (0, 0, -0.005), "Mat_Spatial_Ghost", corner_segs=8, parent=root, parts_list=parts)
    sc.make_rounded_frame_outline(f"{name}_CeilingInner", 0.14, 0.14, 0.008, 0.006, 0.068,
                                  (0, 0, -0.006), "Mat_Spatial_Ghost", corner_segs=6, parent=root, parts_list=parts)

    # Subtle suspended pod silhouette outline at docked height (-0.14m below ceiling)
    sc.make_rounded_frame_outline(f"{name}_PodSilhouette", 0.728, 0.435, 0.012, 0.006, 0.110,
                                  (0, 0.0125, -0.140), "Mat_Spatial_Ghost", corner_segs=6, parent=root, parts_list=parts)

    # Four thin vertical guide rods showing the docked envelope
    for idx, (vx, vy) in enumerate([(-0.12, -0.08), (0.12, -0.08), (-0.12, 0.08), (0.12, 0.08)]):
        sc.make_cylinder(f"{name}_GuideRod_{idx+1}", 0.0025, 0.130, (vx, vy, -0.072),
                         "Mat_Spatial_Ghost", vertices=8, parent=root, parts_list=parts)

    # Forward (+Y in Blender -> +Z in Unity) facing chevron pointing OUTWARD toward user
    sc.make_beveled_box(f"{name}_ArrowWing_L", (0.048, 0.010, 0.007),
                        ( 0.016, 0.19 + 0.036, -0.140), "Mat_Spatial_Ghost",
                        bevel_width=0.001, rot_deg=(0, 0,  40), parent=root, parts_list=parts)
    sc.make_beveled_box(f"{name}_ArrowWing_R", (0.048, 0.010, 0.007),
                        (-0.016, 0.19 + 0.036, -0.140), "Mat_Spatial_Ghost",
                        bevel_width=0.001, rot_deg=(0, 0, -40), parent=root, parts_list=parts)

    sc.join_static_parts(parts, f"{name}_Mesh", parent=root)

    sc.make_anchor("CeilingContact", (0, 0, 0), root)
    sc.make_anchor("ConfirmationBadgeSocket", (0, 0.24, -0.14), root)
    sc.make_anchor("FacingDirection", (0, 0.28, -0.14), root)

    return sc.export_static_asset(
        name,
        preview_cam_pos=(-0.50, 0.78, -0.28),
        preview_target=(0.0, 0.0, -0.08),
    )


def build_all():
    report = [
        _build_floor_platform("FloorOfficePlatform", width=0.788, depth=0.490, compact=False),
        _build_floor_platform("FloorOfficePlatform_Compact", width=0.58, depth=0.40, compact=True),
        _build_placement_ghost_floor(),
        _build_placement_ghost_ceiling(),
    ]
    out_json = os.path.join(sc.PREVIEW_SPATIAL_DIR, "floor_and_preview_report.json")
    with open(out_json, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[SpatialFloor] Built {len(report)} assets -> {out_json}")


if __name__ == "__main__":
    build_all()
