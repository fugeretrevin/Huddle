import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Priority 3: Stamp Juice (Impact Squash + Opaque Paper Ink Marks)
#
# Generates:
#   1. Doc_Stamp_Approve.fbx & Doc_Stamp_Reject.fbx (with `Stamp_SquashPivot`
#      and baked `Stamp_Impact_Squash` clip: frames 1-10 at 24 FPS, 0.42s, No loop)
#   2. Doc_StampInkMark_Approve.fbx & Doc_StampInkMark_Reject.fbx
#      (opaque 3D decal-style relief seal meshes fitted inside StampZone 80x78mm)
#   3. Blender Eevee before/after inspection renders in Screenshots/ArtPreviews/
# ==============================================================================

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
BLEND_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "Documents")
FBX_DIR = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models", "Documents")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")

for d in (BLEND_DIR, FBX_DIR, PREVIEW_DIR):
    os.makedirs(d, exist_ok=True)


def srgb_to_linear(c):
    res = []
    for ch in c[:3]:
        res.append(ch / 12.92 if ch <= 0.04045 else ((ch + 0.055) / 1.055) ** 2.4)
    res.append(c[3] if len(c) > 3 else 1.0)
    return tuple(res)


PALETTE = {
    "Mat_WarmWood":        {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.46, "metallic": 0.0},
    "Mat_DarkWood":        {"color": (0.420, 0.295, 0.205, 1.0), "roughness": 0.50, "metallic": 0.0},
    "Mat_WarmBeige":       {"color": (0.910, 0.875, 0.815, 1.0), "roughness": 0.42, "metallic": 0.0},
    "Mat_Brass":           {"color": (0.860, 0.700, 0.290, 1.0), "roughness": 0.24, "metallic": 0.80},
    "Mat_PaperWarm":       {"color": (0.988, 0.974, 0.940, 1.0), "roughness": 0.52, "metallic": 0.0},
    "Mat_Developer_Coral": {"color": (0.960, 0.440, 0.350, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Tester_Mint":     {"color": (0.300, 0.780, 0.580, 1.0), "roughness": 0.34, "metallic": 0.0},
    "Mat_StatusGreen":     {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.22, 0.84, 0.45), "emission_strength": 0.95},
    "Mat_StatusRed":       {"color": (0.910, 0.250, 0.230, 1.0), "roughness": 0.20, "metallic": 0.0, "emission": (0.91, 0.25, 0.23), "emission_strength": 0.95},
    "Mat_StampInk_Approve": {"color": (0.102, 0.420, 0.239, 1.0), "roughness": 0.72, "metallic": 0.0},
    "Mat_StampInk_Reject":  {"color": (0.620, 0.161, 0.129, 1.0), "roughness": 0.72, "metallic": 0.0},
}


def get_mat(name):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    spec = PALETTE[name]
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = srgb_to_linear(spec["color"])
        bsdf.inputs["Roughness"].default_value = spec["roughness"]
        bsdf.inputs["Metallic"].default_value = spec["metallic"]
        if "emission" in spec and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = srgb_to_linear(spec["emission"] + (1.0,))
            bsdf.inputs["Emission Strength"].default_value = spec.get("emission_strength", 1.0)
    return mat


def assign_mat(obj, mat_name):
    mat = get_mat(mat_name)
    if len(obj.data.materials) == 0:
        obj.data.materials.append(mat)
    else:
        obj.data.materials[0] = mat


def shade_smooth(obj):
    if obj.type != 'MESH':
        return
    for p in obj.data.polygons:
        p.use_smooth = True


def make_box(name, size_xyz, loc=(0, 0, 0), mat_name="Mat_Brass", bevel_width=0.0, rot_deg=(0, 0, 0), parent=None, smooth=True):
    sx, sy, sz = size_xyz
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    if bevel_width > 0.0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel_width, segments=2, affect='EDGES')
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(obj)
    assign_mat(obj, mat_name)
    if smooth:
        shade_smooth(obj)
    if parent:
        obj.parent = parent
    return obj


