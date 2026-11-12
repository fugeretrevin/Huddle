import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Phase 2 Procedural Props Generator
# Generates:
#   1. Physical Kanban Task Board, Task Cards, Badges & Dependency Links (Section 6)
#   2. Paper, Documents, Approval Props & Collaboration Handoff Items (Section 7)
#   3. Spatial Diagram Architecture Kit (Section 9)
#   4. Workspace Decoration Package (Section 10)
# Exports editable .blend files, Unity-ready .fbx files, and studio contact sheets.
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
BLEND_ROOT = os.path.join(PROJECT_ROOT, "ArtSource", "Blender")
FBX_ROOT = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")

SUBDIRS = {
    "TaskBoard":   (os.path.join(BLEND_ROOT, "TaskBoard"),       os.path.join(FBX_ROOT, "TaskBoard")),
    "Documents":   (os.path.join(BLEND_ROOT, "Documents"),       os.path.join(FBX_ROOT, "Documents")),
    "Diagrams":    (os.path.join(BLEND_ROOT, "SpatialDiagrams"), os.path.join(FBX_ROOT, "Diagrams")),
    "Decorations": (os.path.join(BLEND_ROOT, "Decorations"),     os.path.join(FBX_ROOT, "Decorations")),
}

for bdir, fdir in SUBDIRS.values():
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
    "Mat_WarmWood":            {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.48, "metallic": 0.0},
    "Mat_DarkWood":            {"color": (0.460, 0.330, 0.230, 1.0), "roughness": 0.52, "metallic": 0.0},
    "Mat_CreamCeramic":        {"color": (0.955, 0.935, 0.895, 1.0), "roughness": 0.32, "metallic": 0.0},
    "Mat_WarmBeige":           {"color": (0.875, 0.835, 0.775, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_CharcoalVisor":       {"color": (0.130, 0.140, 0.170, 1.0), "roughness": 0.22, "metallic": 0.15},
    "Mat_BrushedMetal":        {"color": (0.650, 0.685, 0.725, 1.0), "roughness": 0.28, "metallic": 0.70},
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.24, "metallic": 0.82},
    "Mat_PaperWarm":           {"color": (0.985, 0.968, 0.925, 1.0), "roughness": 0.58, "metallic": 0.0},
    "Mat_PaperManila":         {"color": (0.930, 0.850, 0.680, 1.0), "roughness": 0.55, "metallic": 0.0},
    "Mat_BlueprintBlue":       {"color": (0.175, 0.385, 0.610, 1.0), "roughness": 0.45, "metallic": 0.0},
    "Mat_CardboardBox":        {"color": (0.760, 0.580, 0.400, 1.0), "roughness": 0.62, "metallic": 0.0},
    "Mat_Terracotta":          {"color": (0.780, 0.430, 0.310, 1.0), "roughness": 0.56, "metallic": 0.0},
    "Mat_PlantGreen":          {"color": (0.340, 0.690, 0.440, 1.0), "roughness": 0.40, "metallic": 0.0},
    "Mat_FeltSlate":           {"color": (0.280, 0.310, 0.360, 1.0), "roughness": 0.72, "metallic": 0.0},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.40, "metallic": 0.0},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.28, "metallic": 0.05, "emission": (0.28, 0.74, 0.68), "emission_strength": 0.45},
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.0},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (1.0, 0.81, 0.32), "emission_strength": 0.75},
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.22, 0.85, 0.45), "emission_strength": 1.1},
    "Mat_StatusRed":           {"color": (0.920, 0.250, 0.240, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.92, 0.25, 0.24), "emission_strength": 1.1},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.98, 0.68, 0.22), "emission_strength": 1.1},
    "Mat_ScreenCode":          {"color": (0.120, 0.240, 0.360, 1.0), "roughness": 0.15, "metallic": 0.0,  "emission": (0.18, 0.55, 0.85), "emission_strength": 0.85},
    "Mat_LampGlow":            {"color": (1.000, 0.950, 0.800, 1.0), "roughness": 0.15, "metallic": 0.0,  "emission": (1.00, 0.94, 0.76), "emission_strength": 1.4},
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


def make_cyl(name, radius, depth, location, mat_name, vertices=18, rot_deg=(0, 0, 0), parent=None):
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


def make_cone(name, r1, r2, depth, location, mat_name, vertices=18, rot_deg=(0, 0, 0), parent=None):
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


def make_torus(name, maj_r, min_r, location, mat_name, maj_seg=20, min_seg=10, rot_deg=(0, 0, 0), parent=None):
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
    emp.empty_display_size = 0.015
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


# ==============================================================================
# SECTION 6 — PHYSICAL KANBAN TASK BOARD & TASK CARDS
# ==============================================================================

