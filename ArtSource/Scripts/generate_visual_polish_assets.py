import bpy
import bmesh
import math
import mathutils
import os
import sys
import json

# ==============================================================================
# MuseDesk — Focused Visual Art, Interaction Affordances & Environment Polish
# Procedural Blender Asset & Optimization Generator
#
# Generates:
#   1. Priority 1: Redesigned Physical Decision Paper (Small & Expanded Frame)
#      - Doc_DecisionPaper_Small.fbx & Doc_SmallPaper.fbx
#      - Doc_DecisionPaper_Expanded.fbx & Doc_ExpandedFrame.fbx
#   2. Priority 2: Physical Task-Assignment Affordances
#      - Affordance_AgentHaloRing.fbx
#      - Affordance_DeskReceivePad.fbx
#      - Affordance_CardSocket.fbx
#      - Affordance_StatePuck.fbx
#   3. Priority 3: Task Board & Task Card Readability Polish
#      - TaskBoard_KanbanFrame.fbx (with dedicated top title pediment & button deck)
#      - TaskCard_Grabbable.fbx (corner indicators tucked into bezel, clean text area)
#   4. Priority 4: Interaction Feedback Polish (Single-Mesh 3D Poke Keys & Stamps)
#      - UI_PokeKey_Housing.fbx (1 combined mesh replacing 5 primitive cubes)
#      - UI_PokeKey_Cap.fbx (1 beveled tactile keycap mesh)
#      - Doc_Stamp_Approve.fbx, Doc_Stamp_Reject.fbx, Doc_ApprovalStamp.fbx, Doc_StampRestPad.fbx
#   5. Priority 5 & 7: Workstation Status Beacon & Optional Mascot Feedback Assets
#      - Workstation_StatusBeacon.fbx
#      - Feedback_Listening.fbx, Feedback_Speaking.fbx, Feedback_Thinking.fbx,
#        Feedback_NeedsAttention.fbx, Feedback_Error.fbx, Feedback_Success.fbx
#   6. Priority 8: Reversible Art Performance Pass (Material-Welded Optimized Variants)
#      - Workstation_Research_Optimized.fbx
#      - Workstation_Developer_Optimized.fbx
#      - Workstation_Testing_Optimized.fbx
#      - TaskBoard_KanbanFrame_Optimized.fbx
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
SCRIPTS_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Scripts")
if SCRIPTS_DIR not in sys.path:
    sys.path.append(SCRIPTS_DIR)

BLEND_ROOT = os.path.join(PROJECT_ROOT, "ArtSource", "Blender")
FBX_ROOT = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "VisualPolish")

DIRS = {
    "Documents":   (os.path.join(BLEND_ROOT, "Documents"),   os.path.join(FBX_ROOT, "Documents")),
    "Affordances": (os.path.join(BLEND_ROOT, "Affordances"), os.path.join(FBX_ROOT, "Affordances")),
    "TaskBoard":   (os.path.join(BLEND_ROOT, "TaskBoard"),   os.path.join(FBX_ROOT, "TaskBoard")),
    "UI":          (os.path.join(BLEND_ROOT, "UI"),          os.path.join(FBX_ROOT, "UI")),
    "Feedback":    (os.path.join(BLEND_ROOT, "Feedback"),    os.path.join(FBX_ROOT, "Feedback")),
    "Optimized":   (os.path.join(BLEND_ROOT, "Optimized"),   os.path.join(FBX_ROOT, "Optimized")),
}

for bdir, fdir in DIRS.values():
    os.makedirs(bdir, exist_ok=True)
    os.makedirs(fdir, exist_ok=True)
os.makedirs(PREVIEW_DIR, exist_ok=True)


def srgb_to_linear(c):
    res = []
    for ch in c[:3]:
        res.append(ch / 12.92 if ch <= 0.04045 else ((ch + 0.055) / 1.055) ** 2.4)
    res.append(c[3] if len(c) > 3 else 1.0)
    return tuple(res)


PALETTE = {
    "Mat_WarmWood":            {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.46, "metallic": 0.0},
    "Mat_DarkWood":            {"color": (0.420, 0.295, 0.205, 1.0), "roughness": 0.50, "metallic": 0.0},
    "Mat_CreamCeramic":        {"color": (0.965, 0.950, 0.915, 1.0), "roughness": 0.30, "metallic": 0.0},
    "Mat_WarmBeige":           {"color": (0.910, 0.875, 0.815, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_CharcoalVisor":       {"color": (0.130, 0.145, 0.180, 1.0), "roughness": 0.24, "metallic": 0.12},
    "Mat_BrushedMetal":        {"color": (0.660, 0.695, 0.735, 1.0), "roughness": 0.28, "metallic": 0.68},
    "Mat_Brass":               {"color": (0.860, 0.700, 0.290, 1.0), "roughness": 0.24, "metallic": 0.80},
    "Mat_PaperWarm":           {"color": (0.988, 0.974, 0.940, 1.0), "roughness": 0.52, "metallic": 0.0},
    "Mat_PaperManila":         {"color": (0.935, 0.860, 0.700, 1.0), "roughness": 0.52, "metallic": 0.0},
    "Mat_BlueprintBlue":       {"color": (0.180, 0.390, 0.615, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_FeltSlate":           {"color": (0.250, 0.280, 0.335, 1.0), "roughness": 0.68, "metallic": 0.0},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.38, "metallic": 0.0},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.28, "metallic": 0.05, "emission": (0.28, 0.74, 0.68), "emission_strength": 0.45},
    "Mat_Developer_Coral":     {"color": (0.960, 0.440, 0.350, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Developer_Amber":     {"color": (1.000, 0.800, 0.310, 1.0), "roughness": 0.25, "metallic": 0.08, "emission": (1.0, 0.80, 0.31), "emission_strength": 0.65},
    "Mat_Tester_Mint":         {"color": (0.300, 0.780, 0.580, 1.0), "roughness": 0.34, "metallic": 0.0},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.505, 0.665, 1.0), "roughness": 0.34, "metallic": 0.15},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.22, 0.84, 0.45), "emission_strength": 0.95},
    "Mat_StatusRed":           {"color": (0.910, 0.250, 0.230, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.91, 0.25, 0.23), "emission_strength": 0.95},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.98, 0.68, 0.22), "emission_strength": 0.95},
    "Mat_StatusCyan":          {"color": (0.250, 0.820, 0.950, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.25, 0.82, 0.95), "emission_strength": 0.95},
    "Mat_ScreenCode":          {"color": (0.120, 0.250, 0.380, 1.0), "roughness": 0.14, "metallic": 0.0, "emission": (0.18, 0.56, 0.86), "emission_strength": 0.85},
    "Mat_LampGlow":            {"color": (1.000, 0.950, 0.800, 1.0), "roughness": 0.15, "metallic": 0.0, "emission": (1.00, 0.94, 0.76), "emission_strength": 1.35},
}


