import bpy
import bmesh
import math
import mathutils
import os
import sys

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import spatial_common as sc

# ==============================================================================
# MuseDesk — Phase 3 Wall Habitat ("Workshop Treehouse") Shared Helpers
#
# Unity Canonical Wall Habitat Coordinate Convention:
#   +Y = unity_up
#   +Z = wall_outward (toward room / seated user; z=0 is flush WallContact)
#   +X = wall_horizontal
#   1 unit = 1 meter
#
# Blender Authoring Convention (flipped +180 around Z on export):
#   Blender +Y -> Unity +Z (outward toward user)
#   Blender  Y=0 -> Unity Z=0 (flush wall plane)
#   Blender +Z -> Unity +Y (up)
#   Blender +X -> Unity -X
#   Blender -X -> Unity +X
# ==============================================================================

WORKTREE_ROOT = os.path.abspath(os.path.join(SCRIPTS_DIR, "..", ".."))
BLEND_WALL_DIR = os.path.join(WORKTREE_ROOT, "ArtSource", "Blender", "WallHabitat")
FBX_WALL_DIR = os.path.join(WORKTREE_ROOT, "Assets", "MuseDesk", "Art", "Models", "WallHabitat")
PREVIEW_WALL_DIR = os.path.join(WORKTREE_ROOT, "Screenshots", "WallHabitat", "Blender")

for _d in (BLEND_WALL_DIR, FBX_WALL_DIR, PREVIEW_WALL_DIR):
    os.makedirs(_d, exist_ok=True)


# Ensure any Phase 3 subtle palette entries resolve cleanly onto existing URP materials
WALL_EXTRA_PALETTE = {
    "Mat_Wall_PortalRim": {"color": (0.280, 0.735, 0.680, 0.78), "roughness": 0.18, "metallic": 0.0, "emission": (0.20, 0.68, 0.62)},
    "Mat_Wall_WoodFloor": sc.PALETTE["Mat_WarmWood"],
    "Mat_Wall_Canopy":    sc.PALETTE["Mat_WarmBeige"],
}
for _k, _v in WALL_EXTRA_PALETTE.items():
    if _k not in sc.PALETTE:
        sc.PALETTE[_k] = _v


def reset_scene():
    sc.reset_scene()


def get_mat(name):
    return sc.get_mat(name)


def make_anchor(name, location, parent=None, rot_deg=(0, 0, 0)):
    return sc.make_anchor(name, location, parent=parent, rot_deg=rot_deg)


