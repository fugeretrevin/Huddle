import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Final Miniature Asset Refinement Generator (Phase 3 Polish)
#
# Coordinates:
#   Blender (+X, +Y, +Z) exported with (axis_forward='-Z', axis_up='Y'):
#     Blender +X -> Unity -X  (Reader's Left when facing -Z)
#     Blender -X -> Unity +X  (Reader's Right when facing -Z)
#     Blender +Y -> Unity +Z  (Back, away from user)
#     Blender -Y -> Unity -Z  (Front face, toward user)
#     Blender +Z -> Unity +Y  (Up)
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
BLEND_ROOT   = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "FinalRefinement")
MODELS_ROOT  = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models")
PREVIEW_DIR  = os.path.join(PROJECT_ROOT, "Screenshots", "FinalRefinement")

CATEGORIES = ["Documents", "UI", "TaskBoard", "Plates", "Optimized"]
DIRS = {}
for cat in CATEGORIES:
    b = os.path.join(BLEND_ROOT, cat)
    f = os.path.join(MODELS_ROOT, cat)
    os.makedirs(b, exist_ok=True)
    os.makedirs(f, exist_ok=True)
    DIRS[cat] = (b, f)
os.makedirs(PREVIEW_DIR, exist_ok=True)


def srgb_to_lin_c(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgba(hex_str, a=1.0):
    s = hex_str.lstrip("#")
    r = srgb_to_lin_c(int(s[0:2], 16) / 255.0)
    g = srgb_to_lin_c(int(s[2:4], 16) / 255.0)
    b = srgb_to_lin_c(int(s[4:6], 16) / 255.0)
    return (r, g, b, a)


# Exact MUSEDESK_DESIGN_SYSTEM.md palette tokens
PALETTE = {
    "Mat_DS_Paper":          (hex_rgba("#F7F2E3"), 0.78, 0.00, 0.0),
    "Mat_DS_PaperEdge":      (hex_rgba("#DDD5BF"), 0.82, 0.00, 0.0),
    "Mat_DS_PaperTint":      (hex_rgba("#ECE4CF"), 0.80, 0.00, 0.0),
    "Mat_DS_Plate":          (hex_rgba("#1B1F27"), 0.62, 0.05, 0.0),
    "Mat_DS_PlateRim":       (hex_rgba("#252A34"), 0.52, 0.10, 0.0),
    "Mat_DS_Housing":        (hex_rgba("#2A2F3A"), 0.50, 0.12, 0.0),
    "Mat_DS_HousingInner":   (hex_rgba("#14171E"), 0.75, 0.05, 0.0),
    "Mat_DS_KeyCapCream":    (hex_rgba("#F4EFE2"), 0.38, 0.02, 0.0),
    "Mat_DS_KeyCapTeal":     (hex_rgba("#2FA39A"), 0.36, 0.02, 0.0),
    "Mat_DS_KeyCapLavender": (hex_rgba("#8C7BD6"), 0.36, 0.02, 0.0),
    "Mat_DS_KeyCapSuccess":  (hex_rgba("#2E9B5F"), 0.36, 0.02, 0.0),
    "Mat_DS_KeyCapCoral":    (hex_rgba("#D9534A"), 0.36, 0.02, 0.0),
    "Mat_DS_KeyStateRim":    (hex_rgba("#FFFFFF"), 0.30, 0.00, 0.12),
    "Mat_DS_AccentAmber":    (hex_rgba("#E0A030"), 0.45, 0.00, 0.0),
    "Mat_DS_AccentBlue":     (hex_rgba("#4C85DB"), 0.45, 0.00, 0.0),
    "Mat_DS_AccentSuccess":  (hex_rgba("#2E9B5F"), 0.45, 0.00, 0.0),
    "Mat_DS_AccentTeal":     (hex_rgba("#2FA39A"), 0.42, 0.00, 0.0),
    "Mat_DS_AccentLavender": (hex_rgba("#8C7BD6"), 0.45, 0.00, 0.0),
    "Mat_DS_InkSoft":        (hex_rgba("#4B5563"), 0.65, 0.00, 0.0),
    "Mat_DS_Brass":          (hex_rgba("#D1A84C"), 0.32, 0.78, 0.0),
    "Mat_DS_TrayWood":       (hex_rgba("#4A372B"), 0.64, 0.02, 0.0),
    "Mat_DS_WarmWood":       (hex_rgba("#7C5840"), 0.58, 0.02, 0.0),
    "Mat_DS_FeltSlate":      (hex_rgba("#272D37"), 0.85, 0.00, 0.0),
}


def get_mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    rgba, rough, metal, emit = PALETTE.get(name, ((0.8, 0.8, 0.8, 1.0), 0.5, 0.0, 0.0))
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = rgba
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = emit
    return m


# ==============================================================================
# PROCEDURAL BMESH HELPERS (Crisp Flat Faces + Smooth Rounded Corners/Chamfers)
# ==============================================================================

def _rounded_rect_pts_2d(w, h, r, segs=5):
    """Returns CCW 2D points (x, z) for a rectangle w x h with circular corner radius r."""
    r = max(0.0, min(r, min(w, h) * 0.49))
    hx, hz = w * 0.5, h * 0.5
    if r < 1e-6:
        return [(hx, hz), (-hx, hz), (-hx, -hz), (hx, -hz)]
    pts = []
    corners = [
        ( hx - r,  hz - r, 0.0),
        (-hx + r,  hz - r, math.pi * 0.5),
        (-hx + r, -hz + r, math.pi),
        ( hx - r, -hz + r, math.pi * 1.5),
    ]
    for cx, cz, a0 in corners:
        for i in range(segs + 1):
            a = a0 + (math.pi * 0.5) * (i / segs)
            pts.append((cx + math.cos(a) * r, cz + math.sin(a) * r))
    return pts


def _mark_planar_flat_curved_smooth(bm, area_thresh_ratio=0.04):
    """Keeps large planar faces crisp flat and curved side/bevel strips smooth."""
    if not bm.faces:
        return
    max_area = max(f.calc_area() for f in bm.faces)
    for f in bm.faces:
        # Large faces & end caps stay dead-flat; small bevel strips/corner arcs smooth!
        is_large_flat = abs(f.normal.y) > 0.92 or abs(f.normal.z) > 0.98 or f.calc_area() > max_area * 0.25
        f.smooth = not is_large_flat


def make_rounded_slab(
    name, size_xyz, location, mat_name,
    radius=0.003, bevel_w=0.0006, corner_segs=5,
    plane='XZ', rot_deg=(0, 0, 0), parent=None
):
    """
    Creates a manifold rounded-corner slab in local axes:
      plane='XZ': width=X, thickness=Y (front=-Y, back=+Y), height=Z (standard vertical UI)
      plane='XY': width=X, thickness=Z (bottom/top=Z), depth=Y (flat tabletop props)
    Supports uniform circular corners AND optional edge chamfer bevel on front/back rims.
    """
    if plane == 'XZ':
        w, thick, h = size_xyz
    else:
        w, h, thick = size_xyz  # size_xyz = (X, Y, Z) -> 2D rect (X, Y), extrude Z

    mesh = bpy.data.meshes.new(name + "_Mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(get_mat(mat_name))

    bm = bmesh.new()
    pts_2d = _rounded_rect_pts_2d(w, h, radius, segs=corner_segs)

    ht = thick * 0.5
    bw = min(bevel_w, ht * 0.42, radius * 0.45 if radius > 0 else ht * 0.42)

    if bw > 1e-5:
        # 4 stepped profiles along thickness axis for crisp tactile bevel without messy n-gon pinch
        inset_pts = _rounded_rect_pts_2d(max(1e-4, w - 2.0 * bw), max(1e-4, h - 2.0 * bw), max(0.0, radius - bw), segs=corner_segs)
        layers = [
            (-ht,        inset_pts),
            (-ht + bw,   pts_2d),
            ( ht - bw,   pts_2d),
            ( ht,        inset_pts),
        ]
    else:
        layers = [
            (-ht, pts_2d),
            ( ht, pts_2d),
        ]

    rings = []
    for d, lpts in layers:
        ring = []
        for u, v in lpts:
            if plane == 'XZ':
                co = (u, d, v)
            else:
                co = (u, v, d)
            ring.append(bm.verts.new(co))
        rings.append(ring)

    n = len(pts_2d)
    for li in range(len(rings) - 1):
        r0 = rings[li]
        r1 = rings[li + 1]
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))

    # Cap front & back faces
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        # Keep planar faces flat so papers/plates don't get gradient shading artifacts!
        if plane == 'XZ':
            f.smooth = abs(f.normal.y) < 0.85
        else:
            f.smooth = abs(f.normal.z) < 0.85

    bm.to_mesh(mesh)
    bm.free()

    obj.location = location
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    if parent:
        obj.parent = parent
    return obj


