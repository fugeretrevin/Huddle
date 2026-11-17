import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Spatial Workspace Expansion: Shared Procedural Blender Helpers
#
# Coordinate Mapping (Blender -> Unity with axis_forward='-Z', axis_up='Y'):
#   Blender +X -> Unity -X  (User's Right in MuseDesk convention: -X is right)
#   Blender -X -> Unity +X  (User's Left in MuseDesk convention: +X is left)
#   Blender +Y -> Unity +Z  (Front, toward seated user!)
#   Blender -Y -> Unity -Z  (Back, away from user)
#   Blender +Z -> Unity +Y  (Up)
#
# Authoring ALL new spatial assets with Front = +Y in Blender guarantees that
# in Unity they natively face +Z (toward the seated user) at rotation (0, 0, 0)!
# ==============================================================================

WORKTREE_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_SPATIAL_DIR = os.path.join(WORKTREE_ROOT, "ArtSource", "Blender", "Spatial")
FBX_SPATIAL_DIR = os.path.join(WORKTREE_ROOT, "Assets", "MuseDesk", "Art", "Models", "Spatial")
PREVIEW_SPATIAL_DIR = os.path.join(WORKTREE_ROOT, "Screenshots", "SpatialWorkspace", "Blender")

for _d in (BLEND_SPATIAL_DIR, FBX_SPATIAL_DIR, PREVIEW_SPATIAL_DIR):
    os.makedirs(_d, exist_ok=True)


def srgb_to_linear(c):
    res = []
    for ch in c[:3]:
        res.append(ch / 12.92 if ch <= 0.04045 else ((ch + 0.055) / 1.055) ** 2.4)
    res.append(c[3] if len(c) > 3 else 1.0)
    return tuple(res)


def hex_rgba(hex_str, alpha=1.0):
    s = hex_str.lstrip("#")
    r = int(s[0:2], 16) / 255.0
    g = int(s[2:4], 16) / 255.0
    b = int(s[4:6], 16) / 255.0
    return srgb_to_linear((r, g, b, alpha))


# Shared MuseDesk Palette (matches Assets/MuseDesk/Art/Materials/*.mat)
PALETTE = {
    "Mat_CreamCeramic":        {"color": (0.955, 0.935, 0.895, 1.0), "roughness": 0.32, "metallic": 0.00},
    "Mat_WarmBeige":           {"color": (0.875, 0.835, 0.775, 1.0), "roughness": 0.42, "metallic": 0.00},
    "Mat_WarmWood":            {"color": (0.785, 0.615, 0.465, 1.0), "roughness": 0.48, "metallic": 0.00},
    "Mat_DarkWood":            {"color": (0.460, 0.330, 0.230, 1.0), "roughness": 0.52, "metallic": 0.00},
    "Mat_CharcoalVisor":       {"color": (0.130, 0.140, 0.170, 1.0), "roughness": 0.22, "metallic": 0.15},
    "Mat_BrushedMetal":        {"color": (0.650, 0.685, 0.725, 1.0), "roughness": 0.28, "metallic": 0.70},
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.24, "metallic": 0.82},
    "Mat_FeltSlate":           {"color": (0.260, 0.290, 0.340, 1.0), "roughness": 0.72, "metallic": 0.00},
    "Mat_PaperWarm":           {"color": (0.985, 0.968, 0.925, 1.0), "roughness": 0.58, "metallic": 0.00},
    "Mat_BlueprintBlue":       {"color": (0.175, 0.385, 0.610, 1.0), "roughness": 0.45, "metallic": 0.00},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.40, "metallic": 0.00},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.28, "metallic": 0.05, "emission": (0.14, 0.38, 0.34)},
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.00},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (0.65, 0.50, 0.18)},
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.00},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.00, "emission": (0.22, 0.85, 0.45)},
    "Mat_StatusRed":           {"color": (0.920, 0.250, 0.240, 1.0), "roughness": 0.20, "metallic": 0.00, "emission": (0.92, 0.25, 0.24)},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.00, "emission": (0.98, 0.68, 0.22)},
    "Mat_StatusCyan":          {"color": (0.250, 0.820, 0.950, 1.0), "roughness": 0.20, "metallic": 0.00, "emission": (0.25, 0.82, 0.95)},
    "Mat_ChamberGlass":        {"color": (0.680, 0.900, 0.880, 1.0), "roughness": 0.12, "metallic": 0.00, "emission": (0.20, 0.45, 0.42)},
    "Mat_Spatial_SlateIcon":   {"color": (0.165, 0.185, 0.230, 1.0), "roughness": 0.45, "metallic": 0.08},
    "Mat_Spatial_Ghost":       {"color": (0.184, 0.639, 0.604, 0.42), "roughness": 0.20, "metallic": 0.00, "emission": (0.12, 0.50, 0.48)},
}


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def get_mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    spec = PALETTE.get(name, {"color": (0.8, 0.8, 0.8, 1.0), "roughness": 0.5, "metallic": 0.0})
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    lin = srgb_to_linear(spec["color"])
    m.diffuse_color = lin
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = lin
        bsdf.inputs["Roughness"].default_value = spec.get("roughness", 0.5)
        bsdf.inputs["Metallic"].default_value = spec.get("metallic", 0.0)
        if spec["color"][3] < 0.99:
            bsdf.inputs["Alpha"].default_value = spec["color"][3]
        if spec.get("emission"):
            em_lin = srgb_to_linear(list(spec["emission"]) + [1.0])
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = em_lin
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = 0.85
    return m


