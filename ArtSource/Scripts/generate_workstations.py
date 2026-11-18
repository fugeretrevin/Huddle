import bpy
import bmesh
import math
import mathutils
import                  








import json

# ==============================================================================
# MuseDesk — Phase 2 Procedural Miniature Workstations Generator
# Generates the 3 complete, detailed miniature workstations (Sections 3, 4, 5):
#   1. Workstation_Research  (Desk, Chair, Open Bookshelf, 6 Colorful Books, Open & Closed
#                             Notebooks, Magnifying Glass, Pen Holder, Desk Lamp, Rolled &
#                             Unrolled Blueprints, Filing Tray, Whiteboard, Sticky Notes,
#                             Mini Celestial Globe, Blueprint Output Printer + All Anchors)
#   2. Workstation_Developer (Desk, Ergonomic Chair, Dual Monitors with Dynamic Screen Meshes,
#                             Keyboard with Visible Keys, Mouse, Desk Mat, PC Tower with Window,
#                             Cable Management, Headset Stand + Headphones, Desk Lamp, Coffee Cup,
#                             Tiny Plant, Debug Duck & Code Trophy, Task Receive Tray, Artifact
#                             Output Tray, Small Debugging Indicator + All Anchors)
#   3. Workstation_Testing   (Test Workbench, Stool/Chair, Testing Chamber with Door & Scanner,
#                             5-State Status Lights [Idle/Testing/Passed/Failed/Blocked],
#                             Test Console, Diagnostic Display, Clipboard, Mounted Inspection
#                             Magnifier, Articulated Testing Tool, Accepted & Rejected Trays,
#                             Error Report Printer, Decorative Tools, Inspection Platform + Anchors)
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
BLEND_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "Workstations")
FBX_DIR = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models", "Workstations")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")

for d in [BLEND_DIR, FBX_DIR, PREVIEW_DIR]:
    os.makedirs(d, exist_ok=True)


def srgb_to_linear(c):
    res = []
    for ch in c[:3]:
        res.append(ch / 12.92 if ch <= 0.04045 else ((ch + 0.055) / 1.055) ** 2.4)
    res.append(c[3] if len(c) > 3 else 1.0)
    return tuple(res)


PALETTE = {
    "Mat_WarmWood":            {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.48, "metallic": 0.0},
    "Mat_DarkWood":            {"color": (0.460, 0.330, 0.230, 1.0), "roughness": 0.52, "metallic": 0.0},
    "Mat_CreamCeramic":        {"color": (0.955, 0.935, 0.895, 1.0), "roughness": 0.32, "metallic": 0.0},
    "Mat_CharcoalVisor":       {"color": (0.130, 0.140, 0.170, 1.0), "roughness": 0.22, "metallic": 0.15},
    "Mat_BrushedMetal":        {"color": (0.650, 0.685, 0.725, 1.0), "roughness": 0.28, "metallic": 0.70},
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.25, "metallic": 0.80},
    "Mat_PaperWarm":           {"color": (0.985, 0.968, 0.925, 1.0), "roughness": 0.58, "metallic": 0.0},
    "Mat_BlueprintBlue":       {"color": (0.180, 0.380, 0.580, 1.0), "roughness": 0.45, "metallic": 0.0},
    "Mat_FeltSlate":           {"color": (0.260, 0.290, 0.340, 1.0), "roughness": 0.72, "metallic": 0.0},
    "Mat_Terracotta":          {"color": (0.780, 0.430, 0.310, 1.0), "roughness": 0.56, "metallic": 0.0},
    "Mat_PlantGreen":          {"color": (0.340, 0.690, 0.440, 1.0), "roughness": 0.40, "metallic": 0.0},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.30, "metallic": 0.05, "emission": (0.28, 0.74, 0.68), "emission_strength": 0.40},
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.0},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (1.0, 0.81, 0.32), "emission_strength": 0.80},
    "Mat_ScreenCode":          {"color": (0.120, 0.260, 0.400, 1.0), "roughness": 0.14, "metallic": 0.0,  "emission": (0.18, 0.58, 0.88), "emission_strength": 0.95},
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.22, 0.85, 0.45), "emission_strength": 1.2},
    "Mat_StatusRed":           {"color": (0.920, 0.250, 0.240, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.92, 0.25, 0.24), "emission_strength": 1.2},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.98, 0.68, 0.22), "emission_strength": 1.2},
    "Mat_StatusCyan":          {"color": (0.250, 0.820, 0.950, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.25, 0.82, 0.95), "emission_strength": 1.1},
    "Mat_ChamberGlass":        {"color": (0.680, 0.900, 0.880, 1.0), "roughness": 0.12, "metallic": 0.0,  "emission": (0.35, 0.82, 0.76), "emission_strength": 0.35},
    "Mat_LampGlow":            {"color": (1.000, 0.950, 0.800, 1.0), "roughness": 0.15, "metallic": 0.0,  "emission": (1.00, 0.94, 0.76), "emission_strength": 1.5},
}


def get_mat(name):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    spec = PALETTE[name]
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