def make_rounded_ring_frame(
    name, outer_wh, wall, depth_y, r_outer,
    location, mat_outer, mat_inner=None,
    back_thick=0.0, corner_segs=5, parent=None,
    with_floor_back=True, front_chamfer=0.001
):
    """
    Creates a TRUE single-piece hollow rounded frame on the XZ plane (depth along Y, -Y is front).
    Guarantees 100% UNIFORM border wall thickness on left, right, top, and bottom regardless of aspect ratio!
    Slot 0 = mat_outer (front rim & outer walls)
    Slot 1 = mat_inner (inner well walls + recessed back floor if with_floor_back)
    """
    ow, oh = outer_wh
    iw = max(0.004, ow - 2.0 * wall)
    ih = max(0.004, oh - 2.0 * wall)
    r_inner = max(0.0005, r_outer - wall)

    outer_pts = _rounded_rect_pts_2d(ow, oh, r_outer, segs=corner_segs)
    inner_pts = _rounded_rect_pts_2d(iw, ih, r_inner, segs=corner_segs)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(get_mat(mat_outer))
    if mat_inner:
        obj.data.materials.append(get_mat(mat_inner))
        inner_slot = 1
    else:
        inner_slot = 0

    bm = bmesh.new()
    n = len(outer_pts)
    hy = depth_y * 0.5
    fc = min(front_chamfer, wall * 0.35, depth_y * 0.25)

    # Front outer chamfer inset, front plane, back plane
    outer_f_in = _rounded_rect_pts_2d(ow - 2 * fc, oh - 2 * fc, max(0.0, r_outer - fc), segs=corner_segs)
    inner_f_in = _rounded_rect_pts_2d(iw + 2 * fc, ih + 2 * fc, max(0.0, r_inner + fc), segs=corner_segs)

    # Rings (x, y, z)
    def mk_ring(pts, y):
        return [bm.verts.new((u, y, v)) for (u, v) in pts]

    r_out_b  = mk_ring(outer_pts,   hy)
    r_out_fc = mk_ring(outer_pts,  -hy + fc)
    r_out_f  = mk_ring(outer_f_in, -hy)
    r_in_f   = mk_ring(inner_f_in, -hy)
    r_in_fc  = mk_ring(inner_pts,  -hy + fc)
    floor_y  = hy - max(back_thick, 0.001)
    r_in_b   = mk_ring(inner_pts,   floor_y)

    def bridge(a, b, mat_idx=0, flip=False):
        for i in range(n):
            j = (i + 1) % n
            verts = (a[i], a[j], b[j], b[i]) if not flip else (b[i], b[j], a[j], a[i])
            f = bm.faces.new(verts)
            f.material_index = mat_idx

    bridge(r_out_b, r_out_fc, 0)
    bridge(r_out_fc, r_out_f, 0)
    bridge(r_out_f, r_in_f, 0)
    bridge(r_in_f, r_in_fc, inner_slot)
    bridge(r_in_fc, r_in_b, inner_slot)

    if with_floor_back:
        # Solid back plate on outer back & inner floor
        f_back = bm.faces.new(r_out_b)
        f_back.material_index = 0
        f_floor = bm.faces.new(list(reversed(r_in_b)))
        f_floor.material_index = inner_slot
    else:
        # Open-center rim (e.g., Card_SelectionRim): close back bridge outer->inner
        r_in_b_open = mk_ring(inner_pts, hy)
        bridge(r_in_b, r_in_b_open, inner_slot)
        bridge(r_in_b_open, r_out_b, 0)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = abs(f.normal.y) < 0.85

    bm.to_mesh(mesh)
    bm.free()

    obj.location = location
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
        p.use_smooth = abs(p.normal.dot(mathutils.Vector((0, 0, 1)))) < 0.85
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    return obj


def make_anchor(name, location, parent, rot_deg=(0, 0, 0)):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.012
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


def join_children_by_material(root, keep_separate_names=None, prefix="Mesh"):
    keep_separate_names = set(keep_separate_names or ())
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
                objs[0].name = f"{prefix}_{mname.replace('Mat_DS_', '').replace('Mat_', '')}"
            continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        merged = bpy.context.active_object
        merged.name = f"{prefix}_{mname.replace('Mat_DS_', '').replace('Mat_', '')}"
        merged.parent = root


# ==============================================================================
# DELIVERABLE 1 — POLISHED DOCUMENT & RECEIPT ASSETS
# ==============================================================================