def build_task_board_assets():
    assets = {}

    # 1. TaskBoard_KanbanFrame (Tabletop standing or angled board: 42cm wide x 28cm tall)
    def make_kanban_board():
        root = bpy.data.objects.new("TaskBoard_KanbanFrame", None)
        bpy.context.collection.objects.link(root)
        # Outer warm-wood frame + charcoal cork/felt backing
        make_box("Board_Backing", (0.420, 0.014, 0.260), (0, 0, 0.145), "Mat_FeltSlate", bevel_width=0.004, parent=root)
        make_box("Board_TopRail", (0.432, 0.022, 0.016), (0, 0, 0.278), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_BotRail", (0.432, 0.026, 0.018), (0, 0, 0.012), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_LeftRail", (0.016, 0.022, 0.272), ( 0.210, 0, 0.145), "Mat_WarmWood", bevel_width=0.003, parent=root)
        make_box("Board_RightRail", (0.016, 0.022, 0.272), (-0.210, 0, 0.145), "Mat_WarmWood", bevel_width=0.003, parent=root)
        # Support feet
        for side, sx in [("L", 0.165), ("R", -0.165)]:
            make_box(f"Board_StandFoot_{side}", (0.036, 0.095, 0.012), (sx, 0.005, 0.006), "Mat_DarkWood", bevel_width=0.003, parent=root)
            make_cyl(f"Board_GrabHandle_{side}", 0.005, 0.048, (sx * 1.30, 0, 0.145), "Mat_Brass", vertices=12, parent=root)

        # 4 Columns: Queued (+X=0.147), InProgress (+X=0.049), Blocked (-X=-0.049), Completed (-X=-0.147)
        cols = [
            ("Queued",     0.147, "Mat_Tester_SlateBlue"),
            ("InProgress", 0.049, "Mat_Developer_Coral"),
            ("Blocked",   -0.049, "Mat_StatusAmber"),
            ("Completed", -0.147, "Mat_Tester_Mint"),
        ]
        for cname, cx, cmat in cols:
            # Header banner plaque (clean for TMP runtime label)
            make_box(f"Header_{cname}", (0.088, 0.008, 0.028), (cx, -0.008, 0.248), cmat, bevel_width=0.002, parent=root)
            make_anchor(f"HeaderLabel_{cname}_Anchor", (cx, -0.013, 0.248), root)
            # Column lane recessed panel
            make_box(f"ColumnLane_{cname}", (0.088, 0.004, 0.198), (cx, -0.006, 0.125), "Mat_CharcoalVisor", bevel_width=0.002, parent=root)
            make_anchor(f"ColumnDropZone_{cname}", (cx, -0.012, 0.125), root)
            # 4 vertical card snap sockets per column
            for row_idx in range(4):
                sz = 0.198 - row_idx * 0.048
                make_box(f"SlotGuide_{cname}_{row_idx+1}", (0.078, 0.003, 0.003), (cx, -0.008, sz - 0.018), "Mat_BrushedMetal", bevel_width=0.0005, parent=root)
                make_anchor(f"Socket_{cname}_{row_idx+1}", (cx, -0.012, sz), root)

        # Column divider bars
        for dx in [0.098, 0.000, -0.098]:
            make_box(f"Divider_{int(dx*1000)}", (0.005, 0.010, 0.236), (dx, -0.006, 0.138), "Mat_WarmWood", bevel_width=0.001, parent=root)

        make_anchor("ExpandedCard_Inspect_Anchor", (0.0, -0.085, 0.165), root)
        return root

    # 2. TaskCard_Grabbable (Reusable physical task card: 7.2cm x 4.4cm x 0.4cm)
    def make_task_card():
        root = bpy.data.objects.new("TaskCard_Grabbable", None)
        bpy.context.collection.objects.link(root)
        # Main card body (centered at origin for natural hand grabbing)
        make_box("Card_Body", (0.072, 0.004, 0.044), (0, 0, 0), "Mat_PaperWarm", bevel_width=0.0012, parent=root)
        # Role-tint header strip at top (+Z)
        make_box("Card_RoleHeaderStrip", (0.068, 0.0048, 0.009), (0, -0.0005, 0.0155), "Mat_Researcher_Lavender", bevel_width=0.0008, parent=root)
        # Agent ownership indicator disc (top-left)
        make_cyl("Card_OwnerIndicator", 0.0045, 0.0055, (0.026, -0.001, 0.0155), "Mat_Researcher_Teal", vertices=16, rot_deg=(90, 0, 0), parent=root)
        # Priority gem indicator (top-right)
        make_sphere("Card_PriorityIndicator", (0.004, 0.0025, 0.004), (-0.026, -0.002, 0.0155), "Mat_Developer_Amber", parent=root)
        # Status dot marker (bottom-right)
        make_cyl("Card_StatusMarker", 0.0038, 0.005, (-0.026, -0.001, -0.014), "Mat_StatusGreen", vertices=14, rot_deg=(90, 0, 0), parent=root)
        # Dependency pin socket (top center)
        make_torus("Card_PinRing", 0.003, 0.001, (0, -0.002, 0.019), "Mat_Brass", maj_seg=14, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        # Paperclip attachment detail on side
        make_box("Card_AttachmentClip", (0.004, 0.0055, 0.014), (0.032, -0.0005, -0.004), "Mat_BrushedMetal", bevel_width=0.0008, parent=root)

        # Named anchors for runtime TextMeshPro & attachments
        make_anchor("Card_Title_Anchor",      ( 0.000, -0.003,  0.003), root)
        make_anchor("Card_Subtitle_Anchor",   ( 0.000, -0.003, -0.009), root)
        make_anchor("Card_Pin_Anchor",        ( 0.000, -0.004,  0.019), root)
        make_anchor("Card_Attachment_Anchor", ( 0.032, -0.003, -0.004), root)
        return root

    # 3. TaskBoard_PriorityBadge (Set of priority indicator tokens)
    def make_priority_badge():
        root = bpy.data.objects.new("TaskBoard_PriorityBadge", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Badge_Base", 0.008, 0.003, (0, 0, 0.0015), "Mat_Brass", vertices=16, parent=root)
        make_cone("Badge_Gem", 0.006, 0.002, 0.005, (0, 0, 0.005), "Mat_Developer_Coral", vertices=6, parent=root)
        return root

    # 4. TaskBoard_StatusMarker (Magnetic status puck)
    def make_status_marker():
        root = bpy.data.objects.new("TaskBoard_StatusMarker", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Status_Rim", 0.009, 0.003, (0, 0, 0.0015), "Mat_CharcoalVisor", vertices=18, parent=root)
        make_sphere("Status_Dome", (0.007, 0.007, 0.004), (0, 0, 0.003), "Mat_StatusGreen", parent=root)
        return root

    # 5. TaskBoard_OwnerIcon (Agent role badge token)
    def make_owner_icon():
        root = bpy.data.objects.new("TaskBoard_OwnerIcon", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Owner_Disc", 0.010, 0.003, (0, 0, 0.0015), "Mat_CreamCeramic", vertices=20, parent=root)
        make_torus("Owner_Rim", 0.010, 0.0015, (0, 0, 0.002), "Mat_Researcher_Lavender", maj_seg=20, min_seg=8, parent=root)
        make_sphere("Owner_Emblem", (0.005, 0.005, 0.003), (0, 0, 0.0035), "Mat_Researcher_Teal", parent=root)
        return root

    # 6. TaskBoard_DependencyLink (Two brass pushpins + connecting cord)
    def make_dependency_link():
        root = bpy.data.objects.new("TaskBoard_DependencyLink", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Pin_Start", 0.004, 0.008, (-0.040, 0, 0.004), "Mat_Brass", vertices=14, parent=root)
        make_cyl("Pin_End",   0.004, 0.008, ( 0.040, 0, 0.004), "Mat_Brass", vertices=14, parent=root)
        make_cyl("Dependency_Cord", 0.0015, 0.080, (0, 0, 0.006), "Mat_Developer_Coral", vertices=10, rot_deg=(0, 90, 0), parent=root)
        make_anchor("Pin_Start_Anchor", (-0.040, 0, 0), root)
        make_anchor("Pin_End_Anchor",   ( 0.040, 0, 0), root)
        return root

    assets["TaskBoard_KanbanFrame"]    = make_kanban_board
    assets["TaskCard_Grabbable"]       = make_task_card
    assets["TaskBoard_PriorityBadge"]  = make_priority_badge
    assets["TaskBoard_StatusMarker"]   = make_status_marker
    assets["TaskBoard_OwnerIcon"]      = make_owner_icon
    assets["TaskBoard_DependencyLink"] = make_dependency_link
    return assets


# ==============================================================================
# SECTION 7 — PAPER, DOCUMENTS, APPROVAL PROPS & COLLABORATION ITEMS
# ==============================================================================

def build_document_assets():
    assets = {}

    def add_doc_anchors(root, w=0.056, h=0.076):
        make_anchor("Content_Surface_Anchor", (0, 0, 0.002), root)
        make_anchor("Title_Anchor",           (0, -h * 0.32, 0.002), root)
        make_anchor("Stamp_Target_Anchor",    (-w * 0.25, h * 0.28, 0.002), root)
        make_anchor("Grab_Pivot_Anchor",      (0, h * 0.42, 0.000), root)

    # 1. Doc_SmallPaper (Held paper sheet with gentle corner lift)
    def make_small_paper():
        root = bpy.data.objects.new("Doc_SmallPaper", None)
        bpy.context.collection.objects.link(root)
        sheet = make_box("Paper_Sheet", (0.056, 0.076, 0.0025), (0, 0, 0.0012), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        # Subtle upward curl on bottom-left corner
        for v in sheet.data.vertices:
            if v.co.x < -0.015 and v.co.y > 0.020:
                v.co.z += 0.0025
        make_box("Paper_HeaderBar", (0.048, 0.008, 0.003), (0, -0.028, 0.0015), "Mat_Developer_Amber", bevel_width=0.0004, parent=root)
        add_doc_anchors(root)
        return root

    # 2. Doc_ExpandedFrame (World-space expanded inspection/approval frame: 26cm x 32cm)
    def make_expanded_frame():
        root = bpy.data.objects.new("Doc_ExpandedFrame", None)
        bpy.context.collection.objects.link(root)
        make_box("Frame_Board", (0.260, 0.010, 0.320), (0, 0, 0), "Mat_DarkWood", bevel_width=0.003, parent=root)
        make_box("Frame_PaperCanvas", (0.236, 0.004, 0.270), (0, -0.005, 0.012), "Mat_PaperWarm", bevel_width=0.001, parent=root)
        make_box("Frame_TopGrabBar", (0.140, 0.014, 0.016), (0, -0.004, 0.164), "Mat_Brass", bevel_width=0.003, parent=root)
        # 3 Physical Action Button Pads at bottom (Approve, Defer, Reject)
        make_box("Btn_Approve", (0.062, 0.008, 0.024), ( 0.072, -0.006, -0.136), "Mat_Tester_Mint", bevel_width=0.002, parent=root)
        make_box("Btn_Defer",   (0.062, 0.008, 0.024), ( 0.000, -0.006, -0.136), "Mat_Developer_Amber", bevel_width=0.002, parent=root)
        make_box("Btn_Reject",  (0.062, 0.008, 0.024), (-0.072, -0.006, -0.136), "Mat_Developer_Coral", bevel_width=0.002, parent=root)
        make_anchor("Content_Surface_Anchor", (0, -0.008, 0.012), root)
        make_anchor("Btn_Approve_Anchor",     ( 0.072, -0.011, -0.136), root)
        make_anchor("Btn_Defer_Anchor",       ( 0.000, -0.011, -0.136), root)
        make_anchor("Btn_Reject_Anchor",      (-0.072, -0.011, -0.136), root)
        make_anchor("Stamp_Target_Anchor",    (-0.070, -0.008, -0.085), root)
        return root

    # 3. Doc_ApprovalPaper (Official decision sheet with brass seal ring)
    def make_approval_paper():
        root = bpy.data.objects.new("Doc_ApprovalPaper", None)
        bpy.context.collection.objects.link(root)
        make_box("Approval_Sheet", (0.060, 0.082, 0.0025), (0, 0, 0.0012), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("Approval_TopBanner", (0.052, 0.010, 0.0032), (0, -0.030, 0.0015), "Mat_Brass", bevel_width=0.0005, parent=root)
        make_cyl("Approval_SealMedallion", 0.008, 0.0035, (-0.016, 0.024, 0.002), "Mat_Developer_Coral", vertices=18, parent=root)
        add_doc_anchors(root, 0.060, 0.082)
        return root

    # 4. Doc_ResearchDocument (Research synthesis sheet with lavender header & diagram inset)
    def make_research_doc():
        root = bpy.data.objects.new("Doc_ResearchDocument", None)
        bpy.context.collection.objects.link(root)
        make_box("Research_Sheet", (0.058, 0.078, 0.0025), (0, 0, 0.0012), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("Research_Header", (0.050, 0.009, 0.0032), (0, -0.029, 0.0015), "Mat_Researcher_Lavender", bevel_width=0.0005, parent=root)
        make_box("Research_ChartBox", (0.022, 0.022, 0.0032), (0.012, 0.004, 0.0015), "Mat_Researcher_Teal", bevel_width=0.0005, parent=root)
        add_doc_anchors(root, 0.058, 0.078)
        return root

    # 5. Doc_TestReport (QA test results document with mint header & status pills)
    def make_test_report():
        root = bpy.data.objects.new("Doc_TestReport", None)
        bpy.context.collection.objects.link(root)
        make_box("TestReport_Sheet", (0.058, 0.078, 0.0025), (0, 0, 0.0012), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("TestReport_Header", (0.050, 0.009, 0.0032), (0, -0.029, 0.0015), "Mat_Tester_Mint", bevel_width=0.0005, parent=root)
        for idx, py in enumerate([-0.010, 0.004, 0.018]):
            make_cyl(f"TestReport_CheckDot_{idx+1}", 0.0035, 0.0032, (0.018, py, 0.0016), "Mat_StatusGreen", vertices=14, parent=root)
        add_doc_anchors(root, 0.058, 0.078)
        return root

    # 6. Doc_CodeReviewSheet (Landscape diff review sheet with coral header)
    def make_code_review():
        root = bpy.data.objects.new("Doc_CodeReviewSheet", None)
        bpy.context.collection.objects.link(root)
        make_box("CodeReview_Sheet", (0.078, 0.058, 0.0025), (0, 0, 0.0012), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("CodeReview_Header", (0.070, 0.008, 0.0032), (0, -0.020, 0.0015), "Mat_Developer_Coral", bevel_width=0.0005, parent=root)
        make_box("CodeReview_PaneL", (0.032, 0.034, 0.0030), ( 0.018, 0.004, 0.0015), "Mat_CharcoalVisor", bevel_width=0.0005, parent=root)
        make_box("CodeReview_PaneR", (0.032, 0.034, 0.0030), (-0.018, 0.004, 0.0015), "Mat_CharcoalVisor", bevel_width=0.0005, parent=root)
        add_doc_anchors(root, 0.078, 0.058)
        return root

    # 7. Doc_BlueprintSheet (Unrolled architectural blueprint with cream border & node emblems)
    def make_blueprint_sheet():
        root = bpy.data.objects.new("Doc_BlueprintSheet", None)
        bpy.context.collection.objects.link(root)
        make_box("Blueprint_Base", (0.088, 0.062, 0.0025), (0, 0, 0.0012), "Mat_BlueprintBlue", bevel_width=0.0006, parent=root)
        make_box("Blueprint_TitleBlock", (0.026, 0.014, 0.0032), (-0.026, 0.019, 0.0015), "Mat_CreamCeramic", bevel_width=0.0004, parent=root)
        make_torus("Blueprint_CompassRing", 0.012, 0.0012, (0.014, -0.004, 0.002), "Mat_CreamCeramic", maj_seg=20, min_seg=8, parent=root)
        add_doc_anchors(root, 0.088, 0.062)
        return root

    # 8. Doc_RolledBlueprint (Rolled blueprint tube with brass clasp band)
    def make_rolled_blueprint():
        root = bpy.data.objects.new("Doc_RolledBlueprint", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Rolled_Tube", 0.0085, 0.078, (0, 0, 0.0085), "Mat_BlueprintBlue", vertices=20, rot_deg=(0, 90, 0), parent=root)
        make_cyl("Rolled_Core", 0.0055, 0.080, (0, 0, 0.0085), "Mat_PaperWarm", vertices=16, rot_deg=(0, 90, 0), parent=root)
        make_torus("Rolled_BrassBand", 0.009, 0.0018, (0, 0, 0.0085), "Mat_Brass", maj_seg=20, min_seg=8, rot_deg=(0, 90, 0), parent=root)
        make_anchor("Grab_Pivot_Anchor", (0, 0, 0.0085), root)
        return root

    # 9. Doc_Folder (Manila tabbed dossier folder with paper stack inside)
    def make_folder():
        root = bpy.data.objects.new("Doc_Folder", None)
        bpy.context.collection.objects.link(root)
        make_box("Folder_BackCover", (0.066, 0.086, 0.0025), (0, 0, 0.0012), "Mat_PaperManila", bevel_width=0.0008, parent=root)
        make_box("Folder_IndexTab", (0.024, 0.010, 0.0025), (0.018, -0.045, 0.0012), "Mat_PaperManila", bevel_width=0.0008, parent=root)
        make_box("Folder_InnerPages", (0.060, 0.080, 0.0035), (0, 0, 0.0040), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("Folder_FrontCover", (0.065, 0.084, 0.0025), (0, 0.001, 0.0070), "Mat_PaperManila", bevel_width=0.0008, rot_deg=(3, 0, 0), parent=root)
        add_doc_anchors(root, 0.066, 0.086)
        return root

    # 10. Doc_Envelope (Sealed message envelope for agent communication)
    def make_envelope():
        root = bpy.data.objects.new("Doc_Envelope", None)
        bpy.context.collection.objects.link(root)
        make_box("Envelope_Body", (0.054, 0.036, 0.0035), (0, 0, 0.0018), "Mat_PaperWarm", bevel_width=0.0008, parent=root)
        make_box("Envelope_Flap", (0.036, 0.036, 0.0020), (0, -0.006, 0.0032), "Mat_WarmBeige", bevel_width=0.0005, rot_deg=(0, 0, 45), parent=root)
        make_cyl("Envelope_WaxSeal", 0.0055, 0.0025, (0, 0.002, 0.0040), "Mat_Developer_Coral", vertices=16, parent=root)
        make_anchor("Grab_Pivot_Anchor", (0, 0, 0.002), root)
        return root

    # 11. Doc_Clipboard (Wooden board + brushed metal clip + checklist sheet)
    def make_clipboard():
        root = bpy.data.objects.new("Doc_Clipboard", None)
        bpy.context.collection.objects.link(root)
        make_box("Clipboard_Board", (0.064, 0.088, 0.004), (0, 0, 0.002), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("Clipboard_Paper", (0.056, 0.076, 0.0025), (0, 0.003, 0.005), "Mat_PaperWarm", bevel_width=0.0006, parent=root)
        make_box("Clipboard_MetalClip", (0.028, 0.012, 0.006), (0, -0.038, 0.006), "Mat_BrushedMetal", bevel_width=0.0012, parent=root)
        add_doc_anchors(root, 0.056, 0.076)
        return root

    # 12. Doc_ApprovalStamp (Turned wood knob handle + brass stamp base)
    def make_approval_stamp():
        root = bpy.data.objects.new("Doc_ApprovalStamp", None)
        bpy.context.collection.objects.link(root)
        make_box("Stamp_Base", (0.028, 0.020, 0.007), (0, 0, 0.0035), "Mat_Brass", bevel_width=0.0015, parent=root)
        make_box("Stamp_RubberPad", (0.025, 0.017, 0.002), (0, 0, 0.001), "Mat_Tester_Mint", bevel_width=0.0005, parent=root)
        make_cyl("Stamp_Stem", 0.0055, 0.018, (0, 0, 0.015), "Mat_DarkWood", vertices=16, parent=root)
        make_sphere("Stamp_Knob", (0.011, 0.011, 0.012), (0, 0, 0.028), "Mat_WarmWood", parent=root)
        make_anchor("Stamp_Contact_Anchor", (0, 0, 0.0), root)
        make_anchor("Grab_Pivot_Anchor",    (0, 0, 0.022), root)
        return root

    # 13. Doc_SignaturePen (Stylized fountain pen / stylus)
    def make_signature_pen():
        root = bpy.data.objects.new("Doc_SignaturePen", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Pen_Barrel", 0.004, 0.062, (0, 0, 0.038), "Mat_CharcoalVisor", vertices=16, parent=root)
        make_cyl("Pen_GripBand", 0.0044, 0.012, (0, 0, 0.014), "Mat_Brass", vertices=16, parent=root)
        make_cone("Pen_Nib", 0.004, 0.0006, 0.010, (0, 0, 0.005), "Mat_Brass", vertices=14, rot_deg=(180, 0, 0), parent=root)
        make_box("Pen_PocketClip", (0.002, 0.003, 0.022), (0.0045, 0, 0.052), "Mat_Brass", bevel_width=0.0005, parent=root)
        make_anchor("Pen_Tip_Anchor",  (0, 0, 0.0), root)
        make_anchor("Grab_Pivot_Anchor", (0, 0, 0.030), root)
        return root

    # 14. Doc_InboxTray (Desktop document inbox tray)
    def make_inbox_tray():
        root = bpy.data.objects.new("Doc_InboxTray", None)
        bpy.context.collection.objects.link(root)
        make_box("Inbox_Floor", (0.076, 0.096, 0.005), (0, 0, 0.0025), "Mat_WarmWood", bevel_width=0.0015, parent=root)
        make_box("Inbox_WallL", (0.006, 0.096, 0.022), ( 0.036, 0, 0.012), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("Inbox_WallR", (0.006, 0.096, 0.022), (-0.036, 0, 0.012), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("Inbox_WallBack", (0.076, 0.006, 0.022), (0, 0.045, 0.012), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("Inbox_Badge", (0.024, 0.004, 0.008), (0.022, -0.046, 0.008), "Mat_Developer_Amber", bevel_width=0.001, parent=root)
        make_anchor("Inbox_Stack_Anchor", (0, 0, 0.006), root)
        return root

    # 15. Doc_OutboxTray (Completed/outgoing document tray)
    def make_outbox_tray():
        root = bpy.data.objects.new("Doc_OutboxTray", None)
        bpy.context.collection.objects.link(root)
        make_box("Outbox_Floor", (0.076, 0.096, 0.005), (0, 0, 0.0025), "Mat_WarmWood", bevel_width=0.0015, parent=root)
        make_box("Outbox_WallL", (0.006, 0.096, 0.022), ( 0.036, 0, 0.012), "Mat_Tester_SlateBlue", bevel_width=0.0015, parent=root)
        make_box("Outbox_WallR", (0.006, 0.096, 0.022), (-0.036, 0, 0.012), "Mat_Tester_SlateBlue", bevel_width=0.0015, parent=root)
        make_box("Outbox_WallBack", (0.076, 0.006, 0.022), (0, 0.045, 0.012), "Mat_Tester_SlateBlue", bevel_width=0.0015, parent=root)
        make_box("Outbox_Badge", (0.024, 0.004, 0.008), (0.022, -0.046, 0.008), "Mat_Tester_Mint", bevel_width=0.001, parent=root)
        make_anchor("Outbox_Stack_Anchor", (0, 0, 0.006), root)
        return root

    # 16. Doc_DocumentArchive (Multi-slot vertical archive rack with binders & blueprint tubes)
    def make_doc_archive():
        root = bpy.data.objects.new("Doc_DocumentArchive", None)
        bpy.context.collection.objects.link(root)
        make_box("Archive_Base", (0.110, 0.076, 0.008), (0, 0, 0.004), "Mat_DarkWood", bevel_width=0.002, parent=root)
        make_box("Archive_BackWall", (0.110, 0.008, 0.092), (0, 0.034, 0.048), "Mat_WarmWood", bevel_width=0.002, parent=root)
        for dx in [-0.050, -0.016, 0.016, 0.050]:
            make_box(f"Archive_Divider_{int(dx*1000)}", (0.006, 0.068, 0.082), (dx, 0, 0.044), "Mat_WarmWood", bevel_width=0.0015, parent=root)
        # Archived colorful folders inside slots
        make_box("Archived_Folder_1", (0.014, 0.058, 0.072), (-0.033, 0, 0.042), "Mat_Researcher_Lavender", bevel_width=0.0015, parent=root)
        make_box("Archived_Folder_2", (0.014, 0.058, 0.072), ( 0.000, 0, 0.042), "Mat_Developer_Coral", bevel_width=0.0015, parent=root)
        make_box("Archived_Folder_3", (0.014, 0.058, 0.072), ( 0.033, 0, 0.042), "Mat_Tester_Mint", bevel_width=0.0015, parent=root)
        make_anchor("Archive_Insert_Anchor", (0, -0.015, 0.065), root)
        return root

    # Collaboration handoff props (Section 8/9)
    def make_collab_package():
        root = bpy.data.objects.new("Collab_PackageBox", None)
        bpy.context.collection.objects.link(root)
        make_box("Package_Cube", (0.038, 0.038, 0.034), (0, 0, 0.017), "Mat_CardboardBox", bevel_width=0.002, parent=root)
        make_box("Package_RibbonX", (0.040, 0.008, 0.036), (0, 0, 0.017), "Mat_Developer_Coral", bevel_width=0.0008, parent=root)
        make_box("Package_RibbonY", (0.008, 0.040, 0.036), (0, 0, 0.017), "Mat_Developer_Coral", bevel_width=0.0008, parent=root)
        make_anchor("Grab_Pivot_Anchor", (0, 0, 0.017), root)
        return root

    def make_collab_artifact():
        root = bpy.data.objects.new("Collab_ArtifactToken", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Artifact_BaseRing", 0.016, 0.006, (0, 0, 0.003), "Mat_BrushedMetal", vertices=18, parent=root)
        make_sphere("Artifact_CoreGem", (0.013, 0.013, 0.015), (0, 0, 0.016), "Mat_Researcher_Teal", segments=12, rings=8, parent=root)
        make_torus("Artifact_OrbitRing", 0.016, 0.002, (0, 0, 0.016), "Mat_Brass", maj_seg=18, min_seg=8, rot_deg=(25, 0, 0), parent=root)
        make_anchor("Grab_Pivot_Anchor", (0, 0, 0.015), root)
        return root

    assets["Doc_SmallPaper"]        = make_small_paper
    assets["Doc_ExpandedFrame"]     = make_expanded_frame
    assets["Doc_ApprovalPaper"]     = make_approval_paper
    assets["Doc_ResearchDocument"]  = make_research_doc
    assets["Doc_TestReport"]        = make_test_report
    assets["Doc_CodeReviewSheet"]   = make_code_review
    assets["Doc_BlueprintSheet"]    = make_blueprint_sheet
    assets["Doc_RolledBlueprint"]   = make_rolled_blueprint
    assets["Doc_Folder"]            = make_folder
    assets["Doc_Envelope"]          = make_envelope
    assets["Doc_Clipboard"]         = make_clipboard
    assets["Doc_ApprovalStamp"]     = make_approval_stamp
    assets["Doc_SignaturePen"]      = make_signature_pen
    assets["Doc_InboxTray"]         = make_inbox_tray
    assets["Doc_OutboxTray"]        = make_outbox_tray
    assets["Doc_DocumentArchive"]   = make_doc_archive
    assets["Collab_PackageBox"]     = make_collab_package
    assets["Collab_ArtifactToken"]  = make_collab_artifact
    return assets


# ==============================================================================
# SECTION 9 — SPATIAL DIAGRAM KIT (14 Modular Architecture Building Blocks)
# ==============================================================================

def build_diagram_assets():
    assets = {}

    def add_node_anchors(root, h=0.055):
        make_anchor("Label_Anchor",    (0.0,   0.0, h + 0.018), root)
        make_anchor("Port_In_Anchor",  (0.028, 0.0, h * 0.5),   root)
        make_anchor("Port_Out_Anchor", (-0.028, 0.0, h * 0.5),  root)
        make_cyl("Node_Plinth", 0.026, 0.006, (0, 0, 0.003), "Mat_CreamCeramic", vertices=20, parent=root)

    # 1. Diagram_FrontendNode (Client browser/screen module)
    def make_frontend():
        root = bpy.data.objects.new("Diagram_FrontendNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.056)
        make_box("Frontend_Frame", (0.046, 0.012, 0.036), (0, 0, 0.028), "Mat_Developer_Coral", bevel_width=0.002, parent=root)
        make_box("Frontend_Screen", (0.038, 0.004, 0.026), (0, -0.005, 0.028), "Mat_ScreenCode", bevel_width=0.001, parent=root)
        return root

    # 2. Diagram_ServerNode (Backend blade tower)
    def make_server():
        root = bpy.data.objects.new("Diagram_ServerNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.064)
        make_box("Server_Chassis", (0.036, 0.036, 0.054), (0, 0, 0.033), "Mat_CharcoalVisor", bevel_width=0.003, parent=root)
        for idx, sz in enumerate([0.020, 0.033, 0.046]):
            make_box(f"Server_Blade_{idx+1}", (0.030, 0.004, 0.006), (0, -0.017, sz), "Mat_BrushedMetal", bevel_width=0.001, parent=root)
            make_sphere(f"Server_LED_{idx+1}", (0.0025, 0.0025, 0.0025), (0.010, -0.019, sz), "Mat_StatusGreen", parent=root)
        return root

    # 3. Diagram_DatabaseNode (3-tier stacked cylinder drum)
    def make_database():
        root = bpy.data.objects.new("Diagram_DatabaseNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.062)
        for idx, sz in enumerate([0.014, 0.030, 0.046]):
            make_cyl(f"DB_Drum_{idx+1}", 0.020, 0.012, (0, 0, sz), "Mat_Tester_SlateBlue", vertices=22, parent=root)
            make_torus(f"DB_Ring_{idx+1}", 0.020, 0.0018, (0, 0, sz), "Mat_Researcher_Teal", maj_seg=22, min_seg=8, parent=root)
        return root

    # 4. Diagram_ApiGatewayNode (Hexagonal portal arch)
    def make_api_gateway():
        root = bpy.data.objects.new("Diagram_ApiGatewayNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.060)
        make_torus("Gateway_Arch", 0.020, 0.005, (0, 0, 0.030), "Mat_Researcher_Lavender", maj_seg=6, min_seg=10, rot_deg=(90, 0, 0), parent=root)
        make_sphere("Gateway_Core", (0.008, 0.008, 0.008), (0, 0, 0.030), "Mat_Developer_Amber", parent=root)
        return root

    # 5. Diagram_AuthNode (Security shield & lock emblem)
    def make_auth():
        root = bpy.data.objects.new("Diagram_AuthNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.058)
        make_box("Auth_Shield", (0.034, 0.012, 0.042), (0, 0, 0.030), "Mat_Brass", bevel_width=0.004, parent=root)
        make_torus("Auth_Shackle", 0.008, 0.0025, (0, -0.005, 0.038), "Mat_BrushedMetal", maj_seg=16, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        make_box("Auth_LockBody", (0.018, 0.006, 0.016), (0, -0.006, 0.026), "Mat_Tester_Mint", bevel_width=0.002, parent=root)
        return root

    # 6. Diagram_CloudNode (Stylized puffy cloud cluster)
    def make_cloud():
        root = bpy.data.objects.new("Diagram_CloudNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.056)
        make_sphere("Cloud_Center", (0.018, 0.014, 0.016), (0, 0, 0.032), "Mat_CreamCeramic", parent=root)
        make_sphere("Cloud_L", (0.013, 0.012, 0.012), ( 0.014, 0, 0.026), "Mat_CreamCeramic", parent=root)
        make_sphere("Cloud_R", (0.013, 0.012, 0.012), (-0.014, 0, 0.026), "Mat_CreamCeramic", parent=root)
        return root

    # 7. Diagram_StorageNode (Vault cube with dial)
    def make_storage():
        root = bpy.data.objects.new("Diagram_StorageNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.056)
        make_box("Storage_Vault", (0.038, 0.038, 0.038), (0, 0, 0.025), "Mat_BrushedMetal", bevel_width=0.003, parent=root)
        make_cyl("Storage_Dial", 0.010, 0.005, (0, -0.019, 0.025), "Mat_Brass", vertices=16, rot_deg=(90, 0, 0), parent=root)
        return root

    # 8. Diagram_QueueNode (FIFO pipeline tube with message spheres)
    def make_queue():
        root = bpy.data.objects.new("Diagram_QueueNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.050)
        make_cyl("Queue_Chute", 0.012, 0.048, (0, 0, 0.026), "Mat_Tester_SlateBlue", vertices=18, rot_deg=(0, 90, 0), parent=root)
        for idx, qx in enumerate([-0.014, 0.0, 0.014]):
            make_sphere(f"Queue_Msg_{idx+1}", (0.006, 0.006, 0.006), (qx, -0.008, 0.026), "Mat_Developer_Amber", parent=root)
        return root

    # 9. Diagram_ProcessNode (Cog/compute node)
    def make_process():
        root = bpy.data.objects.new("Diagram_ProcessNode", None)
        bpy.context.collection.objects.link(root)
        add_node_anchors(root, 0.056)
        make_cyl("Process_Core", 0.018, 0.014, (0, 0, 0.028), "Mat_Developer_Coral", vertices=16, rot_deg=(90, 0, 0), parent=root)
        for i in range(6):
            ang = i * 30
            make_box(f"Process_Tooth_{i+1}", (0.042, 0.010, 0.007), (0, 0, 0.028), "Mat_Developer_Coral", bevel_width=0.001, rot_deg=(0, ang, 0), parent=root)
        return root

    # 10. Diagram_ConnectorArrow (Directional flow arrow)
    def make_arrow():
        root = bpy.data.objects.new("Diagram_ConnectorArrow", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Arrow_Shaft", 0.004, 0.036, (0.008, 0, 0.015), "Mat_Researcher_Teal", vertices=12, rot_deg=(0, 90, 0), parent=root)
        make_cone("Arrow_Head", 0.009, 0.001, 0.018, (-0.016, 0, 0.015), "Mat_Developer_Amber", vertices=14, rot_deg=(0, -90, 0), parent=root)
        return root

    # 11. Diagram_ConnectionLine (Data conduit segment)
    def make_line():
        root = bpy.data.objects.new("Diagram_ConnectionLine", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Conduit_Tube", 0.0035, 0.080, (0, 0, 0.015), "Mat_Researcher_Teal", vertices=12, rot_deg=(0, 90, 0), parent=root)
        make_torus("Conduit_FlangeL", 0.005, 0.0015, ( 0.038, 0, 0.015), "Mat_Brass", maj_seg=14, min_seg=8, rot_deg=(0, 90, 0), parent=root)
        make_torus("Conduit_FlangeR", 0.005, 0.0015, (-0.038, 0, 0.015), "Mat_Brass", maj_seg=14, min_seg=8, rot_deg=(0, 90, 0), parent=root)
        return root

    # 12. Diagram_LabelPlaque (Runtime label holder plaque)
    def make_label_plaque():
        root = bpy.data.objects.new("Diagram_LabelPlaque", None)
        bpy.context.collection.objects.link(root)
        make_box("Plaque_Board", (0.048, 0.005, 0.018), (0, 0, 0.012), "Mat_CharcoalVisor", bevel_width=0.0015, rot_deg=(15, 0, 0), parent=root)
        make_box("Plaque_Rim", (0.050, 0.004, 0.020), (0, 0.001, 0.012), "Mat_Brass", bevel_width=0.0015, rot_deg=(15, 0, 0), parent=root)
        make_anchor("Text_Surface_Anchor", (0, -0.004, 0.012), root)
        return root

    # 13. Diagram_DisplayPlatform (Circular architecture presentation stage)
    def make_platform():
        root = bpy.data.objects.new("Diagram_DisplayPlatform", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Platform_Base", 0.190, 0.012, (0, 0, 0.006), "Mat_DarkWood", vertices=36, parent=root)
        make_cyl("Platform_Stage", 0.176, 0.006, (0, 0, 0.014), "Mat_CharcoalVisor", vertices=36, parent=root)
        make_torus("Platform_GlowRim", 0.178, 0.003, (0, 0, 0.015), "Mat_Researcher_Teal", maj_seg=36, min_seg=10, parent=root)
        for idx, nx in enumerate([0.120, 0.040, -0.040, -0.120]):
            make_anchor(f"Node_Slot_{idx+1}", (nx, 0, 0.017), root)
        return root

    # 14. Diagram_SelectionHighlight (Glowing ring indicator)
    def make_highlight():
        root = bpy.data.objects.new("Diagram_SelectionHighlight", None)
        bpy.context.collection.objects.link(root)
        make_torus("Highlight_Ring", 0.030, 0.0025, (0, 0, 0.003), "Mat_Developer_Amber", maj_seg=28, min_seg=8, parent=root)
        return root

    assets["Diagram_FrontendNode"]       = make_frontend
    assets["Diagram_ServerNode"]         = make_server
    assets["Diagram_DatabaseNode"]       = make_database
    assets["Diagram_ApiGatewayNode"]     = make_api_gateway
    assets["Diagram_AuthNode"]           = make_auth
    assets["Diagram_CloudNode"]          = make_cloud
    assets["Diagram_StorageNode"]        = make_storage
    assets["Diagram_QueueNode"]          = make_queue
    assets["Diagram_ProcessNode"]        = make_process
    assets["Diagram_ConnectorArrow"]     = make_arrow
    assets["Diagram_ConnectionLine"]     = make_line
    assets["Diagram_LabelPlaque"]        = make_label_plaque
    assets["Diagram_DisplayPlatform"]    = make_platform
    assets["Diagram_SelectionHighlight"] = make_highlight
    return assets


# ==============================================================================
# SECTION 10 — WORKSPACE DECORATION PACKAGE (16 Modular Decorations)
# ==============================================================================

def build_decoration_assets():
    assets = {}

    # 1. Decor_MiniPlant (Succulent in terracotta pot)
    def make_plant():
        root = bpy.data.objects.new("Decor_MiniPlant", None)
        bpy.context.collection.objects.link(root)
        make_cone("Plant_Pot", 0.012, 0.016, 0.024, (0, 0, 0.012), "Mat_Terracotta", vertices=18, parent=root)
        make_torus("Plant_PotRim", 0.0155, 0.0025, (0, 0, 0.023), "Mat_Terracotta", maj_seg=18, min_seg=8, parent=root)
        make_sphere("Plant_CenterBud", (0.008, 0.008, 0.010), (0, 0, 0.028), "Mat_PlantGreen", parent=root)
        for i in range(6):
            ang = math.radians(i * 60)
            lx, ly = math.cos(ang) * 0.010, math.sin(ang) * 0.010
            make_sphere(f"Plant_Leaf_{i+1}", (0.008, 0.006, 0.005), (lx, ly, 0.026), "Mat_PlantGreen", rot_deg=(0, 25, i * 60), parent=root)
        return root

    # 2. Decor_DeskLamp (Arched brass desk lamp)
    def make_lamp():
        root = bpy.data.objects.new("Decor_DeskLamp", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Lamp_Base", 0.018, 0.006, (0, 0, 0.003), "Mat_Brass", vertices=20, parent=root)
        make_cyl("Lamp_Stem", 0.0035, 0.058, (0, 0.008, 0.032), "Mat_Brass", vertices=12, rot_deg=(12, 0, 0), parent=root)
        make_cone("Lamp_Shade", 0.018, 0.006, 0.020, (0, -0.008, 0.058), "Mat_Tester_Mint", vertices=18, rot_deg=(-20, 0, 0), parent=root)
        make_sphere("Lamp_Bulb", (0.008, 0.008, 0.008), (0, -0.009, 0.052), "Mat_LampGlow", parent=root)
        return root

    # 3. Decor_BookStack (Stack of 3 colorful books)
    def make_book_stack():
        root = bpy.data.objects.new("Decor_BookStack", None)
        bpy.context.collection.objects.link(root)
        make_box("Book_1", (0.042, 0.032, 0.010), (0, 0, 0.005), "Mat_Researcher_Lavender", bevel_width=0.0015, rot_deg=(0, 0, 4), parent=root)
        make_box("Book_2", (0.038, 0.030, 0.009), (0, 0, 0.0145), "Mat_Developer_Coral", bevel_width=0.0015, rot_deg=(0, 0, -8), parent=root)
        make_box("Book_3", (0.036, 0.028, 0.009), (0, 0, 0.0235), "Mat_Tester_Mint", bevel_width=0.0015, rot_deg=(0, 0, 10), parent=root)
        return root

    # 4. Decor_CoffeeMug (Cozy ceramic mug with handle)
    def make_mug():
        root = bpy.data.objects.new("Decor_CoffeeMug", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Mug_Body", 0.011, 0.024, (0, 0, 0.012), "Mat_Developer_Coral", vertices=18, parent=root)
        make_cyl("Mug_Coffee", 0.0095, 0.002, (0, 0, 0.023), "Mat_DarkWood", vertices=16, parent=root)
        make_torus("Mug_Handle", 0.0065, 0.0020, (0.011, 0, 0.012), "Mat_CreamCeramic", maj_seg=16, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        return root

    # 5. Decor_DeskMat (Rounded felt desk mat)
    def make_desk_mat():
        root = bpy.data.objects.new("Decor_DeskMat", None)
        bpy.context.collection.objects.link(root)
        make_box("DeskMat_Felt", (0.145, 0.075, 0.0025), (0, 0, 0.0012), "Mat_FeltSlate", bevel_width=0.001, parent=root)
        return root

    # 6. Decor_Trophy (Golden achievement cup trophy)
    def make_trophy():
        root = bpy.data.objects.new("Decor_Trophy", None)
        bpy.context.collection.objects.link(root)
        make_box("Trophy_Pedestal", (0.026, 0.026, 0.014), (0, 0, 0.007), "Mat_DarkWood", bevel_width=0.002, parent=root)
        make_cyl("Trophy_Stem", 0.004, 0.014, (0, 0, 0.020), "Mat_Brass", vertices=14, parent=root)
        make_cone("Trophy_Cup", 0.006, 0.014, 0.022, (0, 0, 0.036), "Mat_Brass", vertices=18, parent=root)
        make_torus("Trophy_Handles", 0.013, 0.002, (0, 0, 0.038), "Mat_Brass", maj_seg=18, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        return root

    # 7. Decor_AwardPlaque (Wooden shield plaque with brass star)
    def make_award():
        root = bpy.data.objects.new("Decor_AwardPlaque", None)
        bpy.context.collection.objects.link(root)
        make_box("Award_Board", (0.042, 0.010, 0.050), (0, 0, 0.026), "Mat_DarkWood", bevel_width=0.003, rot_deg=(12, 0, 0), parent=root)
        make_cyl("Award_Medallion", 0.012, 0.004, (0, -0.005, 0.028), "Mat_Brass", vertices=18, rot_deg=(102, 0, 0), parent=root)
        return root

    # 8. Decor_Toolbox (Miniature latched metal toolbox)
    def make_toolbox():
        root = bpy.data.objects.new("Decor_Toolbox", None)
        bpy.context.collection.objects.link(root)
        make_box("Toolbox_Body", (0.054, 0.028, 0.024), (0, 0, 0.012), "Mat_Developer_Coral", bevel_width=0.002, parent=root)
        make_box("Toolbox_Lid", (0.056, 0.030, 0.008), (0, 0, 0.026), "Mat_Developer_Coral", bevel_width=0.002, parent=root)
        make_box("Toolbox_Latch", (0.010, 0.004, 0.008), (0, -0.015, 0.023), "Mat_Brass", bevel_width=0.001, parent=root)
        make_torus("Toolbox_Handle", 0.010, 0.002, (0, 0, 0.031), "Mat_CharcoalVisor", maj_seg=14, min_seg=8, rot_deg=(90, 0, 0), parent=root)
        return root

    # 9. Decor_SmallShelf (Mini wooden shelf unit)
    def make_small_shelf():
        root = bpy.data.objects.new("Decor_SmallShelf", None)
        bpy.context.collection.objects.link(root)
        make_box("Shelf_Plank", (0.088, 0.036, 0.006), (0, 0, 0.028), "Mat_WarmWood", bevel_width=0.0015, parent=root)
        make_box("Shelf_LegL", (0.008, 0.032, 0.028), ( 0.036, 0, 0.014), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        make_box("Shelf_LegR", (0.008, 0.032, 0.028), (-0.036, 0, 0.014), "Mat_DarkWood", bevel_width=0.0015, parent=root)
        return root

    # 10. Decor_FilingCabinet (2-drawer pastel filing cabinet)
    def make_filing_cabinet():
        root = bpy.data.objects.new("Decor_FilingCabinet", None)
        bpy.context.collection.objects.link(root)
        make_box("Cabinet_Body", (0.058, 0.064, 0.086), (0, 0, 0.043), "Mat_Tester_SlateBlue", bevel_width=0.003, parent=root)
        for idx, dz in enumerate([0.024, 0.062]):
            make_box(f"Cabinet_Drawer_{idx+1}", (0.050, 0.006, 0.032), (0, -0.031, dz), "Mat_CreamCeramic", bevel_width=0.002, parent=root)
            make_box(f"Cabinet_Handle_{idx+1}", (0.018, 0.005, 0.004), (0, -0.035, dz + 0.004), "Mat_Brass", bevel_width=0.001, parent=root)
        return root

    # 11. Decor_DecorativeMonitor (Retro-cute mini status monitor)
    def make_decor_monitor():
        root = bpy.data.objects.new("Decor_DecorativeMonitor", None)
        bpy.context.collection.objects.link(root)
        make_cyl("MiniMon_Stand", 0.016, 0.006, (0, 0, 0.003), "Mat_WarmBeige", vertices=16, parent=root)
        make_box("MiniMon_Housing", (0.044, 0.036, 0.038), (0, 0, 0.024), "Mat_CreamCeramic", bevel_width=0.003, parent=root)
        make_box("MiniMon_Screen", (0.034, 0.004, 0.026), (0, -0.017, 0.025), "Mat_ScreenCode", bevel_width=0.001, parent=root)
        return root

    # 12. Decor_PosterSign (Framed tabletop easel sign)
    def make_poster_sign():
        root = bpy.data.objects.new("Decor_PosterSign", None)
        bpy.context.collection.objects.link(root)
        make_box("Sign_Frame", (0.052, 0.008, 0.064), (0, 0, 0.033), "Mat_WarmWood", bevel_width=0.002, rot_deg=(12, 0, 0), parent=root)
        make_box("Sign_Canvas", (0.042, 0.004, 0.054), (0, -0.004, 0.033), "Mat_PaperWarm", bevel_width=0.001, rot_deg=(12, 0, 0), parent=root)
        make_anchor("Sign_Text_Anchor", (0, -0.007, 0.033), root)
        return root

    # 13. Decor_StatusIndicator (Desktop 3-orb status totem)
    def make_status_indicator():
        root = bpy.data.objects.new("Decor_StatusIndicator", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Indicator_Base", 0.016, 0.008, (0, 0, 0.004), "Mat_CharcoalVisor", vertices=18, parent=root)
        make_cyl("Indicator_Mast", 0.0035, 0.054, (0, 0, 0.032), "Mat_BrushedMetal", vertices=12, parent=root)
        make_sphere("Orb_Green", (0.007, 0.007, 0.007), (0, 0, 0.056), "Mat_StatusGreen", parent=root)
        make_sphere("Orb_Amber", (0.007, 0.007, 0.007), (0, 0, 0.040), "Mat_StatusAmber", parent=root)
        make_sphere("Orb_Red",   (0.007, 0.007, 0.007), (0, 0, 0.024), "Mat_StatusRed",   parent=root)
        return root

    # 14. Decor_MiniClock (Twin-bell desk clock)
    def make_clock():
        root = bpy.data.objects.new("Decor_MiniClock", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Clock_Body", 0.016, 0.010, (0, 0, 0.018), "Mat_Developer_Coral", vertices=20, rot_deg=(90, 0, 0), parent=root)
        make_cyl("Clock_Face", 0.013, 0.011, (0, -0.001, 0.018), "Mat_CreamCeramic", vertices=18, rot_deg=(90, 0, 0), parent=root)
        for sx in [-0.011, 0.011]:
            make_sphere(f"Clock_Bell_{int(sx*1000)}", (0.006, 0.006, 0.005), (sx, 0, 0.034), "Mat_Brass", parent=root)
        return root

    # 15. Decor_ProgressAward (Glowing crystal obelisk award)
    def make_progress_award():
        root = bpy.data.objects.new("Decor_ProgressAward", None)
        bpy.context.collection.objects.link(root)
        make_cyl("Award_Plinth", 0.016, 0.010, (0, 0, 0.005), "Mat_Brass", vertices=18, parent=root)
        make_cone("Award_Crystal", 0.011, 0.002, 0.042, (0, 0, 0.030), "Mat_Researcher_Teal", vertices=6, parent=root)
        return root

    # 16. Decor_ProjectDisplayShelf (Completed-project showcase shelf rack)
    def make_project_shelf():
        root = bpy.data.objects.new("Decor_ProjectDisplayShelf", None)
        bpy.context.collection.objects.link(root)
        make_box("Showcase_Frame", (0.140, 0.048, 0.130), (0, 0, 0.065), "Mat_WarmWood", bevel_width=0.003, parent=root)
        for idx, sz in enumerate([0.042, 0.086]):
            make_box(f"Showcase_Tier_{idx+1}", (0.128, 0.044, 0.006), (0, -0.002, sz), "Mat_DarkWood", bevel_width=0.0015, parent=root)
            make_anchor(f"Trophy_Slot_{idx+1}_L", ( 0.038, -0.004, sz + 0.004), root)
            make_anchor(f"Trophy_Slot_{idx+1}_R", (-0.038, -0.004, sz + 0.004), root)
        return root

    assets["Decor_MiniPlant"]           = make_plant
    assets["Decor_DeskLamp"]            = make_lamp
    assets["Decor_BookStack"]           = make_book_stack
    assets["Decor_CoffeeMug"]           = make_mug
    assets["Decor_DeskMat"]             = make_desk_mat
    assets["Decor_Trophy"]              = make_trophy
    assets["Decor_AwardPlaque"]         = make_award
    assets["Decor_Toolbox"]             = make_toolbox
    assets["Decor_SmallShelf"]          = make_small_shelf
    assets["Decor_FilingCabinet"]       = make_filing_cabinet
    assets["Decor_DecorativeMonitor"]   = make_decor_monitor
    assets["Decor_PosterSign"]          = make_poster_sign
    assets["Decor_StatusIndicator"]     = make_status_indicator
    assets["Decor_MiniClock"]           = make_clock
    assets["Decor_ProgressAward"]       = make_progress_award
    assets["Decor_ProjectDisplayShelf"] = make_project_shelf
    return assets


# ==============================================================================
# EXPORT & CONTACT SHEET RENDER PIPELINE
# ==============================================================================

def export_category(cat_name, builders_dict):
    bdir, fdir = SUBDIRS[cat_name]
    cat_report = []

    for asset_name, builder_fn in builders_dict.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root = builder_fn()

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
        cat_report.append({
            "category": cat_name,
            "asset": asset_name,
            "blend": blend_path,
            "fbx": fbx_path,
            "fbx_bytes": os.path.getsize(fbx_path),
            "vertices": total_verts,
            "triangles": total_tris,
        })

    # Render a studio contact sheet of all assets in this category arranged on a grid
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cols = 4
    items = list(builders_dict.items())
    spacing_x = 0.22 if cat_name != "TaskBoard" else 0.36
    spacing_y = 0.18 if cat_name != "TaskBoard" else 0.24
    for idx, (aname, bfn) in enumerate(items):
        r = bfn()
        gx = (idx % cols - (cols - 1) * 0.5) * spacing_x
        gy = (idx // cols) * spacing_y
        r.location = (gx, gy, 0.0)

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)

    for lname, loc, energy in [("Key", (0.5, -0.8, 0.9), 65.0), ("Fill", (-0.6, -0.5, 0.6), 35.0)]:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 42
    cam_o = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o
    cam_pos = (0.0, -0.78, 0.58)
    target  = (0.0,  0.18, 0.05)
    cam_o.location = cam_pos
    cam_o.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    sheet_png = os.path.join(PREVIEW_DIR, f"ContactSheet_{cat_name}.png")
    scene.render.filepath = sheet_png
    bpy.ops.render.render(write_still=True)

    return cat_report


def generate_all_props():
    all_reports = []
    all_reports.extend(export_category("TaskBoard",   build_task_board_assets()))
    all_reports.extend(export_category("Documents",   build_document_assets()))
    all_reports.extend(export_category("Diagrams",    build_diagram_assets()))
    all_reports.extend(export_category("Decorations", build_decoration_assets()))

    with open(os.path.join(PREVIEW_DIR, "props_build_report.json"), "w") as f:
        json.dump(all_reports, f, indent=2)


if __name__ == "__main__":
    generate_all_props()