def _apply_crisp_shading(obj, flat_normal_threshold=0.92):
    """Keep large planar tops/bottoms/sides flat-shaded while bevels/curves shade smooth."""
    if obj.type != 'MESH':
        return
    for poly in obj.data.polygons:
        n = poly.normal
        is_cardinal_flat = (
            abs(n.x) >= flat_normal_threshold
            or abs(n.y) >= flat_normal_threshold
            or abs(n.z) >= flat_normal_threshold
        )
        poly.use_smooth = not is_cardinal_flat


def bind_weight(obj, bone_name):
    if not bone_name:
        return
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    indices = [v.index for v in obj.data.vertices]
    if indices:
        vg.add(indices, 1.0, 'REPLACE')


def _rounded_rect_pts_2d(w, d, r, segs=4):
    r = max(0.0, min(r, min(w, d) * 0.49))
    hx, hy = w * 0.5, d * 0.5
    if r < 1e-6:
        return [(hx, hy), (-hx, hy), (-hx, -hy), (hx, -hy)]
    pts = []
    corners = [
        ( hx - r,  hy - r, 0.0),
        (-hx + r,  hy - r, math.pi * 0.5),
        (-hx + r, -hy + r, math.pi),
        ( hx - r, -hy + r, math.pi * 1.5),
    ]
    for cx, cy, a0 in corners:
        for i in range(segs + 1):
            a = a0 + (math.pi * 0.5) * (i / segs)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def make_rounded_slab(name, width, depth, height, radius, location, mat_name,
                      bevel=0.003, corner_segs=4, rot_deg=(0, 0, 0),
                      parent=None, bone_name=None, parts_list=None):
    """Creates a clean rounded-corner slab centered in XY, extending Z in [-height/2, +height/2]."""
    bm = bmesh.new()
    pts = _rounded_rect_pts_2d(width, depth, radius, segs=corner_segs)
    hz = height * 0.5
    bottom_verts = [bm.verts.new((px, py, -hz)) for (px, py) in pts]
    top_verts = [bm.verts.new((px, py, hz)) for (px, py) in pts]
    bm.verts.ensure_lookup_table()
    bm.faces.new(list(reversed(bottom_verts)))
    bm.faces.new(top_verts)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([bottom_verts[i], bottom_verts[j], top_verts[j], top_verts[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    if bevel > 0.0 and bevel < height * 0.45:
        bmesh.ops.bevel(
            bm,
            geom=list(bm.edges),
            offset=bevel,
            offset_type='OFFSET',
            segments=2,
            profile=0.5,
            affect='EDGES',
            clamp_overlap=True,
        )
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    obj.data.materials.append(get_mat(mat_name))
    _apply_crisp_shading(obj, flat_normal_threshold=0.92)
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_beveled_box(name, size_xyz, location, mat_name, bevel_width=0.002,
                     segments=2, rot_deg=(0, 0, 0), parent=None,
                     bone_name=None, parts_list=None):
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
        bev.limit_method = 'NONE'
        bpy.ops.object.modifier_apply(modifier="Bevel")
    _apply_crisp_shading(obj, flat_normal_threshold=0.92)
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_cylinder(name, radius, depth, location, mat_name, vertices=20,
                  rot_deg=(0, 0, 0), bevel_caps=0.0, parent=None,
                  bone_name=None, parts_list=None):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=location, rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    if bevel_caps > 0.0:
        bev = obj.modifiers.new("Bevel", 'BEVEL')
        bev.width = bevel_caps
        bev.segments = 2
        bev.limit_method = 'ANGLE'
        bev.angle_limit = math.radians(35)
        bpy.ops.object.modifier_apply(modifier="Bevel")
    _apply_crisp_shading(obj, flat_normal_threshold=0.95)
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_cone(name, r1, r2, depth, location, mat_name, vertices=20,
              rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=r1, radius2=r2, depth=depth,
        location=location, rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    _apply_crisp_shading(obj, flat_normal_threshold=0.95)
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_sphere(name, radius_xyz, location, mat_name, segments=16, rings=10,
                rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=1.0,
        location=location, rotation=tuple(math.radians(a) for a in rot_deg)
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
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_torus(name, maj_r, min_r, location, mat_name, maj_seg=24, min_seg=8,
               rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=maj_seg, minor_segments=min_seg,
        major_radius=maj_r, minor_radius=min_r,
        location=location, rotation=tuple(math.radians(a) for a in rot_deg)
    )
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_rounded_frame_outline(name, width, depth, border_w, height, radius,
                               location, mat_name, corner_segs=4,
                               parent=None, bone_name=None, parts_list=None):
    """Creates a clean hollow rounded-rectangle perimeter outline (great for ghost previews & rims)."""
    bm = bmesh.new()
    outer = _rounded_rect_pts_2d(width, depth, radius, segs=corner_segs)
    inner_r = max(0.002, radius - border_w)
    inner = _rounded_rect_pts_2d(max(0.01, width - border_w * 2), max(0.01, depth - border_w * 2), inner_r, segs=corner_segs)
    hz = height * 0.5
    n = len(outer)

    ob = [bm.verts.new((px, py, -hz)) for (px, py) in outer]
    ot = [bm.verts.new((px, py,  hz)) for (px, py) in outer]
    ib = [bm.verts.new((px, py, -hz)) for (px, py) in inner]
    it = [bm.verts.new((px, py,  hz)) for (px, py) in inner]
    bm.verts.ensure_lookup_table()

    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([ot[i], ot[j], it[j], it[i]])
        bm.faces.new([ib[i], ib[j], ob[j], ob[i]])
        bm.faces.new([ob[i], ob[j], ot[j], ot[i]])
        bm.faces.new([it[i], it[j], ib[j], ib[i]])

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name + "_Mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.data.materials.append(get_mat(mat_name))
    _apply_crisp_shading(obj, flat_normal_threshold=0.92)
    if parent:
        obj.parent = parent
    if bone_name:
        bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_anchor(name, location, parent=None, rot_deg=(0, 0, 0)):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.020
    emp.location = location
    emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.collection.objects.link(emp)
    if parent:
        emp.parent = parent
    return emp


def join_static_parts(objs, name, parent=None):
    """Joins multiple static mesh objects into a single multi-material mesh to minimize draw calls."""
    meshes = [o for o in objs if o is not None and o.type == 'MESH']
    if not meshes:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = name
    _apply_crisp_shading(joined, flat_normal_threshold=0.92)
    if parent:
        joined.parent = parent
    return joined


def join_parts_to_armature(parts, arm_obj, mesh_name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = mesh_name
    joined.parent = arm_obj
    mod = joined.modifiers.new(name="Armature", type='ARMATURE')
    mod.object = arm_obj
    _apply_crisp_shading(joined, flat_normal_threshold=0.92)
    return joined


def push_action_to_nla(arm_obj, clip_name, start_frame, end_frame):
    action = arm_obj.animation_data.action
    if action:
        action.name = clip_name
        track = arm_obj.animation_data.nla_tracks.new()
        track.name = clip_name
        strip = track.strips.new(clip_name, int(start_frame), action)
        strip.action_frame_start = start_frame
        strip.action_frame_end = end_frame
        arm_obj.animation_data.action = None


def mesh_stats():
    deps = bpy.context.evaluated_depsgraph_get()
    total_tris = 0
    total_verts = 0
    mats = set()
    meshes = 0
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            meshes += 1
            for m in obj.data.materials:
                if m:
                    mats.add(m.name)
            ev = obj.evaluated_get(deps)
            em = ev.to_mesh()
            em.calc_loop_triangles()
            total_tris += len(em.loop_triangles)
            total_verts += len(em.vertices)
            ev.to_mesh_clear()
    return {
        "mesh_objects": meshes,
        "vertices": total_verts,
        "triangles": total_tris,
        "materials": sorted(mats),
        "material_count": len(mats),
    }


def _rotate_roots_for_unity_plus_z():
    """Flip top-level scene roots +180 deg around Blender Z so Blender +Y front maps to Unity +Z."""
    for obj in bpy.context.scene.objects:
        if obj.parent is None and obj.type in {'EMPTY', 'ARMATURE', 'MESH'}:
            obj.rotation_euler[2] += math.pi
    bpy.context.view_layer.update()


def export_static_asset(asset_name, preview_cam_pos=(0.45, 0.72, 0.42), preview_target=(0.0, 0.0, 0.06)):
    stats = mesh_stats()
    png_path = render_preview(asset_name, preview_cam_pos, preview_target)
    _rotate_roots_for_unity_plus_z()
    blend_path = os.path.join(BLEND_SPATIAL_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_SPATIAL_DIR, f"{asset_name}.fbx")
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
    stats.update({"asset": asset_name, "blend": blend_path, "fbx": fbx_path, "preview": png_path, "animated": False, "clips": []})
    return stats


def export_animated_asset(asset_name, clip_names, preview_cam_pos=(0.42, 0.68, 0.36), preview_target=(0.0, 0.0, 0.06)):
    stats = mesh_stats()
    png_path = render_preview(asset_name, preview_cam_pos, preview_target)
    _rotate_roots_for_unity_plus_z()
    blend_path = os.path.join(BLEND_SPATIAL_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_SPATIAL_DIR, f"{asset_name}.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        object_types={'ARMATURE', 'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
    )
    stats.update({"asset": asset_name, "blend": blend_path, "fbx": fbx_path, "preview": png_path, "animated": True, "clips": list(clip_names)})
    return stats


def render_preview(asset_name, cam_pos, target):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    scene.render.fps = 24

    world = bpy.data.worlds.get("SpatialStudioWorld") or bpy.data.worlds.new("SpatialStudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
        bg.inputs[1].default_value = 1.15

    temp_objs = []
    # Front-right (+Y, -X) key, front-left (+Y, +X) fill, rear rim
    lights = [
        ("KeyLight",  (-0.55,  0.75, 0.65), 60.0, (1.00, 0.96, 0.90)),
        ("FillLight", ( 0.60,  0.55, 0.45), 32.0, (0.85, 0.92, 1.00)),
        ("RimLight",  ( 0.00, -0.65, 0.55), 38.0, (1.00, 0.95, 0.85)),
    ]
    for lname, loc, energy, col in lights:
        ldata = bpy.data.lights.new(name=lname, type='POINT')
        ldata.energy = energy
        ldata.color = col
        lobj = bpy.data.objects.new(name=lname, object_data=ldata)
        lobj.location = loc
        bpy.context.collection.objects.link(lobj)
        temp_objs.append(lobj)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 45
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    temp_objs.append(cam_obj)

    cam_obj.location = cam_pos
    cam_obj.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_SPATIAL_DIR, f"{asset_name}_preview.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    for o in temp_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return out_png