def build_doc_paper_v2_expanded(root_name="Doc_PaperV2_Expanded"):
    """
    Expanded 300 x 300 mm Decision Folio aligned to BuildPaperV2 coordinates:
      - Unity Reader is on -Z (Blender -Y). Front paper surface is at y = 0.
      - Thickness extends backward (+Y = +Z in Unity): 2.6 mm cream slab + 1.8 mm offset edge backing.
      - Rounded 3.5 mm corners, crisp warm edge bevel, physical top brass binder tab.
      - Distinct rounded ModeBand sub-mesh (Mat_DS_AccentAmber) for Demo/Live color swaps.
      - Recessed Stamp Zone on reader's right (Unity +X = Blender -X, x = -0.091) with sculpted
        corner L-brackets and zero overlapping outlines.
      - Subtle key pocket recesses for the 4 chips & 2 primary wide keys.
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    W, H = 0.300, 0.300
    R = 0.004  # 4 mm soft rounded paper corners

    # Primary cream paper sheet: front surface at y = 0.0, thickness 2.4 mm (-Y -> Unity +Z)
    make_rounded_slab("Paper_MainSheet", (W, 0.0024, H), (0, -0.0012, 0),
                      "Mat_DS_Paper", radius=R, bevel_w=0.0005, corner_segs=5, parent=root)

    # Subtle stepped second sheet behind (-Y = -0.0033 -> Unity +Z) offset slightly down/right for paper stack depth
    make_rounded_slab("Paper_UnderRim", (W - 0.002, 0.0018, H - 0.002), (-0.0025, -0.0033, -0.0025),
                      "Mat_DS_PaperEdge", radius=R, bevel_w=0.0004, corner_segs=5, parent=root)

    # Soft physical corner fold-over / miniature brass folio clip on top edge (+Z = +0.150)
    make_rounded_slab("Paper_CornerTabClip", (0.044, 0.0046, 0.011), (0.0, -0.0008, H * 0.5 + 0.002),
                      "Mat_DS_Brass", radius=0.0025, bevel_w=0.0007, corner_segs=4, parent=root)

    # ModeBand (26 mm tall across top, +Y -> Unity -Z front): kept SEPARATE for runtime DecisionPanel.modeBand tinting!
    band_h = 0.026
    make_rounded_slab("ModeBand", (W - 0.002, 0.0012, band_h), (0.0, 0.0003, H * 0.5 - band_h * 0.5),
                      "Mat_DS_AccentAmber", radius=0.003, bevel_w=0.0003, corner_segs=4, parent=root)

    # StampZone (Unity +X = +0.091 -> Blender -X = -0.091, Y_up = +0.0335, 86 x 67 mm)
    stamp_bx, stamp_bz = -0.091, 0.0335
    sw, sh = 0.086, 0.067
    make_rounded_slab("StampZone_Pad", (sw, 0.0008, sh), (stamp_bx, 0.0002, stamp_bz),
                      "Mat_DS_PaperTint", radius=0.004, bevel_w=0.0002, corner_segs=4, parent=root)
    # 4 delicate corner bracket ticks instead of full solid outline box
    tick_len, tick_w = 0.014, 0.0018
    for sx in (-1, 1):
        for sz in (-1, 1):
            cx = stamp_bx + sx * (sw * 0.5 - 0.003)
            cz = stamp_bz + sz * (sh * 0.5 - 0.003)
            make_rounded_slab(f"StampTickH_{sx}_{sz}", (tick_len, 0.0006, tick_w),
                              (cx - sx * (tick_len * 0.5 - tick_w * 0.5), 0.00045, cz),
                              "Mat_DS_InkSoft", radius=0.0004, bevel_w=0.0, corner_segs=2, parent=root)
            make_rounded_slab(f"StampTickV_{sx}_{sz}", (tick_w, 0.0006, tick_len),
                              (cx, 0.00045, cz - sz * (tick_len * 0.5 - tick_w * 0.5)),
                              "Mat_DS_InkSoft", radius=0.0004, bevel_w=0.0, corner_segs=2, parent=root)

    # Subtle recessed key wells on the paper face for 4 chips & 2 primary wide keys (visual polish cues)
    margin = 0.016
    content_w = W - 2 * margin
    chip_w, chip_h = 0.060, 0.028
    pitch = (content_w - chip_w) / 3.0
    for i in range(4):
        ux = (-W * 0.5 + margin) + chip_w * 0.5 + pitch * i
        make_rounded_slab(f"ChipPocket_{i}", (chip_w + 0.003, 0.0005, chip_h + 0.003), (-ux, 0.0001, -0.042),
                          "Mat_DS_PaperEdge", radius=0.005, bevel_w=0.0001, corner_segs=4, parent=root)
    for px in (-0.070, 0.070):
        make_rounded_slab(f"PrimPocket_{int(px*1000)}", (0.132, 0.0006, 0.054), (-px, 0.0001, -0.109),
                          "Mat_DS_PaperEdge", radius=0.006, bevel_w=0.0001, corner_segs=4, parent=root)

    # Merge static parts by material while keeping ModeBand & StampZone_Pad independent
    join_children_by_material(root, keep_separate_names={"ModeBand", "StampZone_Pad"}, prefix="PaperExp")

    # Anchors (Blender +Y = Unity -Z front surface) matching BuildPaperV2 exact front surface
    make_anchor("Anchor_ContentSurface", (0.0, 0.0005, 0.0), root)
    make_anchor("Anchor_Band",           (0.0, 0.0014, H * 0.5 - band_h * 0.5), root)
    make_anchor("Anchor_Title",          (0.0, 0.0005, 0.094), root)
    make_anchor("Anchor_Summary",        (-(-W * 0.5 + margin + 0.085), 0.0005, 0.052), root)
    make_anchor("Anchor_Reco",           (-(-W * 0.5 + margin + 0.085), 0.0005, 0.026), root)
    make_anchor("Anchor_Provenance",     (-(-W * 0.5 + margin + 0.085), 0.0005, 0.008), root)
    make_anchor("Anchor_Status",         (0.0, 0.0005, -0.016), root)
    make_anchor("Anchor_StampZone",      (stamp_bx, 0.0010, stamp_bz), root)
    make_anchor("Anchor_Btn_Approve",    (0.070, 0.0060, -0.109), root)
    make_anchor("Anchor_Btn_Reject",     (-0.070, 0.0060, -0.109), root)
    make_anchor("Anchor_DetailsTop",     (0.0, 0.0, -H * 0.5 - 0.006), root)
    return root


def build_doc_paper_v2_details(root_name="Doc_PaperV2_DetailsSheet"):
    """Companion 300 x 134 mm rounded pull-out sheet for Details / Why."""
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    W, H = 0.300, 0.134
    make_rounded_slab("DetailsPlate", (W, 0.0022, H), (0, -0.0011, 0),
                      "Mat_DS_Paper", radius=0.004, bevel_w=0.0005, corner_segs=5, parent=root)
    make_rounded_slab("DetailsEdge", (W - 0.002, 0.0016, H - 0.002), (-0.002, -0.0029, -0.002),
                      "Mat_DS_PaperEdge", radius=0.004, bevel_w=0.0004, corner_segs=5, parent=root)
    make_rounded_slab("DetailsStrip", (W - 0.002, 0.0012, 0.007), (0, 0.0003, H * 0.5 - 0.0035),
                      "Mat_DS_AccentLavender", radius=0.002, bevel_w=0.0002, corner_segs=3, parent=root)
    join_children_by_material(root, keep_separate_names={"DetailsStrip"}, prefix="PaperDet")
    make_anchor("Anchor_Body",   (0, 0.0005,  0.015), root)
    make_anchor("Anchor_Answer", (0, 0.0005, -0.039), root)
    return root


def build_doc_paper_v2_small(root_name="Doc_PaperV2_Small"):
    """
    Carried Small Paper Folio Cover (authored at 0.30 x 0.30 m sheet space so at heldScale=0.22
    it renders as a 66 x 66 mm mini stack with bold tactile 3.3 mm corners, chunky stepped under-sheets,
    rounded Mode CoverBand, and sculptural top teal TAKE tab!).
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    W, H = 0.300, 0.300
    R = 0.015  # 15 mm in sheet space -> ~3.3 mm at 0.22 carried scale!

    # Top cover card (front face at y = 0, +Y -> Unity -Z front)
    make_rounded_slab("CoverSheet", (W, 0.006, H), (0, -0.003, 0),
                      "Mat_DS_Paper", radius=R, bevel_w=0.0018, corner_segs=6, parent=root)
    # Two sculpted stepped sheets peeking behind (-Y -> Unity +Z)
    make_rounded_slab("UnderSheet1", (W, 0.005, H), (-0.012, -0.0095, -0.012),
                      "Mat_DS_PaperEdge", radius=R, bevel_w=0.0015, corner_segs=6, parent=root)
    make_rounded_slab("UnderSheet2", (W, 0.005, H), (-0.024, -0.0155, -0.024),
                      "Mat_DS_PaperEdge", radius=R, bevel_w=0.0015, corner_segs=6, parent=root)

    # Top CoverBand (85 mm in sheet space, separate for DecisionPanel.coverBand)
    band_h = 0.085
    make_rounded_slab("CoverBand", (W - 0.004, 0.003, band_h), (0, 0.0012, H * 0.5 - band_h * 0.5),
                      "Mat_DS_AccentAmber", radius=R * 0.85, bevel_w=0.0008, corner_segs=5, parent=root)

    # Tactile teal TAKE grab tab rising from top edge
    tab_w, tab_h = 0.136, 0.048
    make_rounded_slab("GrabTab", (tab_w, 0.006, tab_h + 0.010), (0, -0.002, H * 0.5 + tab_h * 0.5 - 0.005),
                      "Mat_DS_AccentTeal", radius=0.014, bevel_w=0.0018, corner_segs=5, parent=root)
    # Subtle grip notch bar on top edge of TAKE tab
    make_rounded_slab("GrabTab_GripBar", (0.048, 0.0075, 0.005), (0, -0.0015, H * 0.5 + tab_h - 0.008),
                      "Mat_DS_Paper", radius=0.002, bevel_w=0.0008, corner_segs=3, parent=root)

    join_children_by_material(root, keep_separate_names={"CoverBand", "GrabTab"}, prefix="SmallCover")
    make_anchor("Anchor_CoverMode",  (0, 0.0032, H * 0.5 - band_h * 0.5), root)
    make_anchor("Anchor_CoverTitle", (0, 0.0024, -0.020), root)
    make_anchor("Anchor_TakeLabel",  (0, 0.0024, H * 0.5 + tab_h * 0.5), root)
    return root