def get_mat(name):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    spec = PALETTE.get(name, PALETTE["Mat_PaperWarm"])
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    lin = srgb_to_linear(spec["color"])
    mat.diffuse_color = lin
    if bsdf:
        bsdf.inputs["Base Color"].default_value = lin
        bsdf.inputs["Roughness"].default_value = spec["roughness"]
        bsdf.inputs["Metallic"].default_value = spec["metallic"]
        if spec.get("emission"):
            em_lin = srgb_to_linear(list(spec["emission"]) + [1.0])
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = em_lin
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = spec.get("emission_strength", 1.0)
    return mat


def make_box(name, size_xyz, location, mat_name, bevel_width=0.002, segments=2, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size_xyz
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel_width > 0:
        bev = obj.modifiers.new("Bevel", 'BEVEL')
        bev.width = bevel_width
        bev.segments = segments
        bpy.ops.object.modifier_apply(modifier="Bevel")
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_cyl(name, radius, depth, location, mat_name, vertices=20, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=location,
        rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_cone(name, r1, r2, depth, location, mat_name, vertices=18, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=r1, radius2=r2, depth=depth,
        location=location,
        rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_sphere(name, radius_xyz, location, mat_name, segments=16, rings=10, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=1.0,
        location=location,
        rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = radius_xyz
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_torus(name, maj_r, min_r, location, mat_name, maj_seg=24, min_seg=10, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=maj_seg, minor_segments=min_seg,
        major_radius=maj_r, minor_radius=min_r,
        location=location,
        rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_anchor(name, location, parent, rot_deg=(0, 0, 0)):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.015
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


def join_children_by_material(root, keep_separate_names=None, prefix="Mesh"):
    """
    Combines child mesh objects under `root` that share a single material into one mesh per material,
    skipping any object whose name is in `keep_separate_names`.
    """
    if keep_separate_names is None:
        keep_separate_names = set()
    else:
        keep_separate_names = set(keep_separate_names)

    groups = {}
    for obj in list(root.children):
        if obj.type != 'MESH':
            continue
        if obj.name in keep_separate_names or any(obj.name.startswith(p) for p in keep_separate_names):
            continue
        if len(obj.data.materials) == 1 and obj.data.materials[0] is not None:
            mname = obj.data.materials[0].name
            groups.setdefault(mname, []).append(obj)

    for mname, objs in groups.items():
        if len(objs) <= 1:
            if len(objs) == 1:
                objs[0].name = f"{prefix}_{mname.replace('Mat_', '')}"
            continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        merged = bpy.context.active_object
        merged.name = f"{prefix}_{mname.replace('Mat_', '')}"
        merged.parent = root


# ==============================================================================
# PRIORITY 1 — DECISION PAPER REDESIGN (SMALL & EXPANDED FOLIO)
# ==============================================================================

def build_decision_paper_small(root_name="Doc_DecisionPaper_Small"):
    """
    Compact handheld decision summary card (9.2 cm wide x 12.6 cm tall x 0.4 cm thick).
    Features:
      - Rounded dark-wood miniature folio backing + warm cream high-contrast paper sheet
      - Top charcoal/brass provenance header band
      - Distinct Summary panel & Recommendation pill bar
      - Dedicated lower-right Stamp Target seal ring (never overlapping action area)
      - Ergonomic right-side thumb grab tab with brass grip ridge
      - Unbaked dynamic text anchors
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    # Note on coordinate convention:
    # In Blender (-Y forward -> Unity -Z front face, +Z up -> Unity +Y up):
    # Width along X, Thickness along Y (-Y is front toward user), Height along Z.
    w, h = 0.092, 0.126
    make_box("SmallPaper_BackBoard", (w, 0.0035, h), (0, 0.001, 0), "Mat_DarkWood", bevel_width=0.002, parent=root)
    make_box("SmallPaper_Sheet", (w - 0.006, 0.0025, h - 0.006), (0, -0.0012, 0), "Mat_PaperWarm", bevel_width=0.0012, parent=root)

    # Top miniature brass clipboard clip
    make_box("SmallPaper_TopClip", (0.036, 0.0055, 0.009), (0, -0.002, h * 0.5 + 0.001), "Mat_Brass", bevel_width=0.0015, parent=root)

    # Ergonomic side thumb-grab tab on right edge (-X in Blender -> +X right side in Unity)
    make_box("SmallPaper_GrabTab", (0.012, 0.0035, 0.034), (-w * 0.5 - 0.004, 0.000, -0.010), "Mat_WarmWood", bevel_width=0.0015, parent=root)
    make_box("SmallPaper_GrabTabPip", (0.004, 0.0045, 0.022), (-w * 0.5 - 0.005, -0.0005, -0.010), "Mat_Brass", bevel_width=0.0008, parent=root)

    # Provenance / Priority Header Band
    make_box("SmallPaper_HeaderBand", (w - 0.012, 0.002, 0.014), (0, -0.0022, 0.046), "Mat_CharcoalVisor", bevel_width=0.0008, parent=root)

    # Summary Inset Panel (high-contrast cream ceramic)
    make_box("SmallPaper_SummaryCard", (w - 0.012, 0.0018, 0.056), (0, -0.0021, 0.006), "Mat_CreamCeramic", bevel_width=0.0008, parent=root)

    # Recommendation Pill Strip
    make_box("SmallPaper_RecoPill", (0.054, 0.0020, 0.011), (0.012, -0.0023, -0.030), "Mat_WarmBeige", bevel_width=0.0008, parent=root)

    # Dedicated Stamp Target Ring (bottom-right, separated from text)
    make_cyl("SmallPaper_StampPad", 0.013, 0.0020, (-0.024, -0.0022, -0.040), "Mat_WarmBeige", vertices=24, rot_deg=(90, 0, 0), parent=root)
    make_torus("SmallPaper_StampRing", 0.013, 0.0012, (-0.024, -0.0028, -0.040), "Mat_Brass", maj_seg=24, min_seg=8, rot_deg=(90, 0, 0), parent=root)

    # Weld static meshes by material to keep renderer count minimal (5 renderers total)
    join_children_by_material(root, prefix="SmallPaper")

    # Unbaked Dynamic Text & Interaction Anchors
    make_anchor("Content_Surface_Anchor",     ( 0.000, -0.0035,  0.006), root)
    make_anchor("Header_Text_Anchor",         ( 0.000, -0.0038,  0.046), root)
    make_anchor("Title_Anchor",               ( 0.000, -0.0038,  0.022), root)
    make_anchor("Summary_Text_Anchor",        ( 0.000, -0.0038,  0.000), root)
    make_anchor("Recommendation_Text_Anchor", ( 0.012, -0.0038, -0.030), root)
    make_anchor("Stamp_Target_Anchor",        (-0.024, -0.0038, -0.040), root)
    make_anchor("Grab_Pivot_Anchor",          (-w * 0.5 - 0.004, 0.000, -0.010), root)
    return root


def build_decision_paper_expanded(root_name="Doc_DecisionPaper_Expanded"):
    """
    Expanded World-Space Decision Folio (32.0 cm wide x 30.0 cm tall x 1.2 cm thick).
    Explicitly solves every issue in REMAINING_ISSUES.md #56 and Section 2:
      1. High-contrast warm ivory reading surface (`Mat_PaperWarm` & `Mat_CreamCeramic`) for dark charcoal text.
      2. Distinct visual regions:
         - Top Provenance/Priority Header Bar (`z = +0.118`)
         - Upper Summary & Recommendation Region (`z = +0.054`)
         - Lower-Left Details Region (`z = -0.030, x = +0.044` in Blender -> left side in Unity)
         - Lower-Right Dedicated Stamp Target Seal Pad (`z = -0.030, x = -0.096` in Blender -> right side in Unity),
           completely separated from both the Details text and the Action buttons!
      3. Bottom Single-Row Action Deck (`z = -0.114`) inside the lower frame bezel (`bottom = -0.150`),
         housing all 4 poke buttons (`Approve`, `Ask why`, `Details`, `Reject`) in one clean horizontal row
         so `Details` never hangs below the paper's edge!
      4. Left/Right ergonomic grab handle bars + top brass clipboard clasp.
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    fw, fh = 0.320, 0.300
    # 1. Rounded Dark Walnut Folio Backing Frame
    make_box("Folio_Board", (fw, 0.010, fh), (0, 0.003, 0), "Mat_DarkWood", bevel_width=0.004, parent=root)
    # Warm wood inner bezel trim
    make_box("Folio_InnerTrim", (fw - 0.010, 0.008, fh - 0.010), (0, 0.000, 0), "Mat_WarmWood", bevel_width=0.003, parent=root)

    # Top Brass Clipboard Clasp + Side Hand-Grab Rails
    make_box("Folio_TopBrassClip", (0.110, 0.014, 0.016), (0, -0.003, fh * 0.5 + 0.002), "Mat_Brass", bevel_width=0.003, parent=root)
    for side, sx in [("L", 0.164), ("R", -0.164)]:
        make_box(f"Folio_GrabTab_{side}", (0.012, 0.008, 0.076), (sx, 0.000, 0.010), "Mat_WarmWood", bevel_width=0.0025, parent=root)
        make_cyl(f"Folio_GrabGrip_{side}", 0.0035, 0.056, (sx * 1.02, -0.002, 0.010), "Mat_Brass", vertices=14, parent=root)

    # 2. High-Contrast Main Paper Sheet (covers upper & middle folio: z = -0.078 to +0.138)
    make_box("Folio_PaperSheet", (0.294, 0.004, 0.218), (0, -0.0035, 0.030), "Mat_PaperWarm", bevel_width=0.0015, parent=root)

    # 3. Region 1: Top Provenance & Priority Header Bar (z = +0.118)
    make_box("Region_HeaderBar", (0.280, 0.003, 0.024), (0, -0.0052, 0.118), "Mat_CharcoalVisor", bevel_width=0.001, parent=root)
    make_box("Region_HeaderRule", (0.280, 0.0032, 0.0025), (0, -0.0055, 0.104), "Mat_Brass", bevel_width=0.0005, parent=root)

    # 4. Region 2: Summary & Recommendation Card (z = +0.054)
    make_box("Region_SummaryCard", (0.280, 0.0025, 0.082), (0, -0.0050, 0.056), "Mat_CreamCeramic", bevel_width=0.0012, parent=root)
    # Subtle recommendation pill highlight inside summary bottom
    make_box("Region_RecoStrip", (0.160, 0.0028, 0.016), (0, -0.0054, 0.024), "Mat_WarmBeige", bevel_width=0.001, parent=root)
    make_box("Region_SectionDivider", (0.276, 0.0028, 0.002), (0, -0.0054, 0.011), "Mat_WarmWood", bevel_width=0.0004, parent=root)

    # 5. Region 3: Details & Rationale Panel (Left 68%: x = -0.044 in Blender -> -0.044 screen-left in Unity, width = 0.190, z = -0.032)
    make_box("Region_DetailsCard", (0.190, 0.0025, 0.078), (-0.044, -0.0050, -0.032), "Mat_WarmBeige", bevel_width=0.0012, parent=root)
    make_box("Region_DetailsHeaderStrip", (0.186, 0.0028, 0.004), (-0.044, -0.0054, 0.004), "Mat_Researcher_Lavender", bevel_width=0.0005, parent=root)

    # 6. Region 4: Dedicated Stamp Target Zone (Right 30%: x = +0.096 in Blender -> +0.096 screen-right in Unity, z = -0.032)
    # Completely separated from Details text and well above the Action button row!
    sx, sz = 0.096, -0.032
    make_box("Region_StampBox", (0.082, 0.0025, 0.078), (sx, -0.0050, sz), "Mat_CreamCeramic", bevel_width=0.0012, parent=root)
    make_cyl("StampTarget_Disc", 0.031, 0.0028, (sx, -0.0055, sz), "Mat_WarmBeige", vertices=28, rot_deg=(90, 0, 0), parent=root)
    make_torus("StampTarget_OuterRing", 0.031, 0.0018, (sx, -0.0062, sz), "Mat_Brass", maj_seg=28, min_seg=10, rot_deg=(90, 0, 0), parent=root)
    make_torus("StampTarget_InnerRing", 0.023, 0.0010, (sx, -0.0060, sz), "Mat_Developer_Amber", maj_seg=24, min_seg=8, rot_deg=(90, 0, 0), parent=root)

    # 7. Region 5: Bottom Action Button Deck (z = -0.114, inside the frame bottom at -0.150)
    make_box("Region_ActionDeck", (0.294, 0.005, 0.056), (0, -0.0030, -0.114), "Mat_FeltSlate", bevel_width=0.002, parent=root)
    make_box("Region_ActionTopRule", (0.294, 0.0055, 0.003), (0, -0.0040, -0.084), "Mat_Brass", bevel_width=0.0006, parent=root)

    # 4 recessed socket cradles for the 4 single-row buttons (Approve, Ask why, Details, Reject)
    btn_xs = [-0.105, -0.035, 0.035, 0.105]
    btn_names = ["Approve", "AskWhy", "Details", "Reject"]
    for bx, bname in zip(btn_xs, btn_names):
        make_box(f"ActionSocket_{bname}", (0.060, 0.004, 0.042), (bx, -0.0045, -0.114), "Mat_CharcoalVisor", bevel_width=0.0015, parent=root)

    # Join static parts by material so the entire expanded folio is only ~8 renderers!
    join_children_by_material(root, prefix="Folio")

    # Named Dynamic Text & Interaction Anchors (Backwards-compatible + New Region Anchors)
    make_anchor("Content_Surface_Anchor",     ( 0.000, -0.007,  0.030), root)
    make_anchor("Header_Text_Anchor",         ( 0.000, -0.007,  0.118), root)
    make_anchor("Status_Text_Anchor",         ( 0.000, -0.007,  0.136), root)
    make_anchor("Title_Text_Anchor",          ( 0.000, -0.007,  0.080), root)
    make_anchor("Summary_Text_Anchor",        ( 0.000, -0.007,  0.052), root)
    make_anchor("Recommendation_Text_Anchor", ( 0.000, -0.007,  0.024), root)
    make_anchor("Details_Text_Anchor",        (-0.044, -0.007, -0.024), root)
    make_anchor("Answer_Text_Anchor",         (-0.044, -0.007, -0.058), root)
    make_anchor("Stamp_Target_Anchor",        (-0.096, -0.007, -0.032), root)
    make_anchor("ActionRow_Anchor",           ( 0.000, -0.007, -0.114), root)
    make_anchor("Btn_Approve_Anchor",         ( 0.105, -0.008, -0.114), root)
    make_anchor("Btn_AskWhy_Anchor",          ( 0.035, -0.008, -0.114), root)
    make_anchor("Btn_Details_Anchor",         (-0.035, -0.008, -0.114), root)
    make_anchor("Btn_Reject_Anchor",          (-0.105, -0.008, -0.114), root)
    make_anchor("Btn_Defer_Anchor",           ( 0.035, -0.008, -0.114), root)
    make_anchor("Grab_Handle_Left_Anchor",    ( 0.164,  0.000,  0.010), root)
    make_anchor("Grab_Handle_Right_Anchor",   (-0.164,  0.000,  0.010), root)
    return root


# ==============================================================================
# PRIORITY 2 — PHYSICAL TASK-ASSIGNMENT AFFORDANCES
# ==============================================================================

def build_affordance_assets():
    assets = {}

    # 1. Affordance_AgentHaloRing (Floor ring under each agent / workstation for StationDropZone.halo)
    def make_agent_halo():
        root = bpy.data.objects.new("Affordance_AgentHaloRing", None)
        bpy.context.collection.objects.link(root)
        # Outer chamfered ring (radius 12.5 cm) + inner subtle glow ring + 4 inward directional chevrons
        make_torus("Halo_OuterRing", 0.125, 0.0045, (0, 0, 0.0045), "Mat_StatusGreen", maj_seg=36, min_seg=10, parent=root)
        make_torus("Halo_InnerRing", 0.098, 0.0025, (0, 0, 0.0030), "Mat_CreamCeramic", maj_seg=32, min_seg=8, parent=root)
        for angle_deg in [0, 90, 180, 270]:
            rad = math.radians(angle_deg)
            cx = math.cos(rad) * 0.112
            cy = math.sin(rad) * 0.112
            make_cone(
                f"Halo_Chevron_{angle_deg}", 0.009, 0.001, 0.014,
                (cx, cy, 0.005), "Mat_StatusGreen", vertices=4,
                rot_deg=(90, 0, angle_deg - 90), parent=root
            )
        join_children_by_material(root, prefix="Halo")
        make_anchor("Agent_Center_Anchor", (0, 0, 0), root)
        return root

    # 2. Affordance_DeskReceivePad (Miniature task-card receiving pad on workstation desk)
    def make_desk_receive_pad():
        root = bpy.data.objects.new("Affordance_DeskReceivePad", None)
        bpy.context.collection.objects.link(root)
        make_box("ReceivePad_Base", (0.086, 0.056, 0.005), (0, 0, 0.0025), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("ReceivePad_SlotFloor", (0.076, 0.046, 0.003), (0, 0, 0.0050), "Mat_FeltSlate", bevel_width=0.001, parent=root)
        # Brass corner guide rails
        for sx in [-0.039, 0.039]:
            make_box(f"ReceivePad_Rail_{int(sx*1000)}", (0.004, 0.044, 0.005), (sx, 0, 0.0065), "Mat_Brass", bevel_width=0.0008, parent=root)
        # Front status indicator bar (controllable renderer for Pending / Confirmed / Invalid / Completed)
        make_box("ReceivePad_StatusBar", (0.064, 0.005, 0.004), (0, -0.025, 0.0065), "Mat_StatusGreen", bevel_width=0.0008, parent=root)
        join_children_by_material(root, keep_separate_names={"ReceivePad_StatusBar"}, prefix="ReceivePad")
        make_anchor("Card_Rest_Anchor", (0, 0, 0.007), root)
        return root

    # 3. Affordance_CardSocket (Snap socket frame for board/desk card placement)
    def make_card_socket():
        root = bpy.data.objects.new("Affordance_CardSocket", None)
        bpy.context.collection.objects.link(root)
        make_box("Socket_Backplate", (0.084, 0.003, 0.052), (0, 0, 0), "Mat_FeltSlate", bevel_width=0.001, parent=root)
        make_box("Socket_BottomLedge", (0.084, 0.006, 0.004), (0, -0.002, -0.025), "Mat_Brass", bevel_width=0.0008, parent=root)
        make_box("Socket_HighlightRim", (0.086, 0.002, 0.054), (0, 0.001, 0), "Mat_StatusCyan", bevel_width=0.001, parent=root)
        make_anchor("Socket_Snap_Anchor", (0, -0.003, 0), root)
        return root

    # 4. Affordance_StatePuck (Compact assignment state badge: Pending / Confirmed / Invalid / Completed)
    def make_state_puck():
        root = bpy.data.objects.new("Affordance_StatePuck", None)
        bpy.context.collection.objects.link(root)
        make_cyl("StatePuck_Base", 0.014, 0.004, (0, 0, 0.002), "Mat_CharcoalVisor", vertices=20, parent=root)
        make_torus("StatePuck_Rim", 0.014, 0.0018, (0, 0, 0.0035), "Mat_Brass", maj_seg=20, min_seg=8, parent=root)
        make_sphere("StatePuck_GlowCore", (0.010, 0.010, 0.005), (0, 0, 0.0045), "Mat_StatusGreen", parent=root)
        return root

    assets["Affordance_AgentHaloRing"]  = make_agent_halo
    assets["Affordance_DeskReceivePad"] = make_desk_receive_pad
    assets["Affordance_CardSocket"]     = make_card_socket
    assets["Affordance_StatePuck"]     = make_state_puck
    return assets


# ==============================================================================
# PRIORITY 3 — TASK BOARD & TASK CARD READABILITY IMPROVEMENTS
# ==============================================================================

def build_polished_taskboard_assets():
    assets = {}

    def make_polished_kanban_board():
        """
        Improved TaskBoard_KanbanFrame:
          - Dedicated top title pediment plaque (`Board_TitlePlaque` at z = +0.298) so 'TASK BOARD'
            sits cleanly above the 4 column headers and NEVER overlaps 'Working' or 'Needs me'!
          - High-contrast column lanes and clean slot guides.
          - Integrated lower button console rail (`Board_ButtonConsole`) for '+ New', 'Pause', 'Priority'.
        """
        root = bpy.data.objects.new("TaskBoard_KanbanFrame", None)
        bpy.context.collection.objects.link(root)

        make_box("Board_Backing", (0.420, 0.014, 0.260), (0, 0, 0.145), "Mat_FeltSlate", bevel_width=0.004, parent=root)
        make_box("Board_TopRail", (0.432, 0.022, 0.016), (0, 0, 0.278), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_BotRail", (0.432, 0.026, 0.018), (0, 0, 0.012), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_LeftRail", (0.016, 0.022, 0.272), ( 0.210, 0, 0.145), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_RightRail", (0.016, 0.022, 0.272), (-0.210, 0, 0.145), "Mat_WarmWood", bevel_width=0.003, parent=root)

        # Dedicated Top Title Pediment Plaque (prevents 'TASK BOARD' from overlapping column headers!)
        make_box("Board_TitlePediment", (0.200, 0.018, 0.028), (0, -0.002, 0.296), "Mat_DarkWood", bevel_width=0.0025, parent=root)
        make_box("Board_TitleFace", (0.188, 0.004, 0.020), (0, -0.010, 0.296), "Mat_CharcoalVisor", bevel_width=0.001, parent=root)
        make_anchor("Board_Title_Anchor", (0, -0.014, 0.296), root)

        for side, sx in [("L", 0.165), ("R", -0.165)]:
            make_box(f"Board_StandFoot_{side}", (0.036, 0.095, 0.012), (sx, 0.005, 0.006), "Mat_DarkWood", bevel_width=0.003, parent=root)
            make_cyl(f"Board_GrabHandle_{side}", 0.005, 0.048, (sx * 1.30, 0, 0.145), "Mat_Brass", vertices=12, parent=root)

        cols = [
            ("Queued",     0.147, "Mat_Tester_SlateBlue"),
            ("InProgress", 0.049, "Mat_Developer_Coral"),
            ("Blocked",   -0.049, "Mat_StatusAmber"),
            ("Completed", -0.147, "Mat_Tester_Mint"),
        ]
        for cname, cx, cmat in cols:
            make_box(f"Header_{cname}", (0.088, 0.008, 0.026), (cx, -0.008, 0.248), cmat, bevel_width=0.002, parent=root)
            make_anchor(f"HeaderLabel_{cname}_Anchor", (cx, -0.013, 0.248), root)
            make_box(f"ColumnLane_{cname}", (0.088, 0.004, 0.198), (cx, -0.006, 0.125), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
            make_anchor(f"ColumnDropZone_{cname}", (cx, -0.012, 0.125), root)
            for row_idx in range(4):
                sz = 0.198 - row_idx * 0.048
                make_box(f"SlotGuide_{cname}_{row_idx+1}", (0.078, 0.003, 0.003), (cx, -0.008, sz - 0.018), "Mat_BrushedMetal", bevel_width=0.0005, parent=root)
                make_anchor(f"Socket_{cname}_{row_idx+1}", (cx, -0.012, sz), root)

        for dx in [0.098, 0.000, -0.098]:
            make_box(f"Divider_{int(dx*1000)}", (0.005, 0.010, 0.236), (dx, -0.006, 0.138), "Mat_WarmWood", bevel_width=0.001, parent=root)

        make_anchor("ExpandedCard_Inspect_Anchor", (0.0, -0.085, 0.165), root)
        return root

    def make_polished_task_card():
        """
        Improved TaskCard_Grabbable:
          - High-contrast warm ivory face (7.6 cm x 4.8 cm x 0.4 cm)
          - Compact top role strip + tiny corner indicators tucked into the outer 3.5 mm border
            so they NEVER clip or overlap 'Unassigned', 'P2', 'Title', or 'SCRIPTED DEMO'!
        """
        root = bpy.data.objects.new("TaskCard_Grabbable", None)
        bpy.context.collection.objects.link(root)

        make_box("Card_Body", (0.076, 0.004, 0.048), (0, 0, 0), "Mat_PaperWarm", bevel_width=0.0012, parent=root)
        make_box("Card_BorderFrame", (0.078, 0.0032, 0.050), (0, 0.0006, 0), "Mat_WarmWood", bevel_width=0.0012, parent=root)
        # Slim top role strip (leaves central text zone 100% clear)
        make_box("Card_RoleHeaderStrip", (0.072, 0.0046, 0.0065), (0, -0.0004, 0.0195), "Mat_Researcher_Lavender", bevel_width=0.0006, parent=root)
        # Compact corner indicators tucked into extreme corners
        make_cyl("Card_OwnerIndicator", 0.0030, 0.0048, (0.032, -0.0006, 0.0195), "Mat_Researcher_Teal", vertices=14, rot_deg=(90, 0, 0), parent=root)
        make_sphere("Card_PriorityIndicator", (0.0030, 0.0020, 0.0030), (-0.032, -0.0016, 0.0195), "Mat_Developer_Amber", parent=root)
        make_cyl("Card_StatusMarker", 0.0028, 0.0046, (-0.032, -0.0006, -0.0185), "Mat_StatusGreen", vertices=14, rot_deg=(90, 0, 0), parent=root)
        make_torus("Card_PinRing", 0.0025, 0.0008, (0, -0.0018, 0.0225), "Mat_Brass", maj_seg=14, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        make_box("Card_AttachmentClip", (0.003, 0.0048, 0.010), (0.0355, -0.0004, -0.004), "Mat_BrushedMetal", bevel_width=0.0006, parent=root)

        make_anchor("Card_Title_Anchor",      ( 0.000, -0.003,  0.003), root)
        make_anchor("Card_Subtitle_Anchor",   ( 0.000, -0.003, -0.010), root)
        make_anchor("Card_Pin_Anchor",        ( 0.000, -0.004,  0.022), root)
        make_anchor("Card_Attachment_Anchor", ( 0.035, -0.003, -0.004), root)
        return root

    assets["TaskBoard_KanbanFrame"] = make_polished_kanban_board
    assets["TaskCard_Grabbable"]    = make_polished_task_card
    return assets


# ==============================================================================
# PRIORITY 4 — INTERACTION FEEDBACK POLISH: SINGLE-MESH 3D POKE KEYS & STAMPS
# ==============================================================================

def build_ui_poke_key_assets():
    """
    Replaces the 5-cube primitive housing (`Back`, `Top`, `Bottom`, `Left`, `Right`) and
    primitive cube cap (`REMAINING_ISSUES.md` #59: ~96 renderers across 16 keys) with:
      1. `UI_PokeKey_Housing.fbx`: Single combined mesh with beveled rim and recessed key well (1 renderer!).
      2. `UI_PokeKey_Cap.fbx`: Single tactile keycap mesh with rounded corners and finger dish (1 renderer!).
    Dimensions are authored in unit button-local coordinates matching `PolishPass2.Buttons()`
    (`outer = 1.20`, `inner = 1.03`, `cap = 1.00 x 1.00 x 0.276`) so it drops directly into any
    `PokeInteractable` button!
    """
    assets = {}

    def make_poke_key_housing():
        root = bpy.data.objects.new("UI_PokeKey_Housing", None)
        bpy.context.collection.objects.link(root)

        # In Blender (-Y forward -> Unity -Z, +Y back -> Unity +Z):
        # PolishPass2 had:
        #   travel = 0.30, rim = 0.186, back = 0.16, wallDepth = 0.346, wallZ = -0.013 (in Unity local Z)
        #   In Blender Y = -Unity Z:
        #   Back plate at y = -(0.16 - 0.02) = -0.14? Wait:
        #   Let's check ModelBoundsFixer!
        #    Blender FBX exporter uses axis_forward='-Z', axis_up='Y'.
        #   Wait: in Blender, +Y is exported to Unity -Z or +Z?
        #   Let's check: in Blender, (x, y, z) with axis_forward='-Z', axis_up='Y' + ModelBoundsFixer:
        #   In ModelBoundsFixer, `mf.transform.TransformPoint(verts[i])` bakes the FBX root's -90 X rotation!
        #   Under -90 deg X rotation:
        #     Blender +X -> Unity -X (or +X)
        #     Blender +Y -> Unity +Z
        #     Blender +Z -> Unity +Y
        #   Let's verify that! In `PolishPass2.Buttons()`:
        #     In Unity button local space:
        #       `visual.localPosition = (0, 0, -0.30)` (cap moves along -Z toward user, presses toward +Z).
        #       `Back` is at `z = +0.14`, size `(1.20, 1.20, 0.04)`.
        #       `Top/Bottom/Left/Right` walls have `wallDepth = 0.346`, centered at `z = -0.013`.
        #   Since Blender +Y maps to Unity +Z and Blender +Z maps to Unity +Y:
        #     Unity `(x, y, z)` corresponds to Blender `(x, z, y)`!
        outer = 1.20
        wall = 0.095
        inner = outer - 2.0 * wall
        rim = 0.186
        back = 0.160
        wall_depth = rim + back
        wall_y = (back - rim) * 0.5  # maps to Unity wallZ = -0.013

        make_box("Housing_Back",   (outer, 0.045, outer), (0, back - 0.02, 0), "Mat_CharcoalVisor", bevel_width=0.018, parent=root)
        make_box("Housing_Top",    (outer, wall_depth, wall), (0, wall_y,  (outer - wall) * 0.5), "Mat_CharcoalVisor", bevel_width=0.018, parent=root)
        make_box("Housing_Bottom", (outer, wall_depth, wall), (0, wall_y, -(outer - wall) * 0.5), "Mat_CharcoalVisor", bevel_width=0.018, parent=root)
        make_box("Housing_Left",   (wall, wall_depth, inner), (-(outer - wall) * 0.5, wall_y, 0), "Mat_CharcoalVisor", bevel_width=0.018, parent=root)
        make_box("Housing_Right",  (wall, wall_depth, inner), ( (outer - wall) * 0.5, wall_y, 0), "Mat_CharcoalVisor", bevel_width=0.018, parent=root)

        # Join all 5 walls into ONE single mesh (`Housing_SingleMesh`)!
        join_children_by_material(root, prefix="Housing")
        return root

    def make_poke_key_cap():
        root = bpy.data.objects.new("UI_PokeKey_Cap", None)
        bpy.context.collection.objects.link(root)

        # In Unity `ButtonVisual` local space:
        # `Cap3D` is centered at `z = 0.004 + capDepth * 0.5 = 0.142`, size `(0.98, 0.98, 0.276)`.
        # In Blender `(x, z, y)`: y = +0.142, size = `(0.98, 0.276, 0.98)`.
        cap_depth = 0.276
        cap_y = 0.004 + cap_depth * 0.5
        cap = make_box("KeyCap_Body", (0.98, cap_depth, 0.98), (0, cap_y, 0), "Mat_CreamCeramic", bevel_width=0.065, segments=3, parent=root)
        return root

    assets["UI_PokeKey_Housing"] = make_poke_key_housing
    assets["UI_PokeKey_Cap"]     = make_poke_key_cap
    return assets


def build_polished_stamp_assets():
    """
    Refined physical stamps (`Doc_Stamp_Approve`, `Doc_Stamp_Reject`, `Doc_ApprovalStamp`, `Doc_StampRestPad`):
      - Ergonomic turned-wood handle with distinct green/red top crown
      - Brass collar with a front-facing directional indicator badge so orientation is obvious
      - Textured rubber stamp pad
      - Dedicated dual-slot ink-pad rest dock (`Doc_StampRestPad`)
    """
    assets = {}

    def make_stamp_variant(name, crown_mat, pad_mat):
        root = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(root)

        # Base collar (4.2 cm x 3.2 cm)
        make_box("Stamp_BrassBase", (0.042, 0.032, 0.010), (0, 0, 0.006), "Mat_Brass", bevel_width=0.002, parent=root)
        make_box("Stamp_RubberDie", (0.038, 0.028, 0.003), (0, 0, 0.0015), pad_mat, bevel_width=0.0008, parent=root)
        # Front orientation indicator emblem on the front (-Y) of the base
        make_box("Stamp_FrontPip", (0.014, 0.003, 0.005), (0, -0.0165, 0.0065), crown_mat, bevel_width=0.0008, parent=root)
        # Turned dark-wood waist + color-coded ergonomic top knob
        make_cyl("Stamp_WoodStem", 0.0085, 0.026, (0, 0, 0.024), "Mat_DarkWood", vertices=18, parent=root)
        make_torus("Stamp_FerrulRing", 0.0095, 0.0018, (0, 0, 0.014), "Mat_Brass", maj_seg=18, min_seg=8, parent=root)
        make_sphere("Stamp_HandleKnob", (0.016, 0.016, 0.017), (0, 0, 0.044), "Mat_WarmWood", parent=root)
        make_sphere("Stamp_ColorCrown", (0.013, 0.013, 0.007), (0, 0, 0.056), crown_mat, parent=root)

        join_children_by_material(root, prefix="Stamp")
        make_anchor("Stamp_Contact_Anchor", (0, 0, 0.000), root)
        make_anchor("Grab_Pivot_Anchor",    (0, 0, 0.038), root)
        return root

    def make_stamp_rest_pad():
        root = bpy.data.objects.new("Doc_StampRestPad", None)
        bpy.context.collection.objects.link(root)
        make_box("RestPad_Tray", (0.118, 0.054, 0.007), (0, 0, 0.0035), "Mat_DarkWood", bevel_width=0.002, parent=root)
        make_box("RestPad_ApproveWell", (0.046, 0.036, 0.004), ( 0.028, 0, 0.0075), "Mat_StatusGreen", bevel_width=0.001, parent=root)
        make_box("RestPad_RejectWell",  (0.046, 0.036, 0.004), (-0.028, 0, 0.0075), "Mat_StatusRed",   bevel_width=0.001, parent=root)
        make_box("RestPad_CenterDivider", (0.004, 0.042, 0.006), (0, 0, 0.007), "Mat_Brass", bevel_width=0.0008, parent=root)
        make_anchor("Approve_Stamp_Rest_Anchor", ( 0.028, 0, 0.009), root)
        make_anchor("Reject_Stamp_Rest_Anchor",  (-0.028, 0, 0.009), root)
        return root

    assets["Doc_Stamp_Approve"] = lambda: make_stamp_variant("Doc_Stamp_Approve", "Mat_StatusGreen", "Mat_Tester_Mint")
    assets["Doc_Stamp_Reject"]  = lambda: make_stamp_variant("Doc_Stamp_Reject",  "Mat_StatusRed",   "Mat_Developer_Coral")
    assets["Doc_ApprovalStamp"] = lambda: make_stamp_variant("Doc_ApprovalStamp", "Mat_StatusGreen", "Mat_Tester_Mint")
    assets["Doc_StampRestPad"]  = make_stamp_rest_pad
    return assets


# ==============================================================================
# PRIORITY 7 — OPTIONAL MASCOT FEEDBACK ASSETS (WITHOUT CHANGING RIGS)
# ==============================================================================

def build_mascot_feedback_assets():
    """
    Lightweight, adorable 3D feedback indicators (~3.2 cm wide) that attach above `Head_Attach`
    or beside a workstation without touching mascot rigs or obstructing faces:
      - Feedback_Listening
      - Feedback_Speaking
      - Feedback_Thinking
      - Feedback_NeedsAttention
      - Feedback_Error
      - Feedback_Success
    """
    assets = {}

    def make_listening():
        root = bpy.data.objects.new("Feedback_Listening", None)
        bpy.context.collection.objects.link(root)
        make_sphere("Listen_Core", (0.007, 0.004, 0.007), (0, 0, 0), "Mat_Researcher_Teal", parent=root)
        make_torus("Listen_WaveInner", 0.013, 0.0020, (0, 0, 0), "Mat_StatusCyan", maj_seg=20, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        make_torus("Listen_WaveOuter", 0.020, 0.0016, (0, 0, 0), "Mat_Researcher_Teal", maj_seg=24, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        join_children_by_material(root, prefix="FeedbackListening")
        return root

    def make_speaking():
        root = bpy.data.objects.new("Feedback_Speaking", None)
        bpy.context.collection.objects.link(root)
        # Stylized 3-bar soundwave chime inside a warm brass ring
        make_torus("Speak_Ring", 0.018, 0.0018, (0, 0, 0), "Mat_Brass", maj_seg=24, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        for bx, bh in [(-0.007, 0.012), (0.000, 0.020), (0.007, 0.014)]:
            make_box(f"Speak_Bar_{int(bx*1000)}", (0.0038, 0.0038, bh), (bx, 0, 0), "Mat_Developer_Amber", bevel_width=0.001, parent=root)
        join_children_by_material(root, prefix="FeedbackSpeaking")
        return root

    def make_thinking():
        root = bpy.data.objects.new("Feedback_Thinking", None)
        bpy.context.collection.objects.link(root)
        # Miniature thought cloud puff + trail dots
        make_sphere("Think_PuffC", (0.012, 0.007, 0.010), ( 0.000, 0,  0.004), "Mat_CreamCeramic", parent=root)
        make_sphere("Think_PuffL", (0.009, 0.006, 0.008), ( 0.009, 0,  0.002), "Mat_CreamCeramic", parent=root)
        make_sphere("Think_PuffR", (0.009, 0.006, 0.008), (-0.009, 0,  0.002), "Mat_CreamCeramic", parent=root)
        make_sphere("Think_Dot1",  (0.004, 0.004, 0.004), ( 0.005, 0, -0.010), "Mat_Researcher_Lavender", parent=root)
        make_sphere("Think_Dot2",  (0.0025, 0.0025, 0.0025), (0.008, 0, -0.016), "Mat_Researcher_Lavender", parent=root)
        join_children_by_material(root, prefix="FeedbackThinking")
        return root

    def make_needs_attention():
        root = bpy.data.objects.new("Feedback_NeedsAttention", None)
        bpy.context.collection.objects.link(root)
        make_box("Attn_Stem", (0.006, 0.006, 0.018), (0, 0, 0.006), "Mat_StatusAmber", bevel_width=0.0018, parent=root)
        make_sphere("Attn_Dot", (0.0042, 0.0042, 0.0042), (0, 0, -0.010), "Mat_StatusAmber", parent=root)
        make_torus("Attn_Halo", 0.019, 0.0018, (0, 0, 0.000), "Mat_Brass", maj_seg=22, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        join_children_by_material(root, prefix="FeedbackAttention")
        return root

    def make_error():
        root = bpy.data.objects.new("Feedback_Error", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Error_Badge", 0.017, 0.004, (0, 0, 0), "Mat_StatusRed", vertices=6, rot_deg=(90, 30, 0), parent=root)
        make_box("Error_Cross1", (0.018, 0.0055, 0.004), (0, 0, 0), "Mat_CreamCeramic", bevel_width=0.0008, rot_deg=(0, 45, 0), parent=root)
        make_box("Error_Cross2", (0.018, 0.0055, 0.004), (0, 0, 0), "Mat_CreamCeramic", bevel_width=0.0008, rot_deg=(0, -45, 0), parent=root)
        join_children_by_material(root, prefix="FeedbackError")
        return root

    def make_success():
        root = bpy.data.objects.new("Feedback_Success", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Success_Medallion", 0.017, 0.004, (0, 0, 0), "Mat_StatusGreen", vertices=20, rot_deg=(90, 0, 0), parent=root)
        make_torus("Success_Rim", 0.017, 0.0018, (0, 0, 0), "Mat_Brass", maj_seg=20, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        make_box("Success_CheckShort", (0.004, 0.006, 0.009), ( 0.004, 0, -0.002), "Mat_CreamCeramic", bevel_width=0.0008, rot_deg=(0, -40, 0), parent=root)
        make_box("Success_CheckLong",  (0.004, 0.006, 0.016), (-0.003, 0,  0.001), "Mat_CreamCeramic", bevel_width=0.0008, rot_deg=(0,  35, 0), parent=root)
        join_children_by_material(root, prefix="FeedbackSuccess")
        return root

    assets["Feedback_Listening"]      = make_listening
    assets["Feedback_Speaking"]       = make_speaking
    assets["Feedback_Thinking"]       = make_thinking
    assets["Feedback_NeedsAttention"] = make_needs_attention
    assets["Feedback_Error"]          = make_error
    assets["Feedback_Success"]        = make_success
    return assets


# ==============================================================================
# PRIORITY 8 — REVERSIBLE ART PERFORMANCE PASS (OPTIMIZED STATIC VARIANTS)
# ==============================================================================

def build_optimized_variants():
    """
    Imports the existing workstation builders from `generate_workstations.py` and our
    polished `TaskBoard_KanbanFrame`, then welds all static non-interactive meshes by material
    while keeping every interactive, animated, screen, or status-light mesh separate!
    Saves to `Assets/MuseDesk/Art/Models/Optimized/*_Optimized.fbx` so the original multi-part
    FBXs remain 100% untouched and reversible.
    """
    import generate_workstations as gw

    keep_separate = {
        # Research interactive/animated/emissive parts
        "Printer_OutputBlueprint",
        "Research_LampBulb",
        "Research_WhiteboardSurface",
        # Developer interactive/screen/status parts
        "Developer_MainMonitorScreen",
        "Developer_SideMonitorScreen",
        "Developer_PCTowerLedBar",
        "Developer_PCTowerWindow",
        "Developer_ArtifactGlowPad",
        "Developer_DebugIndicatorLight",
        "Developer_LampBulb",
        # Testing interactive/animated/status parts
        "Testing_ChamberDoor",
        "Testing_ScannerRing",
        "Testing_InspectionPlatform",
        "Testing_DisplayPanelScreen",
        "Testing_ErrorReportSheet",
        "Light_Pass",
        "Light_Testing",
        "Light_Warn",
        "Light_Blocked",
        "Light_Fail",
        "Light_Idle",
        "Console_BtnPass",
        "Console_BtnRun",
        "Console_BtnFail",
    }

    def make_opt_research():
        root = gw.build_research_workstation()
        root.name = "Workstation_Research_Optimized"
        # Add subtle role status beacon onto the Research desk back-right corner
        make_cyl("Research_StatusBeaconBase", 0.007, 0.004, (0.095, -0.092, 0.095), "Mat_CharcoalVisor", vertices=14, parent=root)
        make_sphere("Research_StatusBeaconLight", (0.0055, 0.0055, 0.007), (0.095, -0.092, 0.101), "Mat_Researcher_Teal", parent=root)
        keep = set(keep_separate) | {"Research_StatusBeaconLight"}
        join_children_by_material(root, keep_separate_names=keep, prefix="Research_Static")
        return root

    def make_opt_developer():
        root = gw.build_developer_workstation()
        root.name = "Workstation_Developer_Optimized"
        join_children_by_material(root, keep_separate_names=keep_separate, prefix="Developer_Static")
        return root

    def make_opt_testing():
        root = gw.build_testing_workstation()
        root.name = "Workstation_Testing_Optimized"
        join_children_by_material(root, keep_separate_names=keep_separate, prefix="Testing_Static")
        return root

    def make_opt_kanban():
        tb_builders = build_polished_taskboard_assets()
        root = tb_builders["TaskBoard_KanbanFrame"]()
        root.name = "TaskBoard_KanbanFrame_Optimized"
        keep_headers = {"Header_Queued", "Header_InProgress", "Header_Blocked", "Header_Completed"}
        join_children_by_material(root, keep_separate_names=keep_headers, prefix="Kanban_Static")
        return root

    return {
        "Workstation_Research_Optimized":  make_opt_research,
        "Workstation_Developer_Optimized": make_opt_developer,
        "Workstation_Testing_Optimized":   make_opt_testing,
        "TaskBoard_KanbanFrame_Optimized": make_opt_kanban,
    }


# ==============================================================================
# EXPORT & VERIFICATION PIPELINE
# ==============================================================================

def export_asset(cat_key, asset_name, builder_fn):
    bdir, fdir = DIRS[cat_key]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = builder_fn()

    mesh_count = 0
    anchor_count = 0
    total_verts = 0
    total_tris = 0
    deps = bpy.context.evaluated_depsgraph_get()
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            mesh_count += 1
            em = obj.evaluated_get(deps).to_mesh()
            em.calc_loop_triangles()
            total_verts += len(em.vertices)
            total_tris += len(em.loop_triangles)
            obj.evaluated_get(deps).to_mesh_clear()
        elif obj.type == 'EMPTY' and obj != root:
            anchor_count += 1

    blend_path = os.path.join(bdir, f"{asset_name}.blend")
    fbx_path = os.path.join(fdir, f"{asset_name}.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        bake_anim=False,
        axis_forward='-Z',
        axis_up='Y',
    )
    return {
        "category": cat_key,
        "asset": asset_name,
        "blend": blend_path,
        "fbx": fbx_path,
        "fbx_bytes": os.path.getsize(fbx_path),
        "mesh_renderers": mesh_count,
        "anchors": anchor_count,
        "vertices": total_verts,
        "triangles": total_tris,
    }


def main():
    report = []

    # 1. Priority 1: Decision Paper (both new named assets + in-place updates to Doc_SmallPaper & Doc_ExpandedFrame)
    doc_builders = {
        "Doc_DecisionPaper_Small":    lambda: build_decision_paper_small("Doc_DecisionPaper_Small"),
        "Doc_SmallPaper":             lambda: build_decision_paper_small("Doc_SmallPaper"),
        "Doc_DecisionPaper_Expanded": lambda: build_decision_paper_expanded("Doc_DecisionPaper_Expanded"),
        "Doc_ExpandedFrame":          lambda: build_decision_paper_expanded("Doc_ExpandedFrame"),
    }
    doc_builders.update(build_polished_stamp_assets())
    for name, fn in doc_builders.items():
        report.append(export_asset("Documents", name, fn))

    # 2. Priority 2: Physical Task-Assignment Affordances
    for name, fn in build_affordance_assets().items():
        report.append(export_asset("Affordances", name, fn))

    # 3. Priority 3: Polished Task Board & Task Card
    for name, fn in build_polished_taskboard_assets().items():
        report.append(export_asset("TaskBoard", name, fn))

    # 4. Priority 4: Single-Mesh 3D Poke Keys
    for name, fn in build_ui_poke_key_assets().items():
        report.append(export_asset("UI", name, fn))

    # 5. Priority 7: Optional Mascot Feedback Assets
    for name, fn in build_mascot_feedback_assets().items():
        report.append(export_asset("Feedback", name, fn))

    # 6. Priority 8: Reversible Material-Welded Optimized Workstation & Board Variants
    for name, fn in build_optimized_variants().items():
        report.append(export_asset("Optimized", name, fn))

    report_path = os.path.join(PREVIEW_DIR, "visual_polish_export_report.json")
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[VisualPolish] Exported {len(report)} assets. Report written to {report_path}")


if __name__ == "__main__":
    main()