def make_cyl(name, radius, depth, loc=(0, 0, 0), mat_name="Mat_DarkWood", vertices=18, parent=None):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=vertices, radius1=radius, radius2=radius, depth=depth)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    bpy.context.collection.objects.link(obj)
    assign_mat(obj, mat_name)
    shade_smooth(obj)
    if parent:
        obj.parent = parent
    return obj


def make_torus(name, maj_r, min_r, loc=(0, 0, 0), mat_name="Mat_Brass", maj_seg=18, min_seg=8, parent=None):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=maj_r, minor_radius=min_r,
        major_segments=maj_seg, minor_segments=min_seg,
        location=loc
    )
    obj = bpy.context.active_object
    obj.name = name
    assign_mat(obj, mat_name)
    shade_smooth(obj)
    if parent:
        obj.parent = parent
    return obj


def make_sphere(name, radii, loc=(0, 0, 0), mat_name="Mat_WarmWood", parent=None):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=1.0)
    bmesh.ops.scale(bm, vec=radii, verts=bm.verts)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    bpy.context.collection.objects.link(obj)
    assign_mat(obj, mat_name)
    shade_smooth(obj)
    if parent:
        obj.parent = parent
    return obj


def make_anchor(name, loc=(0, 0, 0), parent=None):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.012
    emp.location = loc
    bpy.context.collection.objects.link(emp)
    if parent:
        emp.parent = parent
    return emp


def join_children_by_material(root_obj, prefix="Joined"):
    by_mat = {}
    for ch in list(root_obj.children):
        if ch.type != 'MESH' or not ch.data.materials:
            continue
        mname = ch.data.materials[0].name
        by_mat.setdefault(mname, []).append(ch)

    for mname, objs in by_mat.items():
        if len(objs) == 1:
            objs[0].name = f"{prefix}_{mname}"
            continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        joined = bpy.context.active_object
        joined.name = f"{prefix}_{mname}"
        joined.parent = root_obj


def count_root_stats(root_obj):
    deps = bpy.context.evaluated_depsgraph_get()
    mesh_count = 0
    total_verts = 0
    total_tris = 0
    queue = list(root_obj.children)
    while queue:
        obj = queue.pop(0)
        queue.extend(obj.children)
        if obj.type == 'MESH':
            mesh_count += 1
            em = obj.evaluated_get(deps).to_mesh()
            em.calc_loop_triangles()
            total_verts += len(em.vertices)
            total_tris += len(em.loop_triangles)
            obj.evaluated_get(deps).to_mesh_clear()
    return mesh_count, total_verts, total_tris


# ==============================================================================
# 1. STAMP WITH IMPACT SQUASH ANIMATION (`Stamp_Impact_Squash`, 10f @ 24fps)
# ==============================================================================