def build_doc_receipt_card(root_name="Doc_ReceiptCard"):
    """
    Coordinated Completion Receipt Card (140 x 96 x 2.6 mm landscape ticket).
    Features:
      - Soft 4.5 mm rounded cream paper card + subtle darker paper edge rim
      - Perforated / notched top receipt tear strip accent + brass pin eyelet at top-left
      - Separate rounded ModeStrip across top for Delivered (green) / Stopped (amber) / Failed (coral)
      - Clean left text zone (78 mm) & right key pocket (52 x 32 mm) for wide 'Run again' key
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    W, H = 0.140, 0.096
    make_rounded_slab("Receipt_Sheet", (W, 0.0026, H), (0, -0.0013, 0),
                      "Mat_DS_Paper", radius=0.0045, bevel_w=0.0006, corner_segs=5, parent=root)
    make_rounded_slab("Receipt_UnderEdge", (W - 0.002, 0.0018, H - 0.002), (-0.0015, -0.0032, -0.0015),
                      "Mat_DS_PaperEdge", radius=0.0045, bevel_w=0.0004, corner_segs=5, parent=root)

    # Separate ModeStrip top status ribbon (6.5 mm tall)
    make_rounded_slab("ModeStrip", (W - 0.004, 0.0012, 0.007), (0, 0.0004, H * 0.5 - 0.0045),
                      "Mat_DS_AccentSuccess", radius=0.0025, bevel_w=0.0003, corner_segs=4, parent=root)

    # Subtle perforated dashed tear line below ModeStrip
    for i in range(11):
        px = -0.055 + i * 0.011
        make_rounded_slab(f"Receipt_Perf_{i}", (0.005, 0.0005, 0.0012), (px, 0.0003, H * 0.5 - 0.0125),
                          "Mat_DS_InkSoft", radius=0.0003, bevel_w=0.0, corner_segs=2, parent=root)

    # Miniature brass corner ticket pin (top-left: Blender +X = Unity -X)
    make_cyl("Receipt_BrassPin", 0.0028, 0.0038, (W * 0.5 - 0.008, -0.0006, H * 0.5 - 0.008),
             "Mat_DS_Brass", vertices=16, rot_deg=(90, 0, 0), parent=root)

    # Right-column subtle recessed pocket for 50 x 28 mm Run again Wide key (Unity +X = +0.039 -> Blender -X = -0.039)
    make_rounded_slab("Receipt_KeyWell", (0.052, 0.0006, 0.030), (-0.039, 0.0002, -0.014),
                      "Mat_DS_PaperTint", radius=0.0045, bevel_w=0.0002, corner_segs=4, parent=root)

    join_children_by_material(root, keep_separate_names={"ModeStrip"}, prefix="Receipt")
    make_anchor("Anchor_ModeText",    ( 0.020, 0.0006,  0.030), root)
    make_anchor("Anchor_Heading",     ( 0.022, 0.0006,  0.018), root)
    make_anchor("Anchor_Body",        ( 0.022, 0.0006, -0.004), root)
    make_anchor("Anchor_Detail",      ( 0.022, 0.0006, -0.028), root)
    make_anchor("Anchor_RunAgainKey", (-0.039, 0.0004, -0.014), root)
    make_anchor("Anchor_Hint",        (-0.039, 0.0006, -0.014), root)
    return root


def build_doc_archive_tray_polished(root_name="Doc_ArchiveTray_Polished"):
    """
    Miniature Desktop File Organizer Out-Tray (145 x 112 x 48 mm) for Deliverable 6.
    Tabletop prop (Z is UP in Blender -> Y is UP in Unity).
    Features:
      - Beveled walnut base + slotted side rails with low front reach-in U-cutout (-Y = toward user)
      - Warm wood front trim lip + brass corner pins
      - Miniature neat stack of filed cream paper edges inside + standing vertical rear tab divider
      - Angled front badge mount pocket (`Anchor_FiledPlate`) & rear readout card cradle (`Anchor_ReadoutAnchor`)
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, d = 0.145, 0.112
    # Tray floor (Z = 0..0.008)
    make_rounded_slab("Tray_Floor", (w, d, 0.008), (0, 0, 0.004),
                      "Mat_DS_TrayWood", radius=0.008, bevel_w=0.0015, corner_segs=5, plane='XY', parent=root)
    # Left & Right sculpted side walls (+Y = front toward Unity -Z user)
    for side, sx in [("L", (w - 0.010) * 0.5), ("R", -(w - 0.010) * 0.5)]:
        make_rounded_slab(f"Tray_SideWall_{side}", (0.010, d - 0.004, 0.034), (sx, 0.0, 0.021),
                          "Mat_DS_WarmWood", radius=0.004, bevel_w=0.0012, corner_segs=4, plane='XY', parent=root)
        make_cyl(f"Tray_BrassPin_{side}", 0.0022, 0.004, (sx * 1.02, d * 0.5 - 0.014, 0.026),
                 "Mat_DS_Brass", vertices=12, rot_deg=(0, 90, 0), parent=root)

    # Rear high binder wall (-Y -> Unity +Z back)
    make_rounded_slab("Tray_BackWall", (w, 0.012, 0.044), (0, -d * 0.5 + 0.006, 0.026),
                      "Mat_DS_TrayWood", radius=0.005, bevel_w=0.0015, corner_segs=4, plane='XY', parent=root)
    # Low front retention lip (+Y = toward Unity -Z user) with two side bolsters leaving center open
    for side, sx in [("L", 0.046), ("R", -0.046)]:
        make_rounded_slab(f"Tray_FrontLip_{side}", (0.044, 0.010, 0.018), (sx, d * 0.5 - 0.005, 0.013),
                          "Mat_DS_WarmWood", radius=0.003, bevel_w=0.001, corner_segs=3, plane='XY', parent=root)

    # Angled miniature upright divider tab at rear + built-in interior paper base stack cue
    make_rounded_slab("Tray_FiledPaperBase", (0.112, 0.082, 0.005), (0, 0.002, 0.0105),
                      "Mat_DS_PaperEdge", radius=0.004, bevel_w=0.0008, corner_segs=4, plane='XY', parent=root)
    make_rounded_slab("Tray_DividerTab", (0.098, 0.005, 0.038), (0, -0.034, 0.032),
                      "Mat_DS_AccentTeal", radius=0.003, bevel_w=0.0008, corner_segs=4, plane='XY', parent=root)

    # Slanted front plate cradle (for FiledPoke wide key / count badge)
    make_rounded_slab("Tray_FrontBadgeBack", (0.064, 0.006, 0.026), (0, d * 0.5 + 0.001, 0.018),
                      "Mat_DS_Plate", radius=0.004, bevel_w=0.0008, corner_segs=4, rot_deg=(-28, 0, 0), parent=root)

    join_children_by_material(root, prefix="ArchiveTray")
    make_anchor("Anchor_SpawnPoint",   (0,  0.002, 0.015), root)
    make_anchor("Anchor_StackSlot_0",  (0,  0.002, 0.014), root)
    make_anchor("Anchor_StackSlot_1",  (0,  0.002, 0.020), root)
    make_anchor("Anchor_StackSlot_2",  (0,  0.002, 0.026), root)
    make_anchor("Anchor_StackSlot_3",  (0,  0.002, 0.032), root)
    make_anchor("Anchor_FiledPlate",   (0,  d * 0.5 + 0.005, 0.018), root, rot_deg=(-28, 0, 0))
    make_anchor("Anchor_ReadoutAnchor",(0, -0.030, 0.090), root)
    return root


