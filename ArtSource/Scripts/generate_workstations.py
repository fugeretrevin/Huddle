import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Procedural Miniature Workstations Generator
# Generates:
#   1. Workstation_Research  (Desk, Chair, Bookshelf, Books, Notebooks, Magnifying Glass,
#                             Lamp, Blueprints, Document Tray, Pen Holder, Mini Globe + Anchors)
#   2. Workstation_Developer (Desk, Main Monitor, Side Monitor, Keyboard, Mouse, Chair,
#                             PC Tower, Assembly Platform, Tool Tray, Output Tray, Debug Duck + Anchors)
#   3. Workstation_Testing   (Test Bench, Testing Chamber, Status Light Tower [Pass/Fail/Warn],
#                             Inspection Wand, Clipboard, Failed Tray, Passed Tray, Display Panel,
#                             Stool, Repair Tools + Anchors)
# Plus individual standalone modular prop exports so any prop can be spawned independently!
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
    "Mat_WarmWood":            {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.50, "metallic": 0.0},
    "Mat_DarkWood":            {"color": (0.460, 0.330, 0.230, 1.0), "roughness": 0.55, "metallic": 0.0},
    "Mat_CreamCeramic":        {"color": (0.955, 0.935, 0.895, 1.0), "roughness": 0.35, "metallic": 0.0},
    "Mat_CharcoalVisor":       {"color": (0.130, 0.140, 0.170, 1.0), "roughness": 0.22, "metallic": 0.15},
    "Mat_BrushedMetal":        {"color": (0.650, 0.685, 0.725, 1.0), "roughness": 0.28, "metallic": 0.70},
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.25, "metallic": 0.80},
    "Mat_PaperWarm":           {"color": (0.980, 0.960, 0.910, 1.0), "roughness": 0.60, "metallic": 0.0},
    "Mat_BlueprintBlue":       {"color": (0.180, 0.380, 0.580, 1.0), "roughness": 0.45, "metallic": 0.0},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.30, "metallic": 0.05},
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.0},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (1.0, 0.81, 0.32), "emission_strength": 0.8},
    "Mat_ScreenCode":          {"color": (0.120, 0.240, 0.360, 1.0), "roughness": 0.15, "metallic": 0.0,  "emission": (0.18, 0.55, 0.85), "emission_strength": 0.9},
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.22, 0.85, 0.45), "emission_strength": 1.2},
    "Mat_StatusRed":           {"color": (0.920, 0.250, 0.240, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.92, 0.25, 0.24), "emission_strength": 1.2},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.98, 0.68, 0.22), "emission_strength": 1.2},
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


def make_beveled_box(name, size_xyz, location, mat_name, bevel_width=0.003, segments=2, rot_deg=(0, 0, 0), parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size_xyz[0], size_xyz[1], size_xyz[2])
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
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


def make_cylinder(name, radius, depth, location, mat_name, vertices=20, rot_deg=(0, 0, 0), parent=None):
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


def make_sphere(name, radius_xyz, location, mat_name, segments=20, rings=12, rot_deg=(0, 0, 0), parent=None):
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


def make_anchor(name, location, parent, rot_deg=(0, 0, 0)):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.02
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


def build_shared_desk_base(root, prefix, width=0.22, depth=0.12, height=0.088, top_mat="Mat_WarmWood", leg_mat="Mat_DarkWood"):
    # Desk tabletop at z = height
    make_beveled_box(f"{prefix}_DeskTop", (width, depth, 0.010), (0, -0.055, height), top_mat, bevel_width=0.003, parent=root)
    # 4 tapered/rounded legs
    lx = width * 0.42
    ly_front = -0.055 - depth * 0.38
    ly_back  = -0.055 + depth * 0.38
    leg_h = height - 0.005
    for idx, (x, y) in enumerate([(-lx, ly_front), (lx, ly_front), (-lx, ly_back), (lx, ly_back)]):
        make_cylinder(f"{prefix}_DeskLeg_{idx+1}", 0.006, leg_h, (x, y, leg_h * 0.5), leg_mat, vertices=14, parent=root)