def build_squashable_stamp(name, crown_mat, pad_mat):
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    # Bottom contact squash pivot stays fixed at Z=0 so rubber pad stays flush on paper
    squash_pivot = make_anchor("Stamp_SquashPivot", (0, 0, 0.0), parent=root)

    make_box("Stamp_BrassBase", (0.042, 0.032, 0.010), (0, 0, 0.006), "Mat_Brass", bevel_width=0.002, parent=squash_pivot)
    make_box("Stamp_RubberDie", (0.038, 0.028, 0.003), (0, 0, 0.0015), pad_mat, bevel_width=0.0008, parent=squash_pivot)
    make_box("Stamp_FrontPip", (0.014, 0.003, 0.005), (0, -0.0165, 0.0065), crown_mat, bevel_width=0.0008, parent=squash_pivot)
    make_cyl("Stamp_WoodStem", 0.0085, 0.026, (0, 0, 0.024), "Mat_DarkWood", vertices=18, parent=squash_pivot)
    make_torus("Stamp_FerrulRing", 0.0095, 0.0018, (0, 0, 0.014), "Mat_Brass", maj_seg=18, min_seg=8, parent=squash_pivot)
    make_sphere("Stamp_HandleKnob", (0.016, 0.016, 0.017), (0, 0, 0.044), "Mat_WarmWood", parent=squash_pivot)
    make_sphere("Stamp_ColorCrown", (0.013, 0.013, 0.007), (0, 0, 0.056), crown_mat, parent=squash_pivot)

    join_children_by_material(squash_pivot, prefix="Stamp")

    make_anchor("Stamp_Contact_Anchor", (0, 0, 0.000), root)
    make_anchor("Grab_Pivot_Anchor", (0, 0, 0.038), root)

    # Bake slight volume-preserving impact squash on Stamp_SquashPivot
    # Blender Z-up -> (X, Y, Z) corresponds to Unity (X, Z, Y) vertical squash on Z
    squash_pivot.animation_data_create()
    action = bpy.data.actions.new(name="Stamp_Impact_Squash")
    squash_pivot.animation_data.action = action

    keys = [
        (1,  (1.00, 1.00, 1.00)),
        (3,  (1.14, 1.14, 0.78)),
        (6,  (0.95, 0.95, 1.08)),
        (8,  (1.02, 1.02, 0.97)),
        (10, (1.00, 1.00, 1.00)),
    ]
    for f, sc in keys:
        squash_pivot.location = (0.0, 0.0, 0.0)
        squash_pivot.scale = sc
        squash_pivot.keyframe_insert(data_path="location", frame=f)
        squash_pivot.keyframe_insert(data_path="scale", frame=f)

    if hasattr(action, "fcurves"):
        for fc in action.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = 'BEZIER'
                kp.handle_left_type = 'AUTO_CLAMPED'
                kp.handle_right_type = 'AUTO_CLAMPED'

    track = squash_pivot.animation_data.nla_tracks.new()
    track.name = "Stamp_Impact_Squash"
    strip = track.strips.new(name="Stamp_Impact_Squash", start=1, action=action)
    strip.action_frame_start = 1
    strip.action_frame_end = 10
    squash_pivot.animation_data.action = None
    bpy.context.scene.frame_set(1)
    squash_pivot.scale = (1.0, 1.0, 1.0)

    return root, squash_pivot


# ==============================================================================
# 2. OPAQUE PAPER INK MARKS (`Doc_StampInkMark_Approve`, `Doc_StampInkMark_Reject`)
# Fits centered inside paper StampZone (80 mm x 78 mm, rotated -12 deg)
# ==============================================================================

def _build_ink_rect_border(width, height, thick, z_h, mat_name, parent):
    hw = width * 0.5
    hh = height * 0.5
    zc = z_h * 0.5
    bw = thick * 0.12
    # Top & bottom horizontal bars
    make_box("Border_Top",    (width, thick, z_h), (0,  hh - thick * 0.5, zc), mat_name, bevel_width=bw, parent=parent, smooth=False)
    make_box("Border_Bottom", (width, thick, z_h), (0, -hh + thick * 0.5, zc), mat_name, bevel_width=bw, parent=parent, smooth=False)
    # Left & right vertical bars (tucked neatly inside top/bottom to avoid overlap)
    side_h = height - thick * 2.0 + 0.0002
    make_box("Border_Left",   (thick, side_h, z_h), (-hw + thick * 0.5, 0, zc), mat_name, bevel_width=bw, parent=parent, smooth=False)
    make_box("Border_Right",  (thick, side_h, z_h), ( hw - thick * 0.5, 0, zc), mat_name, bevel_width=bw, parent=parent, smooth=False)