# ==============================================================================
# DELIVERABLE 2 — PROPORTIONED PHYSICAL POKE BUTTONS (4 ASPECT VARIANTS)
# ==============================================================================

def _build_poke_key_variant(variant_name, cap_w, cap_h, corner_r=0.14, wall=0.092, clearance=0.028, back_y=0.160, rim_y=0.186):
    """
    Creates cleanly matched Housing + Cap FBX pair for a given aspect ratio!
    Preserves exact BuildPaperV2 / BuildTaskDockV2 Unity kinematics:
      - Housing rests on z=0 plane (`back = 0.160`, `rim = 0.186`) -> ButtonVisual at `z = -0.22` (or -0.30).
      - Cap sits in ButtonVisual-local space centered at `y = 0.004 + 0.276 * 0.5 = 0.142`, depth `0.276`.
      - Housing uses uniform `wall` + uniform `clearance` around ALL 4 sides — ZERO corner/rim distortion!
      - Cap includes Slot 0 = `Mat_DS_KeyCapCream` body + Slot 1 = `Mat_DS_KeyStateRim` accent collar.
    """
    well_w = cap_w + 2.0 * clearance
    well_h = cap_h + 2.0 * clearance
    outer_w = well_w + 2.0 * wall
    outer_h = well_h + 2.0 * wall
    outer_r = corner_r + wall * 0.65 + clearance * 0.5
    wall_depth = back_y + rim_y
    wall_y = (back_y - rim_y) * 0.5

    def make_housing():
        root = bpy.data.objects.new(f"{variant_name}_Housing", None)
        bpy.context.collection.objects.link(root)
        make_rounded_ring_frame(
            "Housing_Mesh",
            outer_wh=(outer_w, outer_h),
            wall=wall,
            depth_y=wall_depth,
            r_outer=outer_r,
            location=(0, wall_y, 0),
            mat_outer="Mat_DS_Housing",
            mat_inner="Mat_DS_HousingInner",
            back_thick=0.048,
            corner_segs=5,
            parent=root,
            with_floor_back=True,
            front_chamfer=0.022,
        )
        return root

    def make_cap():
        root = bpy.data.objects.new(f"{variant_name}_Cap", None)
        bpy.context.collection.objects.link(root)
        cap_depth = 0.276
        cap_y = 0.004 + cap_depth * 0.5
        # Main softly contoured keycap body
        make_rounded_slab(
            "Cap_Body",
            (cap_w, cap_depth, cap_h),
            (0, cap_y, 0),
            "Mat_DS_KeyCapCream",
            radius=corner_r,
            bevel_w=0.055,
            corner_segs=5,
            parent=root,
        )
        # Subtle recessed state/hover accent rim collar hugging cap perimeter cleanly inside the clearance gap
        make_rounded_ring_frame(
            "Cap_StateRim",
            outer_wh=(cap_w + 0.016, cap_h + 0.016),
            wall=0.024,
            depth_y=0.042,
            r_outer=corner_r + 0.008,
            location=(0, cap_y - cap_depth * 0.28, 0),
            mat_outer="Mat_DS_KeyStateRim",
            mat_inner="Mat_DS_KeyStateRim",
            back_thick=0.0,
            corner_segs=5,
            parent=root,
            with_floor_back=False,
            front_chamfer=0.006,
        )
        make_anchor("Anchor_CapLabel", (0, cap_y - cap_depth * 0.5 - 0.015, 0), root)
        return root

    return make_housing, make_cap


# ==============================================================================
# DELIVERABLE 3 — BETTER TASK CARDS, TASK DOCK & KANBAN BOARD
# ==============================================================================