def build_mini_chair(root, prefix, cushion_mat, location=(0, 0.025, 0)):
    cx, cy, cz = location
    # Chair base pedestal
    make_cylinder(f"{prefix}_ChairBase", 0.026, 0.005, (cx, cy, cz + 0.0025), "Mat_DarkWood", vertices=20, parent=root)
    make_cylinder(f"{prefix}_ChairStem", 0.006, 0.036, (cx, cy, cz + 0.020), "Mat_BrushedMetal", vertices=14, parent=root)
    # Plush rounded seat cushion
    make_beveled_box(f"{prefix}_ChairSeat", (0.062, 0.058, 0.012), (cx, cy, cz + 0.042), cushion_mat, bevel_width=0.004, parent=root)
    # Low curved backrest (behind agent at +Y)
    make_beveled_box(f"{prefix}_ChairBack", (0.056, 0.010, 0.038), (cx, cy + 0.026, cz + 0.068), cushion_mat, bevel_width=0.004, parent=root)


# ==============================================================================
# 1. RESEARCH WORKSTATION
# ==============================================================================

def build_research_workstation():
    root = bpy.data.objects.new("Workstation_Research", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Research", width=0.22, depth=0.12, height=0.088)
    build_mini_chair(root, "Research", "Mat_Researcher_Lavender", location=(0, 0.028, 0))

    # Side Bookshelf (to the left of the desk at +X = 0.135)
    bx, by = 0.138, -0.040
    make_beveled_box("Research_BookshelfFrame", (0.065, 0.042, 0.155), (bx, by, 0.0775), "Mat_WarmWood", bevel_width=0.0025, parent=root)
    for sz in [0.048, 0.096]:
        make_beveled_box(f"Research_ShelfTier_{int(sz*100)}", (0.058, 0.040, 0.005), (bx, by - 0.002, sz), "Mat_DarkWood", bevel_width=0.001, parent=root)

    # Colorful Books inside bookshelf & stacked on desk
    book_specs = [
        ("Book_Lavender", (bx - 0.016, by - 0.004, 0.068), (0.010, 0.030, 0.034), "Mat_Researcher_Lavender", (0, 0, 0)),
        ("Book_Teal",     (bx - 0.004, by - 0.004, 0.070), (0.011, 0.030, 0.038), "Mat_Researcher_Teal",     (0, 0, 0)),
        ("Book_Amber",    (bx + 0.010, by - 0.004, 0.067), (0.010, 0.030, 0.032), "Mat_Brass",               (0, -10, 0)),
        ("Book_Coral",    (bx - 0.012, by - 0.004, 0.116), (0.012, 0.030, 0.034), "Mat_Developer_Coral",     (0, 0, 0)),
        ("Book_Mint",     (bx + 0.004, by - 0.004, 0.115), (0.011, 0.030, 0.032), "Mat_Tester_Mint",         (0, 6, 0)),
    ]
    for bname, bloc, bsize, bmat, brot in book_specs:
        make_beveled_box(bname, bsize, bloc, bmat, bevel_width=0.0015, rot_deg=brot, parent=root)

    # Open Notebook on Desk Center
    make_beveled_box("Research_OpenNotebook_L", (0.032, 0.042, 0.004), ( 0.016, -0.048, 0.095), "Mat_PaperWarm", bevel_width=0.001, rot_deg=(0, -5, 0), parent=root)
    make_beveled_box("Research_OpenNotebook_R", (0.032, 0.042, 0.004), (-0.016, -0.048, 0.095), "Mat_PaperWarm", bevel_width=0.001, rot_deg=(0,  5, 0), parent=root)
    # Closed Notebook
    make_beveled_box("Research_ClosedNotebook", (0.036, 0.046, 0.007), (0.068, -0.045, 0.096), "Mat_Researcher_Lavender", bevel_width=0.0015, rot_deg=(0, 0, 14), parent=root)

    # Magnifying Glass on Desk
    make_cylinder("Research_MagnifierRim", 0.013, 0.003, (-0.055, -0.042, 0.096), "Mat_Brass", vertices=20, parent=root)
    make_cylinder("Research_MagnifierLens", 0.010, 0.0035, (-0.055, -0.042, 0.096), "Mat_Researcher_Teal", vertices=18, parent=root)
    make_cylinder("Research_MagnifierHandle", 0.0028, 0.022, (-0.064, -0.028, 0.096), "Mat_DarkWood", vertices=12, rot_deg=(90, 0, -28), parent=root)

    # Miniature Desk Lamp (Arched Brass Arm + Glowing Shade)
    make_cylinder("Research_LampBase", 0.016, 0.005, (-0.082, -0.082, 0.095), "Mat_Brass", vertices=18, parent=root)
    make_cylinder("Research_LampStem", 0.003, 0.052, (-0.082, -0.082, 0.122), "Mat_Brass", vertices=12, rot_deg=(-12, 0, 0), parent=root)
    make_sphere("Research_LampShade", (0.016, 0.016, 0.012), (-0.076, -0.068, 0.148), "Mat_Researcher_Teal", parent=root)
    make_sphere("Research_LampBulb", (0.008, 0.008, 0.008), (-0.076, -0.068, 0.142), "Mat_LampGlow", parent=root)

    # Blueprint Sheet & Rolled Blueprint
    make_beveled_box("Research_BlueprintSheet", (0.048, 0.034, 0.002), (-0.045, -0.078, 0.094), "Mat_BlueprintBlue", bevel_width=0.0005, rot_deg=(0, 0, -8), parent=root)
    make_cylinder("Research_RolledBlueprint", 0.0055, 0.048, (-0.045, -0.095, 0.098), "Mat_BlueprintBlue", vertices=16, rot_deg=(0, 90, 5), parent=root)

    # Document Tray + Pen Holder + Mini Celestial Globe Decoration
    make_beveled_box("Research_DocumentTray", (0.044, 0.052, 0.008), (0.076, -0.078, 0.097), "Mat_DarkWood", bevel_width=0.002, parent=root)
    make_cylinder("Research_PenHolder", 0.009, 0.022, (0.036, -0.085, 0.104), "Mat_Researcher_Lavender", vertices=16, parent=root)
    make_cylinder("Research_Pen_1", 0.002, 0.028, (0.034, -0.085, 0.114), "Mat_Developer_Coral", vertices=10, rot_deg=(10, 8, 0), parent=root)
    make_cylinder("Research_Pen_2", 0.002, 0.028, (0.038, -0.085, 0.114), "Mat_Brass", vertices=10, rot_deg=(-8, -10, 0), parent=root)
    # Mini Globe on top of Bookshelf
    make_cylinder("Research_GlobeStand", 0.012, 0.004, (bx, by, 0.157), "Mat_Brass", vertices=16, parent=root)
    make_sphere("Research_GlobeOrb", (0.016, 0.016, 0.016), (bx, by, 0.176), "Mat_Researcher_Teal", parent=root)

    # Named Interaction Anchors
    make_anchor("Seat_Anchor",            ( 0.000,  0.000, 0.000), root)
    make_anchor("Work_Hands_Anchor",      ( 0.000, -0.045, 0.098), root)
    make_anchor("Blueprint_Spawn_Anchor", (-0.045, -0.078, 0.098), root)
    make_anchor("Document_Tray_Anchor",   ( 0.076, -0.078, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor",   ( 0.000, -0.055, 0.140), root)
    make_anchor("Artifact_Drop_Anchor",   (-0.055, -0.055, 0.102), root)

    return root


# ==============================================================================
# 2. DEVELOPER WORKSTATION
# ==============================================================================

def build_developer_workstation():
    root = bpy.data.objects.new("Workstation_Developer", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Developer", width=0.24, depth=0.125, height=0.088)
    build_mini_chair(root, "Developer", "Mat_Developer_Coral", location=(0, 0.028, 0))

    # Primary Widescreen Monitor (with separate ScreenSurface for dynamic text/material updates)
    make_cylinder("Developer_MonitorBase", 0.018, 0.004, (0.0, -0.088, 0.095), "Mat_CharcoalVisor", vertices=18, parent=root)
    make_cylinder("Developer_MonitorNeck", 0.0045, 0.032, (0.0, -0.090, 0.110), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Developer_MainMonitorFrame", (0.092, 0.008, 0.056), (0.0, -0.084, 0.142), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    make_beveled_box("Developer_MainMonitorScreen", (0.084, 0.003, 0.048), (0.0, -0.080, 0.142), "Mat_ScreenCode", bevel_width=0.0005, parent=root)

    # Secondary Vertical Diagnostic Screen (at +X = 0.068)
    make_cylinder("Developer_SideMonitorNeck", 0.0035, 0.028, (0.068, -0.080, 0.108), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Developer_SideMonitorFrame", (0.038, 0.007, 0.054), (0.068, -0.074, 0.136), "Mat_CharcoalVisor", bevel_width=0.002, rot_deg=(0, 0, -22), parent=root)
    make_beveled_box("Developer_SideMonitorScreen", (0.032, 0.003, 0.048), (0.066, -0.071, 0.136), "Mat_ScreenCode", bevel_width=0.0005, rot_deg=(0, 0, -22), parent=root)

    # Miniature Mechanical Keyboard + Ergonomic Mouse
    make_beveled_box("Developer_KeyboardBase", (0.062, 0.026, 0.005), (0.0, -0.044, 0.095), "Mat_CharcoalVisor", bevel_width=0.0015, parent=root)
    make_beveled_box("Developer_KeycapsMain", (0.046, 0.018, 0.003), (0.004, -0.044, 0.098), "Mat_CreamCeramic", bevel_width=0.001, parent=root)
    make_beveled_box("Developer_KeycapAccent", (0.009, 0.018, 0.0035), (-0.023, -0.044, 0.098), "Mat_Developer_Coral", bevel_width=0.001, parent=root)
    make_sphere("Developer_Mouse", (0.008, 0.012, 0.005), (-0.046, -0.044, 0.095), "Mat_CreamCeramic", parent=root)

    # Miniature Computer Tower beside Desk (-X = -0.145)
    tx, ty = -0.145, -0.050
    make_beveled_box("Developer_PCTowerChassis", (0.044, 0.082, 0.105), (tx, ty, 0.0525), "Mat_CharcoalVisor", bevel_width=0.003, parent=root)
    make_beveled_box("Developer_PCTowerLedBar", (0.008, 0.004, 0.078), (tx, ty - 0.041, 0.055), "Mat_Developer_Amber", bevel_width=0.001, parent=root)
    make_beveled_box("Developer_PCTowerWindow", (0.003, 0.056, 0.068), (tx + 0.022, ty, 0.058), "Mat_ScreenCode", bevel_width=0.0005, parent=root)

    # Component Assembly Platform + Tool Tray + Output Document Tray + Debug Duck Decor
    make_cylinder("Developer_AssemblyPlatform", 0.020, 0.006, (-0.082, -0.048, 0.096), "Mat_BrushedMetal", vertices=22, parent=root)
    make_cylinder("Developer_AssemblyPadGlow", 0.014, 0.007, (-0.082, -0.048, 0.097), "Mat_Developer_Amber", vertices=20, parent=root)
    make_beveled_box("Developer_ToolTray", (0.032, 0.038, 0.006), (-0.082, -0.088, 0.096), "Mat_Developer_Coral", bevel_width=0.0015, parent=root)
    make_beveled_box("Developer_OutputTray", (0.042, 0.048, 0.008), (0.086, -0.044, 0.097), "Mat_WarmWood", bevel_width=0.002, parent=root)
    # Cute Debug Rubber Duck on PC Tower top
    make_sphere("Developer_DebugDuckBody", (0.009, 0.012, 0.008), (tx, ty - 0.015, 0.112), "Mat_Developer_Amber", parent=root)
    make_sphere("Developer_DebugDuckHead", (0.006, 0.006, 0.006), (tx, ty - 0.022, 0.121), "Mat_Developer_Amber", parent=root)

    # Named Interaction Anchors
    make_anchor("Seat_Anchor",            ( 0.000,  0.000, 0.000), root)
    make_anchor("Keyboard_Hands_Anchor",  ( 0.000, -0.044, 0.099), root)
    make_anchor("Monitor_Screen_Anchor",  ( 0.000, -0.079, 0.142), root)
    make_anchor("Task_Receive_Anchor",    ( 0.086, -0.044, 0.104), root)
    make_anchor("Artifact_Output_Anchor", (-0.082, -0.048, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor",   ( 0.000, -0.055, 0.145), root)

    return root


# ==============================================================================
# 3. TESTING WORKSTATION
# ==============================================================================

def build_testing_workstation():
    root = bpy.data.objects.new("Workstation_Testing", None)
    bpy.context.collection.objects.link(root)

    build_shared_desk_base(root, "Testing", width=0.24, depth=0.125, height=0.088, top_mat="Mat_BrushedMetal", leg_mat="Mat_Tester_SlateBlue")
    build_mini_chair(root, "Testing", "Mat_Tester_Mint", location=(0, 0.028, 0))

    # Small Testing Chamber / Diagnostic Apparatus in Desk Center-Back
    make_beveled_box("Testing_ChamberBase", (0.076, 0.064, 0.012), (0.0, -0.068, 0.098), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    make_beveled_box("Testing_ChamberTop",  (0.074, 0.062, 0.010), (0.0, -0.068, 0.162), "Mat_Tester_SlateBlue", bevel_width=0.002, parent=root)
    # 4 Corner Pillars + Scanner Ring inside Chamber
    for idx, (px, py) in enumerate([(-0.032, -0.042), (0.032, -0.042), (-0.032, -0.094), (0.032, -0.094)]):
        make_cylinder(f"Testing_ChamberPillar_{idx+1}", 0.0035, 0.054, (px, py, 0.131), "Mat_BrushedMetal", vertices=12, parent=root)
    make_cylinder("Testing_ScannerRing", 0.024, 0.006, (0.0, -0.068, 0.132), "Mat_Tester_Mint", vertices=24, parent=root)

    # Independent Status Light Tower (Light_Pass, Light_Warn, Light_Fail as separate named objects!)
    lx, ly = -0.056, -0.088
    make_cylinder("Testing_LightTowerPole", 0.0035, 0.052, (lx, ly, 0.118), "Mat_BrushedMetal", vertices=12, parent=root)
    make_beveled_box("Testing_LightHousing", (0.016, 0.016, 0.044), (lx, ly, 0.156), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
    make_sphere("Light_Pass", (0.006, 0.006, 0.006), (lx, ly - 0.007, 0.170), "Mat_StatusGreen", parent=root)
    make_sphere("Light_Warn", (0.006, 0.006, 0.006), (lx, ly - 0.007, 0.156), "Mat_StatusAmber", parent=root)
    make_sphere("Light_Fail", (0.006, 0.006, 0.006), (lx, ly - 0.007, 0.142), "Mat_StatusRed",   parent=root)

    # Diagnostic Display Panel
    make_beveled_box("Testing_DisplayPanelFrame", (0.046, 0.008, 0.036), (0.058, -0.084, 0.118), "Mat_CharcoalVisor", bevel_width=0.002, rot_deg=(12, 0, -18), parent=root)
    make_beveled_box("Testing_DisplayPanelScreen", (0.040, 0.003, 0.030), (0.057, -0.081, 0.118), "Mat_ScreenCode", bevel_width=0.0005, rot_deg=(12, 0, -18), parent=root)

    # Passed-Test Tray (+X = +0.086, Mint) & Failed-Test Tray (-X = -0.086, Red/Coral)
    make_beveled_box("Testing_PassedTray", (0.042, 0.050, 0.008), ( 0.088, -0.044, 0.097), "Mat_Tester_Mint",     bevel_width=0.002, parent=root)
    make_beveled_box("Testing_FailedTray", (0.042, 0.050, 0.008), (-0.088, -0.044, 0.097), "Mat_Developer_Coral", bevel_width=0.002, parent=root)

    # Miniature Clipboard + Inspection Wand + Repair Tools on Bench
    make_beveled_box("Testing_ClipboardBoard", (0.028, 0.038, 0.003), (0.044, -0.038, 0.095), "Mat_DarkWood", bevel_width=0.001, rot_deg=(0, 0, 12), parent=root)
    make_beveled_box("Testing_ClipboardPaper", (0.024, 0.032, 0.002), (0.044, -0.038, 0.097), "Mat_PaperWarm", bevel_width=0.0005, rot_deg=(0, 0, 12), parent=root)
    make_cylinder("Testing_InspectionWand", 0.003, 0.036, (-0.048, -0.038, 0.096), "Mat_Tester_SlateBlue", vertices=12, rot_deg=(90, 0, 24), parent=root)
    make_sphere("Testing_WandTip", (0.005, 0.005, 0.005), (-0.055, -0.053, 0.096), "Mat_StatusGreen", parent=root)
    make_beveled_box("Testing_RepairWrench", (0.008, 0.030, 0.003), (-0.034, -0.040, 0.095), "Mat_BrushedMetal", bevel_width=0.001, rot_deg=(0, 0, -15), parent=root)

    # Named Interaction Anchors
    make_anchor("Seat_Anchor",          ( 0.000,  0.000, 0.000), root)
    make_anchor("Chamber_Slot_Anchor",  ( 0.000, -0.068, 0.115), root)
    make_anchor("Pass_Tray_Anchor",     ( 0.088, -0.044, 0.104), root)
    make_anchor("Fail_Tray_Anchor",     (-0.088, -0.044, 0.104), root)
    make_anchor("TaskCard_Drop_Anchor", ( 0.000, -0.055, 0.145), root)

    return root


def render_station_preview(station_name):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 720
    scene.render.resolution_y = 540

    world = bpy.data.worlds.get("StudioWorld") or bpy.data.worlds.new("StudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
        bg.inputs[1].default_value = 1.1

    lights = [
        ("KeyLight",  ( 0.45, -0.55, 0.55), 45.0, (1.0, 0.96, 0.90)),
        ("FillLight", (-0.45, -0.45, 0.40), 22.0, (0.85, 0.92, 1.0)),
        ("RimLight",  ( 0.00,  0.50, 0.45), 32.0, (1.0, 0.95, 0.85)),
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
    cam_data.lens = 50
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    temp_objs.append(cam_obj)

    cam_pos = (0.28, -0.38, 0.26)
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

        # Count total triangles across child meshes
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
