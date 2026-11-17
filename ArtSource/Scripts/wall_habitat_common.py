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
    "Mat_TreeBark":       {"color": (0.305, 0.205, 0.135, 1.0), "roughness": 0.85, "metallic": 0.00},
    "Mat_ForestMoss":     {"color": (0.215, 0.460, 0.250, 1.0), "roughness": 0.78, "metallic": 0.00},
    "Mat_CardboardBox":   {"color": (0.760, 0.580, 0.400, 1.0), "roughness": 0.62, "metallic": 0.00},
    "Mat_Terracotta":     {"color": (0.780, 0.430, 0.310, 1.0), "roughness": 0.56, "metallic": 0.00},
    "Mat_PlantGreen":     {"color": (0.340, 0.690, 0.440, 1.0), "roughness": 0.40, "metallic": 0.00},
    "Mat_LampGlow":       {"color": (1.000, 0.950, 0.800, 1.0), "roughness": 0.15, "metallic": 0.00, "emission": (1.10, 1.00, 0.80)},
    "Mat_LeatherSatchel": {"color": (0.520, 0.350, 0.230, 1.0), "roughness": 0.55, "metallic": 0.00},
    "Mat_ScreenCode":     {"color": (0.120, 0.260, 0.400, 1.0), "roughness": 0.14, "metallic": 0.00, "emission": (0.16, 0.52, 0.82)},
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
                 rail_mat="Mat_WarmWood", post_mat="Mat_TreeBark", mid_mat="Mat_LeatherSatchel",
                 parent=None, parts_list=None):
    """Creates a rustic treehouse timber & leather-rope safety railing with acorn/bark posts."""
    dx = end_xy[0] - start_xy[0]
    dy = end_xy[1] - start_xy[1]
    length = math.hypot(dx, dy)
    if length < 1e-4:
        return
    ang_z = math.degrees(math.atan2(dy, dx))
    cx = (start_xy[0] + end_xy[0]) * 0.5
    cy = (start_xy[1] + end_xy[1]) * 0.5

    # Warm rounded timber top handrail
    make_beveled_box(f"{name}_TopRail", (length + 0.005, 0.0055, 0.0055),
                     (cx, cy, z_base + rail_h), rail_mat, bevel_width=0.0014,
                     rot_deg=(0, 0, ang_z), parent=parent, parts_list=parts_list)
    # Mid rustic twig/rope cross rail
    make_beveled_box(f"{name}_MidBar", (length, 0.0032, 0.0032),
                     (cx, cy, z_base + rail_h * 0.50), mid_mat, bevel_width=0.0007,
                     rot_deg=(0, 0, ang_z), parent=parent, parts_list=parts_list)
    # Chunky treehouse stanchion posts + subtle brass post caps
    n_posts = max(2, posts)
    for i in range(n_posts):
        t = i / max(1, n_posts - 1)
        px = start_xy[0] + dx * t
        py = start_xy[1] + dy * t
        make_cylinder(f"{name}_Post_{i}", 0.0026, rail_h, (px, py, z_base + rail_h * 0.5),
                      post_mat, vertices=8, parent=parent, parts_list=parts_list)
        if i == 0 or i == n_posts - 1:
            make_sphere(f"{name}_Finial_{i}", (0.0032, 0.0032, 0.0024),
                        (px, py, z_base + rail_h + 0.0032), "Mat_TreeBark",
                        segments=8, rings=5, parent=parent, parts_list=parts_list)