def build_ink_mark_approve():
    root = bpy.data.objects.new("Doc_StampInkMark_Approve", None)
    bpy.context.collection.objects.link(root)

    mat = "Mat_StampInk_Approve"
    z_h = 0.00036
    zc = z_h * 0.5

    # Double rounded seal frame (58 x 24 mm — crisp inside 80x78 mm zone even at -12 deg)
    _build_ink_rect_border(0.058, 0.024, 0.0022, z_h, mat, root)
    _build_ink_rect_border(0.052, 0.018, 0.0009, z_h, mat, root)

    # Checkmark emblem on left side (Blender -Y is readable bottom in UV/top-down view, +Y is top of stamp)
    # Using Blender XY top-down: +X = right, +Y = top!
    make_box("Check_Short", (0.0068, 0.0022, z_h), (-0.0165, -0.0012, zc), mat, bevel_width=0.0002, rot_deg=(0, 0, -45), parent=root, smooth=False)
    make_box("Check_Long",  (0.0125, 0.0022, z_h), (-0.0108,  0.0006, zc), mat, bevel_width=0.0002, rot_deg=(0, 0,  48), parent=root, smooth=False)

    # Clean vertical divider + 3 crisp ink "APPROVED" bar motifs on right
    make_box("Ink_Divider", (0.0011, 0.0125, z_h), (-0.0022, 0.0, zc), mat, bevel_width=0.00015, parent=root, smooth=False)
    make_box("Ink_BarTop",  (0.0210, 0.0028, z_h), ( 0.0110,  0.0040, zc), mat, bevel_width=0.0002, parent=root, smooth=False)
    make_box("Ink_BarMid",  (0.0175, 0.0020, z_h), ( 0.0092,  0.0000, zc), mat, bevel_width=0.00015, parent=root, smooth=False)
    make_box("Ink_BarBot",  (0.0135, 0.0020, z_h), ( 0.0072, -0.0040, zc), mat, bevel_width=0.00015, parent=root, smooth=False)

    join_children_by_material(root, prefix="InkMarkApprove")
    return root


def build_ink_mark_reject():
    root = bpy.data.objects.new("Doc_StampInkMark_Reject", None)
    bpy.context.collection.objects.link(root)

    mat = "Mat_StampInk_Reject"
    z_h = 0.00036
    zc = z_h * 0.5

    # Bold rectangular border with distinct corner notch blocks
    _build_ink_rect_border(0.058, 0.024, 0.0026, z_h, mat, root)
    _build_ink_rect_border(0.051, 0.017, 0.0009, z_h, mat, root)

    # Cross ('X') emblem on left side
    make_box("Cross_A", (0.0135, 0.0024, z_h), (-0.0135, 0.0, zc), mat, bevel_width=0.0002, rot_deg=(0, 0,  45), parent=root, smooth=False)
    make_box("Cross_B", (0.0135, 0.0024, z_h), (-0.0135, 0.0, zc), mat, bevel_width=0.0002, rot_deg=(0, 0, -45), parent=root, smooth=False)

    # Heavy warning bars on right ("REJECTED" motif)
    make_box("Ink_Divider",  (0.0012, 0.0125, z_h), (-0.0022, 0.0, zc), mat, bevel_width=0.00015, parent=root, smooth=False)
    make_box("Ink_WarnTop",  (0.0210, 0.0032, z_h), ( 0.0110,  0.0036, zc), mat, bevel_width=0.0002, parent=root, smooth=False)
    make_box("Ink_WarnBot",  (0.0210, 0.0032, z_h), ( 0.0110, -0.0036, zc), mat, bevel_width=0.0002, parent=root, smooth=False)

    join_children_by_material(root, prefix="InkMarkReject")
    return root


# ==============================================================================
# 3. RENDER VERIFICATION PREVIEWS & EXPORT
# ==============================================================================

def setup_studio(bg_color=(0.14, 0.16, 0.20, 1.0)):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 768
    scene.render.resolution_y = 640
    world = bpy.data.worlds.get("StudioWorld") or bpy.data.worlds.new("StudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = bg_color
        bg.inputs[1].default_value = 1.15

    lights = [
        ("KeyLight",  ( 0.18, -0.22, 0.28), 18.0, (1.0, 0.97, 0.92)),
        ("FillLight", (-0.22, -0.18, 0.20), 10.0, (0.88, 0.94, 1.0)),
        ("RimLight",  ( 0.00,  0.22, 0.24), 14.0, (1.0, 0.96, 0.88)),
    ]
    objs = []
    for lname, loc, energy, col in lights:
        ld = bpy.data.lights.new(lname, type='POINT')
        ld.energy = energy
        ld.color = col
        ld.shadow_soft_size = 0.08
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)
        objs.append(lo)

    cd = bpy.data.cameras.new("PreviewCam")
    cd.lens = 55
    co = bpy.data.objects.new("PreviewCam", cd)
    bpy.context.collection.objects.link(co)
    scene.camera = co
    objs.append(co)
    return co, objs