def make_beveled_box(name, size_xyz, location, mat_name, bevel_width=0.0025, segments=2, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size_xyz[0], size_xyz[1], size_xyz[2])
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


def make_cylinder(name, radius, depth, location, mat_name, vertices=18, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                        location=location,
                                        rotation=tuple(math.radians(a) for a in rot_deg))
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_cone(name, r1, r2, depth, location, mat_name, vertices=16, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth,
                                    location=location,
                                    rotation=tuple(math.radians(a) for a in rot_deg))
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_sphere(name, radius_xyz, location, mat_name, segments=16, rings=10, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1.0,
                                         location=location,
                                         rotation=tuple(math.radians(a) for a in rot_deg))
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


def make_torus(name, maj_r, min_r, location, mat_name, maj_seg=18, min_seg=8, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_torus_add(major_segments=maj_seg, minor_segments=min_seg,
                                     major_radius=maj_r, minor_radius=min_r,
                                     location=location,
                                     rotation=tuple(math.radians(a) for a in rot_deg))
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
    emp.empty_display_size = 0.018
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


def build_shared_desk_base(root, prefix, width=0.25, depth=0.13, height=0.088, top_mat="Mat_WarmWood", leg_mat="Mat_DarkWood"):
    # Desk tabletop at z = height (centered at y = -0.055)
    make_beveled_box(f"{prefix}_DeskTop", (width, depth, 0.011), (0, -0.055, height), top_mat, bevel_width=0.0035, parent=root)
    # Modesty panel / under-desk crossbar
    make_beveled_box(f"{prefix}_UnderFrame", (width * 0.82, 0.010, 0.014), (0, -0.085, height - 0.011), leg_mat, bevel_width=0.002, parent=root)
    # 4 rounded legs
    lx = width * 0.42
    ly_front = -0.055 - depth * 0.38
    ly_back  = -0.055 + depth * 0.38
    leg_h = height - 0.005
    for idx, (x, y) in enumerate([(-lx, ly_front), (lx, ly_front), (-lx, ly_back), (lx, ly_back)]):
        make_cylinder(f"{prefix}_DeskLeg_{idx+1}", 0.0065, leg_h, (x, y, leg_h * 0.5), leg_mat, vertices=14, parent=root)
        make_cylinder(f"{prefix}_LegFoot_{idx+1}", 0.0080, 0.004, (x, y, 0.002), "Mat_Brass", vertices=14, parent=root)


def build_mini_chair(root, prefix, cushion_mat, location=(0, 0.032, 0)):
    cx, cy, cz = location
    # 5-star caster base + stem
    make_cylinder(f"{prefix}_ChairBase", 0.028, 0.005, (cx, cy, cz + 0.0025), "Mat_CharcoalVisor", vertices=20, parent=root)
    make_cylinder(f"{prefix}_ChairStem", 0.0055, 0.036, (cx, cy, cz + 0.020), "Mat_BrushedMetal", vertices=14, parent=root)
    # Plush rounded seat cushion
    make_beveled_box(f"{prefix}_ChairSeat", (0.064, 0.060, 0.013), (cx, cy, cz + 0.042), cushion_mat, bevel_width=0.004, parent=root)
    # Curved ergonomic backrest + armrests
    make_beveled_box(f"{prefix}_ChairBack", (0.058, 0.011, 0.042), (cx, cy + 0.026, cz + 0.070), cushion_mat, bevel_width=0.004, parent=root)
    for side, sx in [("L", 0.032), ("R", -0.032)]:
        make_beveled_box(f"{prefix}_ChairArm_{side}", (0.007, 0.034, 0.006), (cx + sx, cy + 0.004, cz + 0.056), "Mat_DarkWood", bevel_width=0.002, parent=root)


# ==============================================================================
# 1. RESEARCH WORKSTATION (Section 4)
# ==============================================================================

def build_research_workstation():
    root = bpy.data.objects.new("Workstation_Research", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Research", width=0.25, depth=0.13, height=0.088)
    build_mini_chair(root, "Research", "Mat_Researcher_Lavender", location=(0, 0.032, 0))

    # 1. Open-Front Wooden Bookshelf (at +X = 0.160, open on both -Y and +Y so books are visible from all angles!)
    bx, by = 0.162, -0.045
    make_beveled_box("Research_BookshelfLeftWall",  (0.007, 0.046, 0.160), (bx + 0.032, by, 0.080), "Mat_WarmWood", bevel_width=0.0015, parent=root)
    make_beveled_box("Research_BookshelfRightWall", (0.007, 0.046, 0.160), (bx - 0.032, by, 0.080), "Mat_WarmWood", bevel_width=0.0015, parent=root)
    for idx, sz in enumerate([0.006, 0.054, 0.104, 0.156]):
        make_beveled_box(f"Research_ShelfPlank_{idx+1}", (0.068, 0.046, 0.007), (bx, by, sz), "Mat_DarkWood", bevel_width=0.0015, parent=root)

    # 6 Colorful Books inside Open Bookshelf
    book_specs = [
        ("Book_Lavender", (bx - 0.018, by, 0.026), (0.010, 0.032, 0.034), "Mat_Researcher_Lavender", (0, 0, 0)),
        ("Book_Teal",     (bx - 0.006, by, 0.028), (0.011, 0.032, 0.038), "Mat_Researcher_Teal",     (0, 0, 0)),
        ("Book_Amber",    (bx + 0.008, by, 0.025), (0.010, 0.032, 0.032), "Mat_Brass",               (0, -10, 0)),
        ("Book_Coral",    (bx - 0.016, by, 0.075), (0.011, 0.032, 0.034), "Mat_Developer_Coral",     (0, 0, 0)),
        ("Book_Mint",     (bx - 0.003, by, 0.074), (0.011, 0.032, 0.032), "Mat_Tester_Mint",         (0, 0, 0)),
        ("Book_Blue",     (bx + 0.011, by, 0.076), (0.010, 0.032, 0.036), "Mat_BlueprintBlue",       (0, -8, 0)),
    ]
    for bname, bloc, bsize, bmat, brot in book_specs:
        make_beveled_box(bname, bsize, bloc, bmat, bevel_width=0.0015, rot_deg=brot, parent=root)

    # 2. Miniature Celestial Globe on Top of Bookshelf
    make_cylinder("Research_GlobeStand", 0.013, 0.005, (bx, by, 0.162), "Mat_Brass", vertices=16, parent=root)
    make_torus("Research_GlobeGimbal", 0.018, 0.002, (bx, by, 0.182), "Mat_Brass", maj_seg=18, min_seg=8, rot_deg=(90, 18, 0), parent=root)
    make_sphere("Research_GlobeOrb", (0.015, 0.015, 0.015), (bx, by, 0.182), "Mat_Researcher_Teal", parent=root)

    # 3. Open Notebook on Desk Center + Closed Notebook Stack
    make_beveled_box("Research_OpenNotebook_L", (0.032, 0.042, 0.004), ( 0.016, -0.045, 0.095), "Mat_PaperWarm", bevel_width=0.001, rot_deg=(0, -4, 0), parent=root)
    make_beveled_box("Research_OpenNotebook_R", (0.032, 0.042, 0.004), (-0.016, -0.045, 0.095), "Mat_PaperWarm", bevel_width=0.001, rot_deg=(0,  4, 0), parent=root)
    make_beveled_box("Research_ClosedNotebook_1", (0.036, 0.046, 0.007), (0.068, -0.042, 0.096), "Mat_Researcher_Lavender", bevel_width=0.0015, rot_deg=(0, 0, 12), parent=root)
    make_beveled_box("Research_ClosedNotebook_2", (0.034, 0.044, 0.006), (0.068, -0.042, 0.103), "Mat_Researcher_Teal", bevel_width=0.0015, rot_deg=(0, 0, -6), parent=root)

    # 4. Magnifying Glass + Pencil/Pen Holder + Desk Lamp
    make_cylinder("Research_MagnifierRim", 0.013, 0.003, (-0.052, -0.038, 0.096), "Mat_Brass", vertices=18, parent=root)
    make_cylinder("Research_MagnifierLens", 0.010, 0.0035, (-0.052, -0.038, 0.096), "Mat_Researcher_Teal", vertices=16, parent=root)
    make_cylinder("Research_MagnifierHandle", 0.0028, 0.022, (-0.062, -0.024, 0.096), "Mat_DarkWood", vertices=12, rot_deg=(90, 0, -28), parent=root)

    make_cylinder("Research_PenHolder", 0.009, 0.022, (0.036, -0.088, 0.104), "Mat_Researcher_Lavender", vertices=16, parent=root)
    make_cylinder("Research_Pen_1", 0.002, 0.028, (0.034, -0.088, 0.116), "Mat_Developer_Coral", vertices=10, rot_deg=(10, 8, 0), parent=root)
    make_cylinder("Research_Pen_2", 0.002, 0.028, (0.038, -0.088, 0.116), "Mat_Brass", vertices=10, rot_deg=(-8, -10, 0), parent=root)

    make_cylinder("Research_LampBase", 0.016, 0.005, (-0.095, -0.088, 0.095), "Mat_Brass", vertices=16, parent=root)
    make_cylinder("Research_LampStem", 0.003, 0.052, (-0.095, -0.088, 0.122), "Mat_Brass", vertices=12, rot_deg=(-12, 0, 0), parent=root)
    make_sphere("Research_LampShade", (0.016, 0.016, 0.012), (-0.088, -0.074, 0.148), "Mat_Researcher_Teal", parent=root)
    make_sphere("Research_LampBulb", (0.008, 0.008, 0.008), (-0.088, -0.074, 0.142), "Mat_LampGlow", parent=root)

    # 5. Unrolled Blueprint Sheet + Rolled Blueprints + Research Filing Tray
    make_beveled_box("Research_BlueprintSheet", (0.048, 0.034, 0.002), (-0.045, -0.078, 0.095), "Mat_BlueprintBlue", bevel_width=0.0005, rot_deg=(0, 0, -8), parent=root)
    make_cylinder("Research_RolledBlueprint_1", 0.005, 0.046, (-0.045, -0.098, 0.098), "Mat_BlueprintBlue", vertices=14, rot_deg=(0, 90, 5), parent=root)
    make_cylinder("Research_RolledBlueprint_2", 0.005, 0.046, (-0.045, -0.106, 0.098), "Mat_PaperWarm", vertices=14, rot_deg=(0, 90, -3), parent=root)
    make_beveled_box("Research_FilingTray", (0.044, 0.052, 0.009), (0.088, -0.082, 0.097), "Mat_DarkWood", bevel_width=0.002, parent=root)

    # 6. Small Standing Whiteboard + Colorful Sticky Notes (at back-center/right of desk)
    make_cylinder("Research_WhiteboardPost", 0.0035, 0.038, (0.000, -0.098, 0.112), "Mat_DarkWood", vertices=12, parent=root)
    make_beveled_box("Research_WhiteboardFrame", (0.074, 0.006, 0.048), (0.000, -0.096, 0.142), "Mat_WarmWood", bevel_width=0.002, parent=root)
    make_beveled_box("Research_WhiteboardSurface", (0.066, 0.008, 0.040), (0.000, -0.096, 0.142), "Mat_PaperWarm", bevel_width=0.0008, parent=root)
    sticky_specs = [
        ("StickyNote_1", ( 0.018, -0.091, 0.148), "Mat_Developer_Amber"),
        ("StickyNote_2", ( 0.002, -0.091, 0.146), "Mat_Developer_Coral"),
        ("StickyNote_3", (-0.016, -0.091, 0.150), "Mat_Tester_Mint"),
        ("StickyNote_4", ( 0.014, -0.099, 0.148), "Mat_Researcher_Lavender"),
    ]
    for sname, sloc, smat in sticky_specs:
        make_beveled_box(sname, (0.011, 0.002, 0.011), sloc, smat, bevel_width=0.0004, parent=root)

    # 7. Blueprint Output Machine / Miniature Printer (beside desk at -X = -0.155)
    px, py = -0.155, -0.055
    make_beveled_box("Research_PrinterStand", (0.056, 0.056, 0.068), (px, py, 0.034), "Mat_WarmWood", bevel_width=0.0025, parent=root)
    make_beveled_box("Research_BlueprintPrinter", (0.062, 0.048, 0.028), (px, py, 0.082), "Mat_Researcher_Lavender", bevel_width=0.003, parent=root)
    make_cylinder("Printer_FeedRoller", 0.005, 0.050, (px, py - 0.018, 0.094), "Mat_Brass", vertices=14, rot_deg=(0, 90, 0), parent=root)
    make_beveled_box("Printer_OutputBlueprint", (0.042, 0.032, 0.002), (px, py - 0.032, 0.088), "Mat_BlueprintBlue", bevel_width=0.0005, rot_deg=(18, 0, 0), parent=root)

    # Named Interaction Anchors (Phase 2 Contract + Legacy Aliases)
    make_anchor("AgentWorkPosition",      ( 0.000,  0.000, 0.000), root)
    make_anchor("AgentSeatPosition",      ( 0.000,  0.032, 0.042), root)
    make_anchor("TaskDropZone",           ( 0.000, -0.055, 0.145), root)
    make_anchor("DocumentPickupPoint",    ( 0.088, -0.082, 0.104), root)
    make_anchor("ArtifactOutputPoint",    ( px,     py - 0.032, 0.095), root)
    make_anchor("BlueprintSpawnPoint",    (-0.045, -0.078, 0.100), root)
    make_anchor("HandoffReceivePoint",    ( 0.068, -0.042, 0.108), root)
    # Legacy aliases
    make_anchor("Seat_Anchor",            ( 0.000,  0.000, 0.000), root)
    make_anchor("Work_Hands_Anchor",      ( 0.000, -0.045, 0.098), root)
    make_anchor("Blueprint_Spawn_Anchor", (-0.045, -0.078, 0.100), root)
    make_anchor("Document_Tray_Anchor",   ( 0.088, -0.082, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor",   ( 0.000, -0.055, 0.145), root)
    make_anchor("Artifact_Drop_Anchor",   (-0.055, -0.055, 0.102), root)

    return root


# ==============================================================================
# 2. DEVELOPER WORKSTATION (Section 3)
# ==============================================================================

def build_developer_workstation():
    root = bpy.data.objects.new("Workstation_Developer", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Developer", width=0.26, depth=0.135, height=0.088)
    build_mini_chair(root, "Developer", "Mat_Developer_Coral", location=(0, 0.032, 0))

    # 1. Small Felt Desk Mat under Keyboard & Mouse
    make_beveled_box("Developer_DeskMat", (0.115, 0.052, 0.002), (-0.008, -0.042, 0.0945), "Mat_FeltSlate", bevel_width=0.0008, parent=root)

    # 2. Dual Computer Monitors (Left/Main Widescreen & Right/Side Vertical) with Double-Sided Dynamic Screen Surfaces
    make_cylinder("Developer_MonitorBase", 0.018, 0.004, (0.006, -0.088, 0.095), "Mat_CharcoalVisor", vertices=18, parent=root)
    make_cylinder("Developer_MonitorNeck", 0.0045, 0.032, (0.006, -0.090, 0.110), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Developer_MainMonitorFrame", (0.088, 0.007, 0.054), (0.006, -0.084, 0.142), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    # Screen mesh protruding slightly on both sides so it's visible from agent seat (+Y) AND front overview (-Y)
    make_beveled_box("Developer_MainMonitorScreen", (0.080, 0.0085, 0.046), (0.006, -0.084, 0.142), "Mat_ScreenCode", bevel_width=0.0005, parent=root)

    make_cylinder("Developer_SideMonitorNeck", 0.0035, 0.028, (-0.058, -0.080, 0.108), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Developer_SideMonitorFrame", (0.038, 0.007, 0.054), (-0.058, -0.074, 0.136), "Mat_CharcoalVisor", bevel_width=0.002, rot_deg=(0, 0, 20), parent=root)
    make_beveled_box("Developer_SideMonitorScreen", (0.032, 0.0085, 0.048), (-0.058, -0.074, 0.136), "Mat_ScreenCode", bevel_width=0.0005, rot_deg=(0, 0, 20), parent=root)

    # 3. Mechanical Keyboard with Visible Key Rows + Mouse
    make_beveled_box("Developer_KeyboardCase", (0.064, 0.026, 0.005), (0.006, -0.042, 0.096), "Mat_CharcoalVisor", bevel_width=0.0015, parent=root)
    for r_idx, ky in enumerate([-0.050, -0.045, -0.040, -0.035]):
        make_beveled_box(f"Developer_KeyRow_{r_idx+1}", (0.048, 0.0038, 0.003), (0.008, ky, 0.099), "Mat_CreamCeramic", bevel_width=0.0006, parent=root)
    make_beveled_box("Developer_KeyAccentEsc", (0.007, 0.004, 0.0035), (0.033, -0.050, 0.099), "Mat_Developer_Coral", bevel_width=0.0006, parent=root)
    make_beveled_box("Developer_KeyAccentEnter", (0.008, 0.008, 0.0035), (-0.020, -0.042, 0.099), "Mat_Developer_Amber", bevel_width=0.0006, parent=root)
    make_sphere("Developer_Mouse", (0.008, 0.012, 0.005), (-0.042, -0.042, 0.096), "Mat_CreamCeramic", parent=root)

    # 4. Miniature PC Tower (at -X = -0.158) + Cable Management Details
    tx, ty = -0.158, -0.052
    make_beveled_box("Developer_PCTowerChassis", (0.046, 0.084, 0.106), (tx, ty, 0.053), "Mat_CharcoalVisor", bevel_width=0.003, parent=root)
    make_beveled_box("Developer_PCTowerLedBar", (0.009, 0.005, 0.080), (tx, ty - 0.042, 0.056), "Mat_Developer_Amber", bevel_width=0.001, parent=root)
    make_beveled_box("Developer_PCTowerWindow", (0.048, 0.058, 0.068), (tx, ty, 0.058), "Mat_ScreenCode", bevel_width=0.0005, parent=root)
    # Cable-management conduits linking PC Tower to Monitors
    make_cylinder("Developer_CableConduit_1", 0.003, 0.065, (-0.115, -0.088, 0.075), "Mat_CharcoalVisor", vertices=10, rot_deg=(0, 72, 0), parent=root)
    make_cylinder("Developer_CableConduit_2", 0.0025, 0.055, (-0.030, -0.092, 0.098), "Mat_Developer_Coral", vertices=10, rot_deg=(0, 90, 0), parent=root)

    # 5. Headset Stand + Spare Headphones + Desk Lamp + Coffee Cup + Tiny Plant
    make_cylinder("Developer_HeadsetStandBase", 0.012, 0.004, (0.102, -0.088, 0.095), "Mat_Brass", vertices=16, parent=root)
    make_cylinder("Developer_HeadsetStandPole", 0.0025, 0.038, (0.102, -0.088, 0.114), "Mat_BrushedMetal", vertices=10, parent=root)
    make_torus("Developer_StandHeadphones", 0.013, 0.003, (0.102, -0.088, 0.128), "Mat_Developer_Coral", maj_seg=16, min_seg=8, rot_deg=(90, 0, 0), parent=root)

    make_cylinder("Developer_LampBase", 0.014, 0.004, (-0.104, -0.092, 0.095), "Mat_Brass", vertices=14, parent=root)
    make_cylinder("Developer_LampArm", 0.0025, 0.044, (-0.104, -0.092, 0.118), "Mat_Brass", vertices=10, rot_deg=(-14, 0, 0), parent=root)
    make_cone("Developer_LampShade", 0.014, 0.005, 0.016, (-0.100, -0.080, 0.140), "Mat_Developer_Coral", vertices=14, rot_deg=(20, 0, 0), parent=root)
    make_sphere("Developer_LampBulb", (0.006, 0.006, 0.006), (-0.100, -0.080, 0.135), "Mat_LampGlow", parent=root)

    make_cylinder("Developer_CoffeeCup", 0.0085, 0.018, (0.056, -0.036, 0.102), "Mat_Developer_Coral", vertices=16, parent=root)
    make_torus("Developer_CupHandle", 0.005, 0.0015, (0.064, -0.036, 0.102), "Mat_CreamCeramic", maj_seg=12, min_seg=6, rot_deg=(90, 0, 0), parent=root)

    make_cone("Developer_PlantPot", 0.008, 0.011, 0.016, (0.062, -0.088, 0.101), "Mat_Terracotta", vertices=14, parent=root)
    make_sphere("Developer_PlantSucculent", (0.009, 0.009, 0.008), (0.062, -0.088, 0.112), "Mat_PlantGreen", parent=root)

    # 6. Task-Card Receiving Tray + Artifact Output Tray + Small Debugging Indicator + Code Decor
    make_beveled_box("Developer_TaskReceiveTray", (0.042, 0.048, 0.008), (0.096, -0.040, 0.097), "Mat_WarmWood", bevel_width=0.002, parent=root)
    make_cylinder("Developer_ArtifactOutputTray", 0.021, 0.006, (-0.092, -0.042, 0.096), "Mat_BrushedMetal", vertices=20, parent=root)
    make_cylinder("Developer_ArtifactGlowPad", 0.015, 0.007, (-0.092, -0.042, 0.097), "Mat_Developer_Amber", vertices=18, parent=root)

    # Small Debugging Status Indicator Beacon on Desk
    make_cylinder("Developer_DebugIndicatorBase", 0.008, 0.005, (-0.036, -0.088, 0.096), "Mat_CharcoalVisor", vertices=14, parent=root)
    make_sphere("Developer_DebugIndicatorLight", (0.006, 0.006, 0.008), (-0.036, -0.088, 0.103), "Mat_StatusGreen", parent=root)

    # Cute Debug Rubber Duck on PC Tower top
    make_sphere("Developer_DebugDuckBody", (0.010, 0.013, 0.009), (tx, ty - 0.015, 0.113), "Mat_Developer_Amber", parent=root)
    make_sphere("Developer_DebugDuckHead", (0.007, 0.007, 0.007), (tx, ty - 0.023, 0.123), "Mat_Developer_Amber", parent=root)

    # Named Interaction Anchors (Section 3 Exact Names + Legacy Aliases)
    make_anchor("AgentWorkPosition",     ( 0.000,  0.000, 0.000), root)
    make_anchor("AgentSeatPosition",     ( 0.000,  0.032, 0.042), root)
    make_anchor("TaskDropZone",          ( 0.000, -0.055, 0.148), root)
    make_anchor("DocumentPickupPoint",   ( 0.096, -0.040, 0.104), root)
    make_anchor("ArtifactOutputPoint",   (-0.092, -0.042, 0.104), root)
    make_anchor("MonitorScreen_Left",    ( 0.006, -0.079, 0.142), root)
    make_anchor("MonitorScreen_Right",   (-0.058, -0.069, 0.136), root)
    make_anchor("HandoffReceivePoint",   ( 0.096, -0.040, 0.104), root)
    # Legacy aliases
    make_anchor("Seat_Anchor",           ( 0.000,  0.000, 0.000), root)
    make_anchor("Keyboard_Hands_Anchor", ( 0.006, -0.042, 0.100), root)
    make_anchor("Monitor_Screen_Anchor", ( 0.006, -0.079, 0.142), root)
    make_anchor("Task_Receive_Anchor",   ( 0.096, -0.040, 0.104), root)
    make_anchor("Artifact_Output_Anchor",(-0.092, -0.042, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor",  ( 0.000, -0.055, 0.148), root)

    return root


# ==============================================================================
# 3. TESTING WORKSTATION (Section 5)
# ==============================================================================

def build_testing_workstation():
    root = bpy.data.objects.new("Workstation_Testing", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Testing", width=0.26, depth=0.135, height=0.088, top_mat="Mat_BrushedMetal", leg_mat="Mat_Tester_SlateBlue")
    build_mini_chair(root, "Testing", "Mat_Tester_Mint", location=(0, 0.032, 0))

    # 1. Miniature Testing Chamber with Separate Door, Scanner Ring & Inspection Platform
    make_beveled_box("Testing_ChamberBase", (0.078, 0.066, 0.012), (0.0, -0.068, 0.098), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    make_beveled_box("Testing_ChamberTop",  (0.076, 0.064, 0.010), (0.0, -0.068, 0.164), "Mat_Tester_SlateBlue", bevel_width=0.002, parent=root)
    make_cylinder("Testing_InspectionPlatform", 0.022, 0.006, (0.0, -0.068, 0.106), "Mat_Brass", vertices=20, parent=root)
    for idx, (px, py) in enumerate([(-0.033, -0.041), (0.033, -0.041), (-0.033, -0.095), (0.033, -0.095)]):
        make_cylinder(f"Testing_ChamberPillar_{idx+1}", 0.0035, 0.056, (px, py, 0.132), "Mat_BrushedMetal", vertices=12, parent=root)
    make_cylinder("Testing_ScannerRing", 0.025, 0.006, (0.0, -0.068, 0.134), "Mat_Tester_Mint", vertices=24, parent=root)
    make_beveled_box("Testing_ChamberDoor", (0.060, 0.003, 0.048), (0.0, -0.038, 0.132), "Mat_ChamberGlass", bevel_width=0.001, parent=root)

    # 2. 5-State Status Light Tower (Idle, Testing, Passed, Failed, Blocked as separate controllable meshes!)
    lx, ly = -0.056, -0.090
    make_cylinder("Testing_LightTowerPole", 0.0035, 0.058, (lx, ly, 0.120), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Testing_LightHousing", (0.018, 0.018, 0.056), (lx, ly, 0.162), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    make_sphere("Light_Pass",    (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.182), "Mat_StatusGreen", parent=root)
    make_sphere("Light_Testing", (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.171), "Mat_StatusCyan",  parent=root)
    make_sphere("Light_Warn",    (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.160), "Mat_StatusAmber", parent=root)
    make_sphere("Light_Blocked", (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.160), "Mat_StatusAmber", parent=root)
    make_sphere("Light_Fail",    (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.149), "Mat_StatusRed",   parent=root)
    make_sphere("Light_Idle",    (0.006, 0.006, 0.006), (lx, ly - 0.008, 0.138), "Mat_CreamCeramic", parent=root)

    # 3. Test Console (with buttons/lever) + Diagnostic Display Panel
    make_beveled_box("Testing_ControlConsole", (0.044, 0.024, 0.008), (0.000, -0.024, 0.096), "Mat_CharcoalVisor", bevel_width=0.0015, parent=root)
    make_cylinder("Console_BtnPass", 0.004, 0.004, ( 0.012, -0.024, 0.101), "Mat_StatusGreen", vertices=12, parent=root)
    make_cylinder("Console_BtnRun",  0.004, 0.004, ( 0.000, -0.024, 0.101), "Mat_StatusCyan",  vertices=12, parent=root)
    make_cylinder("Console_BtnFail", 0.004, 0.004, (-0.012, -0.024, 0.101), "Mat_StatusRed",   vertices=12, parent=root)

    make_beveled_box("Testing_DisplayPanelFrame", (0.048, 0.008, 0.038), (0.056, -0.084, 0.120), "Mat_CharcoalVisor", bevel_width=0.002, rot_deg=(12, 0, -18), parent=root)
    make_beveled_box("Testing_DisplayPanelScreen", (0.042, 0.009, 0.032), (0.056, -0.084, 0.120), "Mat_ScreenCode", bevel_width=0.0005, rot_deg=(12, 0, -18), parent=root)

    # 4. Mounted Inspection Magnifier + Small Articulated Testing Tool
    make_cylinder("Testing_MagnifierStand", 0.003, 0.032, (0.048, -0.055, 0.108), "Mat_Brass", vertices=10, rot_deg=(0, -18, 0), parent=root)
    make_torus("Testing_MagnifierRing", 0.012, 0.002, (0.038, -0.055, 0.124), "Mat_Brass", maj_seg=18, min_seg=8, rot_deg=(15, 0, 0), parent=root)
    make_cylinder("Testing_MagnifierGlass", 0.011, 0.002, (0.038, -0.055, 0.124), "Mat_ChamberGlass", vertices=16, rot_deg=(15, 0, 0), parent=root)

    make_cylinder("Testing_ArticulatedToolBase", 0.007, 0.008, (-0.048, -0.052, 0.097), "Mat_Tester_SlateBlue", vertices=14, parent=root)
    make_cylinder("Testing_ArticulatedToolArm",  0.0025, 0.028, (-0.042, -0.056, 0.110), "Mat_BrushedMetal", vertices=10, rot_deg=(0, 32, 0), parent=root)
    make_sphere("Testing_ArticulatedToolProbe",  (0.004, 0.004, 0.004), (-0.032, -0.058, 0.122), "Mat_StatusCyan", parent=root)

    # 5. Accepted Artifact Tray (+X, Mint) & Rejected Artifact Tray (-X, Coral/Red)
    make_beveled_box("Testing_AcceptedTray", (0.044, 0.052, 0.008), ( 0.096, -0.044, 0.097), "Mat_Tester_Mint",     bevel_width=0.002, parent=root)
    make_beveled_box("Testing_RejectedTray", (0.044, 0.052, 0.008), (-0.096, -0.044, 0.097), "Mat_Developer_Coral", bevel_width=0.002, parent=root)

    # 6. Error Report Printer (beside bench at -X = -0.155) + Clipboard + Decorative Tools
    px, py = -0.155, -0.055
    make_beveled_box("Testing_PrinterPedestal", (0.052, 0.052, 0.068), (px, py, 0.034), "Mat_Tester_SlateBlue", bevel_width=0.0025, parent=root)
    make_beveled_box("Testing_ErrorReportPrinter", (0.056, 0.044, 0.026), (px, py, 0.081), "Mat_CharcoalVisor", bevel_width=0.0025, parent=root)
    make_beveled_box("Testing_ErrorReportSheet", (0.034, 0.028, 0.002), (px, py - 0.026, 0.090), "Mat_PaperWarm", bevel_width=0.0005, rot_deg=(22, 0, 0), parent=root)

    make_beveled_box("Testing_ClipboardBoard", (0.028, 0.038, 0.003), (0.052, -0.032, 0.095), "Mat_DarkWood", bevel_width=0.001, rot_deg=(0, 0, 12), parent=root)
    make_beveled_box("Testing_ClipboardPaper", (0.024, 0.032, 0.002), (0.052, -0.032, 0.097), "Mat_PaperWarm", bevel_width=0.0005, rot_deg=(0, 0, 12), parent=root)
    make_beveled_box("Testing_RepairWrench", (0.008, 0.028, 0.003), (-0.052, -0.028, 0.095), "Mat_BrushedMetal", bevel_width=0.001, rot_deg=(0, 0, -18), parent=root)

    # Named Interaction Anchors (Section 5 + Legacy Aliases)
    make_anchor("AgentWorkPosition",    ( 0.000,  0.000, 0.000), root)
    make_anchor("AgentSeatPosition",    ( 0.000,  0.032, 0.042), root)
    make_anchor("TaskDropZone",         ( 0.000, -0.055, 0.168), root)
    make_anchor("DocumentPickupPoint",  ( px,     py - 0.026, 0.096), root)
    make_anchor("ArtifactOutputPoint",  ( 0.096, -0.044, 0.104), root)
    make_anchor("ChamberSlotPoint",     ( 0.000, -0.068, 0.114), root)
    make_anchor("AcceptedTrayPoint",    ( 0.096, -0.044, 0.104), root)
    make_anchor("RejectedTrayPoint",    (-0.096, -0.044, 0.104), root)
    make_anchor("HandoffReceivePoint",  (-0.096, -0.044, 0.104), root)
    # Legacy aliases
    make_anchor("Seat_Anchor",          ( 0.000,  0.000, 0.000), root)
    make_anchor("Chamber_Slot_Anchor",  ( 0.000, -0.068, 0.114), root)
    make_anchor("Pass_Tray_Anchor",     ( 0.096, -0.044, 0.104), root)
    make_anchor("Fail_Tray_Anchor",     (-0.096, -0.044, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor", ( 0.000, -0.055, 0.168), root)

    return root


def render_station_preview(station_name):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 800
    scene.render.resolution_y = 600

    world = bpy.data.worlds.get("StudioWorld") or bpy.data.worlds.new("StudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
        bg.inputs[1].default_value = 1.15

    lights = [
        ("KeyLight",  ( 0.45, -0.55, 0.55), 48.0, (1.0, 0.96, 0.90)),
        ("FillLight", (-0.45, -0.45, 0.40), 25.0, (0.85, 0.92, 1.0)),
        ("RimLight",  ( 0.00,  0.50, 0.45), 35.0, (1.0, 0.95, 0.85)),
    ]
    temp_objs = []
    for lname, loc, energy, col in lights:
        ldata = bpy.data.lights.new(name=lname, type='POINT')
        ldata.energy = energy
        ldata.color = col
        lobj = bpy.data.objects.new(name=lname, object_data=ldata)
        lobj.location = loc
        bpy.context.collection.objects.link(lobj)
        temp_objs.append(lobj)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 48
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    temp_objs.append(cam_obj)

    cam_pos = (0.30, -0.42, 0.28)
    target  = (0.00, -0.04, 0.09)
    cam_obj.location = cam_pos
    cam_obj.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_DIR, f"{station_name}_preview.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    for obj in temp_objs:
        bpy.data.objects.remove(obj, do_unlink=True)
    return out_png


def generate_all_workstations():
    stations = [
        ("Workstation_Research",  build_research_workstation),
        ("Workstation_Developer", build_developer_workstation),
        ("Workstation_Testing",   build_testing_workstation),
    ]
    report = []
    for sname, builder in stations:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root = builder()

        total_tris = 0
        total_verts = 0
        deps = bpy.context.evaluated_depsgraph_get()
        for obj in bpy.data.objects:
            if obj.type == 'MESH':
                em = obj.evaluated_get(deps).to_mesh()
                em.calc_loop_triangles()
                total_tris += len(em.loop_triangles)
                total_verts += len(em.vertices)
                obj.evaluated_get(deps).to_mesh_clear()

        preview_png = render_station_preview(sname)
        blend_path = os.path.join(BLEND_DIR, f"{sname}.blend")
        fbx_path = os.path.join(FBX_DIR, f"{sname}.fbx")

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
        report.append({
            "station": sname,
            "blend": blend_path,
            "fbx": fbx_path,
            "fbx_bytes": os.path.getsize(fbx_path),
            "vertices": total_verts,
            "triangles": total_tris,
            "preview": preview_png,
        })

    with open(os.path.join(PREVIEW_DIR, "workstations_build_report.json"), "w") as f:
        json.dump(report, f, indent=2)


if __name__ == "__main__":
    generate_all_workstations()