def build_task_card_v2_polished(root_name="TaskCard_V2_Polished"):
    """
    Refined 76 x 48 x 3.0 mm Miniature Task Card:
      - Soft 3.5 mm rounded ivory card face (`Mat_DS_Paper`) + subtle bevelled edge frame (`Mat_DS_PaperEdge`)
      - Distinct rounded top `Card_RoleHeaderStrip` pocket (`Mat_DS_AccentBlue`) for CardRoleTint MPB
      - Separate toggleable `Card_SelectionRim` rounded teal halo frame (`Mat_DS_AccentTeal`)
      - Zero baked labels; anchors match BuildTaskDockV2.SimplifyCard coordinates
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, h = 0.076, 0.048
    make_rounded_slab("Card_Body", (w, 0.0030, h), (0, -0.0015, 0),
                      "Mat_DS_Paper", radius=0.0035, bevel_w=0.0006, corner_segs=5, parent=root)
    make_rounded_slab("Card_BackingEdge", (w + 0.0014, 0.0022, h + 0.0014), (0, -0.0023, 0),
                      "Mat_DS_PaperEdge", radius=0.0038, bevel_w=0.0005, corner_segs=5, parent=root)

    # Top role banner strip (8.8 mm tall, sits cleanly at +Y -> Unity -Z top leaving central area 100% open)
    strip_h = 0.0088
    make_rounded_slab("Card_RoleHeaderStrip", (w - 0.004, 0.0012, strip_h), (0, 0.0006, h * 0.5 - strip_h * 0.5 - 0.0012),
                      "Mat_DS_AccentBlue", radius=0.0024, bevel_w=0.0003, corner_segs=4, parent=root)

    # Separate toggleable Selection Rim halo frame around outer edge
    make_rounded_ring_frame(
        "Card_SelectionRim",
        outer_wh=(w + 0.006, h + 0.006),
        wall=0.0026,
        depth_y=0.0036,
        r_outer=0.0052,
        location=(0, 0.0002, 0),
        mat_outer="Mat_DS_AccentTeal",
        mat_inner="Mat_DS_AccentTeal",
        back_thick=0.0,
        corner_segs=5,
        parent=root,
        with_floor_back=False,
        front_chamfer=0.0006,
    )

    make_anchor("Anchor_Role",     ( 0.0120, 0.0024,  0.0185), root)
    make_anchor("Anchor_Priority", (-0.0165, 0.0024,  0.0185), root)
    make_anchor("Anchor_Title",    ( 0.0000, 0.0020,  0.0055), root)
    make_anchor("Anchor_State",    ( 0.0000, 0.0020, -0.0095), root)
    make_anchor("Anchor_Source",   ( 0.0000, 0.0020, -0.0185), root)
    return root


def build_task_dock_plate(root_name="TaskDock_Plate"):
    """
    Rounded 160 x 90 x 5 mm Front-Desk TaskDock Plate:
      - Dark `#1B1F27` rounded slate face inside sculpted `#2A2F3A` rounded housing rim (`r = 10 mm`)
      - Rear easel desk cradle foot so the tilted dock rests tangibly on the desk
      - Clean separation between ModeChip pocket on lower-left (48 x 18 mm) and Start/Board keys on lower-right!
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, h = 0.160, 0.090
    make_rounded_slab("Dock_Frame", (w + 0.008, 0.0055, h + 0.008), (0, -0.0028, 0),
                      "Mat_DS_Housing", radius=0.011, bevel_w=0.0014, corner_segs=6, parent=root)
    make_rounded_slab("Dock_Plate", (w, 0.0032, h), (0, -0.0016, 0),
                      "Mat_DS_Plate", radius=0.009, bevel_w=0.0008, corner_segs=6, parent=root)

    # Rear weighted easel prop foot (-Y -> Unity +Z back)
    make_rounded_slab("Dock_EaselFoot", (0.096, 0.018, 0.010), (0, -0.011, -h * 0.5 + 0.007),
                      "Mat_DS_HousingInner", radius=0.004, bevel_w=0.0012, corner_segs=4, parent=root)

    # Separate ModeChip rounded badge plate (lower-left: Unity X = -0.052 -> Blender X = +0.052; plenty of clearance to Start!)
    make_rounded_slab("ModeChip", (0.048, 0.0014, 0.018), (0.052, 0.0009, -0.024),
                      "Mat_DS_AccentAmber", radius=0.0045, bevel_w=0.0004, corner_segs=4, parent=root)

    # Subtle recessed key wells on lower-right (Unity X = +0.004 Start & +0.052 Board -> Blender X = -0.004 & -0.052)
    for kx in (-0.004, -0.052):
        make_rounded_slab(f"Dock_KeyPocket_{int(abs(kx)*1000)}", (0.044, 0.0008, 0.026), (kx, 0.0006, -0.024),
                          "Mat_DS_HousingInner", radius=0.005, bevel_w=0.0002, corner_segs=4, parent=root)

    join_children_by_material(root, keep_separate_names={"ModeChip"}, prefix="TaskDock")
    make_anchor("Anchor_Summary",  ( 0.000, 0.0016,  0.0360), root)
    make_anchor("Anchor_Title",    ( 0.000, 0.0016,  0.0230), root)
    make_anchor("Anchor_State",    ( 0.000, 0.0016,  0.0100), root)
    make_anchor("Anchor_ModeText", ( 0.052, 0.0021, -0.0240), root)
    make_anchor("Anchor_StartKey", (-0.004, 0.0012, -0.0240), root)
    make_anchor("Anchor_BoardKey", (-0.052, 0.0012, -0.0240), root)
    return root


def build_taskboard_kanban_frame_v2(root_name="TaskBoard_KanbanFrame_V2"):
    """
    Refined & Material-Welded Miniature Kanban Frame:
      - Rounded warm wood architectural border rails + slate backing
      - Separate rounded Column Headers (`Header_Queued`, `Header_InProgress`, `Header_Blocked`, `Header_Completed`)
      - Tactile rounded recessed lanes + crisp subtle slot rails
      - Dedicated upper Title Pediment + right-side `+ New` key pocket
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    make_rounded_slab("Board_Backing", (0.422, 0.012, 0.262), (0, -0.003, 0.145),
                      "Mat_DS_FeltSlate", radius=0.010, bevel_w=0.002, corner_segs=5, parent=root)
    make_rounded_slab("Board_TopRail", (0.436, 0.020, 0.016), (0, 0.0, 0.278),
                      "Mat_DS_WarmWood", radius=0.005, bevel_w=0.002, corner_segs=4, parent=root)
    make_rounded_slab("Board_BotRail", (0.436, 0.024, 0.018), (0, 0.0, 0.012),
                      "Mat_DS_WarmWood", radius=0.005, bevel_w=0.002, corner_segs=4, parent=root)
    make_rounded_slab("Board_LeftRail", (0.016, 0.020, 0.274), (0.210, 0.0, 0.145),
                      "Mat_DS_WarmWood", radius=0.005, bevel_w=0.002, corner_segs=4, parent=root)
    make_rounded_slab("Board_RightRail", (0.016, 0.020, 0.274), (-0.210, 0.0, 0.145),
                      "Mat_DS_WarmWood", radius=0.005, bevel_w=0.002, corner_segs=4, parent=root)

    # Top Title Pediment (cleanly rounded) + right-side NewTask pocket (+Y -> Unity -Z front)
    make_rounded_slab("Board_TitlePediment", (0.210, 0.016, 0.030), (0.020, 0.002, 0.298),
                      "Mat_DS_TrayWood", radius=0.006, bevel_w=0.002, corner_segs=4, parent=root)
    make_rounded_slab("Board_TitleFace", (0.194, 0.004, 0.022), (0.020, 0.009, 0.298),
                      "Mat_DS_Plate", radius=0.004, bevel_w=0.0008, corner_segs=4, parent=root)
    make_rounded_slab("Board_NewKeyMount", (0.036, 0.014, 0.032), (-0.176, 0.002, 0.298),
                      "Mat_DS_TrayWood", radius=0.005, bevel_w=0.0015, corner_segs=4, parent=root)

    for side, sx in [("L", 0.165), ("R", -0.165)]:
        make_rounded_slab(f"Board_StandFoot_{side}", (0.036, 0.095, 0.012), (sx, -0.015, 0.006),
                          "Mat_DS_TrayWood", radius=0.005, bevel_w=0.002, corner_segs=4, parent=root)
        make_cyl(f"Board_GrabHandle_{side}", 0.0045, 0.050, (sx * 1.32, 0, 0.145),
                 "Mat_DS_Brass", vertices=12, parent=root)

    cols = [
        ("Queued",     0.147, "Mat_DS_AccentBlue"),
        ("InProgress", 0.049, "Mat_DS_AccentLavender"),
        ("Blocked",   -0.049, "Mat_DS_AccentAmber"),
        ("Completed", -0.147, "Mat_DS_AccentSuccess"),
    ]
    keep_headers = set()
    for cname, cx, cmat in cols:
        hname = f"Header_{cname}"
        keep_headers.add(hname)
        make_rounded_slab(hname, (0.088, 0.007, 0.026), (cx, 0.006, 0.248),
                          cmat, radius=0.0045, bevel_w=0.0012, corner_segs=4, parent=root)
        make_anchor(f"HeaderLabel_{cname}_Anchor", (cx, 0.011, 0.248), root)
        make_rounded_slab(f"ColumnLane_{cname}", (0.088, 0.004, 0.198), (cx, 0.004, 0.125),
                          "Mat_DS_Plate", radius=0.005, bevel_w=0.001, corner_segs=4, parent=root)
        for row_idx in range(4):
            sz = 0.198 - row_idx * 0.048
            make_rounded_slab(f"SlotGuide_{cname}_{row_idx+1}", (0.074, 0.002, 0.003), (cx, 0.0065, sz - 0.018),
                              "Mat_DS_PlateRim", radius=0.001, bevel_w=0.0004, corner_segs=3, parent=root)
            make_anchor(f"Socket_{cname}_{row_idx+1}", (cx, 0.009, sz), root)

    for dx in (0.098, 0.0, -0.098):
        make_rounded_slab(f"Divider_{int(dx*1000)}", (0.005, 0.009, 0.236), (dx, 0.004, 0.138),
                          "Mat_DS_WarmWood", radius=0.0015, bevel_w=0.0005, corner_segs=3, parent=root)

    join_children_by_material(root, keep_separate_names=keep_headers, prefix="KanbanV2")
    make_anchor("Board_Title_Anchor",  ( 0.020, 0.012, 0.298), root)
    make_anchor("Board_NewKey_Anchor", (-0.176, 0.010, 0.298), root)
    return root


# ==============================================================================
# DELIVERABLE 4 — ROUNDED PLATES & COHERENT DESK SIGNAGE
# ==============================================================================

def build_plate_agent_nameplate(root_name="Plate_AgentNameplate"):
    """
    Rounded 110 x 30 x 4 mm Desk Agent Nameplate:
      - Dark `#1B1F27` rounded plate (`r = 4.5 mm`) with `#252A34` crisp outer rim
      - Flush left 8 mm rounded `RoleStrip` bar (Blender +X = Unity -X left edge)
      - Compact miniature easel foot behind so it rests physically on each desk edge
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, h = 0.110, 0.030
    make_rounded_slab("Nameplate_Rim", (w + 0.004, 0.0036, h + 0.004), (0, -0.0026, 0),
                      "Mat_DS_PlateRim", radius=0.005, bevel_w=0.0008, corner_segs=5, parent=root)
    make_rounded_slab("Nameplate_Plate", (w, 0.0024, h), (0, -0.0012, 0),
                      "Mat_DS_Plate", radius=0.0045, bevel_w=0.0005, corner_segs=5, parent=root)
    make_rounded_slab("Nameplate_Foot", (0.056, 0.012, 0.007), (0, -0.007, -h * 0.5 + 0.0035),
                      "Mat_DS_HousingInner", radius=0.003, bevel_w=0.0008, corner_segs=4, parent=root)

    # RoleStrip left accent bar (kept separate for Blue/Green/Amber per-station material)
    strip_w = 0.007
    make_rounded_slab("RoleStrip", (strip_w, 0.0020, h - 0.004), (w * 0.5 - strip_w * 0.5 - 0.003, 0.0006, 0),
                      "Mat_DS_AccentBlue", radius=0.0022, bevel_w=0.0004, corner_segs=4, parent=root)

    join_children_by_material(root, keep_separate_names={"RoleStrip"}, prefix="Nameplate")
    make_anchor("Anchor_Role",     (-0.004, 0.0014,  0.0060), root)
    make_anchor("Anchor_Activity", (-0.004, 0.0014, -0.0065), root)
    return root