def make_branch_tube(name, knots, mat_name="Mat_TreeBark", sides=8,
                     min_y=0.002, parent=None, bone_name=None, parts_list=None):
    """Low-poly swept tapered bark trunk/limb along [((x, y, z), radius), ...]. Clamps y - r >= min_y so backplate stays wall-flush."""
    if len(knots) < 2:
        return None
    bm = bmesh.new()
    rings = []
    for i, (pt, rad) in enumerate(knots):
        p = mathutils.Vector((pt[0], max(pt[1], min_y + rad * 0.92), pt[2]))
        if i == 0:
            tangent = (mathutils.Vector(knots[1][0]) - p).normalized()
        elif i == len(knots) - 1:
            tangent = (p - mathutils.Vector(knots[-2][0])).normalized()
        else:
            tangent = (mathutils.Vector(knots[i + 1][0]) - mathutils.Vector(knots[i - 1][0])).normalized()
        if tangent.length < 1e-6:
            tangent = mathutils.Vector((0, 0, 1))
        up_ref = mathutils.Vector((0, 1, 0)) if abs(tangent.y) < 0.9 else mathutils.Vector((1, 0, 0))
        binorm = tangent.cross(up_ref).normalized()
        norm = binorm.cross(tangent).normalized()
        ring = []
        for k in range(sides):
            ang = (2.0 * math.pi * k) / sides
            wobble = 1.0 + 0.08 * math.sin(ang * 3.0 + i * 1.3)
            offset = (binorm * math.cos(ang) + norm * math.sin(ang)) * (rad * wobble)
            ring.append(bm.verts.new(p + offset))
        rings.append(ring)

    bm.verts.ensure_lookup_table()
    for i in range(len(rings) - 1):
        r0, r1 = rings[i], rings[i + 1]
        for k in range(sides):
            kn = (k + 1) % sides
            bm.faces.new([r0[k], r0[kn], r1[kn], r1[k]])
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(get_mat(mat_name))
    sc._apply_crisp_shading(obj, flat_normal_threshold=0.92)
    if parent:
        obj.parent = parent
    if bone_name:
        sc.bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_foliage_cluster(name, center, scale_xyz, base_mat="Mat_ForestMoss",
                         leaf_mat="Mat_PlantGreen", tip_mat="Mat_Tester_Mint",
                         rot_z=0.0, parent=None, bone_name=None, parts_list=None):
    """Creates a crisp low-poly 3-lobe stylized foliage tuft (~60 tris total)."""
    cx, cy, cz = center
    sx, sy, sz = scale_xyz
    specs = [
        ("Base", (0.0, 0.0, 0.0),                  (sx, sy, sz),                      base_mat, (12, 8, rot_z)),
        ("Top",  (-sx * 0.28, +sy * 0.14, sz * 0.34), (sx * 0.74, sy * 0.72, sz * 0.78), leaf_mat, (-8, 16, rot_z + 25)),
        ("Side", (+sx * 0.34, +sy * 0.18, -sz * 0.10), (sx * 0.66, sy * 0.64, sz * 0.68), tip_mat if sz > 0.014 else leaf_mat, (14, -12, rot_z - 22)),
    ]
    for suf, (ox, oy, oz), lscale, mat, rot in specs:
        bpy.ops.mesh.primitive_ico_sphere_add(
            subdivisions=1, radius=1.0,
            location=(cx + ox, cy + oy, cz + oz),
            rotation=tuple(math.radians(a) for a in rot),
        )
        obj = bpy.context.active_object
        obj.name = f"{name}_{suf}"
        obj.scale = lscale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        for p in obj.data.polygons:
            p.use_smooth = False
        obj.data.materials.append(get_mat(mat))
        if parent:
            obj.parent = parent
        if bone_name:
            sc.bind_weight(obj, bone_name)
        if parts_list is not None:
            parts_list.append(obj)


def make_acorn_lantern(name, pos, scale=1.0, parent=None, bone_name=None,
                       parts_list=None, bulb_list=None):
    """Creates a hanging acorn fairy lantern (brass hook + bark cap + warm glowing bulb)."""
    x, y, z = pos
    make_cylinder(f"{name}_Stem", 0.0014 * scale, 0.006 * scale,
                  (x, y, z + 0.005 * scale), "Mat_Brass", vertices=6,
                  parent=parent, bone_name=bone_name, parts_list=parts_list)
    make_cone(f"{name}_Cap", 0.0075 * scale, 0.0025 * scale, 0.005 * scale,
              (x, y, z + 0.0015 * scale), "Mat_TreeBark", vertices=10,
              parent=parent, bone_name=bone_name, parts_list=parts_list)
    make_sphere(f"{name}_Bulb", (0.0056 * scale, 0.0056 * scale, 0.0065 * scale),
                (x, y, z - 0.003 * scale), "Mat_LampGlow", segments=8, rings=6,
                parent=parent, bone_name=bone_name,
                parts_list=bulb_list if bulb_list is not None else parts_list)


def make_toadstool(name, pos, cap_r=0.009, h=0.015, parent=None, parts_list=None):
    """Tiny stylized woodland mushroom."""
    x, y, z = pos
    make_cylinder(f"{name}_Stem", cap_r * 0.34, h * 0.72,
                  (x, y, z + h * 0.36), "Mat_WarmBeige", vertices=8,
                  parent=parent, parts_list=parts_list)
    make_cone(f"{name}_Cap", cap_r, cap_r * 0.25, h * 0.55,
              (x, y, z + h * 0.80), "Mat_Terracotta", vertices=10,
              parent=parent, parts_list=parts_list)


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