def point_cam(cam, pos, target):
    cam.location = pos
    d = mathutils.Vector(target) - mathutils.Vector(pos)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def render_stamp_impact_preview(asset_name, stamp_root, pivot, out_png):
    cam, studio = setup_studio()
    # Temporary paper sheet under stamp
    paper = make_box("PreviewPaper", (0.14, 0.12, 0.002), (0, 0, -0.0012), "Mat_PaperWarm", bevel_width=0.0005)
    studio.append(paper)

    # Pose squash peak (frame 3)
    pivot.scale = (1.14, 1.14, 0.78)
    point_cam(cam, (0.09, -0.14, 0.075), (0.0, 0.0, 0.022))
    bpy.context.scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    pivot.scale = (1.0, 1.0, 1.0)
    for o in studio:
        bpy.data.objects.remove(o, do_unlink=True)


def render_ink_marks_sheet():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cam, studio = setup_studio()

    paper = make_box("SamplePaper", (0.16, 0.12, 0.002), (0, 0, -0.0011), "Mat_PaperWarm", bevel_width=0.0006)
    m_app = build_ink_mark_approve()
    m_app.location = (-0.036, 0.0, 0.0001)
    m_app.rotation_euler = (0, 0, math.radians(-12))

    m_rej = build_ink_mark_reject()
    m_rej.location = (0.036, 0.0, 0.0001)
    m_rej.rotation_euler = (0, 0, math.radians(-12))

    point_cam(cam, (0.0, -0.045, 0.195), (0.0, 0.0, 0.0))
    out_png = os.path.join(PREVIEW_DIR, "StampJuice_Marks_TopDown.png")
    bpy.context.scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)


def export_stamp_with_anim(asset_name, crown_mat, pad_mat, preview_png):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, pivot = build_squashable_stamp(asset_name, crown_mat, pad_mat)
    render_stamp_impact_preview(asset_name, root, pivot, preview_png)

    mesh_c, verts, tris = count_root_stats(root)
    blend_path = os.path.join(BLEND_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_DIR, f"{asset_name}.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
    )
    return {
        "asset": asset_name,
        "blend": blend_path,
        "fbx": fbx_path,
        "meshes": mesh_c,
        "vertices": verts,
        "triangles": tris,
        "clips": 1,
    }


def export_ink_mark(asset_name, builder_fn):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = builder_fn()
    mesh_c, verts, tris = count_root_stats(root)
    blend_path = os.path.join(BLEND_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_DIR, f"{asset_name}.fbx")
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
        "asset": asset_name,
        "blend": blend_path,
        "fbx": fbx_path,
        "meshes": mesh_c,
        "vertices": verts,
        "triangles": tris,
        "clips": 0,
    }


def main():
    report = [
        export_stamp_with_anim(
            "Doc_Stamp_Approve", "Mat_StatusGreen", "Mat_Tester_Mint",
            os.path.join(PREVIEW_DIR, "StampJuice_Approve_Impact.png")
        ),
        export_stamp_with_anim(
            "Doc_Stamp_Reject", "Mat_StatusRed", "Mat_Developer_Coral",
            os.path.join(PREVIEW_DIR, "StampJuice_Reject_Impact.png")
        ),
        export_ink_mark("Doc_StampInkMark_Approve", build_ink_mark_approve),
        export_ink_mark("Doc_StampInkMark_Reject",  build_ink_mark_reject),
    ]
    render_ink_marks_sheet()

    rep_path = os.path.join(PREVIEW_DIR, "stamp_juice_report.json")
    with open(rep_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print(f"[MuseDesk] Stamp juice export complete -> {rep_path}")


if __name__ == "__main__":
    import traceback
    log_path = os.path.join(PREVIEW_DIR, "stamp_juice_py.log")
    try:
        with open(log_path, "w", encoding="utf-8") as lf:
            lf.write("START\n")
        main()
        with open(log_path, "a", encoding="utf-8") as lf:
            lf.write("OK\n")
    except Exception:
        with open(log_path, "a", encoding="utf-8") as lf:
            traceback.print_exc(file=lf)
        raise