def build_plate_mode_plaque(root_name="Plate_ModePlaque"):
    """
    Rounded 120 x 24 x 4 mm Mode Plaque (+ integral guide strip companion option).
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, h = 0.120, 0.024
    make_rounded_slab("ModePlaque_Rim", (w + 0.004, 0.0036, h + 0.004), (0, -0.0026, 0),
                      "Mat_DS_PlateRim", radius=0.0045, bevel_w=0.0008, corner_segs=5, parent=root)
    make_rounded_slab("ModePlaque_Plate", (w, 0.0024, h), (0, -0.0012, 0),
                      "Mat_DS_Plate", radius=0.004, bevel_w=0.0005, corner_segs=5, parent=root)
    make_rounded_slab("ModePlaque_Foot", (0.064, 0.012, 0.006), (0, -0.007, -h * 0.5 + 0.003),
                      "Mat_DS_HousingInner", radius=0.003, bevel_w=0.0008, corner_segs=4, parent=root)
    make_rounded_slab("ModeStrip", (0.006, 0.0020, h - 0.004), (w * 0.5 - 0.006, 0.0006, 0),
                      "Mat_DS_AccentAmber", radius=0.002, bevel_w=0.0004, corner_segs=4, parent=root)

    join_children_by_material(root, keep_separate_names={"ModeStrip"}, prefix="ModePlaque")
    make_anchor("Anchor_Mode",   (-0.003, 0.0014,  0.0050), root)
    make_anchor("Anchor_Status", (-0.003, 0.0014, -0.0060), root)
    return root


def build_plate_utility_sign(root_name="Plate_UtilitySign"):
    """
    Compact Rounded 200 x 18 x 3 mm Utility Banner Plate (for CardToolbar / AssignDock captions
    and FirstUseGuide strips).
    """
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, h = 0.200, 0.018
    make_rounded_slab("Utility_Rim", (w + 0.004, 0.0030, h + 0.004), (0, -0.0022, 0),
                      "Mat_DS_PlateRim", radius=0.0035, bevel_w=0.0006, corner_segs=4, parent=root)
    make_rounded_slab("Utility_Plate", (w, 0.0020, h), (0, -0.0010, 0),
                      "Mat_DS_Plate", radius=0.0030, bevel_w=0.0004, corner_segs=4, parent=root)
    make_rounded_slab("UtilityStrip", (0.005, 0.0016, h - 0.004), (w * 0.5 - 0.005, 0.0005, 0),
                      "Mat_DS_AccentTeal", radius=0.0015, bevel_w=0.0003, corner_segs=3, parent=root)

    join_children_by_material(root, keep_separate_names={"UtilityStrip"}, prefix="Utility")
    make_anchor("Anchor_Text", (-0.003, 0.0013, 0), root)
    return root


# ==============================================================================
# DELIVERABLE 7 — MATERIAL-WELDED TRAY OPTIMIZATION
# ==============================================================================

def build_doc_inbox_tray_optimized(root_name="Doc_InboxTray_Optimized"):
    """Lightweight rounded Inbox Tray (145 x 112 x 32 mm), 3 merged static materials."""
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)

    w, d = 0.145, 0.112
    make_rounded_slab("Inbox_Floor", (w, d, 0.007), (0, 0, 0.0035),
                      "Mat_DS_WarmWood", radius=0.008, bevel_w=0.0015, corner_segs=5, plane='XY', parent=root)
    for side, sx in [("L", (w - 0.010) * 0.5), ("R", -(w - 0.010) * 0.5)]:
        make_rounded_slab(f"Inbox_Wall_{side}", (0.010, d - 0.004, 0.026), (sx, 0, 0.0165),
                          "Mat_DS_TrayWood", radius=0.004, bevel_w=0.0012, corner_segs=4, plane='XY', parent=root)
    make_rounded_slab("Inbox_Back", (w, 0.010, 0.028), (0, -d * 0.5 + 0.005, 0.0175),
                      "Mat_DS_TrayWood", radius=0.004, bevel_w=0.0012, corner_segs=4, plane='XY', parent=root)
    make_rounded_slab("Inbox_BrassLip", (0.048, 0.004, 0.005), (0, d * 0.5 - 0.002, 0.007),
                      "Mat_DS_Brass", radius=0.002, bevel_w=0.0005, corner_segs=3, plane='XY', parent=root)

    join_children_by_material(root, prefix="InboxOpt")
    make_anchor("Anchor_PaperSlot", (0, 0.002, 0.010), root)
    return root


# ==============================================================================
# EXPORT & COMPARISON RENDER PIPELINE
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


def render_blender_overview():
    """Renders a crisp Blender EEVEE showcase sheet of the new rounded documents, keys, cards, and plates."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 960

    world = bpy.data.worlds.new("ShowcaseWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.11, 0.13, 0.16, 1.0)
        bg.inputs[1].default_value = 1.25

    make_rounded_slab("Studio_Desk", (1.20, 0.80, 0.012), (0, 0, -0.006),
                      "Mat_DS_FeltSlate", radius=0.02, bevel_w=0.002, plane='XY')

    # Expanded Folio (center-back)
    exp = build_doc_paper_v2_expanded("Showcase_Expanded")
    exp.location = (0.14, -0.10, 0.16)
    exp.rotation_euler = (math.radians(12), 0, 0)

    # Receipt Card (left-front)
    rec = build_doc_receipt_card("Showcase_Receipt")
    rec.location = (0.40, 0.08, 0.065)
    rec.rotation_euler = (math.radians(20), 0, math.radians(10))

    # Archive Tray (right-back)
    tray = build_doc_archive_tray_polished("Showcase_Tray")
    tray.location = (-0.28, -0.08, 0.0)

    # TaskDock + Card + Plates (front row)
    dock = build_task_dock_plate("Showcase_Dock")
    dock.location = (-0.06, 0.14, 0.055)
    dock.rotation_euler = (math.radians(28), 0, 0)

    card = build_task_card_v2_polished("Showcase_Card")
    card.location = (-0.26, 0.14, 0.040)
    card.rotation_euler = (math.radians(22), 0, 0)

    np = build_plate_agent_nameplate("Showcase_Nameplate")
    np.location = (0.18, 0.18, 0.022)
    np.rotation_euler = (math.radians(28), 0, 0)

    mp = build_plate_mode_plaque("Showcase_ModePlaque")
    mp.location = (0.34, 0.18, 0.020)
    mp.rotation_euler = (math.radians(28), 0, 0)

    # Keycap family row in foreground
    key_specs = [
        ("Square",   1.00, 1.00, 0.14, 0.042, 0.042, ( 0.06, 0.26, 0.016), "Mat_DS_KeyCapCream"),
        ("Wide",     1.75, 0.96, 0.14, 0.032, 0.032, (-0.03, 0.26, 0.016), "Mat_DS_KeyCapTeal"),
        ("Chip",     2.14, 0.92, 0.24, 0.028, 0.028, (-0.12, 0.26, 0.016), "Mat_DS_KeyCapLavender"),
        ("HeroWide", 2.50, 0.96, 0.14, 0.046, 0.046, (-0.24, 0.26, 0.016), "Mat_DS_KeyCapSuccess"),
    ]
    for kname, cw, ch, kr, sx, sz, kloc, kmat in key_specs:
        mk_h, mk_c = _build_poke_key_variant(f"UI_PokeKey_{kname}", cw, ch, corner_r=kr)
        h = mk_h()
        h.scale = (sx, sz, sz)
        h.location = kloc
        h.rotation_euler = (math.radians(55), 0, 0)
        c = mk_c()
        c.scale = (sx, sz, sz)
        # Offset along local +Y by 0.22 * sz to show realistic rest key position proud of housing
        offset = mathutils.Euler((math.radians(55), 0, 0)).to_matrix() @ mathutils.Vector((0, 0.22 * sz, 0))
        c.location = mathutils.Vector(kloc) + offset
        c.rotation_euler = (math.radians(55), 0, 0)
        for ch_obj in c.children:
            if ch_obj.type == 'MESH' and len(ch_obj.data.materials) > 0:
                if "Cap_Body" in ch_obj.name:
                    ch_obj.data.materials[0] = get_mat(kmat)

    for lname, loc, energy, col in [
        ("Key",  ( 0.55,  0.85, 0.85), 95.0, (1.0, 0.97, 0.92)),
        ("Fill", (-0.65,  0.60, 0.55), 48.0, (0.88, 0.93, 1.0)),
        ("Rim",  ( 0.00, -0.65, 0.65), 55.0, (1.0, 0.96, 0.90)),
    ]:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        ld.color = col
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 38
    cam_o = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o
    cam_pos = mathutils.Vector((0.06, 1.04, 0.42))
    target  = mathutils.Vector((0.06, 0.05, 0.11))
    cam_o.location = cam_pos
    cam_o.rotation_euler = (target - cam_pos).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_DIR, "Blender_FinalRefinement_Showcase.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)
    return out_png


def main():
    report = []

    # 1. Documents & Archive Tray
    doc_builders = {
        "Doc_PaperV2_Expanded":     lambda: build_doc_paper_v2_expanded("Doc_PaperV2_Expanded"),
        "Doc_PaperV2_DetailsSheet": lambda: build_doc_paper_v2_details("Doc_PaperV2_DetailsSheet"),
        "Doc_PaperV2_Small":        lambda: build_doc_paper_v2_small("Doc_PaperV2_Small"),
        "Doc_ReceiptCard":          lambda: build_doc_receipt_card("Doc_ReceiptCard"),
        "Doc_ArchiveTray_Polished": lambda: build_doc_archive_tray_polished("Doc_ArchiveTray_Polished"),
    }
    for name, fn in doc_builders.items():
        report.append(export_asset("Documents", name, fn))

    # 2. Proportioned Poke Buttons (Square 1:1, Wide 1.75:1, HeroWide 2.50:1, Chip 2.14:1)
    key_specs = [
        ("UI_PokeKey_Square",   1.00, 0.98, 0.14),
        ("UI_PokeKey_Wide",     1.75, 0.96, 0.14),
        ("UI_PokeKey_HeroWide", 2.50, 0.96, 0.14),
        ("UI_PokeKey_Chip",     2.14, 0.92, 0.24),
    ]
    for base_name, cw, ch, cr in key_specs:
        mk_h, mk_c = _build_poke_key_variant(base_name, cw, ch, corner_r=cr)
        report.append(export_asset("UI", f"{base_name}_Housing", mk_h))
        report.append(export_asset("UI", f"{base_name}_Cap", mk_c))

    # 3. Task Cards, Dock & Board
    tb_builders = {
        "TaskCard_V2_Polished":     lambda: build_task_card_v2_polished("TaskCard_V2_Polished"),
        "TaskDock_Plate":           lambda: build_task_dock_plate("TaskDock_Plate"),
        "TaskBoard_KanbanFrame_V2": lambda: build_taskboard_kanban_frame_v2("TaskBoard_KanbanFrame_V2"),
    }
    for name, fn in tb_builders.items():
        report.append(export_asset("TaskBoard", name, fn))

    # 4. Rounded Plates
    plate_builders = {
        "Plate_AgentNameplate": lambda: build_plate_agent_nameplate("Plate_AgentNameplate"),
        "Plate_ModePlaque":     lambda: build_plate_mode_plaque("Plate_ModePlaque"),
        "Plate_UtilitySign":    lambda: build_plate_utility_sign("Plate_UtilitySign"),
    }
    for name, fn in plate_builders.items():
        report.append(export_asset("Plates", name, fn))

    # 5. Material-Welded Inbox Tray
    report.append(export_asset("Optimized", "Doc_InboxTray_Optimized",
                               lambda: build_doc_inbox_tray_optimized("Doc_InboxTray_Optimized")))

    # 6. Blender EEVEE showcase render
    showcase_png = render_blender_overview()

    summary = {
        "exported_count": len(report),
        "showcase_render": showcase_png,
        "assets": report,
    }
    report_path = os.path.join(PREVIEW_DIR, "final_refinement_export_report.json")
    with open(report_path, "w") as f:
        json.dump(summary, f, indent=2)
    print(f"[FinalRefinement] Exported {len(report)} assets. Report: {report_path}")


if __name__ == "__main__":
    main()