def make_beveled_box(name, size_xyz, location, mat_name, bevel_width=0.002,
                     segments=2, rot_deg=(0, 0, 0), parent=None,
                     bone_name=None, parts_list=None):
    return sc.make_beveled_box(
        name, size_xyz, location, mat_name,
        bevel_width=bevel_width, segments=segments, rot_deg=rot_deg,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def make_rounded_slab(name, width, depth, height, radius, location, mat_name,
                      bevel=0.0025, corner_segs=4, rot_deg=(0, 0, 0),
                      parent=None, bone_name=None, parts_list=None):
    return sc.make_rounded_slab(
        name, width, depth, height, radius, location, mat_name,
        bevel=bevel, corner_segs=corner_segs, rot_deg=rot_deg,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def make_cylinder(name, radius, depth, location, mat_name, vertices=20,
                  rot_deg=(0, 0, 0), bevel_caps=0.0, parent=None,
                  bone_name=None, parts_list=None):
    return sc.make_cylinder(
        name, radius, depth, location, mat_name,
        vertices=vertices, rot_deg=rot_deg, bevel_caps=bevel_caps,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def make_cone(name, r1, r2, depth, location, mat_name, vertices=20,
              rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    return sc.make_cone(
        name, r1, r2, depth, location, mat_name,
        vertices=vertices, rot_deg=rot_deg,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def make_sphere(name, radius_xyz, location, mat_name, segments=16, rings=10,
                rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    return sc.make_sphere(
        name, radius_xyz, location, mat_name,
        segments=segments, rings=rings, rot_deg=rot_deg,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def make_torus(name, maj_r, min_r, location, mat_name, maj_seg=24, min_seg=8,
               rot_deg=(0, 0, 0), parent=None, bone_name=None, parts_list=None):
    return sc.make_torus(
        name, maj_r, min_r, location, mat_name,
        maj_seg=maj_seg, min_seg=min_seg, rot_deg=rot_deg,
        parent=parent, bone_name=bone_name, parts_list=parts_list,
    )


def join_static_parts(objs, name, parent=None):
    return sc.join_static_parts(objs, name, parent=parent)


def make_arch_wall(name, wall_depth, wall_height, wall_thick, opening_width, opening_height,
                   location, mat_name, arch_segs=10, parent=None, bone_name=None, parts_list=None):
    """
    Creates a side wall extending along Y (width=wall_depth) with an arched pass-through opening.
    Local orientation spans:
      X in [-wall_thick/2, +wall_thick/2]
      Y in [-wall_depth/2, +wall_depth/2]
      Z in [0, wall_height]
    Opening centered at Y=0, from Z=0 to Z=opening_height (semicircular top crown).
    Built cleanly from bmesh extrusion along X (no booleans, clean normals!).
    """
    bm = bmesh.new()
    hy = wall_depth * 0.5
    ho = opening_width * 0.5
    r_arch = ho
    spring_z = max(0.02, opening_height - r_arch)
    hx = wall_thick * 0.5

    # Front pillar (-Y) and Back pillar (+Y) + Arch header
    # Build 2D profile in YZ plane around arch and solidify along X
    # Pillars below spring_z
    # Left (-Y) pier: y in [-hy, -ho], z in [0, spring_z]
    # Right (+Y) pier: y in [ho, hy], z in [0, spring_z]
    def add_box(y0, y1, z0, z1):
        if y1 - y0 < 1e-5 or z1 - z0 < 1e-5:
            return
        v = [
            bm.verts.new((-hx, y0, z0)),
            bm.verts.new((-hx, y1, z0)),
            bm.verts.new((-hx, y1, z1)),
            bm.verts.new((-hx, y0, z1)),
            bm.verts.new(( hx, y0, z0)),
            bm.verts.new(( hx, y1, z0)),
            bm.verts.new(( hx, y1, z1)),
            bm.verts.new(( hx, y0, z1)),
        ]
        bm.faces.new([v[0], v[3], v[2], v[1]])
        bm.faces.new([v[4], v[5], v[6], v[7]])
        bm.faces.new([v[0], v[1], v[5], v[4]])
        bm.faces.new([v[2], v[3], v[7], v[6]])
        bm.faces.new([v[0], v[4], v[7], v[3]])
        bm.faces.new([v[1], v[2], v[6], v[5]])

    add_box(-hy, -ho, 0.0, spring_z)
    add_box( ho,  hy, 0.0, spring_z)

    # Top header above spring_z with curved inner arch
    # Subdivide outer border between y=-hy and y=hy up to z=wall_height
    arch_pts = []
    for i in range(arch_segs + 1):
        ang = math.pi * (1.0 - i / arch_segs)  # pi (y=-ho) to 0 (y=+ho)
        ay = math.cos(ang) * r_arch
        az = spring_z + math.sin(ang) * r_arch
        arch_pts.append((ay, az))

    outer_pts = []
    for i in range(arch_segs + 1):
        t = i / arch_segs
        oy = -hy + t * (2.0 * hy)
        outer_pts.append((oy, wall_height))

    for side_x, flip in [(-hx, False), (hx, True)]:
        ring_in = [bm.verts.new((side_x, py, pz)) for (py, pz) in arch_pts]
        ring_out = [bm.verts.new((side_x, py, pz)) for (py, pz) in outer_pts]
        if side_x == -hx:
            left_in, left_out = ring_in, ring_out
        else:
            right_in, right_out = ring_in, ring_out

    for i in range(arch_segs):
        bm.faces.new([left_in[i], left_in[i + 1], left_out[i + 1], left_out[i]])
        bm.faces.new([right_in[i + 1], right_in[i], right_out[i], right_out[i + 1]])
        # Inner intrados
        bm.faces.new([left_in[i], right_in[i], right_in[i + 1], left_in[i + 1]])
        # Top cap
        bm.faces.new([left_out[i + 1], right_out[i + 1], right_out[i], left_out[i]])

    # Close left/right outer vertical cheeks above spring_z
    add_box(-hy, -ho, spring_z, wall_height)
    add_box( ho,  hy, spring_z, wall_height)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name + "_Mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.data.materials.append(get_mat(mat_name))
    sc._apply_crisp_shading(obj, flat_normal_threshold=0.92)
    if parent:
        obj.parent = parent
    if bone_name:
        sc.bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_curved_canopy(name, width, depth, thickness, arch_rise, location, mat_name,
                       segs=12, parent=None, parts_list=None):
    """Creates a softly curved roof awning arching over width (X) and projecting +Y outward."""
    bm = bmesh.new()
    hx = width * 0.5
    top_l = []
    top_r = []
    bot_l = []
    bot_r = []
    for i in range(segs + 1):
        t = -1.0 + 2.0 * (i / segs)
        x = t * hx
        z = arch_rise * (1.0 - t * t)
        top_l.append(bm.verts.new((x, 0.0, z + thickness)))
        top_r.append(bm.verts.new((x, depth, z + thickness)))
        bot_l.append(bm.verts.new((x, 0.0, z)))
        bot_r.append(bm.verts.new((x, depth, z)))

    for i in range(segs):
        bm.faces.new([top_l[i], top_r[i], top_r[i + 1], top_l[i + 1]])
        bm.faces.new([bot_l[i + 1], bot_r[i + 1], bot_r[i], bot_l[i]])
        bm.faces.new([top_r[i], bot_r[i], bot_r[i + 1], top_r[i + 1]])
        bm.faces.new([bot_l[i], top_l[i], top_l[i + 1], bot_l[i + 1]])
    bm.faces.new([bot_l[0], bot_r[0], top_r[0], top_l[0]])
    bm.faces.new([top_l[-1], top_r[-1], bot_r[-1], bot_l[-1]])

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name + "_Mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.data.materials.append(get_mat(mat_name))
    sc._apply_crisp_shading(obj, flat_normal_threshold=0.92)
    if parent:
        obj.parent = parent
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_railing(name, start_xy, end_xy, z_base, rail_h=0.028, posts=3,
                 rail_mat="Mat_WarmWood", post_mat="Mat_Brass", parent=None, parts_list=None):
    """Creates a delicate architectural safety railing with slim posts and rounded top bar."""
    dx = end_xy[0] - start_xy[0]
    dy = end_xy[1] - start_xy[1]
    length = math.hypot(dx, dy)
    if length < 1e-4:
        return
    ang_z = math.degrees(math.atan2(dy, dx))
    cx = (start_xy[0] + end_xy[0]) * 0.5
    cy = (start_xy[1] + end_xy[1]) * 0.5

    # Top handrail
    make_beveled_box(f"{name}_TopRail", (length + 0.004, 0.005, 0.005),
                     (cx, cy, z_base + rail_h), rail_mat, bevel_width=0.0012,
                     rot_deg=(0, 0, ang_z), parent=parent, parts_list=parts_list)
    # Mid thin brass bar
    make_beveled_box(f"{name}_MidBar", (length, 0.003, 0.003),
                     (cx, cy, z_base + rail_h * 0.52), post_mat, bevel_width=0.0006,
                     rot_deg=(0, 0, ang_z), parent=parent, parts_list=parts_list)
    # Stanchion posts
    for i in range(max(2, posts)):
        t = i / max(1, posts - 1)
        px = start_xy[0] + dx * t
        py = start_xy[1] + dy * t
        make_cylinder(f"{name}_Post_{i}", 0.0022, rail_h, (px, py, z_base + rail_h * 0.5),
                      post_mat, vertices=10, parent=parent, parts_list=parts_list)


def render_wall_preview(asset_name, cam_pos=(0.35, 1.05, 0.16), target=(0.0, 0.10, 0.0), suffix="preview"):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 720
    scene.render.fps = 24

    world = bpy.data.worlds.get("WallStudioWorld") or bpy.data.worlds.new("WallStudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.145, 0.165, 0.205, 1.0)
        bg.inputs[1].default_value = 1.15

    temp_objs = []
    lights = [
        ("KeyLight",  (-0.65, 1.15, 0.85), 115.0, (1.00, 0.96, 0.90)),
        ("FillLight", ( 0.75, 0.95, 0.45),  65.0, (0.86, 0.93, 1.00)),
        ("RimLight",  ( 0.00, 0.35, 1.05),  70.0, (1.00, 0.94, 0.84)),
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
    cam_data.lens = 42
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    temp_objs.append(cam_obj)

    cam_obj.location = cam_pos
    cam_obj.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_WALL_DIR, f"{asset_name}_{suffix}.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    for o in temp_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return out_png


def export_wall_static(asset_name, cam_pos=(0.35, 1.05, 0.16), target=(0.0, 0.10, 0.0), extra_cam=None):
    stats = sc.mesh_stats()
    png_path = render_wall_preview(asset_name, cam_pos, target, suffix="preview")
    extra_png = None
    if extra_cam:
        extra_png = render_wall_preview(asset_name, extra_cam[0], extra_cam[1], suffix="threequarter")

    sc._rotate_roots_for_unity_plus_z()
    blend_path = os.path.join(BLEND_WALL_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_WALL_DIR, f"{asset_name}.fbx")
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
    stats.update({
        "asset": asset_name,
        "blend": blend_path,
        "fbx": fbx_path,
        "preview": png_path,
        "threequarter_preview": extra_png,
        "animated": False,
        "clips": [],
    })
    return stats


def export_wall_animated(asset_name, clip_names, cam_pos=(0.35, 0.90, 0.15), target=(0.0, 0.08, 0.0)):
    stats = sc.mesh_stats()
    png_path = render_wall_preview(asset_name, cam_pos, target, suffix="preview")
    sc._rotate_roots_for_unity_plus_z()
    blend_path = os.path.join(BLEND_WALL_DIR, f"{asset_name}.blend")
    fbx_path = os.path.join(FBX_WALL_DIR, f"{asset_name}.fbx")
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
    stats.update({
        "asset": asset_name,
        "blend": blend_path,
        "fbx": fbx_path,
        "preview": png_path,
        "animated": True,
        "clips": list(clip_names),
    })
    return stats
