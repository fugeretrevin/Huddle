import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Procedural Character Modeling, Rigging & 39-Clip Animation Generator
# Generates the 3 "Pipkin Sprite" miniature AI coworkers:
#   1. Agent_Researcher (Scholar Beret, Brass Glasses, Leaf-Sprout Antenna, Capelet, Scroll Satchel)
#   2. Agent_Developer  (Tech Crest, Studio Headphones + Mic, Cyber-Fins, Hoodie Vest, Power Pack)
#   3. Agent_Tester     (Inspector Band, Flip-Down Dual Loupe Visor, Radar Dishes, Utility Harness, Analyzer Pack)
# ==============================================================================

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
BLEND_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "Characters")
FBX_DIR = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models", "Characters")
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")

for d in [BLEND_DIR, FBX_DIR, PREVIEW_DIR]:
    os.makedirs(d, exist_ok=True)


def srgb_to_linear(c):
    res = []
    for ch in c[:3]:
        if ch <= 0.04045:
            res.append(ch / 12.92)
        else:
            res.append(((ch + 0.055) / 1.055) ** 2.4)
    res.append(c[3] if len(c) > 3 else 1.0)
    return tuple(res)


PALETTE = {
    "Mat_CreamCeramic":        {"color": (0.955, 0.935, 0.895, 1.0), "roughness": 0.32, "metallic": 0.0,  "emission": None},
    "Mat_WarmBeige":           {"color": (0.875, 0.835, 0.775, 1.0), "roughness": 0.42, "metallic": 0.0,  "emission": None},
    "Mat_CharcoalVisor":       {"color": (0.118, 0.125, 0.155, 1.0), "roughness": 0.18, "metallic": 0.12, "emission": None},
    "Mat_DarkBoots":           {"color": (0.180, 0.190, 0.225, 1.0), "roughness": 0.38, "metallic": 0.08, "emission": None},
    "Mat_EyeGlow":             {"color": (0.990, 0.985, 0.940, 1.0), "roughness": 0.10, "metallic": 0.0,  "emission": (0.99, 0.98, 0.90), "emission_strength": 1.6},
    "Mat_CheekBlush":          {"color": (0.960, 0.620, 0.600, 1.0), "roughness": 0.45, "metallic": 0.0,  "emission": (0.96, 0.55, 0.52), "emission_strength": 0.35},
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.25, "metallic": 0.80, "emission": None},
    "Mat_BrushedMetal":        {"color": (0.650, 0.685, 0.725, 1.0), "roughness": 0.28, "metallic": 0.70, "emission": None},
    "Mat_LeatherSatchel":      {"color": (0.520, 0.350, 0.230, 1.0), "roughness": 0.55, "metallic": 0.0,  "emission": None},
    "Mat_PaperWarm":           {"color": (0.980, 0.960, 0.910, 1.0), "roughness": 0.60, "metallic": 0.0,  "emission": None},
    # Soft matte role trim accents (distinct from status teal/amber/green/coral)
    "Mat_RoleTrim_Researcher":      {"color": (0.663, 0.722, 0.941, 1.0), "roughness": 0.76, "metallic": 0.0, "emission": None},  # #A9B8F0
    "Mat_RoleTrim_Researcher_Soft": {"color": (0.761, 0.804, 0.965, 1.0), "roughness": 0.78, "metallic": 0.0, "emission": None},  # #C2CDF6
    "Mat_RoleTrim_Developer":       {"color": (0.824, 0.651, 0.910, 1.0), "roughness": 0.76, "metallic": 0.0, "emission": None},  # #D2A6E8
    "Mat_RoleTrim_Developer_Soft":  {"color": (0.902, 0.808, 0.957, 1.0), "roughness": 0.78, "metallic": 0.0, "emission": None},  # #E6CEF4
    "Mat_RoleTrim_Tester":          {"color": (0.941, 0.663, 0.769, 1.0), "roughness": 0.76, "metallic": 0.0, "emission": None},  # #F0A9C4
    "Mat_RoleTrim_Tester_Soft":     {"color": (0.847, 0.537, 0.659, 1.0), "roughness": 0.76, "metallic": 0.0, "emission": None},  # #D889A8
    "Mat_RoleTrim_Tester_Lens":     {"color": (0.973, 0.792, 0.863, 1.0), "roughness": 0.72, "metallic": 0.0, "emission": None},  # #F8CADC
    # Legacy palette entries kept for non-character spatial props
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.42, "metallic": 0.0,  "emission": None},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.30, "metallic": 0.05, "emission": (0.28, 0.74, 0.68), "emission_strength": 0.45},
    # Developer accents
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.0,  "emission": None},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (1.00, 0.80, 0.28), "emission_strength": 0.85},
    # Tester accents
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.0,  "emission": None},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18, "emission": None},
    "Mat_Tester_LensGlow":     {"color": (0.420, 0.920, 0.820, 1.0), "roughness": 0.12, "metallic": 0.0,  "emission": (0.40, 0.92, 0.80), "emission_strength": 0.95},
}


def get_or_create_material(mat_name):
    if mat_name in bpy.data.materials:
        return bpy.data.materials[mat_name]
    spec = PALETTE[mat_name]
    mat = bpy.data.materials.new(name=mat_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    lin_c = srgb_to_linear(spec["color"])
    mat.diffuse_color = lin_c
    if bsdf:
        bsdf.inputs["Base Color"].default_value = lin_c
        bsdf.inputs["Roughness"].default_value = spec["roughness"]
        bsdf.inputs["Metallic"].default_value = spec["metallic"]
        if spec.get("emission"):
            em_lin = srgb_to_linear(list(spec["emission"]) + [1.0])
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = em_lin
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = spec.get("emission_strength", 1.0)
    return mat


def assign_material(obj, mat_name):
    mat = get_or_create_material(mat_name)
    if len(obj.data.materials) == 0:
        obj.data.materials.append(mat)
    else:
        obj.data.materials[0] = mat


def shade_smooth(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = True


def assign_single_bone_weight(obj, bone_name, weight=1.0):
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    indices = [v.index for v in obj.data.vertices]
    if indices:
        vg.add(indices, weight, 'REPLACE')


def assign_z_gradient_weights(obj, bone_stops):
    """
    bone_stops: list of (z_height, bone_name) sorted by z_height ascending.
    Smoothly blends vertex weights between adjacent bones along world Z.
    """
    for _, bname in bone_stops:
        if bname not in obj.vertex_groups:
            obj.vertex_groups.new(name=bname)
    mw = obj.matrix_world
    for v in obj.data.vertices:
        wz = (mw @ v.co).z
        if wz <= bone_stops[0][0]:
            obj.vertex_groups[bone_stops[0][1]].add([v.index], 1.0, 'REPLACE')
        elif wz >= bone_stops[-1][0]:
            obj.vertex_groups[bone_stops[-1][1]].add([v.index], 1.0, 'REPLACE')
        else:
            for i in range(len(bone_stops) - 1):
                z0, b0 = bone_stops[i]
                z1, b1 = bone_stops[i + 1]
                if z0 <= wz <= z1:
                    t = (wz - z0) / max(1e-5, (z1 - z0))
                    # Smoothstep blend
                    s = t * t * (3.0 - 2.0 * t)
                    if b0 == b1:
                        obj.vertex_groups[b0].add([v.index], 1.0, 'REPLACE')
                    else:
                        obj.vertex_groups[b0].add([v.index], 1.0 - s, 'REPLACE')
                        obj.vertex_groups[b1].add([v.index], s, 'REPLACE')
                    break


# ==============================================================================
# 1. ARMATURE CONSTRUCTION (22 Deforming Bones + 5 Attachment Empties)
# Note on orientation:
#   In Blender, -Y is Character Forward, +Z is Up, -X is Character Right, +X is Character Left.
# ==============================================================================

def create_character_armature(char_name):
    arm_data = bpy.data.armatures.new(f"{char_name}_ArmatureData")
    arm_obj = bpy.data.objects.new("Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    eb = arm_data.edit_bones

    def make_bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = parent
            b.use_connect = connect
        return b

    # Core spine
    b_root  = make_bone("Root",  (0.0, 0.0, 0.000), (0.0, 0.0, 0.030))
    b_hips  = make_bone("Hips",  (0.0, 0.0, 0.044), (0.0, 0.0, 0.068), b_root)
    b_spine = make_bone("Spine", (0.0, 0.0, 0.068), (0.0, 0.0, 0.092), b_hips, True)
    b_chest = make_bone("Chest", (0.0, 0.0, 0.092), (0.0, 0.0, 0.116), b_spine, True)
    b_neck  = make_bone("Neck",  (0.0, 0.0, 0.116), (0.0, 0.0, 0.128), b_chest, True)
    b_head  = make_bone("Head",  (0.0, 0.0, 0.128), (0.0, 0.0, 0.192), b_neck, True)

    # Face & Accessory bones (-Y is front!)
    make_bone("Eye_L",  ( 0.017, -0.036, 0.152), ( 0.017, -0.046, 0.152), b_head)
    make_bone("Eye_R",  (-0.017, -0.036, 0.152), (-0.017, -0.046, 0.152), b_head)
    make_bone("Brow_L", ( 0.018, -0.037, 0.167), ( 0.018, -0.047, 0.167), b_head)
    make_bone("Brow_R", (-0.018, -0.037, 0.167), (-0.018, -0.047, 0.167), b_head)

    b_acc_root = make_bone("Accessory_Root", (0.0, 0.000, 0.190), (0.0,  0.000, 0.212), b_head)
    b_acc_tip  = make_bone("Accessory_Tip",  (0.0, 0.000, 0.212), (0.0, -0.006, 0.232), b_acc_root, True)

    # Left & Right Arms (+X is Left, -X is Right)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        b_sh = make_bone(f"Shoulder_{side}", (sx * 0.026, 0.0, 0.106), (sx * 0.038,  0.000, 0.104), b_chest)
        b_ua = make_bone(f"UpperArm_{side}", (sx * 0.038, 0.0, 0.104), (sx * 0.045,  0.002, 0.078), b_sh, True)
        b_fa = make_bone(f"Forearm_{side}",  (sx * 0.045, 0.002, 0.078), (sx * 0.047, -0.006, 0.056), b_ua, True)
        make_bone(f"Hand_{side}",            (sx * 0.047, -0.006, 0.056), (sx * 0.047, -0.014, 0.040), b_fa, True)

    # Left & Right Legs (+X is Left, -X is Right)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        b_th = make_bone(f"Thigh_{side}", (sx * 0.019, 0.000, 0.044), (sx * 0.019,  0.000, 0.024), b_hips)
        b_sn = make_bone(f"Shin_{side}",  (sx * 0.019, 0.000, 0.024), (sx * 0.019,  0.000, 0.008), b_th, True)
        make_bone(f"Foot_{side}",         (sx * 0.019, 0.000, 0.008), (sx * 0.019, -0.020, 0.004), b_sn, True)

    bpy.ops.object.mode_set(mode='OBJECT')

    # Set all pose bones to XYZ Euler for clean procedural animation curves
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'

    # Create the 5 mandatory attachment Empties parented to bones + 9 Section 16 sockets
    attachments = [
        ("RightHand_Attach", "Hand_R", (-0.047, -0.016, 0.045)),
        ("LeftHand_Attach",  "Hand_L", ( 0.047, -0.016, 0.045)),
        ("Head_Attach",      "Head",   ( 0.000,  0.000, 0.194)),
        ("Back_Attach",      "Chest",  ( 0.000,  0.040, 0.095)),
        ("Document_Attach",  "Chest",  ( 0.000, -0.072, 0.092)),
        # Section 16 canonical character sockets
        ("DocumentHold",           "Chest",  ( 0.000, -0.056, 0.090)),
        ("DocumentOffer",          "Chest",  ( 0.000, -0.088, 0.104)),
        ("TaskCardHold",           "Hand_R", (-0.047, -0.020, 0.048)),
        ("ArtifactHold",           "Chest",  ( 0.000, -0.068, 0.086)),
        ("HeadLookTarget",         "Head",   ( 0.000, -0.036, 0.156)),
        ("ConversationFocus",      "Head",   ( 0.000, -0.060, 0.155)),
        ("FootGroundReference",    "Hips",   ( 0.000,  0.000, 0.000)),
        ("ElevatorPassengerRoot",  "Hips",   ( 0.000,  0.000, 0.000)),
        ("PortalPresentationRoot", "Hips",   ( 0.000,  0.000, 0.000)),
    ]
    for att_name, parent_bone, world_pos in attachments:
        emp = bpy.data.objects.new(att_name, None)
        emp.empty_display_type = 'PLAIN_AXES'
        emp.empty_display_size = 0.015
        bpy.context.collection.objects.link(emp)
        emp.parent = arm_obj
        emp.parent_type = 'BONE'
        emp.parent_bone = parent_bone
        # Compute bone-local matrix from desired world location
        bone = arm_obj.data.bones[parent_bone]
        bone_tail_mat = bone.matrix_local.copy()
        bone_tail_mat.translation = bone.tail_local
        emp.matrix_parent_inverse = bone_tail_mat.inverted()
        emp.location = world_pos

    return arm_obj


# ==============================================================================
# 2. SCULPTED GEOMETRY BUILDERS (Pipkin Sprite Shared Body + Role Accessories)
# ==============================================================================

def create_lathe_mesh(name, profile_rz, segments=16, scale_xy=(1.0, 1.0), location=(0, 0, 0)):
    """
    profile_rz: list of (radius, z) from bottom to top.
    """
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for r, z in profile_rz:
        if r <= 1e-5:
            v = bm.verts.new((0.0, 0.0, z))
            rings.append([v])
        else:
            ring = []
            for s in range(segments):
                ang = (2.0 * math.pi * s) / segments
                x = math.cos(ang) * r * scale_xy[0]
                y = math.sin(ang) * r * scale_xy[1]
                ring.append(bm.verts.new((x, y, z)))
            rings.append(ring)

    for i in range(len(rings) - 1):
        r0 = rings[i]
        r1 = rings[i + 1]
        if len(r0) == 1 and len(r1) > 1:
            for s in range(segments):
                bm.faces.new([r0[0], r1[(s + 1) % segments], r1[s]])
        elif len(r1) == 1 and len(r0) > 1:
            for s in range(segments):
                bm.faces.new([r0[s], r0[(s + 1) % segments], r1[0]])
        elif len(r0) > 1 and len(r1) > 1:
            for s in range(segments):
                sn = (s + 1) % segments
                bm.faces.new([r0[s], r0[sn], r1[sn], r1[s]])

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    bpy.context.collection.objects.link(obj)
    shade_smooth(obj)
    return obj


def build_shared_body_parts(role_primary_mat, role_secondary_mat):
    parts = []

    # 1. Pear-Pod Torso (Hips -> Spine -> Chest)
    torso_profile = [
        (0.000, 0.034),
        (0.022, 0.036),
        (0.036, 0.043),
        (0.043, 0.054),
        (0.044, 0.066),
        (0.041, 0.079),
        (0.036, 0.092),
        (0.031, 0.104),
        (0.023, 0.114),
        (0.015, 0.122),
        (0.000, 0.124),
    ]
    torso = create_lathe_mesh("Torso", torso_profile, segments=18, scale_xy=(1.02, 0.92))
    assign_material(torso, "Mat_CreamCeramic")
    assign_z_gradient_weights(torso, [
        (0.048, "Hips"),
        (0.078, "Spine"),
        (0.105, "Chest"),
        (0.124, "Neck"),
    ])
    parts.append(torso)

    # Subtle belly inset patch flush on front of torso (-Y)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1.0, location=(0.0, -0.039, 0.072))
    belly = bpy.context.active_object
    belly.name = "BellyPatch"
    belly.scale = (0.022, 0.005, 0.021)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(belly)
    assign_material(belly, "Mat_WarmBeige")
    assign_z_gradient_weights(belly, [(0.055, "Hips"), (0.078, "Spine"), (0.098, "Chest")])
    parts.append(belly)

    # 2. Cozy Sculpted Head with Chubby Cheek Puffs (Head)
    mesh_head = bpy.data.meshes.new("HeadShell")
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=1.0)
    for v in bm.verts:
        x, y, z = v.co.x, v.co.y, v.co.z
        # Wide rounded-squircle silhouette + cheek puffs on lower sides
        cheek_factor = math.exp(-((z + 0.25) ** 2) / 0.18) * (0.14 if abs(x) > 0.35 else 0.0)
        sx = (0.048 + cheek_factor * 0.045) * (1.0 + 0.08 * (1.0 - z * z))
        sy = 0.041
        sz = 0.036
        nx = x * sx
        ny = y * sy
        nz = 0.158 + z * sz
        # Slightly flatten front (-Y) for visor cradle mount
        if y < -0.45 and -0.45 < z < 0.45:
            ny = max(ny, -0.035)
        v.co = (nx, ny, nz)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh_head)
    bm.free()
    head_obj = bpy.data.objects.new("HeadShell", mesh_head)
    bpy.context.collection.objects.link(head_obj)
    shade_smooth(head_obj)
    assign_material(head_obj, "Mat_CreamCeramic")
    assign_z_gradient_weights(head_obj, [(0.124, "Neck"), (0.134, "Head")])
    parts.append(head_obj)

    # 3. Recessed Charcoal Visor Faceplate & Frame on Front (-Y)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=18, ring_count=10, radius=1.0, location=(0.0, -0.033, 0.154))
    visor = bpy.context.active_object
    visor.name = "VisorInset"
    visor.scale = (0.034, 0.008, 0.021)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(visor)
    assign_material(visor, "Mat_CharcoalVisor")
    assign_single_bone_weight(visor, "Head")
    parts.append(visor)

    # Cute Ear-Pods on Left/Right of Head (seated proud outside HeadShell cheek equator)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.010, depth=0.011,
                                            location=(sx * 0.054, 0.0, 0.156),
                                            rotation=(0.0, math.radians(90), 0.0))
        ear = bpy.context.active_object
        ear.name = f"EarPod_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(ear)
        assign_material(ear, role_primary_mat)
        assign_single_bone_weight(ear, "Head")
        parts.append(ear)

    # 4. Expressive Glowing Eyes + Catchlights + Cheek Blushes + Floating Brows
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        # Main luminous eye dome
        bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=8, radius=1.0,
                                             location=(sx * 0.016, -0.040, 0.152))
        eye = bpy.context.active_object
        eye.name = f"EyeMesh_{side}"
        eye.scale = (0.0078, 0.0035, 0.0098)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        shade_smooth(eye)
        assign_material(eye, "Mat_EyeGlow")
        assign_single_bone_weight(eye, f"Eye_{side}")
        parts.append(eye)

        # Cheek blush oval beneath outer eye edge
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1.0,
                                             location=(sx * 0.025, -0.039, 0.141))
        blush = bpy.context.active_object
        blush.name = f"Blush_{side}"
        blush.scale = (0.0052, 0.0020, 0.0032)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(blush)
        assign_material(blush, "Mat_CheekBlush")
        assign_single_bone_weight(blush, "Head")
        parts.append(blush)

        # Expressive floating brow pill
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * 0.016, -0.041, 0.166))
        brow = bpy.context.active_object
        brow.name = f"BrowMesh_{side}"
        brow.scale = (0.015, 0.005, 0.0044)
        brow.rotation_euler = (0.0, math.radians(-sx * 6.0), 0.0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        bev = brow.modifiers.new("Bevel", 'BEVEL')
        bev.width = 0.0012
        bev.segments = 2
        bpy.context.view_layer.objects.active = brow
        bpy.ops.object.modifier_apply(modifier="Bevel")
        shade_smooth(brow)
        assign_material(brow, "Mat_EyeGlow")
        assign_single_bone_weight(brow, f"Brow_{side}")
        parts.append(brow)

    # 5. Articulated Tapered Arms + Wrist Cuffs + Mitten Hands with Thumb
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        # Arm sleeve (Shoulder -> UpperArm -> Forearm)
        arm_profile = [
            (0.000, 0.054),
            (0.010, 0.056),
            (0.012, 0.068),
            (0.011, 0.082),
            (0.012, 0.095),
            (0.011, 0.106),
            (0.007, 0.111),
            (0.000, 0.113),
        ]
        arm_mesh = create_lathe_mesh(f"Arm_{side}", arm_profile, segments=12)
        for v in arm_mesh.data.vertices:
            z = v.co.z
            v.co.x += sx * (0.036 + 0.011 * max(0.0, min(1.0, (0.110 - z) / 0.054)))
            v.co.y += -0.006 * max(0.0, (0.080 - z) / 0.026)
        assign_material(arm_mesh, "Mat_CreamCeramic")
        assign_z_gradient_weights(arm_mesh, [
            (0.058, f"Hand_{side}"),
            (0.076, f"Forearm_{side}"),
            (0.100, f"UpperArm_{side}"),
            (0.112, f"Shoulder_{side}"),
        ])
        parts.append(arm_mesh)

        # Role-accent wrist cuff
        bpy.ops.mesh.primitive_torus_add(major_segments=14, minor_segments=8,
                                         major_radius=0.0105, minor_radius=0.0032,
                                         location=(sx * 0.047, -0.005, 0.057),
                                         rotation=(math.radians(12), 0, 0))
        cuff = bpy.context.active_object
        cuff.name = f"WristCuff_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(cuff)
        assign_material(cuff, role_primary_mat)
        assign_single_bone_weight(cuff, f"Forearm_{side}")
        parts.append(cuff)

        # Mitten Hand Palm
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1.0,
                                             location=(sx * 0.047, -0.010, 0.045))
        palm = bpy.context.active_object
        palm.name = f"HandPalm_{side}"
        palm.scale = (0.0095, 0.0115, 0.0115)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(palm)
        assign_material(palm, "Mat_WarmBeige")
        assign_single_bone_weight(palm, f"Hand_{side}")
        parts.append(palm)

        # Opposable Thumb Nub (inner front side of mitten)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=1.0,
                                             location=(sx * 0.039, -0.017, 0.048))
        thumb = bpy.context.active_object
        thumb.name = f"HandThumb_{side}"
        thumb.scale = (0.0050, 0.0065, 0.0050)
        thumb.rotation_euler = (math.radians(-25), 0, math.radians(sx * 25))
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(thumb)
        assign_material(thumb, "Mat_WarmBeige")
        assign_single_bone_weight(thumb, f"Hand_{side}")
        parts.append(thumb)

    # 6. Sturdy Short Legs + Planted Rounded Boots (Thigh -> Shin -> Foot)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        leg_profile = [
            (0.000, 0.008),
            (0.012, 0.009),
            (0.013, 0.024),
            (0.015, 0.040),
            (0.014, 0.048),
            (0.000, 0.050),
        ]
        leg = create_lathe_mesh(f"Leg_{side}", leg_profile, segments=12, location=(sx * 0.019, 0.0, 0.0))
        bpy.context.view_layer.objects.active = leg
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        assign_material(leg, "Mat_CreamCeramic")
        assign_z_gradient_weights(leg, [
            (0.012, f"Foot_{side}"),
            (0.025, f"Shin_{side}"),
            (0.044, f"Thigh_{side}"),
        ])
        parts.append(leg)

        # Rounded Boot with flat sole at z=0.0
        mesh_boot = bpy.data.meshes.new(f"Boot_{side}")
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=1.0)
        for v in bm.verts:
            bx = sx * 0.019 + v.co.x * 0.0145
            # Extend toe forward (-Y)
            by = -0.007 + v.co.y * (0.022 if v.co.y < 0 else 0.014)
            bz = max(0.0, 0.008 + v.co.z * 0.011)
            v.co = (bx, by, bz)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh_boot)
        bm.free()
        boot = bpy.data.objects.new(f"Boot_{side}", mesh_boot)
        bpy.context.collection.objects.link(boot)
        shade_smooth(boot)
        assign_material(boot, "Mat_DarkBoots")
        assign_single_bone_weight(boot, f"Foot_{side}")
        parts.append(boot)

    return parts


# ==============================================================================
# 3. ROLE-SPECIFIC GARMENTS & ACCESSORIES
# ==============================================================================

def build_researcher_accessories():
    parts = []

    # 1. Pleated Lavender Scholar Capelet & Collar around Chest/Shoulders
    cape_profile = [
        (0.036, 0.088),
        (0.041, 0.095),
        (0.042, 0.106),
        (0.035, 0.118),
        (0.027, 0.124),
    ]
    cape = create_lathe_mesh("Researcher_Capelet", cape_profile, segments=18, scale_xy=(1.06, 0.96))
    for i, v in enumerate(cape.data.vertices):
        ang = math.atan2(v.co.y, v.co.x)
        # Gentle scalloped hem wave
        if v.co.z < 0.096:
            v.co.z += 0.0025 * math.sin(ang * 8.0)
    assign_material(cape, "Mat_RoleTrim_Researcher")
    assign_z_gradient_weights(cape, [(0.090, "Spine"), (0.118, "Chest")])
    parts.append(cape)

    # Brass Clasp Button on front of Capelet (-Y)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.0065, depth=0.004,
                                        location=(0.0, -0.037, 0.106),
                                        rotation=(math.radians(78), 0, 0))
    clasp = bpy.context.active_object
    clasp.name = "Researcher_Clasp"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(clasp)
    assign_material(clasp, "Mat_Brass")
    assign_single_bone_weight(clasp, "Chest")
    parts.append(clasp)

    # 2. Brass-Rimmed Round Reading Spectacles on Visor (-Y)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_torus_add(major_segments=16, minor_segments=8,
                                         major_radius=0.0115, minor_radius=0.0018,
                                         location=(sx * 0.0165, -0.042, 0.152),
                                         rotation=(math.radians(90), 0, 0))
        rim = bpy.context.active_object
        rim.name = f"GlassesRim_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(rim)
        assign_material(rim, "Mat_Brass")
        assign_single_bone_weight(rim, "Head")
        parts.append(rim)

    # Spectacles nose bridge
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.0016, depth=0.010,
                                        location=(0.0, -0.043, 0.154),
                                        rotation=(0, math.radians(90), 0))
    bridge = bpy.context.active_object
    bridge.name = "GlassesBridge"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(bridge)
    assign_material(bridge, "Mat_Brass")
    assign_single_bone_weight(bridge, "Head")
    parts.append(bridge)

    # 3. Tilted Scholar Beret on Crown of Head
    bpy.ops.mesh.primitive_uv_sphere_add(segments=18, ring_count=10, radius=1.0,
                                         location=(-0.006, 0.004, 0.188))
    beret = bpy.context.active_object
    beret.name = "ScholarBeret"
    beret.scale = (0.040, 0.038, 0.013)
    beret.rotation_euler = (math.radians(-8), math.radians(-12), 0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(beret)
    assign_material(beret, "Mat_RoleTrim_Researcher")
    assign_single_bone_weight(beret, "Head")
    parts.append(beret)

    # 4. Kinetic Twin-Leaf Botanical Sprout Antenna (Accessory_Root -> Accessory_Tip)
    stem_profile = [
        (0.0045, 0.188),
        (0.0035, 0.202),
        (0.0028, 0.215),
        (0.0020, 0.228),
        (0.0000, 0.231),
    ]
    stem = create_lathe_mesh("SproutStem", stem_profile, segments=10)
    for v in stem.data.vertices:
        t = max(0.0, (v.co.z - 0.188) / 0.042)
        v.co.y -= 0.006 * (t * t)
    assign_material(stem, "Mat_RoleTrim_Researcher_Soft")
    assign_z_gradient_weights(stem, [(0.192, "Head"), (0.208, "Accessory_Root"), (0.228, "Accessory_Tip")])
    parts.append(stem)

    # Twin sculpted leaves on the sprout
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1.0,
                                             location=(sx * 0.014, -0.004, 0.218))
        leaf = bpy.context.active_object
        leaf.name = f"SproutLeaf_{side}"
        leaf.scale = (0.014, 0.007, 0.003)
        leaf.rotation_euler = (math.radians(15), math.radians(sx * -28), math.radians(sx * 15))
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(leaf)
        assign_material(leaf, "Mat_RoleTrim_Researcher_Soft")
        assign_single_bone_weight(leaf, "Accessory_Tip")
        parts.append(leaf)

    # Glowing dewdrop orb at tip
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.0055,
                                         location=(0.0, -0.008, 0.232))
    orb = bpy.context.active_object
    orb.name = "SproutDewOrb"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(orb)
    assign_material(orb, "Mat_EyeGlow")
    assign_single_bone_weight(orb, "Accessory_Tip")
    parts.append(orb)

    # 5. Scroll Satchel & Rolled Parchment on Back (+Y)
    bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.011, depth=0.052,
                                        location=(0.0, 0.038, 0.088),
                                        rotation=(0, math.radians(72), 0))
    satchel = bpy.context.active_object
    satchel.name = "ScrollSatchel"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(satchel)
    assign_material(satchel, "Mat_LeatherSatchel")
    assign_single_bone_weight(satchel, "Chest")
    parts.append(satchel)

    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.0082, depth=0.064,
                                        location=(0.0, 0.038, 0.088),
                                        rotation=(0, math.radians(72), 0))
    scroll = bpy.context.active_object
    scroll.name = "RolledScroll"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(scroll)
    assign_material(scroll, "Mat_PaperWarm")
    assign_single_bone_weight(scroll, "Chest")
    parts.append(scroll)

    return parts


def build_developer_accessories():
    parts = []

    # 1. Coral Tech-Hoodie Vest & Collar
    vest_profile = [
        (0.043, 0.060),
        (0.045, 0.072),
        (0.042, 0.088),
        (0.038, 0.104),
        (0.034, 0.118),
        (0.028, 0.125),
    ]
    vest = create_lathe_mesh("Developer_HoodieVest", vest_profile, segments=18, scale_xy=(1.05, 0.95))
    assign_material(vest, "Mat_RoleTrim_Developer")
    assign_z_gradient_weights(vest, [(0.064, "Hips"), (0.082, "Spine"), (0.112, "Chest")])
    parts.append(vest)

    # Role-colored Code Badge on Chest (-Y)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.011, -0.038, 0.095))
    badge = bpy.context.active_object
    badge.name = "Developer_CodeBadge"
    badge.scale = (0.014, 0.004, 0.009)
    badge.rotation_euler = (math.radians(12), 0, math.radians(-14))
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(badge)
    assign_material(badge, "Mat_RoleTrim_Developer_Soft")
    assign_single_bone_weight(badge, "Chest")
    parts.append(badge)

    # 2. Oversized Studio Headphones + Headband Arch + Mic Boom
    # Headband arch over top of head
    bpy.ops.mesh.primitive_torus_add(major_segments=20, minor_segments=8,
                                     major_radius=0.054, minor_radius=0.0045,
                                     location=(0.0, 0.002, 0.156),
                                     rotation=(math.radians(90), 0, 0))
    band = bpy.context.active_object
    band.name = "HeadphoneBand"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # Clip bottom half of torus below ears (z < 0.152)
    bm = bmesh.new()
    bm.from_mesh(band.data)
    to_del = [v for v in bm.verts if v.co.z < 0.150]
    bmesh.ops.delete(bm, geom=to_del, context='VERTS')
    bm.to_mesh(band.data)
    bm.free()
    shade_smooth(band)
    assign_material(band, "Mat_CharcoalVisor")
    assign_single_bone_weight(band, "Head")
    parts.append(band)

    for side, sx in [("L", 1.0), ("R", -1.0)]:
        # Cushioned earcup shell
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.016, depth=0.014,
                                            location=(sx * 0.055, 0.002, 0.155),
                                            rotation=(0, math.radians(90), 0))
        cup = bpy.context.active_object
        cup.name = f"HeadphoneCup_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(cup)
        assign_material(cup, "Mat_RoleTrim_Developer")
        assign_single_bone_weight(cup, "Head")
        parts.append(cup)

        # Soft matte ring on earcup
        bpy.ops.mesh.primitive_torus_add(major_segments=16, minor_segments=8,
                                         major_radius=0.013, minor_radius=0.0022,
                                         location=(sx * 0.060, 0.002, 0.155),
                                         rotation=(0, math.radians(90), 0))
        ring = bpy.context.active_object
        ring.name = f"HeadphoneGlowRing_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(ring)
        assign_material(ring, "Mat_RoleTrim_Developer_Soft")
        assign_single_bone_weight(ring, "Head")
        parts.append(ring)

    # Curved Microphone Boom on Left Cheek
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.0020, depth=0.032,
                                        location=(0.042, -0.022, 0.142),
                                        rotation=(math.radians(65), math.radians(-18), math.radians(28)))
    mic_arm = bpy.context.active_object
    mic_arm.name = "HeadphoneMicBoom"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(mic_arm)
    assign_material(mic_arm, "Mat_BrushedMetal")
    assign_single_bone_weight(mic_arm, "Head")
    parts.append(mic_arm)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=8, radius=0.0042,
                                         location=(0.032, -0.036, 0.136))
    mic_tip = bpy.context.active_object
    mic_tip.name = "HeadphoneMicTip"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(mic_tip)
    assign_material(mic_tip, "Mat_RoleTrim_Developer_Soft")
    assign_single_bone_weight(mic_tip, "Head")
    parts.append(mic_tip)

    # 3. Twin Angled Cyber-Fin Crest Antennae on Crown (Accessory_Root -> Accessory_Tip)
    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.009, radius2=0.002, depth=0.036,
                                        location=(sx * 0.015, 0.002, 0.206),
                                        rotation=(math.radians(-10), math.radians(sx * 22), 0))
        fin = bpy.context.active_object
        fin.name = f"CyberFin_{side}"
        fin.scale = (0.65, 1.35, 1.0)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(fin)
        assign_material(fin, "Mat_RoleTrim_Developer")
        assign_z_gradient_weights(fin, [(0.192, "Head"), (0.206, "Accessory_Root"), (0.222, "Accessory_Tip")])
        parts.append(fin)

    # Soft matte center beacon on Crown
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.007,
                                         location=(0.0, -0.002, 0.202))
    beacon = bpy.context.active_object
    beacon.name = "CyberCrestBeacon"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(beacon)
    assign_material(beacon, "Mat_RoleTrim_Developer_Soft")
    assign_single_bone_weight(beacon, "Accessory_Root")
    parts.append(beacon)

    # 4. Dual-Cell Power-Core Backpack on Back (+Y)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.038, 0.088))
    pack = bpy.context.active_object
    pack.name = "DevPowerPack"
    pack.scale = (0.038, 0.020, 0.042)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bev = pack.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.003
    bev.segments = 2
    bpy.context.view_layer.objects.active = pack
    bpy.ops.object.modifier_apply(modifier="Bevel")
    shade_smooth(pack)
    assign_material(pack, "Mat_CharcoalVisor")
    assign_single_bone_weight(pack, "Chest")
    parts.append(pack)

    for side, sx in [("L", 1.0), ("R", -1.0)]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.006, depth=0.022,
                                            location=(sx * 0.011, 0.046, 0.088))
        cell = bpy.context.active_object
        cell.name = f"PowerCell_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(cell)
        assign_material(cell, "Mat_RoleTrim_Developer_Soft")
        assign_single_bone_weight(cell, "Chest")
        parts.append(cell)

    return parts


def build_tester_accessories():
    parts = []

    # 1. Mint & Slate QA Utility Harness Vest
    harness_profile = [
        (0.043, 0.062),
        (0.044, 0.076),
        (0.040, 0.094),
        (0.036, 0.110),
        (0.029, 0.120),
    ]
    harness = create_lathe_mesh("Tester_UtilityVest", harness_profile, segments=18, scale_xy=(1.05, 0.95))
    assign_material(harness, "Mat_RoleTrim_Tester")
    assign_z_gradient_weights(harness, [(0.065, "Hips"), (0.084, "Spine"), (0.112, "Chest")])
    parts.append(harness)

    # Muted Rose Utility Belt around Waist
    bpy.ops.mesh.primitive_torus_add(major_segments=18, minor_segments=8,
                                     major_radius=0.043, minor_radius=0.0045,
                                     location=(0.0, 0.0, 0.064))
    belt = bpy.context.active_object
    belt.name = "Tester_UtilityBelt"
    belt.scale = (1.04, 0.94, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(belt)
    assign_material(belt, "Mat_RoleTrim_Tester_Soft")
    assign_single_bone_weight(belt, "Spine")
    parts.append(belt)

    # 2. Precision Inspector Helmet-Band & Articulated Dual-Loupe Magnifier Visor
    bpy.ops.mesh.primitive_torus_add(major_segments=20, minor_segments=8,
                                     major_radius=0.047, minor_radius=0.0040,
                                     location=(0.0, -0.002, 0.175),
                                     rotation=(math.radians(8), 0, 0))
    hband = bpy.context.active_object
    hband.name = "InspectorHeadband"
    hband.scale = (1.03, 0.90, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(hband)
    assign_material(hband, "Mat_RoleTrim_Tester_Soft")
    assign_single_bone_weight(hband, "Head")
    parts.append(hband)

    # Dual Flip-Up Inspection Loupes on Forehead (weighted to Accessory_Root / Accessory_Tip!)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, -0.042, 0.182))
    loupe_bar = bpy.context.active_object
    loupe_bar.name = "LoupeMountBar"
    loupe_bar.scale = (0.042, 0.008, 0.006)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(loupe_bar)
    assign_material(loupe_bar, "Mat_BrushedMetal")
    assign_single_bone_weight(loupe_bar, "Accessory_Root")
    parts.append(loupe_bar)

    for side, sx in [("L", 1.0), ("R", -1.0)]:
        # Loupe barrel
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.011, depth=0.010,
                                            location=(sx * 0.015, -0.046, 0.180),
                                            rotation=(math.radians(78), 0, 0))
        barrel = bpy.context.active_object
        barrel.name = f"LoupeBarrel_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(barrel)
        assign_material(barrel, "Mat_RoleTrim_Tester")
        assign_single_bone_weight(barrel, "Accessory_Tip")
        parts.append(barrel)

        # Soft matte inspection lens
        bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.0085, depth=0.011,
                                            location=(sx * 0.015, -0.047, 0.180),
                                            rotation=(math.radians(78), 0, 0))
        lens = bpy.context.active_object
        lens.name = f"LoupeLens_{side}"
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        shade_smooth(lens)
        assign_material(lens, "Mat_RoleTrim_Tester_Lens")
        assign_single_bone_weight(lens, "Accessory_Tip")
        parts.append(lens)

    # Top Calibration Dish Antenna on Crown
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.016, radius2=0.003, depth=0.012,
                                    location=(0.0, 0.004, 0.200),
                                    rotation=(math.radians(-18), 0, 0))
    dish = bpy.context.active_object
    dish.name = "Tester_CalibrationDish"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(dish)
    assign_material(dish, "Mat_RoleTrim_Tester_Soft")
    assign_single_bone_weight(dish, "Accessory_Root")
    parts.append(dish)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=8, radius=0.005,
                                         location=(0.0, 0.000, 0.214))
    dish_tip = bpy.context.active_object
    dish_tip.name = "Tester_DishProbe"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade_smooth(dish_tip)
    assign_material(dish_tip, "Mat_RoleTrim_Tester_Lens")
    assign_single_bone_weight(dish_tip, "Accessory_Tip")
    parts.append(dish_tip)

    # 3. Calibrated Signal Analyzer Pack on Back (+Y)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.039, 0.088))
    apack = bpy.context.active_object
    apack.name = "Tester_AnalyzerPack"
    apack.scale = (0.040, 0.020, 0.040)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bev = apack.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.003
    bev.segments = 2
    bpy.context.view_layer.objects.active = apack
    bpy.ops.object.modifier_apply(modifier="Bevel")
    shade_smooth(apack)
    assign_material(apack, "Mat_RoleTrim_Tester_Soft")
    assign_single_bone_weight(apack, "Chest")
    parts.append(apack)

    return parts


def join_and_bind_to_armature(parts, arm_obj, mesh_name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = mesh_name
    joined.data.name = f"{mesh_name}_Mesh"
    shade_smooth(joined)

    # Parent to Armature and add Armature modifier
    joined.parent = arm_obj
    mod = joined.modifiers.new(name="Armature", type='ARMATURE')
    mod.object = arm_obj
    return joined


# ==============================================================================
# 4. SHARED 39-CLIP ANIMATION LIBRARY GENERATOR
# ==============================================================================

def reset_pose(arm_obj):
    for pb in arm_obj.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def key_bone(arm_obj, bone_name, frame, loc=None, rot_deg=None, scale=None):
    pb = arm_obj.pose.bones.get(bone_name)
    if not pb:
        return
    if loc is not None:
        pb.location = loc
        pb.keyframe_insert(data_path="location", frame=frame)
    if rot_deg is not None:
        rx, ry, rz = rot_deg
        # Arm bones point down (-Z), so negative X swings the arm forward (-Y)
        if bone_name.startswith(("UpperArm_", "Forearm_", "Hand_")):
            rx = -rx
        pb.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)
    if scale is not None:
        pb.scale = scale
        pb.keyframe_insert(data_path="scale", frame=frame)


def key_all_rest(arm_obj, frame):
    for pb in arm_obj.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)
        pb.keyframe_insert(data_path="location", frame=frame)
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)
        pb.keyframe_insert(data_path="scale", frame=frame)


def build_shared_animation_library(arm_obj):
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    # Clear any old actions/tracks
    for tr in list(arm_obj.animation_data.nla_tracks):
        arm_obj.animation_data.nla_tracks.remove(tr)

    # Define all 39 clips with expressive multi-bone keyframes:
    # Note on bone local axes:
    #   Spine/Chest/Neck/Head: +X bends backward, -X leans forward, +Y tilts right/left, +Z twists yaw
    #   UpperArm/Forearm: +Y/-Y or +X/-X swings arm forward/up/out
    clips_spec = [
        # --- A. BASIC MOVEMENT (9) ---
        ("Idle_Breathing", 48, [
            (1,  {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (0,0,0)}, "Head": {"rot": (0,0,0)}, "UpperArm_L": {"rot": (0,0,-8)}, "UpperArm_R": {"rot": (0,0,8)}, "Accessory_Root": {"rot": (0,0,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (16, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (-3,0,2)}, "Head": {"rot": (4,3,-2)}, "UpperArm_L": {"rot": (6,0,-10)}, "UpperArm_R": {"rot": (-5,0,10)}, "Accessory_Root": {"rot": (8,4,0)}}),
            (26, {"Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (29, {"Eye_L": {"scale": (1,1,0.12)}, "Eye_R": {"scale": (1,1,0.12)}}),
            (32, {"Hips": {"loc": (0,-0.001,0)}, "Chest": {"rot": (2,0,-2)}, "Head": {"rot": (-3,-3,2)}, "UpperArm_L": {"rot": (-5,0,-7)}, "UpperArm_R": {"rot": (6,0,7)}, "Accessory_Root": {"rot": (-6,-4,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (48, {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (0,0,0)}, "Head": {"rot": (0,0,0)}, "UpperArm_L": {"rot": (0,0,-8)}, "UpperArm_R": {"rot": (0,0,8)}, "Accessory_Root": {"rot": (0,0,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
        ]),
        ("Idle_Fidget", 60, [
            (1,  {}),
            (15, {"Hips": {"loc": (0.004,0.002,0)}, "Chest": {"rot": (0,5,-8)}, "Head": {"rot": (-5,8,-18)}, "UpperArm_L": {"rot": (18,0,-12)}, "UpperArm_R": {"rot": (15,0,12)}, "Accessory_Tip": {"rot": (12,10,0)}}),
            (32, {"Hips": {"loc": (-0.004,0.003,0)}, "Chest": {"rot": (-4,-5,10)}, "Head": {"rot": (6,-10,20)}, "Forearm_L": {"rot": (28,0,15)}, "Forearm_R": {"rot": (28,0,-15)}, "Accessory_Tip": {"rot": (-12,-10,0)}}),
            (46, {"Hips": {"loc": (0,0.004,0)}, "Head": {"rot": (-8,0,0)}, "UpperArm_L": {"rot": (25,0,10)}, "UpperArm_R": {"rot": (25,0,-10)}, "Eye_L": {"scale": (1,1,0.15)}, "Eye_R": {"scale": (1,1,0.15)}}),
            (60, {}),
        ]),
        ("Happy_Idle", 40, [
            (1,  {}),
            (10, {"Hips": {"loc": (0,0.006,0)}, "Chest": {"rot": (-5,0,5)}, "Head": {"rot": (6,8,-5)}, "UpperArm_L": {"rot": (22,0,-18)}, "UpperArm_R": {"rot": (-18,0,18)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (14,8,0)}}),
            (20, {"Hips": {"loc": (0,-0.002,0)}, "Chest": {"rot": (3,0,0)}, "Head": {"rot": (-4,0,0)}}),
            (30, {"Hips": {"loc": (0,0.006,0)}, "Chest": {"rot": (-5,0,-5)}, "Head": {"rot": (6,-8,5)}, "UpperArm_L": {"rot": (-18,0,-18)}, "UpperArm_R": {"rot": (22,0,18)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (14,-8,0)}}),
            (40, {}),
        ]),
        ("Walk_Start", 12, [
            (1,  {}),
            (4,  {"Hips": {"loc": (0,0.012,0), "rot": (4,4,-4)}, "Chest": {"rot": (4,-4,4)}, "Head": {"rot": (-3,3,0)}, "UpperArm_L": {"rot": (12,0,-10)}, "UpperArm_R": {"rot": (-12,0,10)}, "Thigh_R": {"rot": (10,0,0)}, "Shin_R": {"rot": (12,0,0)}, "Foot_R": {"rot": (-10,0,0)}, "Thigh_L": {"rot": (-18,0,0)}, "Shin_L": {"rot": (38,0,0)}, "Foot_L": {"rot": (12,0,0)}, "Accessory_Root": {"rot": (6,0,0)}}),
            (8,  {"Hips": {"loc": (0,0.016,0), "rot": (-2,2,2)}, "Chest": {"rot": (-2,-3,-3)}, "Head": {"rot": (2,2,2)}, "UpperArm_L": {"rot": (28,0,-10)}, "Forearm_L": {"rot": (15,0,0)}, "UpperArm_R": {"rot": (-24,0,10)}, "Forearm_R": {"rot": (8,0,0)}, "Thigh_L": {"rot": (-28,0,0)}, "Shin_L": {"rot": (18,0,0)}, "Foot_L": {"rot": (-8,0,0)}, "Thigh_R": {"rot": (22,0,0)}, "Shin_R": {"rot": (26,0,0)}, "Foot_R": {"rot": (14,0,0)}, "Accessory_Root": {"rot": (-8,3,0)}}),
            (12, {"Hips": {"loc": (0,0.010,0), "rot": (-6,0,5)}, "Chest": {"rot": (-5,0,-6)}, "Head": {"rot": (5,3,4)}, "UpperArm_L": {"rot": (34,0,-10)}, "Forearm_L": {"rot": (18,0,0)}, "UpperArm_R": {"rot": (-30,0,10)}, "Forearm_R": {"rot": (10,0,0)}, "Thigh_L": {"rot": (-32,0,0)}, "Shin_L": {"rot": (12,0,0)}, "Thigh_R": {"rot": (28,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Foot_L": {"rot": (0,0,0)}, "Foot_R": {"rot": (0,0,0)}, "Accessory_Root": {"rot": (12,5,0)}}),
        ]),
        ("Walk_Loop", 24, [
            (1,  {"Hips": {"loc": (0,0.010,0), "rot": (-6,0,5)}, "Chest": {"rot": (-5,0,-6)}, "Head": {"rot": (5,3,4)}, "UpperArm_L": {"rot": (34,0,-10)}, "Forearm_L": {"rot": (18,0,0)}, "UpperArm_R": {"rot": (-30,0,10)}, "Forearm_R": {"rot": (10,0,0)}, "Thigh_L": {"rot": (-32,0,0)}, "Shin_L": {"rot": (12,0,0)}, "Thigh_R": {"rot": (28,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Accessory_Root": {"rot": (12,5,0)}}),
            (7,  {"Hips": {"loc": (0,0.015,0), "rot": (-4,3,0)}, "Chest": {"rot": (-3,-2,0)}, "Head": {"rot": (2,-3,0)}, "Thigh_L": {"rot": (-5,0,0)}, "Shin_L": {"rot": (5,0,0)}, "Thigh_R": {"rot": (-15,0,0)}, "Shin_R": {"rot": (35,0,0)}, "Accessory_Root": {"rot": (-8,0,0)}}),
            (13, {"Hips": {"loc": (0,0.010,0), "rot": (-6,0,-5)}, "Chest": {"rot": (-5,0,6)}, "Head": {"rot": (5,-3,-4)}, "UpperArm_L": {"rot": (-30,0,-10)}, "Forearm_L": {"rot": (10,0,0)}, "UpperArm_R": {"rot": (34,0,10)}, "Forearm_R": {"rot": (18,0,0)}, "Thigh_L": {"rot": (28,0,0)}, "Shin_L": {"rot": (22,0,0)}, "Thigh_R": {"rot": (-32,0,0)}, "Shin_R": {"rot": (12,0,0)}, "Accessory_Root": {"rot": (12,-5,0)}}),
            (19, {"Hips": {"loc": (0,0.015,0), "rot": (-4,-3,0)}, "Chest": {"rot": (-3,2,0)}, "Head": {"rot": (2,3,0)}, "Thigh_L": {"rot": (-15,0,0)}, "Shin_L": {"rot": (35,0,0)}, "Thigh_R": {"rot": (-5,0,0)}, "Shin_R": {"rot": (5,0,0)}, "Accessory_Root": {"rot": (-8,0,0)}}),
            (24, {"Hips": {"loc": (0,0.010,0), "rot": (-6,0,5)}, "Chest": {"rot": (-5,0,-6)}, "Head": {"rot": (5,3,4)}, "UpperArm_L": {"rot": (34,0,-10)}, "Forearm_L": {"rot": (18,0,0)}, "UpperArm_R": {"rot": (-30,0,10)}, "Forearm_R": {"rot": (10,0,0)}, "Thigh_L": {"rot": (-32,0,0)}, "Shin_L": {"rot": (12,0,0)}, "Thigh_R": {"rot": (28,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Accessory_Root": {"rot": (12,5,0)}}),
        ]),
        ("Walk_Stop", 14, [
            (1,  {"Hips": {"loc": (0,0.010,0), "rot": (-4,0,4)}, "Chest": {"rot": (-4,0,-4)}, "Head": {"rot": (4,2,2)}, "UpperArm_L": {"rot": (26,0,-10)}, "Forearm_L": {"rot": (14,0,0)}, "UpperArm_R": {"rot": (-22,0,10)}, "Forearm_R": {"rot": (8,0,0)}, "Thigh_L": {"rot": (-24,0,0)}, "Shin_L": {"rot": (14,0,0)}, "Foot_L": {"rot": (-8,0,0)}, "Thigh_R": {"rot": (20,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Foot_R": {"rot": (10,0,0)}}),
            (5,  {"Hips": {"loc": (0,0.014,0), "rot": (4,0,2)}, "Chest": {"rot": (6,0,-2)}, "Head": {"rot": (-5,0,0)}, "UpperArm_L": {"rot": (8,0,-10)}, "Forearm_L": {"rot": (10,0,0)}, "UpperArm_R": {"rot": (-6,0,10)}, "Thigh_L": {"rot": (-12,0,0)}, "Shin_L": {"rot": (24,0,0)}, "Foot_L": {"rot": (-6,0,0)}, "Thigh_R": {"rot": (-14,0,0)}, "Shin_R": {"rot": (32,0,0)}, "Foot_R": {"rot": (8,0,0)}, "Accessory_Root": {"rot": (8,0,0)}}),
            (10, {"Hips": {"loc": (0,0.005,0)}, "Chest": {"rot": (-3,0,0)}, "Head": {"rot": (4,0,0)}, "Thigh_L": {"rot": (-4,0,0)}, "Shin_L": {"rot": (8,0,0)}, "Foot_L": {"rot": (-4,0,0)}, "Thigh_R": {"rot": (-4,0,0)}, "Shin_R": {"rot": (8,0,0)}, "Foot_R": {"rot": (-4,0,0)}, "UpperArm_L": {"rot": (-4,0,-8)}, "UpperArm_R": {"rot": (-4,0,8)}, "Accessory_Root": {"rot": (-5,0,0)}}),
            (14, {}),
        ]),
        ("Walk_Carry_Paper", 24, [
            (1,  {"Hips": {"loc": (0,0.013,0), "rot": (-4,2,4)}, "Chest": {"rot": (6,-2,-2)}, "Head": {"rot": (-5,2,1)}, "UpperArm_L": {"rot": (54,0,14)}, "Forearm_L": {"rot": (34,0,10)}, "UpperArm_R": {"rot": (54,0,-14)}, "Forearm_R": {"rot": (34,0,-10)}, "Thigh_L": {"rot": (-30,0,0)}, "Shin_L": {"rot": (12,0,0)}, "Foot_L": {"rot": (-6,0,0)}, "Thigh_R": {"rot": (26,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Foot_R": {"rot": (10,0,0)}, "Accessory_Root": {"rot": (10,4,0)}}),
            (7,  {"Hips": {"loc": (0,0.017,0), "rot": (-2,2,0)}, "Chest": {"rot": (5,0,0)}, "Head": {"rot": (-4,-2,0)}, "UpperArm_L": {"rot": (56,0,14)}, "Forearm_L": {"rot": (36,0,10)}, "UpperArm_R": {"rot": (56,0,-14)}, "Forearm_R": {"rot": (36,0,-10)}, "Thigh_L": {"rot": (-5,0,0)}, "Shin_L": {"rot": (6,0,0)}, "Foot_L": {"rot": (0,0,0)}, "Thigh_R": {"rot": (-16,0,0)}, "Shin_R": {"rot": (36,0,0)}, "Foot_R": {"rot": (8,0,0)}, "Accessory_Root": {"rot": (-6,0,0)}}),
            (13, {"Hips": {"loc": (0,0.013,0), "rot": (-4,-2,-4)}, "Chest": {"rot": (6,2,2)}, "Head": {"rot": (-5,-2,-1)}, "UpperArm_L": {"rot": (54,0,14)}, "Forearm_L": {"rot": (34,0,10)}, "UpperArm_R": {"rot": (54,0,-14)}, "Forearm_R": {"rot": (34,0,-10)}, "Thigh_L": {"rot": (26,0,0)}, "Shin_L": {"rot": (22,0,0)}, "Foot_L": {"rot": (10,0,0)}, "Thigh_R": {"rot": (-30,0,0)}, "Shin_R": {"rot": (12,0,0)}, "Foot_R": {"rot": (-6,0,0)}, "Accessory_Root": {"rot": (10,-4,0)}}),
            (19, {"Hips": {"loc": (0,0.017,0), "rot": (-2,-2,0)}, "Chest": {"rot": (5,0,0)}, "Head": {"rot": (-4,2,0)}, "UpperArm_L": {"rot": (56,0,14)}, "Forearm_L": {"rot": (36,0,10)}, "UpperArm_R": {"rot": (56,0,-14)}, "Forearm_R": {"rot": (36,0,-10)}, "Thigh_L": {"rot": (-16,0,0)}, "Shin_L": {"rot": (36,0,0)}, "Foot_L": {"rot": (8,0,0)}, "Thigh_R": {"rot": (-5,0,0)}, "Shin_R": {"rot": (6,0,0)}, "Foot_R": {"rot": (0,0,0)}, "Accessory_Root": {"rot": (-6,0,0)}}),
            (24, {"Hips": {"loc": (0,0.013,0), "rot": (-4,2,4)}, "Chest": {"rot": (6,-2,-2)}, "Head": {"rot": (-5,2,1)}, "UpperArm_L": {"rot": (54,0,14)}, "Forearm_L": {"rot": (34,0,10)}, "UpperArm_R": {"rot": (54,0,-14)}, "Forearm_R": {"rot": (34,0,-10)}, "Thigh_L": {"rot": (-30,0,0)}, "Shin_L": {"rot": (12,0,0)}, "Foot_L": {"rot": (-6,0,0)}, "Thigh_R": {"rot": (26,0,0)}, "Shin_R": {"rot": (22,0,0)}, "Foot_R": {"rot": (10,0,0)}, "Accessory_Root": {"rot": (10,4,0)}}),
        ]),
        ("Turn_Left", 20, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,0.011,0), "rot": (2,8,4)}, "Chest": {"rot": (4,12,-2)}, "Head": {"rot": (-2,22,-4)}, "Thigh_L": {"rot": (-26,10,-4)}, "Shin_L": {"rot": (44,0,0)}, "Foot_L": {"rot": (-12,0,0)}, "Thigh_R": {"rot": (4,-6,0)}, "Shin_R": {"rot": (12,0,0)}, "Foot_R": {"rot": (8,0,0)}, "UpperArm_L": {"rot": (-14,0,-14)}, "UpperArm_R": {"rot": (18,0,12)}, "Accessory_Root": {"rot": (0,12,0)}}),
            (11, {"Hips": {"loc": (0,0.014,0), "rot": (-2,6,-4)}, "Chest": {"rot": (2,-4,2)}, "Head": {"rot": (2,10,0)}, "Thigh_L": {"rot": (-6,-4,0)}, "Shin_L": {"rot": (12,0,0)}, "Foot_L": {"rot": (-6,0,0)}, "Thigh_R": {"rot": (-24,-10,4)}, "Shin_R": {"rot": (42,0,0)}, "Foot_R": {"rot": (10,0,0)}, "UpperArm_L": {"rot": (14,0,-10)}, "UpperArm_R": {"rot": (-12,0,10)}, "Accessory_Root": {"rot": (-6,-8,0)}}),
            (16, {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (2,0,0)}, "Head": {"rot": (-2,0,0)}, "Thigh_L": {"rot": (-4,0,0)}, "Shin_L": {"rot": (8,0,0)}, "Foot_L": {"rot": (-4,0,0)}, "Thigh_R": {"rot": (-4,0,0)}, "Shin_R": {"rot": (8,0,0)}, "Foot_R": {"rot": (-4,0,0)}}),
            (20, {}),
        ]),
        ("Turn_Right", 20, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,0.011,0), "rot": (2,-8,-4)}, "Chest": {"rot": (4,-12,2)}, "Head": {"rot": (-2,-22,4)}, "Thigh_R": {"rot": (-26,-10,4)}, "Shin_R": {"rot": (44,0,0)}, "Foot_R": {"rot": (-12,0,0)}, "Thigh_L": {"rot": (4,6,0)}, "Shin_L": {"rot": (12,0,0)}, "Foot_L": {"rot": (8,0,0)}, "UpperArm_R": {"rot": (-14,0,14)}, "UpperArm_L": {"rot": (18,0,-12)}, "Accessory_Root": {"rot": (0,-12,0)}}),
            (11, {"Hips": {"loc": (0,0.014,0), "rot": (-2,-6,4)}, "Chest": {"rot": (2,4,-2)}, "Head": {"rot": (2,-10,0)}, "Thigh_R": {"rot": (-6,4,0)}, "Shin_R": {"rot": (12,0,0)}, "Foot_R": {"rot": (-6,0,0)}, "Thigh_L": {"rot": (-24,10,-4)}, "Shin_L": {"rot": (42,0,0)}, "Foot_L": {"rot": (10,0,0)}, "UpperArm_R": {"rot": (14,0,10)}, "UpperArm_L": {"rot": (-12,0,-10)}, "Accessory_Root": {"rot": (-6,8,0)}}),
            (16, {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (2,0,0)}, "Head": {"rot": (-2,0,0)}, "Thigh_L": {"rot": (-4,0,0)}, "Shin_L": {"rot": (8,0,0)}, "Foot_L": {"rot": (-4,0,0)}, "Thigh_R": {"rot": (-4,0,0)}, "Shin_R": {"rot": (8,0,0)}, "Foot_R": {"rot": (-4,0,0)}}),
            (20, {}),
        ]),
        ("Sit_Down", 24, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0,-0.005,0), "rot": (-12,0,0)}, "Chest": {"rot": (-10,0,0)}, "UpperArm_L": {"rot": (20,0,-10)}, "UpperArm_R": {"rot": (20,0,10)}}),
            (18, {"Hips": {"loc": (0,-0.018,-0.010), "rot": (5,0,0)}, "Thigh_L": {"rot": (-78,0,0)}, "Shin_L": {"rot": (65,0,0)}, "Thigh_R": {"rot": (-78,0,0)}, "Shin_R": {"rot": (65,0,0)}, "UpperArm_L": {"rot": (35,0,-8)}, "Forearm_L": {"rot": (25,0,0)}, "UpperArm_R": {"rot": (35,0,8)}, "Forearm_R": {"rot": (25,0,0)}}),
            (24, {"Hips": {"loc": (0,-0.016,-0.008), "rot": (2,0,0)}, "Thigh_L": {"rot": (-75,0,0)}, "Shin_L": {"rot": (60,0,0)}, "Thigh_R": {"rot": (-75,0,0)}, "Shin_R": {"rot": (60,0,0)}, "UpperArm_L": {"rot": (30,0,-6)}, "Forearm_L": {"rot": (22,0,0)}, "UpperArm_R": {"rot": (30,0,6)}, "Forearm_R": {"rot": (22,0,0)}}),
        ]),
        ("Seated_Idle", 48, [
            (1,  {"Hips": {"loc": (0,-0.016,-0.008)}, "Thigh_L": {"rot": (-75,0,0)}, "Shin_L": {"rot": (60,0,0)}, "Thigh_R": {"rot": (-75,0,0)}, "Shin_R": {"rot": (60,0,0)}, "UpperArm_L": {"rot": (30,0,-6)}, "Forearm_L": {"rot": (22,0,0)}, "UpperArm_R": {"rot": (30,0,6)}, "Forearm_R": {"rot": (22,0,0)}}),
            (24, {"Hips": {"loc": (0,-0.014,-0.008)}, "Chest": {"rot": (-4,0,3)}, "Head": {"rot": (5,4,-3)}, "Thigh_L": {"rot": (-75,0,0)}, "Shin_L": {"rot": (48,0,0)}, "Thigh_R": {"rot": (-75,0,0)}, "Shin_R": {"rot": (68,0,0)}, "UpperArm_L": {"rot": (34,0,-6)}, "Forearm_L": {"rot": (26,0,0)}, "UpperArm_R": {"rot": (28,0,6)}, "Forearm_R": {"rot": (20,0,0)}}),
            (48, {"Hips": {"loc": (0,-0.016,-0.008)}, "Thigh_L": {"rot": (-75,0,0)}, "Shin_L": {"rot": (60,0,0)}, "Thigh_R": {"rot": (-75,0,0)}, "Shin_R": {"rot": (60,0,0)}, "UpperArm_L": {"rot": (30,0,-6)}, "Forearm_L": {"rot": (22,0,0)}, "UpperArm_R": {"rot": (30,0,6)}, "Forearm_R": {"rot": (22,0,0)}}),
        ]),
        ("Stand_Up", 24, [
            (1,  {"Hips": {"loc": (0,-0.016,-0.008)}, "Thigh_L": {"rot": (-75,0,0)}, "Shin_L": {"rot": (60,0,0)}, "Thigh_R": {"rot": (-75,0,0)}, "Shin_R": {"rot": (60,0,0)}, "UpperArm_L": {"rot": (30,0,-6)}, "Forearm_L": {"rot": (22,0,0)}, "UpperArm_R": {"rot": (30,0,6)}, "Forearm_R": {"rot": (22,0,0)}}),
            (10, {"Hips": {"loc": (0,-0.006,0.004), "rot": (-16,0,0)}, "Chest": {"rot": (-14,0,0)}, "Head": {"rot": (12,0,0)}, "Thigh_L": {"rot": (-35,0,0)}, "Thigh_R": {"rot": (-35,0,0)}}),
            (24, {}),
        ]),

        # --- B. COMMUNICATION (10) ---
        ("Talking", 36, [
            (1,  {}),
            (9,  {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (-4,0,-5)}, "Head": {"rot": (-6,6,-8)}, "UpperArm_L": {"rot": (38,0,-18)}, "Forearm_L": {"rot": (32,0,10)}, "UpperArm_R": {"rot": (22,0,12)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (18, {"Hips": {"loc": (0,0.001,0)}, "Chest": {"rot": (-2,0,5)}, "Head": {"rot": (5,-5,8)}, "UpperArm_L": {"rot": (18,0,-12)}, "UpperArm_R": {"rot": (42,0,18)}, "Forearm_R": {"rot": (35,0,-10)}}),
            (27, {"Hips": {"loc": (0,0.004,0)}, "Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-8,4,0)}, "UpperArm_L": {"rot": (35,0,-24)}, "UpperArm_R": {"rot": (35,0,24)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}}),
            (36, {}),
        ]),
        ("Listening", 48, [
            (1,  {}),
            (16, {"Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-5,10,-6)}, "UpperArm_L": {"rot": (14,0,8)}, "UpperArm_R": {"rot": (14,0,-8)}, "Accessory_Root": {"rot": (10,8,0)}}),
            (28, {"Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-12,8,-4)}, "UpperArm_L": {"rot": (14,0,8)}, "UpperArm_R": {"rot": (14,0,-8)}}),
            (36, {"Chest": {"rot": (-5,0,0)}, "Head": {"rot": (-3,10,-6)}, "UpperArm_L": {"rot": (14,0,8)}, "UpperArm_R": {"rot": (14,0,-8)}}),
            (48, {}),
        ]),
        ("Thinking", 60, [
            (1,  {}),
            (15, {"Chest": {"rot": (-4,4,-5)}, "Head": {"rot": (10,-8,10)}, "UpperArm_R": {"rot": (58,0,-15)}, "Forearm_R": {"rot": (48,0,-22)}, "UpperArm_L": {"rot": (26,0,18)}, "Forearm_L": {"rot": (30,0,25)}, "Brow_L": {"rot": (0,12,0)}, "Brow_R": {"rot": (0,8,0)}, "Accessory_Tip": {"rot": (15,-12,0)}}),
            (35, {"Chest": {"rot": (-4,4,-5)}, "Head": {"rot": (14,6,-8)}, "UpperArm_R": {"rot": (62,0,-15)}, "Forearm_R": {"rot": (52,0,-22)}, "UpperArm_L": {"rot": (26,0,18)}, "Forearm_L": {"rot": (30,0,25)}, "Brow_L": {"rot": (0,14,0)}, "Brow_R": {"rot": (0,10,0)}, "Accessory_Tip": {"rot": (-12,14,0)}}),
            (60, {}),
        ]),
        ("Confused", 48, [
            (1,  {}),
            (14, {"Hips": {"loc": (0,-0.002,0)}, "Chest": {"rot": (4,-6,0)}, "Head": {"rot": (4,18,-10)}, "UpperArm_L": {"rot": (28,0,-35)}, "Forearm_L": {"rot": (35,0,-15)}, "UpperArm_R": {"rot": (28,0,35)}, "Forearm_R": {"rot": (35,0,15)}, "Brow_L": {"loc": (0,0.003,0), "rot": (0,-15,0)}, "Brow_R": {"loc": (0,-0.002,0), "rot": (0,15,0)}}),
            (32, {"Hips": {"loc": (0,-0.002,0)}, "Chest": {"rot": (4,6,0)}, "Head": {"rot": (4,-18,10)}, "UpperArm_L": {"rot": (32,0,-38)}, "Forearm_L": {"rot": (35,0,-15)}, "UpperArm_R": {"rot": (32,0,38)}, "Forearm_R": {"rot": (35,0,15)}, "Brow_L": {"loc": (0,-0.002,0), "rot": (0,-15,0)}, "Brow_R": {"loc": (0,0.003,0), "rot": (0,15,0)}}),
            (48, {}),
        ]),
        ("Excited", 32, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0,0.012,0)}, "Chest": {"rot": (6,0,0)}, "Head": {"rot": (8,5,0)}, "UpperArm_L": {"rot": (75,0,-28)}, "UpperArm_R": {"rot": (75,0,28)}, "Eye_L": {"scale": (1.15,1,1.18)}, "Eye_R": {"scale": (1.15,1,1.18)}, "Accessory_Root": {"rot": (-18,0,0)}}),
            (16, {"Hips": {"loc": (0,-0.003,0)}, "Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-4,-5,0)}, "UpperArm_L": {"rot": (45,0,-20)}, "UpperArm_R": {"rot": (45,0,20)}, "Accessory_Root": {"rot": (16,0,0)}}),
            (24, {"Hips": {"loc": (0,0.012,0)}, "Chest": {"rot": (6,0,0)}, "Head": {"rot": (8,-5,0)}, "UpperArm_L": {"rot": (78,0,-32)}, "UpperArm_R": {"rot": (78,0,32)}, "Eye_L": {"scale": (1.15,1,1.18)}, "Eye_R": {"scale": (1.15,1,1.18)}}),
            (32, {}),
        ]),
        ("Worried", 40, [
            (1,  {}),
            (12, {"Hips": {"loc": (0,-0.005,0)}, "Chest": {"rot": (-10,0,3)}, "Head": {"rot": (-8,4,5)}, "UpperArm_L": {"rot": (42,0,22)}, "Forearm_L": {"rot": (38,0,20)}, "UpperArm_R": {"rot": (42,0,-22)}, "Forearm_R": {"rot": (38,0,-20)}, "Brow_L": {"rot": (0,-18,0)}, "Brow_R": {"rot": (0,18,0)}}),
            (20, {"Hips": {"loc": (0,-0.005,0)}, "Chest": {"rot": (-10,0,-3)}, "Head": {"rot": (-8,-4,-5)}, "UpperArm_L": {"rot": (44,0,22)}, "Forearm_L": {"rot": (40,0,20)}, "UpperArm_R": {"rot": (44,0,-22)}, "Forearm_R": {"rot": (40,0,-20)}, "Brow_L": {"rot": (0,-18,0)}, "Brow_R": {"rot": (0,18,0)}}),
            (28, {"Hips": {"loc": (0,-0.005,0)}, "Chest": {"rot": (-10,0,3)}, "Head": {"rot": (-8,4,5)}, "UpperArm_L": {"rot": (42,0,22)}, "Forearm_L": {"rot": (38,0,20)}, "UpperArm_R": {"rot": (42,0,-22)}, "Forearm_R": {"rot": (38,0,-20)}, "Brow_L": {"rot": (0,-18,0)}, "Brow_R": {"rot": (0,18,0)}}),
            (40, {}),
        ]),
        ("Pointing", 36, [
            (1,  {}),
            (12, {"Chest": {"rot": (-4,0,12)}, "Head": {"rot": (6,0,14)}, "UpperArm_R": {"rot": (78,0,10)}, "Forearm_R": {"rot": (12,0,0)}, "Hand_R": {"rot": (-10,0,0)}, "UpperArm_L": {"rot": (12,0,-10)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (26, {"Chest": {"rot": (-4,0,12)}, "Head": {"rot": (8,4,14)}, "UpperArm_R": {"rot": (82,0,12)}, "Forearm_R": {"rot": (10,0,0)}, "Hand_R": {"rot": (-12,0,0)}, "UpperArm_L": {"rot": (12,0,-10)}}),
            (36, {}),
        ]),
        ("Waving", 36, [
            (1,  {}),
            (8,  {"Chest": {"rot": (0,-4,0)}, "Head": {"rot": (5,-6,0)}, "UpperArm_R": {"rot": (105,0,35)}, "Forearm_R": {"rot": (20,0,25)}}),
            (16, {"Chest": {"rot": (0,-6,0)}, "Head": {"rot": (5,-8,0)}, "UpperArm_R": {"rot": (105,0,10)}, "Forearm_R": {"rot": (20,0,-22)}}),
            (24, {"Chest": {"rot": (0,-4,0)}, "Head": {"rot": (5,-6,0)}, "UpperArm_R": {"rot": (105,0,35)}, "Forearm_R": {"rot": (20,0,25)}}),
            (36, {}),
        ]),
        ("Nodding", 28, [
            (1,  {}),
            (7,  {"Chest": {"rot": (-5,0,0)}, "Head": {"rot": (-16,0,0)}, "Accessory_Root": {"rot": (-18,0,0)}}),
            (14, {"Chest": {"rot": (2,0,0)}, "Head": {"rot": (6,0,0)}, "Accessory_Root": {"rot": (12,0,0)}}),
            (21, {"Chest": {"rot": (-5,0,0)}, "Head": {"rot": (-16,0,0)}, "Accessory_Root": {"rot": (-18,0,0)}}),
            (28, {}),
        ]),
        ("Shaking_Head", 28, [
            (1,  {}),
            (7,  {"Head": {"rot": (0,4,-22)}, "Accessory_Tip": {"rot": (0,0,18)}}),
            (14, {"Head": {"rot": (0,-4,22)}, "Accessory_Tip": {"rot": (0,0,-18)}}),
            (21, {"Head": {"rot": (0,4,-18)}, "Accessory_Tip": {"rot": (0,0,15)}}),
            (28, {}),
        ]),
        ("React_Approve", 30, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,-0.009,0)}, "Thigh_L": {"rot": (-14,0,0)}, "Shin_L": {"rot": (28,0,0)}, "Foot_L": {"rot": (-14,0,0)}, "Thigh_R": {"rot": (-14,0,0)}, "Shin_R": {"rot": (28,0,0)}, "Foot_R": {"rot": (-14,0,0)}, "Chest": {"rot": (10,0,0)}, "Head": {"rot": (12,0,0)}, "UpperArm_R": {"rot": (-10,0,14)}, "Forearm_R": {"rot": (36,0,0)}, "UpperArm_L": {"rot": (-8,0,-12)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (12, {"Hips": {"loc": (0,0.024,0), "rot": (-4,6,0)}, "Thigh_L": {"rot": (8,0,0)}, "Shin_L": {"rot": (6,0,0)}, "Foot_L": {"rot": (16,0,0)}, "Thigh_R": {"rot": (8,0,0)}, "Shin_R": {"rot": (6,0,0)}, "Foot_R": {"rot": (16,0,0)}, "Chest": {"rot": (-10,8,0)}, "Head": {"rot": (-12,8,-5)}, "UpperArm_R": {"rot": (128,0,-28)}, "Forearm_R": {"rot": (16,0,-8)}, "UpperArm_L": {"rot": (38,0,-22)}, "Forearm_L": {"rot": (22,0,-12)}, "Brow_L": {"loc": (0,0.004,0)}, "Brow_R": {"loc": (0,0.004,0)}, "Eye_L": {"scale": (1.08,1,0.72)}, "Eye_R": {"scale": (1.08,1,0.72)}, "Accessory_Root": {"rot": (-16,6,0)}}),
            (19, {"Hips": {"loc": (0,-0.005,0)}, "Thigh_L": {"rot": (-10,0,0)}, "Shin_L": {"rot": (20,0,0)}, "Foot_L": {"rot": (-10,0,0)}, "Thigh_R": {"rot": (-10,0,0)}, "Shin_R": {"rot": (20,0,0)}, "Foot_R": {"rot": (-10,0,0)}, "Chest": {"rot": (10,0,0)}, "Head": {"rot": (16,0,0)}, "UpperArm_R": {"rot": (92,0,-18)}, "Forearm_R": {"rot": (34,0,-8)}, "UpperArm_L": {"rot": (16,0,-14)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}, "Accessory_Root": {"rot": (14,0,0)}}),
            (24, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (-3,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_R": {"rot": (36,0,14)}, "Forearm_R": {"rot": (16,0,0)}, "UpperArm_L": {"rot": (8,0,-8)}}),
            (30, {}),
        ]),
        ("React_Reject", 30, [
            (1,  {}),
            (6,  {"Hips": {"loc": (0,-0.007,0)}, "Thigh_L": {"rot": (-8,0,0)}, "Shin_L": {"rot": (16,0,0)}, "Foot_L": {"rot": (-8,0,0)}, "Thigh_R": {"rot": (-8,0,0)}, "Shin_R": {"rot": (16,0,0)}, "Foot_R": {"rot": (-8,0,0)}, "Chest": {"rot": (16,0,0)}, "Head": {"rot": (18,6,-4)}, "UpperArm_L": {"rot": (-8,0,-16)}, "Forearm_L": {"rot": (18,0,-14)}, "UpperArm_R": {"rot": (-8,0,16)}, "Forearm_R": {"rot": (18,0,14)}, "Brow_L": {"loc": (0,-0.002,0), "rot": (0,-14,0)}, "Brow_R": {"loc": (0,-0.002,0), "rot": (0,14,0)}, "Eye_L": {"scale": (1,1,0.78)}, "Eye_R": {"scale": (1,1,0.78)}, "Accessory_Root": {"rot": (16,0,0)}}),
            (14, {"Hips": {"loc": (0,-0.005,0)}, "Chest": {"rot": (14,0,0)}, "Head": {"rot": (15,-14,4)}, "UpperArm_L": {"rot": (-10,0,-12)}, "Forearm_L": {"rot": (14,0,-8)}, "UpperArm_R": {"rot": (-10,0,12)}, "Forearm_R": {"rot": (14,0,8)}, "Brow_L": {"loc": (0,-0.002,0), "rot": (0,-12,0)}, "Brow_R": {"loc": (0,-0.002,0), "rot": (0,12,0)}, "Accessory_Tip": {"rot": (10,0,-8)}}),
            (21, {"Hips": {"loc": (0,-0.002,0)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (10,12,-4)}, "UpperArm_L": {"rot": (-4,0,-8)}, "Forearm_L": {"rot": (8,0,0)}, "UpperArm_R": {"rot": (-4,0,8)}, "Forearm_R": {"rot": (8,0,0)}, "Accessory_Tip": {"rot": (-6,0,6)}}),
            (30, {}),
        ]),

        # --- C. WORKING (6) ---
        ("Typing", 24, [
            (1,  {"Chest": {"rot": (-8,0,0)}, "Head": {"rot": (-10,0,-5)}, "UpperArm_L": {"rot": (46,0,10)}, "Forearm_L": {"rot": (32,0,8)}, "Hand_L": {"rot": (-18,0,0)}, "UpperArm_R": {"rot": (38,0,-10)}, "Forearm_R": {"rot": (22,0,-8)}, "Hand_R": {"rot": (12,0,0)}}),
            (6,  {"Chest": {"rot": (-9,0,0)}, "Head": {"rot": (-10,2,0)}, "UpperArm_L": {"rot": (38,0,10)}, "Forearm_L": {"rot": (22,0,8)}, "Hand_L": {"rot": (12,0,0)}, "UpperArm_R": {"rot": (46,0,-10)}, "Forearm_R": {"rot": (32,0,-8)}, "Hand_R": {"rot": (-18,0,0)}}),
            (12, {"Chest": {"rot": (-8,0,0)}, "Head": {"rot": (-11,0,5)}, "UpperArm_L": {"rot": (48,0,12)}, "Forearm_L": {"rot": (34,0,8)}, "Hand_L": {"rot": (-20,0,0)}, "UpperArm_R": {"rot": (36,0,-12)}, "Forearm_R": {"rot": (20,0,-8)}, "Hand_R": {"rot": (14,0,0)}}),
            (18, {"Chest": {"rot": (-9,0,0)}, "Head": {"rot": (-10,-2,0)}, "UpperArm_L": {"rot": (36,0,10)}, "Forearm_L": {"rot": (20,0,8)}, "Hand_L": {"rot": (14,0,0)}, "UpperArm_R": {"rot": (48,0,-10)}, "Forearm_R": {"rot": (34,0,-8)}, "Hand_R": {"rot": (-20,0,0)}}),
            (24, {"Chest": {"rot": (-8,0,0)}, "Head": {"rot": (-10,0,-5)}, "UpperArm_L": {"rot": (46,0,10)}, "Forearm_L": {"rot": (32,0,8)}, "Hand_L": {"rot": (-18,0,0)}, "UpperArm_R": {"rot": (38,0,-10)}, "Forearm_R": {"rot": (22,0,-8)}, "Hand_R": {"rot": (12,0,0)}}),
        ]),
        ("Reading", 48, [
            (1,  {"Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-14,0,-10)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (36,0,14)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (36,0,-14)}}),
            (24, {"Chest": {"rot": (-7,0,0)}, "Head": {"rot": (-14,3,10)}, "UpperArm_L": {"rot": (50,0,16)}, "Forearm_L": {"rot": (38,0,14)}, "UpperArm_R": {"rot": (50,0,-16)}, "Forearm_R": {"rot": (38,0,-14)}, "Accessory_Root": {"rot": (8,0,0)}}),
            (48, {"Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-14,0,-10)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (36,0,14)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (36,0,-14)}}),
        ]),
        ("Writing", 36, [
            (1,  {"Chest": {"rot": (-10,0,-4)}, "Head": {"rot": (-14,-4,-6)}, "UpperArm_L": {"rot": (42,0,14)}, "Forearm_L": {"rot": (30,0,12)}, "UpperArm_R": {"rot": (48,0,-8)}, "Forearm_R": {"rot": (36,0,-18)}}),
            (12, {"Chest": {"rot": (-10,0,-4)}, "Head": {"rot": (-15,2,4)}, "UpperArm_L": {"rot": (42,0,14)}, "Forearm_L": {"rot": (30,0,12)}, "UpperArm_R": {"rot": (52,0,-22)}, "Forearm_R": {"rot": (40,0,6)}}),
            (24, {"Chest": {"rot": (-10,0,-4)}, "Head": {"rot": (-14,-2,-2)}, "UpperArm_L": {"rot": (42,0,14)}, "Forearm_L": {"rot": (30,0,12)}, "UpperArm_R": {"rot": (46,0,-12)}, "Forearm_R": {"rot": (34,0,-12)}}),
            (36, {"Chest": {"rot": (-10,0,-4)}, "Head": {"rot": (-14,-4,-6)}, "UpperArm_L": {"rot": (42,0,14)}, "Forearm_L": {"rot": (30,0,12)}, "UpperArm_R": {"rot": (48,0,-8)}, "Forearm_R": {"rot": (36,0,-18)}}),
        ]),
        ("Inspecting", 48, [
            (1,  {}),
            (16, {"Chest": {"rot": (-14,0,6)}, "Head": {"rot": (-10,8,8)}, "UpperArm_R": {"rot": (68,0,-12)}, "Forearm_R": {"rot": (45,0,-18)}, "Eye_L": {"scale": (1,1,0.65)}, "Eye_R": {"scale": (1.1,1,1.1)}, "Accessory_Root": {"rot": (-25,0,0)}, "Accessory_Tip": {"rot": (-20,0,0)}}),
            (32, {"Chest": {"rot": (-14,0,-6)}, "Head": {"rot": (-10,-8,-8)}, "UpperArm_R": {"rot": (70,0,-6)}, "Forearm_R": {"rot": (45,0,-10)}, "Eye_L": {"scale": (1,1,0.65)}, "Eye_R": {"scale": (1.1,1,1.1)}, "Accessory_Root": {"rot": (-25,0,0)}, "Accessory_Tip": {"rot": (-20,0,0)}}),
            (48, {}),
        ]),
        ("Using_Workstation", 48, [
            (1,  {"Chest": {"rot": (-6,0,-10)}, "Head": {"rot": (-6,0,-14)}, "UpperArm_L": {"rot": (55,0,-10)}, "Forearm_L": {"rot": (25,0,0)}, "UpperArm_R": {"rot": (30,0,8)}}),
            (24, {"Chest": {"rot": (-6,0,10)}, "Head": {"rot": (-6,0,14)}, "UpperArm_L": {"rot": (30,0,-8)}, "UpperArm_R": {"rot": (55,0,10)}, "Forearm_R": {"rot": (25,0,0)}}),
            (48, {"Chest": {"rot": (-6,0,-10)}, "Head": {"rot": (-6,0,-14)}, "UpperArm_L": {"rot": (55,0,-10)}, "Forearm_L": {"rot": (25,0,0)}, "UpperArm_R": {"rot": (30,0,8)}}),
        ]),
        ("Waiting_For_Input", 48, [
            (1,  {"Head": {"rot": (8,0,0)}, "UpperArm_L": {"rot": (28,0,20)}, "Forearm_L": {"rot": (28,0,18)}, "UpperArm_R": {"rot": (28,0,-20)}, "Forearm_R": {"rot": (28,0,-18)}}),
            (24, {"Hips": {"loc": (0,0.003,0)}, "Head": {"rot": (12,8,0)}, "UpperArm_L": {"rot": (30,0,20)}, "Forearm_L": {"rot": (30,0,18)}, "UpperArm_R": {"rot": (30,0,-20)}, "Forearm_R": {"rot": (30,0,-18)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (48, {"Head": {"rot": (8,0,0)}, "UpperArm_L": {"rot": (28,0,20)}, "Forearm_L": {"rot": (28,0,18)}, "UpperArm_R": {"rot": (28,0,-20)}, "Forearm_R": {"rot": (28,0,-18)}}),
        ]),

        # --- D. OBJECT INTERACTIONS & HANDOFFS (8) ---
        ("Pick_Up_Document", 24, [
            (1,  {}),
            (10, {"Hips": {"loc": (0,-0.008,0)}, "Chest": {"rot": (-22,0,0)}, "Head": {"rot": (-16,0,0)}, "UpperArm_L": {"rot": (58,0,12)}, "Forearm_L": {"rot": (20,0,10)}, "UpperArm_R": {"rot": (58,0,-12)}, "Forearm_R": {"rot": (20,0,-10)}}),
            (24, {"Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
        ]),
        ("Hold_Document", 40, [
            (1,  {"Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
            (20, {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (-2,0,0)}, "Head": {"rot": (-3,4,0)}, "UpperArm_L": {"rot": (50,0,16)}, "Forearm_L": {"rot": (37,0,12)}, "UpperArm_R": {"rot": (50,0,-16)}, "Forearm_R": {"rot": (37,0,-12)}}),
            (40, {"Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
        ]),
        ("Offer_Document_Present", 20, [
            (1,  {"Chest": {"rot": (-3,0,0)}, "Head": {"rot": (0,0,0)}, "UpperArm_L": {"rot": (38,0,14)}, "Forearm_L": {"rot": (32,0,10)}, "UpperArm_R": {"rot": (38,0,-14)}, "Forearm_R": {"rot": (32,0,-10)}, "Brow_L": {"loc": (0,0,0)}, "Brow_R": {"loc": (0,0,0)}}),
            (5,  {"Hips": {"loc": (0,-0.004,0)}, "Thigh_L": {"rot": (-10,0,0)}, "Shin_L": {"rot": (20,0,0)}, "Thigh_R": {"rot": (-10,0,0)}, "Shin_R": {"rot": (20,0,0)}, "Chest": {"rot": (2,0,0)}, "Head": {"rot": (-4,0,0)}, "UpperArm_L": {"rot": (32,0,14)}, "Forearm_L": {"rot": (42,0,10)}, "UpperArm_R": {"rot": (32,0,-14)}, "Forearm_R": {"rot": (42,0,-10)}, "Accessory_Root": {"rot": (-6,0,0)}}),
            (13, {"Hips": {"loc": (0,0.005,0)}, "Thigh_L": {"rot": (0,0,0)}, "Shin_L": {"rot": (0,0,0)}, "Thigh_R": {"rot": (0,0,0)}, "Shin_R": {"rot": (0,0,0)}, "Chest": {"rot": (-10,0,0)}, "Head": {"rot": (14,0,-3)}, "UpperArm_L": {"rot": (76,0,10)}, "Forearm_L": {"rot": (16,0,6)}, "UpperArm_R": {"rot": (76,0,-10)}, "Forearm_R": {"rot": (16,0,-6)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}, "Accessory_Root": {"rot": (12,0,0)}}),
            (20, {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (-6,0,0)}, "Head": {"rot": (10,0,0)}, "UpperArm_L": {"rot": (68,0,12)}, "Forearm_L": {"rot": (22,0,8)}, "UpperArm_R": {"rot": (68,0,-12)}, "Forearm_R": {"rot": (22,0,-8)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (0,0,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
        ]),
        ("Offer_Document", 48, [
            (1,  {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (-6,0,0)}, "Head": {"rot": (10,0,0)}, "UpperArm_L": {"rot": (68,0,12)}, "Forearm_L": {"rot": (22,0,8)}, "UpperArm_R": {"rot": (68,0,-12)}, "Forearm_R": {"rot": (22,0,-8)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (0,0,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (14, {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (-7,0,2)}, "Head": {"rot": (12,6,-5)}, "UpperArm_L": {"rot": (71,0,12)}, "Forearm_L": {"rot": (24,0,8)}, "UpperArm_R": {"rot": (71,0,-12)}, "Forearm_R": {"rot": (24,0,-8)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}, "Accessory_Root": {"rot": (8,4,0)}}),
            (24, {"Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (27, {"Hips": {"loc": (0,0.001,0)}, "Eye_L": {"scale": (1,1,0.12)}, "Eye_R": {"scale": (1,1,0.12)}}),
            (30, {"Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
            (36, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (-5,0,-2)}, "Head": {"rot": (11,-5,4)}, "UpperArm_L": {"rot": (69,0,12)}, "Forearm_L": {"rot": (23,0,8)}, "UpperArm_R": {"rot": (69,0,-12)}, "Forearm_R": {"rot": (23,0,-8)}, "Accessory_Root": {"rot": (6,-4,0)}}),
            (48, {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (-6,0,0)}, "Head": {"rot": (10,0,0)}, "UpperArm_L": {"rot": (68,0,12)}, "Forearm_L": {"rot": (22,0,8)}, "UpperArm_R": {"rot": (68,0,-12)}, "Forearm_R": {"rot": (22,0,-8)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (0,0,0)}, "Eye_L": {"scale": (1,1,1)}, "Eye_R": {"scale": (1,1,1)}}),
        ]),
        ("Offer_Document_Release", 20, [
            (1,  {"Hips": {"loc": (0,0,0)}, "Chest": {"rot": (-6,0,0)}, "Head": {"rot": (10,0,0)}, "UpperArm_L": {"rot": (68,0,12)}, "Forearm_L": {"rot": (22,0,8)}, "UpperArm_R": {"rot": (68,0,-12)}, "Forearm_R": {"rot": (22,0,-8)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (0,0,0)}}),
            (6,  {"Hips": {"loc": (0,0.004,0)}, "Chest": {"rot": (4,0,0)}, "Head": {"rot": (12,4,0)}, "UpperArm_L": {"rot": (56,0,-6)}, "Forearm_L": {"rot": (18,0,-4)}, "UpperArm_R": {"rot": (56,0,6)}, "Forearm_R": {"rot": (18,0,4)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}, "Accessory_Root": {"rot": (-8,0,0)}}),
            (13, {"Hips": {"loc": (0,-0.002,0)}, "Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-10,0,0)}, "UpperArm_L": {"rot": (16,0,-10)}, "Forearm_L": {"rot": (12,0,0)}, "UpperArm_R": {"rot": (16,0,10)}, "Forearm_R": {"rot": (12,0,0)}, "Accessory_Root": {"rot": (8,0,0)}}),
            (20, {}),
        ]),
        ("Receive_Document", 28, [
            (1,  {}),
            (10, {"Chest": {"rot": (-8,0,0)}, "Head": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (65,0,-10)}, "Forearm_L": {"rot": (20,0,0)}, "UpperArm_R": {"rot": (65,0,10)}, "Forearm_R": {"rot": (20,0,0)}}),
            (20, {"Chest": {"rot": (-10,0,0)}, "Head": {"rot": (-12,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
            (28, {"Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-4,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
        ]),
        ("Put_Document_Down", 24, [
            (1,  {"Chest": {"rot": (-4,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_L": {"rot": (48,0,16)}, "Forearm_L": {"rot": (35,0,12)}, "UpperArm_R": {"rot": (48,0,-16)}, "Forearm_R": {"rot": (35,0,-12)}}),
            (12, {"Hips": {"loc": (0,-0.007,0)}, "Chest": {"rot": (-20,0,0)}, "Head": {"rot": (-15,0,0)}, "UpperArm_L": {"rot": (56,0,10)}, "Forearm_L": {"rot": (18,0,6)}, "UpperArm_R": {"rot": (56,0,-10)}, "Forearm_R": {"rot": (18,0,-6)}}),
            (24, {}),
        ]),
        ("Carry_Package", 24, [
            (1,  {"Hips": {"loc": (0,0.007,0)}, "Chest": {"rot": (-6,0,-4)}, "UpperArm_L": {"rot": (52,0,16)}, "Forearm_L": {"rot": (36,0,12)}, "UpperArm_R": {"rot": (52,0,-16)}, "Forearm_R": {"rot": (36,0,-12)}, "Thigh_L": {"rot": (-28,0,0)}, "Thigh_R": {"rot": (24,0,0)}}),
            (7,  {"Hips": {"loc": (0,0.013,0)}, "UpperArm_L": {"rot": (54,0,16)}, "Forearm_L": {"rot": (38,0,12)}, "UpperArm_R": {"rot": (54,0,-16)}, "Forearm_R": {"rot": (38,0,-12)}, "Thigh_L": {"rot": (-4,0,0)}, "Thigh_R": {"rot": (-12,0,0)}}),
            (13, {"Hips": {"loc": (0,0.007,0)}, "Chest": {"rot": (-6,0,4)}, "UpperArm_L": {"rot": (52,0,16)}, "Forearm_L": {"rot": (36,0,12)}, "UpperArm_R": {"rot": (52,0,-16)}, "Forearm_R": {"rot": (36,0,-12)}, "Thigh_L": {"rot": (24,0,0)}, "Thigh_R": {"rot": (-28,0,0)}}),
            (19, {"Hips": {"loc": (0,0.013,0)}, "UpperArm_L": {"rot": (54,0,16)}, "Forearm_L": {"rot": (38,0,12)}, "UpperArm_R": {"rot": (54,0,-16)}, "Forearm_R": {"rot": (38,0,-12)}, "Thigh_L": {"rot": (-12,0,0)}, "Thigh_R": {"rot": (-4,0,0)}}),
            (24, {"Hips": {"loc": (0,0.007,0)}, "Chest": {"rot": (-6,0,-4)}, "UpperArm_L": {"rot": (52,0,16)}, "Forearm_L": {"rot": (36,0,12)}, "UpperArm_R": {"rot": (52,0,-16)}, "Forearm_R": {"rot": (36,0,-12)}, "Thigh_L": {"rot": (-28,0,0)}, "Thigh_R": {"rot": (24,0,0)}}),
        ]),
        ("Exchange_Package", 36, [
            (1,  {"UpperArm_L": {"rot": (52,0,16)}, "Forearm_L": {"rot": (36,0,12)}, "UpperArm_R": {"rot": (52,0,-16)}, "Forearm_R": {"rot": (36,0,-12)}}),
            (14, {"Chest": {"rot": (-12,0,0)}, "Head": {"rot": (6,0,0)}, "UpperArm_L": {"rot": (72,0,10)}, "Forearm_L": {"rot": (16,0,6)}, "UpperArm_R": {"rot": (72,0,-10)}, "Forearm_R": {"rot": (16,0,-6)}}),
            (24, {"Chest": {"rot": (-6,0,0)}, "Head": {"rot": (-12,0,0)}, "UpperArm_L": {"rot": (35,0,-15)}, "UpperArm_R": {"rot": (35,0,15)}}),
            (36, {}),
        ]),
        ("Handoff_Give", 28, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,-0.003,0)}, "Chest": {"rot": (-3,-10,0)}, "Head": {"rot": (0,8,0)}, "UpperArm_R": {"rot": (26,0,6)}, "Forearm_R": {"rot": (46,0,-10)}, "UpperArm_L": {"rot": (16,0,-8)}, "Accessory_Root": {"rot": (-6,0,0)}}),
            (12, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (14,10,0)}, "Head": {"rot": (-4,-6,0)}, "UpperArm_R": {"rot": (92,0,-8)}, "Forearm_R": {"rot": (8,0,-2)}, "Hand_R": {"rot": (-8,0,0)}, "UpperArm_L": {"rot": (22,0,-14)}, "Forearm_L": {"rot": (16,0,0)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (10,0,0)}}),
            (15, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (14,10,0)}, "Head": {"rot": (-4,-6,0)}, "UpperArm_R": {"rot": (92,0,-8)}, "Forearm_R": {"rot": (8,0,-2)}, "Hand_R": {"rot": (-8,0,0)}, "UpperArm_L": {"rot": (22,0,-14)}, "Forearm_L": {"rot": (16,0,0)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}, "Accessory_Root": {"rot": (10,0,0)}}),
            (21, {"Hips": {"loc": (0,0.002,0)}, "Chest": {"rot": (8,4,0)}, "Head": {"rot": (12,0,0)}, "UpperArm_R": {"rot": (42,0,4)}, "Forearm_R": {"rot": (16,0,0)}, "UpperArm_L": {"rot": (10,0,-10)}, "Accessory_Root": {"rot": (-8,0,0)}}),
            (28, {}),
        ]),
        ("Handoff_Receive", 28, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,-0.003,0)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (-6,0,0)}, "UpperArm_R": {"rot": (36,0,0)}, "Forearm_R": {"rot": (30,0,0)}, "UpperArm_L": {"rot": (32,0,2)}, "Forearm_L": {"rot": (26,0,0)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (12, {"Hips": {"loc": (0,0.001,0)}, "Chest": {"rot": (12,-6,0)}, "Head": {"rot": (2,4,0)}, "UpperArm_R": {"rot": (78,0,-2)}, "Forearm_R": {"rot": (12,0,0)}, "UpperArm_L": {"rot": (74,0,4)}, "Forearm_L": {"rot": (14,0,0)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}}),
            (15, {"Hips": {"loc": (0,0.001,0)}, "Chest": {"rot": (12,-6,0)}, "Head": {"rot": (2,4,0)}, "UpperArm_R": {"rot": (78,0,-2)}, "Forearm_R": {"rot": (12,0,0)}, "UpperArm_L": {"rot": (74,0,4)}, "Forearm_L": {"rot": (14,0,0)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}}),
            (22, {"Hips": {"loc": (0,0.003,0)}, "Chest": {"rot": (6,0,0)}, "Head": {"rot": (14,0,0)}, "UpperArm_R": {"rot": (46,0,-10)}, "Forearm_R": {"rot": (40,0,-8)}, "UpperArm_L": {"rot": (46,0,10)}, "Forearm_L": {"rot": (40,0,8)}, "Eye_L": {"scale": (1.05,1,0.78)}, "Eye_R": {"scale": (1.05,1,0.78)}, "Accessory_Root": {"rot": (10,0,0)}}),
            (28, {}),
        ]),
        ("Celebrate_Handoff", 36, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0,-0.006,0)}, "Chest": {"rot": (-8,0,0)}}),
            (16, {"Hips": {"loc": (0,0.018,0)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (10,6,0)}, "UpperArm_L": {"rot": (85,0,-25)}, "UpperArm_R": {"rot": (110,0,25)}, "Thigh_L": {"rot": (-15,0,0)}, "Thigh_R": {"rot": (18,0,0)}, "Accessory_Root": {"rot": (-20,0,0)}}),
            (26, {"Hips": {"loc": (0,-0.003,0)}, "UpperArm_L": {"rot": (30,0,-15)}, "UpperArm_R": {"rot": (45,0,15)}}),
            (36, {}),
        ]),

        # --- E. RESULT REACTIONS (7) ---
        ("Success", 36, [
            (1,  {}),
            (7,  {"Hips": {"loc": (0,-0.008,0)}, "Chest": {"rot": (-12,0,0)}, "UpperArm_L": {"rot": (-18,0,-15)}, "UpperArm_R": {"rot": (-18,0,15)}}),
            (16, {"Hips": {"loc": (0,0.024,0)}, "Chest": {"rot": (12,0,0)}, "Head": {"rot": (14,0,0)}, "UpperArm_L": {"rot": (118,0,-32)}, "UpperArm_R": {"rot": (118,0,32)}, "Thigh_L": {"rot": (-20,0,-8)}, "Thigh_R": {"rot": (-20,0,8)}, "Brow_L": {"loc": (0,0.003,0)}, "Brow_R": {"loc": (0,0.003,0)}, "Accessory_Root": {"rot": (-22,0,0)}}),
            (26, {"Hips": {"loc": (0,-0.004,0)}, "Chest": {"rot": (-4,0,0)}, "UpperArm_L": {"rot": (65,0,-22)}, "UpperArm_R": {"rot": (65,0,22)}, "Accessory_Root": {"rot": (16,0,0)}}),
            (36, {}),
        ]),
        ("React_Completion_Cheer", 34, [
            (1,  {}),
            (5,  {"Hips": {"loc": (0,-0.008,0)}, "Thigh_L": {"rot": (-14,0,0)}, "Shin_L": {"rot": (28,0,0)}, "Foot_L": {"rot": (-14,0,0)}, "Thigh_R": {"rot": (-14,0,0)}, "Shin_R": {"rot": (28,0,0)}, "Foot_R": {"rot": (-14,0,0)}, "Chest": {"rot": (10,0,0)}, "Head": {"rot": (12,0,0)}, "UpperArm_L": {"rot": (18,0,-8)}, "Forearm_L": {"rot": (38,0,8)}, "UpperArm_R": {"rot": (18,0,8)}, "Forearm_R": {"rot": (38,0,-8)}}),
            (12, {"Hips": {"loc": (0,0.024,0), "rot": (-4,0,0)}, "Thigh_L": {"rot": (-8,0,-6)}, "Shin_L": {"rot": (10,0,0)}, "Foot_L": {"rot": (16,0,0)}, "Thigh_R": {"rot": (-8,0,6)}, "Shin_R": {"rot": (10,0,0)}, "Foot_R": {"rot": (16,0,0)}, "Chest": {"rot": (-10,0,0)}, "Head": {"rot": (-12,5,-3)}, "UpperArm_L": {"rot": (132,0,28)}, "Forearm_L": {"rot": (14,0,8)}, "UpperArm_R": {"rot": (132,0,-28)}, "Forearm_R": {"rot": (14,0,-8)}, "Brow_L": {"loc": (0,0.004,0)}, "Brow_R": {"loc": (0,0.004,0)}, "Eye_L": {"scale": (1.08,1,0.72)}, "Eye_R": {"scale": (1.08,1,0.72)}, "Accessory_Root": {"rot": (-16,0,0)}}),
            (18, {"Hips": {"loc": (0,-0.003,0)}, "Thigh_L": {"rot": (-8,0,0)}, "Shin_L": {"rot": (16,0,0)}, "Foot_L": {"rot": (-8,0,0)}, "Thigh_R": {"rot": (-8,0,0)}, "Shin_R": {"rot": (16,0,0)}, "Foot_R": {"rot": (-8,0,0)}, "Chest": {"rot": (-2,0,0)}, "Head": {"rot": (-4,-5,3)}, "UpperArm_L": {"rot": (104,0,22)}, "Forearm_L": {"rot": (26,0,8)}, "UpperArm_R": {"rot": (104,0,-22)}, "Forearm_R": {"rot": (26,0,-8)}, "Eye_L": {"scale": (1.08,1,0.72)}, "Eye_R": {"scale": (1.08,1,0.72)}, "Accessory_Root": {"rot": (8,0,0)}}),
            (24, {"Hips": {"loc": (0,0.008,0)}, "Chest": {"rot": (-8,0,0)}, "Head": {"rot": (-10,0,0)}, "UpperArm_L": {"rot": (126,0,26)}, "Forearm_L": {"rot": (14,0,6)}, "UpperArm_R": {"rot": (126,0,-26)}, "Forearm_R": {"rot": (14,0,-6)}, "Accessory_Root": {"rot": (-12,0,0)}}),
            (34, {}),
        ]),
        ("Failure", 40, [
            (1,  {}),
            (12, {"Hips": {"loc": (0,-0.006,0)}, "Chest": {"rot": (-16,0,0)}, "Head": {"rot": (-22,6,0)}, "UpperArm_L": {"rot": (10,0,-4)}, "UpperArm_R": {"rot": (10,0,4)}, "Brow_L": {"rot": (0,-18,0)}, "Brow_R": {"rot": (0,18,0)}, "Accessory_Root": {"rot": (-28,0,0)}, "Accessory_Tip": {"rot": (-25,0,0)}}),
            (26, {"Hips": {"loc": (0,-0.008,0)}, "Chest": {"rot": (-18,0,0)}, "Head": {"rot": (-24,-6,0)}, "UpperArm_L": {"rot": (12,0,-4)}, "UpperArm_R": {"rot": (12,0,4)}, "Brow_L": {"rot": (0,-18,0)}, "Brow_R": {"rot": (0,18,0)}, "Accessory_Root": {"rot": (-30,0,0)}, "Accessory_Tip": {"rot": (-28,0,0)}}),
            (40, {}),
        ]),
        ("Blocked", 40, [
            (1,  {"Chest": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (48,0,28)}, "Forearm_L": {"rot": (45,0,30)}, "UpperArm_R": {"rot": (48,0,-28)}, "Forearm_R": {"rot": (45,0,-30)}, "Brow_L": {"rot": (0,14,0)}, "Brow_R": {"rot": (0,-14,0)}}),
            (10, {"Chest": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (48,0,28)}, "Forearm_L": {"rot": (45,0,30)}, "UpperArm_R": {"rot": (48,0,-28)}, "Forearm_R": {"rot": (45,0,-30)}, "Foot_R": {"rot": (18,0,0)}}),
            (20, {"Chest": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (48,0,28)}, "Forearm_L": {"rot": (45,0,30)}, "UpperArm_R": {"rot": (48,0,-28)}, "Forearm_R": {"rot": (45,0,-30)}, "Foot_R": {"rot": (0,0,0)}}),
            (30, {"Chest": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (48,0,28)}, "Forearm_L": {"rot": (45,0,30)}, "UpperArm_R": {"rot": (48,0,-28)}, "Forearm_R": {"rot": (45,0,-30)}, "Foot_R": {"rot": (18,0,0)}}),
            (40, {"Chest": {"rot": (4,0,0)}, "UpperArm_L": {"rot": (48,0,28)}, "Forearm_L": {"rot": (45,0,30)}, "UpperArm_R": {"rot": (48,0,-28)}, "Forearm_R": {"rot": (45,0,-30)}, "Brow_L": {"rot": (0,14,0)}, "Brow_R": {"rot": (0,-14,0)}}),
        ]),
        ("Error", 32, [
            (1,  {}),
            (6,  {"Hips": {"loc": (0,0.008,0.008)}, "Chest": {"rot": (12,0,0)}, "Head": {"rot": (8,0,-14)}, "UpperArm_L": {"rot": (95,0,-20)}, "Forearm_L": {"rot": (45,0,25)}, "UpperArm_R": {"rot": (95,0,20)}, "Forearm_R": {"rot": (45,0,-25)}, "Eye_L": {"scale": (1.2,1,1.2)}, "Eye_R": {"scale": (1.2,1,1.2)}}),
            (12, {"Hips": {"loc": (0,0.002,0.006)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (8,0,14)}, "UpperArm_L": {"rot": (95,0,-20)}, "Forearm_L": {"rot": (45,0,25)}, "UpperArm_R": {"rot": (95,0,20)}, "Forearm_R": {"rot": (45,0,-25)}}),
            (18, {"Hips": {"loc": (0,0.002,0.006)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (8,0,-12)}, "UpperArm_L": {"rot": (95,0,-20)}, "Forearm_L": {"rot": (45,0,25)}, "UpperArm_R": {"rot": (95,0,20)}, "Forearm_R": {"rot": (45,0,-25)}}),
            (32, {}),
        ]),
        ("Task_Completed", 40, [
            (1,  {}),
            (12, {"Hips": {"loc": (0,0.005,0)}, "Chest": {"rot": (10,0,0)}, "Head": {"rot": (-10,0,0)}, "UpperArm_L": {"rot": (42,0,18)}, "Forearm_L": {"rot": (35,0,20)}, "UpperArm_R": {"rot": (42,0,-18)}, "Forearm_R": {"rot": (35,0,-20)}}),
            (22, {"Hips": {"loc": (0,0.005,0)}, "Chest": {"rot": (8,0,0)}, "Head": {"rot": (6,6,0)}, "UpperArm_L": {"rot": (38,0,-12)}, "UpperArm_R": {"rot": (38,0,12)}, "Brow_L": {"loc": (0,0.002,0)}, "Brow_R": {"loc": (0,0.002,0)}}),
            (40, {}),
        ]),
        ("Cheer", 36, [
            (1,  {"Hips": {"loc": (0,0.015,0)}, "UpperArm_L": {"rot": (115,0,-28)}, "UpperArm_R": {"rot": (115,0,28)}, "Head": {"rot": (10,6,0)}}),
            (9,  {"Hips": {"loc": (0,-0.003,0)}, "UpperArm_L": {"rot": (75,0,-20)}, "UpperArm_R": {"rot": (75,0,20)}, "Head": {"rot": (2,-6,0)}}),
            (18, {"Hips": {"loc": (0,0.016,0)}, "UpperArm_L": {"rot": (120,0,-32)}, "UpperArm_R": {"rot": (120,0,32)}, "Head": {"rot": (12,-6,0)}}),
            (27, {"Hips": {"loc": (0,-0.003,0)}, "UpperArm_L": {"rot": (75,0,-20)}, "UpperArm_R": {"rot": (75,0,20)}, "Head": {"rot": (2,6,0)}}),
            (36, {"Hips": {"loc": (0,0.015,0)}, "UpperArm_L": {"rot": (115,0,-28)}, "UpperArm_R": {"rot": (115,0,28)}, "Head": {"rot": (10,6,0)}}),
        ]),

        # --- F. SHARED WALL HABITAT, ELEVATOR, SELECTION, DESK VISIT & PORTAL (16) ---
        ("Elevator_Queue_Idle", 48, [
            (1,  {}),
            (12, {"Chest": {"rot": (2, 4, 0)}, "Head": {"rot": (4, -10, 4)}, "UpperArm_R": {"rot": (12, 0, -6)}}),
            (24, {"Hips": {"loc": (0, -0.0015, 0)}, "Head": {"rot": (6, 8, -4)}, "Accessory_Root": {"rot": (-4, 0, 0)}}),
            (36, {"Head": {"rot": (-2, 4, 2)}}),
            (48, {}),
        ]),
        ("Elevator_Step_Board", 24, [
            (1,  {}),
            (7,  {"Hips": {"loc": (0, 0.004, 0)}, "Chest": {"rot": (6, 0, 0)}, "Thigh_L": {"rot": (-28, 0, 0)}, "Shin_L": {"rot": (22, 0, 0)}, "Thigh_R": {"rot": (22, 0, 0)}, "UpperArm_R": {"rot": (-14, 0, -10)}}),
            (15, {"Hips": {"loc": (0, 0.002, 0)}, "Head": {"rot": (-8, 0, 0)}, "Thigh_L": {"rot": (20, 0, 0)}, "Thigh_R": {"rot": (-26, 0, 0)}, "Shin_R": {"rot": (20, 0, 0)}}),
            (24, {}),
        ]),
        ("Elevator_Ride_Idle", 48, [
            (1,  {"UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (14, {"Hips": {"loc": (0, 0.002, 0)}, "Head": {"rot": (6, 12, 4)}, "UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (30, {"Hips": {"loc": (0, -0.002, 0)}, "Head": {"rot": (4, -12, -4)}, "UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
            (48, {"UpperArm_L": {"rot": (22, 0, 12)}, "Forearm_L": {"rot": (24, 0, 0)}}),
        ]),
        ("Elevator_Step_Exit", 24, [
            (1,  {"Head": {"rot": (4, 0, 0)}}),
            (8,  {"Hips": {"loc": (0, 0.005, 0)}, "Chest": {"rot": (8, 0, 0)}, "Thigh_R": {"rot": (-30, 0, 0)}, "Thigh_L": {"rot": (20, 0, 0)}, "UpperArm_L": {"rot": (16, 0, 12)}}),
            (16, {"Hips": {"loc": (0, 0.002, 0)}, "Thigh_L": {"rot": (-24, 0, 0)}, "Thigh_R": {"rot": (16, 0, 0)}}),
            (24, {}),
        ]),
        ("Select_Highlighted_Wave", 32, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0, 0.008, 0)}, "Head": {"rot": (8, -6, -6)}, "UpperArm_R": {"rot": (118, 0, -28)}, "Forearm_R": {"rot": (25, 0, 0)}}),
            (16, {"Head": {"rot": (10, 4, 4)}, "UpperArm_R": {"rot": (115, 0, -10)}, "Forearm_R": {"rot": (12, 0, 22)}}),
            (24, {"UpperArm_R": {"rot": (118, 0, -28)}, "Forearm_R": {"rot": (25, 0, 0)}}),
            (32, {}),
        ]),
        ("DeskVisit_Perch_Idle", 48, [
            (1,  {"Head": {"rot": (4, 0, 4)}}),
            (16, {"Hips": {"loc": (0, -0.002, 0)}, "Chest": {"rot": (4, 4, 0)}, "Head": {"rot": (6, 8, 6)}, "UpperArm_R": {"rot": (16, 0, -8)}, "Brow_L": {"loc": (0, 0.001, 0)}}),
            (32, {"Hips": {"loc": (0, 0.001, 0)}, "Chest": {"rot": (2, -4, 0)}, "Head": {"rot": (4, -8, -4)}, "UpperArm_L": {"rot": (14, 0, 8)}}),
            (48, {"Head": {"rot": (4, 0, 4)}}),
        ]),
        ("DeskVisit_Listening", 40, [
            (1,  {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (6, 0, 8)}}),
            (12, {"Chest": {"rot": (10, 0, 0)}, "Head": {"rot": (10, 6, 10)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (-8, 0, 0)}}),
            (26, {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (-2, 4, 10)}}),
            (40, {"Chest": {"rot": (8, 0, 0)}, "Head": {"rot": (6, 0, 8)}}),
        ]),
        ("DeskVisit_Explain", 48, [
            (1,  {}),
            (10, {"Chest": {"rot": (6, 8, 0)}, "Head": {"rot": (6, -6, 4)}, "UpperArm_R": {"rot": (52, 0, -24)}, "Forearm_R": {"rot": (48, 0, 0)}}),
            (22, {"Chest": {"rot": (8, -6, 0)}, "Head": {"rot": (8, 6, -4)}, "UpperArm_L": {"rot": (48, 0, 22)}, "Forearm_L": {"rot": (45, 0, 0)}, "UpperArm_R": {"rot": (32, 0, -14)}}),
            (35, {"Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (-4, 0, 0)}, "UpperArm_L": {"rot": (38, 0, 16)}, "UpperArm_R": {"rot": (42, 0, -16)}}),
            (48, {}),
        ]),
        ("Portal_Emerge_Step", 32, [
            (1,  {"Hips": {"loc": (0, -0.008, 0)}, "Chest": {"rot": (10, 0, 0)}, "UpperArm_L": {"rot": (18, 0, 16)}, "UpperArm_R": {"rot": (18, 0, -16)}}),
            (12, {"Hips": {"loc": (0, 0.010, 0)}, "Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (8, 0, 0)}, "Thigh_L": {"rot": (-26, 0, 0)}, "Thigh_R": {"rot": (18, 0, 0)}, "UpperArm_L": {"rot": (42, 0, 22)}, "UpperArm_R": {"rot": (65, 0, -24)}}),
            (22, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (-2, 0, 0)}, "Head": {"rot": (10, 6, 0)}, "Thigh_L": {"rot": (10, 0, 0)}, "Thigh_R": {"rot": (-14, 0, 0)}, "UpperArm_R": {"rot": (92, 0, -22)}}),
            (32, {}),
        ]),
        ("Portal_Converse_Loop", 48, [
            (1,  {}),
            (12, {"Chest": {"rot": (4, 5, 0)}, "Head": {"rot": (6, -4, 3)}, "UpperArm_R": {"rot": (36, 0, -14)}, "Forearm_R": {"rot": (42, 0, 0)}}),
            (24, {"Hips": {"loc": (0, -0.002, 0)}, "Head": {"rot": (4, 6, -3)}, "UpperArm_L": {"rot": (24, 0, 12)}, "Forearm_L": {"rot": (32, 0, 0)}}),
            (36, {"Chest": {"rot": (2, -4, 0)}, "Head": {"rot": (8, 0, 4)}, "Eye_L": {"scale": (1, 0.15, 1)}, "Eye_R": {"scale": (1, 0.15, 1)}}),
            (48, {}),
        ]),
        ("Portal_Return_Step", 28, [
            (1,  {}),
            (8,  {"Hips": {"rot": (0, 15, 0)}, "Chest": {"rot": (6, 12, 0)}, "Head": {"rot": (-6, 18, 0)}, "UpperArm_R": {"rot": (78, 0, -18)}, "Forearm_R": {"rot": (35, 0, 0)}}),
            (18, {"Hips": {"loc": (0, 0.004, 0), "rot": (0, 145, 0)}, "Chest": {"rot": (2, 10, 0)}, "Thigh_L": {"rot": (-24, 0, 0)}, "Thigh_R": {"rot": (22, 0, 0)}}),
            (28, {"Hips": {"loc": (0, 0.0, 0), "rot": (0, 180, 0)}}),
        ]),
        ("Manager_Lookout_Idle", 48, [
            (1,  {}),
            (14, {"Chest": {"rot": (4, -12, 0)}, "Head": {"rot": (-6, -16, 4)}, "UpperArm_L": {"rot": (28, 0, 14)}, "Forearm_L": {"rot": (46, 0, 0)}}),
            (28, {"Chest": {"rot": (2, 12, 0)}, "Head": {"rot": (-4, 16, -4)}, "UpperArm_R": {"rot": (22, 0, -10)}}),
            (48, {}),
        ]),
        ("Manager_Review_Plan", 48, [
            (1,  {"UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
            (16, {"Chest": {"rot": (8, 4, 0)}, "Head": {"rot": (-10, 4, 6)}, "UpperArm_L": {"rot": (45, 0, 16)}, "Forearm_L": {"rot": (56, 0, 12)}, "UpperArm_R": {"rot": (58, 0, -12)}, "Forearm_R": {"rot": (78, 0, 18)}}),
            (32, {"Chest": {"rot": (6, -4, 0)}, "Head": {"rot": (-6, -4, -4)}, "UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
            (48, {"UpperArm_L": {"rot": (42, 0, 16)}, "Forearm_L": {"rot": (52, 0, 12)}}),
        ]),
        ("Manager_Operate_Printer", 36, [
            (1,  {}),
            (8,  {"Chest": {"rot": (10, -28, 0)}, "Head": {"rot": (-8, -22, 0)}, "UpperArm_R": {"rot": (72, 0, -18)}, "Forearm_R": {"rot": (35, 0, 0)}}),
            (16, {"Chest": {"rot": (12, -28, 0)}, "Head": {"rot": (-10, -20, 0)}, "UpperArm_R": {"rot": (58, 0, -16)}, "Forearm_R": {"rot": (28, 0, 0)}}),
            (26, {"Chest": {"rot": (4, -10, 0)}, "Head": {"rot": (4, -8, 0)}, "UpperArm_R": {"rot": (62, 0, 8)}, "UpperArm_L": {"rot": (56, 0, -8)}}),
            (36, {}),
        ]),
        ("Manager_Delegate_Point", 36, [
            (1,  {}),
            (10, {"Chest": {"rot": (6, -18, 0)}, "Head": {"rot": (-6, -18, 4)}, "UpperArm_R": {"rot": (88, 0, -24)}, "Forearm_R": {"rot": (12, 0, 0)}}),
            (22, {"Chest": {"rot": (4, 18, 0)}, "Head": {"rot": (-6, 18, -4)}, "UpperArm_L": {"rot": (88, 0, 24)}, "Forearm_L": {"rot": (12, 0, 0)}}),
            (36, {}),
        ]),
        ("Manager_Receive_Report", 32, [
            (1,  {}),
            (10, {"Chest": {"rot": (8, 0, 0)}, "UpperArm_L": {"rot": (54, 0, 14)}, "Forearm_L": {"rot": (35, 0, 10)}, "UpperArm_R": {"rot": (54, 0, -14)}, "Forearm_R": {"rot": (35, 0, -10)}}),
            (20, {"Head": {"rot": (-12, 0, 0)}, "UpperArm_L": {"rot": (42, 0, 10)}, "UpperArm_R": {"rot": (42, 0, -10)}}),
            (32, {}),
        ]),

        # --- G. LIVING WORLD INTERACTION, REACTION & ROLE CLIPS (12) ---
        ("React_Glance", 22, [
            (1,  {}),
            (6,  {"Chest": {"rot": (2, 8, 0)}, "Head": {"rot": (4, 18, -5)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (4, 8, 0)}}),
            (14, {"Chest": {"rot": (2, 10, 0)}, "Head": {"rot": (5, 20, -4)}, "Eye_L": {"scale": (1.06, 1, 1.06)}, "Eye_R": {"scale": (1.06, 1, 1.06)}, "Accessory_Root": {"rot": (6, 10, 0)}}),
            (22, {}),
        ]),
        ("React_Poke_Surprise", 24, [
            (1,  {}),
            (4,  {"Hips": {"loc": (0, -0.005, 0)}, "Chest": {"rot": (12, 0, 0)}, "Head": {"rot": (14, -8, 6)}, "UpperArm_L": {"rot": (32, 0, -28)}, "Forearm_L": {"rot": (38, 0, -14)}, "UpperArm_R": {"rot": (32, 0, 28)}, "Forearm_R": {"rot": (38, 0, 14)}, "Eye_L": {"scale": (1.22, 1, 1.24)}, "Eye_R": {"scale": (1.22, 1, 1.24)}, "Brow_L": {"loc": (0, 0.004, 0)}, "Brow_R": {"loc": (0, 0.004, 0)}, "Accessory_Root": {"rot": (-18, 0, 0)}}),
            (10, {"Hips": {"loc": (0, 0.011, 0)}, "Chest": {"rot": (8, 6, 0)}, "Head": {"rot": (10, 10, -4)}, "UpperArm_L": {"rot": (48, 0, -24)}, "Forearm_L": {"rot": (42, 0, 0)}, "UpperArm_R": {"rot": (48, 0, 24)}, "Forearm_R": {"rot": (42, 0, 0)}, "Eye_L": {"scale": (1.16, 1, 1.18)}, "Eye_R": {"scale": (1.16, 1, 1.18)}, "Accessory_Root": {"rot": (14, 0, 0)}}),
            (17, {"Hips": {"loc": (0, -0.002, 0)}, "Chest": {"rot": (-3, 0, 0)}, "Head": {"rot": (-4, 4, 0)}, "UpperArm_L": {"rot": (16, 0, -10)}, "UpperArm_R": {"rot": (16, 0, 10)}}),
            (24, {}),
        ]),
        ("React_Wave_Greet", 32, [
            (1,  {}),
            (7,  {"Hips": {"loc": (0, 0.006, 0)}, "Chest": {"rot": (3, -6, 0)}, "Head": {"rot": (6, -8, -6)}, "UpperArm_R": {"rot": (108, 0, 32)}, "Forearm_R": {"rot": (18, 0, 22)}, "UpperArm_L": {"rot": (10, 0, -10)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (10, -6, 0)}}),
            (14, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (2, -4, 0)}, "Head": {"rot": (7, -6, 4)}, "UpperArm_R": {"rot": (108, 0, 12)}, "Forearm_R": {"rot": (18, 0, -22)}, "UpperArm_L": {"rot": (10, 0, -10)}, "Accessory_Root": {"rot": (8, 6, 0)}}),
            (21, {"Hips": {"loc": (0, 0.006, 0)}, "Chest": {"rot": (3, -6, 0)}, "Head": {"rot": (6, -8, -4)}, "UpperArm_R": {"rot": (108, 0, 32)}, "Forearm_R": {"rot": (18, 0, 22)}, "UpperArm_L": {"rot": (10, 0, -10)}, "Accessory_Root": {"rot": (10, -4, 0)}}),
            (32, {}),
        ]),
        ("React_Interrupt_Listen", 22, [
            (1,  {}),
            (8,  {"Hips": {"loc": (0, 0.003, 0)}, "Chest": {"rot": (4, 8, 0)}, "Head": {"rot": (8, 12, 6)}, "UpperArm_L": {"rot": (24, 0, 10)}, "Forearm_L": {"rot": (28, 0, 8)}, "UpperArm_R": {"rot": (24, 0, -10)}, "Forearm_R": {"rot": (28, 0, -8)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (-8, 6, 0)}}),
            (16, {"Chest": {"rot": (2, 6, 0)}, "Head": {"rot": (-2, 8, 6)}, "UpperArm_L": {"rot": (20, 0, 10)}, "Forearm_L": {"rot": (24, 0, 6)}, "UpperArm_R": {"rot": (20, 0, -10)}, "Forearm_R": {"rot": (24, 0, -6)}}),
            (22, {}),
        ]),
        ("React_Assign_Ack", 20, [
            (1,  {}),
            (6,  {"Hips": {"loc": (0, -0.004, 0)}, "Chest": {"rot": (-8, 0, 0)}, "Head": {"rot": (-14, 0, 0)}, "UpperArm_R": {"rot": (62, 0, -10)}, "Forearm_R": {"rot": (44, 0, -12)}, "UpperArm_L": {"rot": (14, 0, -8)}, "Accessory_Root": {"rot": (-14, 0, 0)}}),
            (13, {"Hips": {"loc": (0, 0.004, 0)}, "Chest": {"rot": (4, 0, 0)}, "Head": {"rot": (6, 4, 0)}, "UpperArm_R": {"rot": (46, 0, -6)}, "Forearm_R": {"rot": (30, 0, 0)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (8, 0, 0)}}),
            (20, {}),
        ]),
        ("Carry_Suspended_Idle", 40, [
            (1,  {"Hips": {"loc": (0, 0.002, 0), "rot": (4, 0, 0)}, "Chest": {"rot": (-3, 0, 0)}, "Head": {"rot": (4, 4, 3)}, "UpperArm_L": {"rot": (16, 0, -14)}, "Forearm_L": {"rot": (24, 0, 6)}, "UpperArm_R": {"rot": (16, 0, 14)}, "Forearm_R": {"rot": (24, 0, -6)}, "Thigh_L": {"rot": (-22, 0, -4)}, "Shin_L": {"rot": (28, 0, 0)}, "Foot_L": {"rot": (12, 0, 0)}, "Thigh_R": {"rot": (-16, 0, 4)}, "Shin_R": {"rot": (22, 0, 0)}, "Foot_R": {"rot": (10, 0, 0)}, "Accessory_Root": {"rot": (6, 3, 0)}}),
            (14, {"Hips": {"loc": (0, 0.005, 0), "rot": (2, 3, 0)}, "Chest": {"rot": (-2, -3, 2)}, "Head": {"rot": (6, 10, -4)}, "UpperArm_L": {"rot": (18, 0, -16)}, "Forearm_L": {"rot": (26, 0, 6)}, "UpperArm_R": {"rot": (14, 0, 14)}, "Forearm_R": {"rot": (22, 0, -6)}, "Thigh_L": {"rot": (-18, 0, -4)}, "Shin_L": {"rot": (24, 0, 0)}, "Foot_L": {"rot": (10, 0, 0)}, "Thigh_R": {"rot": (-22, 0, 4)}, "Shin_R": {"rot": (30, 0, 0)}, "Foot_R": {"rot": (14, 0, 0)}, "Accessory_Root": {"rot": (8, -4, 0)}}),
            (27, {"Hips": {"loc": (0, 0.000, 0), "rot": (5, -3, 0)}, "Chest": {"rot": (-4, 3, -2)}, "Head": {"rot": (3, -8, 5)}, "UpperArm_L": {"rot": (14, 0, -13)}, "Forearm_L": {"rot": (22, 0, 6)}, "UpperArm_R": {"rot": (18, 0, 16)}, "Forearm_R": {"rot": (26, 0, -6)}, "Thigh_L": {"rot": (-24, 0, -4)}, "Shin_L": {"rot": (30, 0, 0)}, "Foot_L": {"rot": (14, 0, 0)}, "Thigh_R": {"rot": (-14, 0, 4)}, "Shin_R": {"rot": (20, 0, 0)}, "Foot_R": {"rot": (8, 0, 0)}, "Eye_L": {"scale": (1, 1, 0.16)}, "Eye_R": {"scale": (1, 1, 0.16)}, "Accessory_Root": {"rot": (4, 5, 0)}}),
            (40, {"Hips": {"loc": (0, 0.002, 0), "rot": (4, 0, 0)}, "Chest": {"rot": (-3, 0, 0)}, "Head": {"rot": (4, 4, 3)}, "UpperArm_L": {"rot": (16, 0, -14)}, "Forearm_L": {"rot": (24, 0, 6)}, "UpperArm_R": {"rot": (16, 0, 14)}, "Forearm_R": {"rot": (24, 0, -6)}, "Thigh_L": {"rot": (-22, 0, -4)}, "Shin_L": {"rot": (28, 0, 0)}, "Foot_L": {"rot": (12, 0, 0)}, "Thigh_R": {"rot": (-16, 0, 4)}, "Shin_R": {"rot": (22, 0, 0)}, "Foot_R": {"rot": (10, 0, 0)}, "Accessory_Root": {"rot": (6, 3, 0)}}),
        ]),
        ("Researcher_Compare_Papers", 48, [
            (1,  {"Chest": {"rot": (-6, 0, 0)}, "Head": {"rot": (-12, 12, 4)}, "UpperArm_L": {"rot": (52, 0, 10)}, "Forearm_L": {"rot": (36, 0, 12)}, "UpperArm_R": {"rot": (46, 0, -16)}, "Forearm_R": {"rot": (30, 0, -10)}, "Accessory_Root": {"rot": (6, 4, 0)}}),
            (16, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (-6, 6, 0)}, "Head": {"rot": (-12, 16, 6)}, "UpperArm_L": {"rot": (56, 0, 8)}, "Forearm_L": {"rot": (40, 0, 12)}, "UpperArm_R": {"rot": (42, 0, -18)}, "Forearm_R": {"rot": (26, 0, -10)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (8, 6, 0)}}),
            (32, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (-6, -6, 0)}, "Head": {"rot": (-12, -16, -6)}, "UpperArm_L": {"rot": (42, 0, 18)}, "Forearm_L": {"rot": (26, 0, 10)}, "UpperArm_R": {"rot": (56, 0, -8)}, "Forearm_R": {"rot": (40, 0, -12)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (8, -6, 0)}}),
            (48, {"Chest": {"rot": (-6, 0, 0)}, "Head": {"rot": (-12, 12, 4)}, "UpperArm_L": {"rot": (52, 0, 10)}, "Forearm_L": {"rot": (36, 0, 12)}, "UpperArm_R": {"rot": (46, 0, -16)}, "Forearm_R": {"rot": (30, 0, -10)}, "Accessory_Root": {"rot": (6, 4, 0)}}),
        ]),
        ("Developer_Adjust_Keyboard", 36, [
            (1,  {}),
            (9,  {"Hips": {"loc": (0, -0.003, 0)}, "Chest": {"rot": (-10, 0, 0)}, "Head": {"rot": (-12, 0, 0)}, "UpperArm_L": {"rot": (52, 0, 6)}, "Forearm_L": {"rot": (34, 0, 6)}, "Hand_L": {"rot": (-10, 0, 0)}, "UpperArm_R": {"rot": (52, 0, -6)}, "Forearm_R": {"rot": (34, 0, -6)}, "Hand_R": {"rot": (-10, 0, 0)}}),
            (18, {"Hips": {"loc": (0, -0.003, 0)}, "Chest": {"rot": (-10, 4, 0)}, "Head": {"rot": (-12, 4, 4)}, "UpperArm_L": {"rot": (50, 0, 12)}, "Forearm_L": {"rot": (32, 0, 8)}, "Hand_L": {"rot": (-12, 0, 0)}, "UpperArm_R": {"rot": (54, 0, -2)}, "Forearm_R": {"rot": (36, 0, -4)}, "Hand_R": {"rot": (-14, 0, 0)}}),
            (27, {"Chest": {"rot": (-8, 0, 0)}, "Head": {"rot": (-8, -2, 0)}, "UpperArm_L": {"rot": (46, 0, 10)}, "Forearm_L": {"rot": (30, 0, 8)}, "UpperArm_R": {"rot": (46, 0, -10)}, "Forearm_R": {"rot": (30, 0, -8)}}),
            (36, {}),
        ]),
        ("Tester_Note_Take", 44, [
            (1,  {"Chest": {"rot": (-8, 4, 0)}, "Head": {"rot": (-12, 6, -4)}, "UpperArm_L": {"rot": (46, 0, 14)}, "Forearm_L": {"rot": (38, 0, 14)}, "UpperArm_R": {"rot": (50, 0, -8)}, "Forearm_R": {"rot": (38, 0, -16)}, "Hand_R": {"rot": (-12, 0, 0)}}),
            (11, {"Chest": {"rot": (-8, 4, 0)}, "Head": {"rot": (-14, 4, -4)}, "UpperArm_L": {"rot": (46, 0, 14)}, "Forearm_L": {"rot": (38, 0, 14)}, "UpperArm_R": {"rot": (54, 0, -18)}, "Forearm_R": {"rot": (42, 0, 4)}, "Hand_R": {"rot": (-16, 0, 0)}}),
            (22, {"Hips": {"loc": (0, 0.002, 0)}, "Chest": {"rot": (-4, 2, 0)}, "Head": {"rot": (4, -4, 3)}, "UpperArm_L": {"rot": (44, 0, 14)}, "Forearm_L": {"rot": (36, 0, 14)}, "UpperArm_R": {"rot": (48, 0, -10)}, "Forearm_R": {"rot": (34, 0, -14)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}}),
            (33, {"Chest": {"rot": (-8, 4, 0)}, "Head": {"rot": (-13, 5, -4)}, "UpperArm_L": {"rot": (46, 0, 14)}, "Forearm_L": {"rot": (38, 0, 14)}, "UpperArm_R": {"rot": (52, 0, -14)}, "Forearm_R": {"rot": (40, 0, -6)}, "Hand_R": {"rot": (-14, 0, 0)}}),
            (44, {"Chest": {"rot": (-8, 4, 0)}, "Head": {"rot": (-12, 6, -4)}, "UpperArm_L": {"rot": (46, 0, 14)}, "Forearm_L": {"rot": (38, 0, 14)}, "UpperArm_R": {"rot": (50, 0, -8)}, "Forearm_R": {"rot": (38, 0, -16)}, "Hand_R": {"rot": (-12, 0, 0)}}),
        ]),
        ("Manager_Welcome_User", 36, [
            (1,  {}),
            (10, {"Hips": {"loc": (0, 0.004, 0)}, "Chest": {"rot": (6, 0, 0)}, "Head": {"rot": (8, 0, -4)}, "UpperArm_L": {"rot": (62, 0, -24)}, "Forearm_L": {"rot": (22, 0, -10)}, "UpperArm_R": {"rot": (78, 0, 26)}, "Forearm_R": {"rot": (18, 0, 10)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (10, 0, 0)}}),
            (22, {"Hips": {"loc": (0, -0.002, 0)}, "Chest": {"rot": (-8, 0, 0)}, "Head": {"rot": (-10, 0, 0)}, "UpperArm_L": {"rot": (48, 0, -18)}, "Forearm_L": {"rot": (24, 0, -6)}, "UpperArm_R": {"rot": (52, 0, 18)}, "Forearm_R": {"rot": (24, 0, 6)}}),
            (36, {}),
        ]),
        ("Manager_Acknowledge_Worker", 28, [
            (1,  {}),
            (9,  {"Chest": {"rot": (4, -10, 0)}, "Head": {"rot": (-10, -12, 3)}, "UpperArm_R": {"rot": (64, 0, -12)}, "Forearm_R": {"rot": (20, 0, 0)}, "UpperArm_L": {"rot": (16, 0, -8)}, "Brow_L": {"loc": (0, 0.002, 0)}, "Brow_R": {"loc": (0, 0.002, 0)}, "Accessory_Root": {"rot": (-8, -4, 0)}}),
            (18, {"Chest": {"rot": (2, -6, 0)}, "Head": {"rot": (4, -8, 0)}, "UpperArm_R": {"rot": (48, 0, -8)}, "Forearm_R": {"rot": (18, 0, 0)}}),
            (28, {}),
        ]),
        ("Return_To_Work", 22, [
            (1,  {}),
            (7,  {"Hips": {"loc": (0, -0.004, 0)}, "Chest": {"rot": (-8, 0, 0)}, "Head": {"rot": (-12, 0, 0)}, "UpperArm_L": {"rot": (38, 0, 8)}, "Forearm_L": {"rot": (32, 0, 8)}, "UpperArm_R": {"rot": (38, 0, -8)}, "Forearm_R": {"rot": (32, 0, -8)}, "Accessory_Root": {"rot": (-10, 0, 0)}}),
            (14, {"Hips": {"loc": (0, 0.003, 0)}, "Chest": {"rot": (-6, 0, 0)}, "Head": {"rot": (-6, 0, 0)}, "UpperArm_L": {"rot": (44, 0, 10)}, "Forearm_L": {"rot": (28, 0, 6)}, "UpperArm_R": {"rot": (44, 0, -10)}, "Forearm_R": {"rot": (28, 0, -6)}}),
            (22, {}),
        ]),
    ]

    created_actions = []
    for clip_name, duration, keyframes in clips_spec:
        if clip_name in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[clip_name])
        act = bpy.data.actions.new(name=clip_name)
        act.use_fake_user = True
        arm_obj.animation_data.action = act

        for frame, bone_map in keyframes:
            reset_pose(arm_obj)
            # First key rest pose on all bones so every frame is a complete pose keyframe
            key_all_rest(arm_obj, frame)
            for bname, xform in bone_map.items():
                key_bone(
                    arm_obj,
                    bname,
                    frame,
                    loc=xform.get("loc"),
                    rot_deg=xform.get("rot"),
                    scale=xform.get("scale"),
                )

        # Push action to NLA strip so FBX exporter exports it cleanly by strip/action name
        track = arm_obj.animation_data.nla_tracks.new()
        track.name = clip_name
        strip = track.strips.new(clip_name, 1, act)
        strip.name = clip_name
        track.mute = False
        created_actions.append(act)

    # Set Idle_Breathing as active pose for preview renders
    arm_obj.animation_data.action = bpy.data.actions.get("Idle_Breathing")
    bpy.context.scene.frame_set(12)
    return created_actions


# ==============================================================================
# 5. STUDIO LIGHTING & VISUAL VERIFICATION CONTACT SHEET RENDERER
# ==============================================================================

def setup_studio_lighting_and_camera():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.film_transparent = False

    # Warm studio backdrop world
    world = bpy.data.worlds.get("StudioWorld") or bpy.data.worlds.new("StudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
        bg.inputs[1].default_value = 1.1

    # Ground pedestal disc
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.14, depth=0.008, location=(0, 0, -0.004))
    ped = bpy.context.active_object
    ped.name = "PreviewPedestal"
    shade_smooth(ped)
    assign_material(ped, "Mat_WarmBeige")

    # 3-point lighting (Key, Fill, Rim)
    lights = [
        ("KeyLight",  ( 0.35, -0.45, 0.45), 35.0, (1.0, 0.96, 0.90)),
        ("FillLight", (-0.40, -0.35, 0.30), 18.0, (0.85, 0.92, 1.0)),
        ("RimLight",  ( 0.00,  0.45, 0.40), 28.0, (1.0, 0.95, 0.85)),
    ]
    light_objs = [ped]
    for lname, loc, energy, col in lights:
        ldata = bpy.data.lights.new(name=lname, type='POINT')
        ldata.energy = energy
        ldata.color = col
        ldata.shadow_soft_size = 0.15
        lobj = bpy.data.objects.new(name=lname, object_data=ldata)
        lobj.location = loc
        bpy.context.collection.objects.link(lobj)
        light_objs.append(lobj)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 55
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    light_objs.append(cam_obj)
    return cam_obj, light_objs


def point_camera_at(cam_obj, pos, target):
    cam_obj.location = pos
    direction = mathutils.Vector(target) - mathutils.Vector(pos)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    cam_obj.rotation_euler = rot_quat.to_euler()


def render_character_views(char_name, arm_obj):
    cam_obj, studio_objs = setup_studio_lighting_and_camera()
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640

    target = (0.0, 0.0, 0.115)
    views = [
        ("front",        ( 0.00, -0.46, 0.145), "Idle_Breathing", 14),
        ("threequarter", ( 0.30, -0.36, 0.165), "Happy_Idle",     10),
        ("side",         ( 0.46,  0.00, 0.140), "Walk_Loop",       1),
        ("offer",        ( 0.24, -0.40, 0.160), "Offer_Document", 14),
    ]
    if char_name == "Agent_Researcher":
        views.extend([
            ("turn_left",  ( 0.30, -0.36, 0.155), "Turn_Left",        5),
            ("walk_carry", ( 0.42, -0.18, 0.150), "Walk_Carry_Paper", 1),
            ("earpod",     ( 0.24, -0.14, 0.165), "Idle_Breathing",   1),
        ])
    elif char_name == "Agent_Developer":
        views.append(("portal_ret", ( 0.32,  0.34, 0.155), "Portal_Return_Step", 28))
    elif char_name == "Agent_Tester":
        views.extend([
            ("walk_start", ( 0.46,  0.00, 0.145), "Walk_Start", 8),
            ("walk_stop",  ( 0.46,  0.00, 0.145), "Walk_Stop",  5),
        ])
    rendered_paths = []
    for vname, cam_pos, act_name, frame in views:
        reset_pose(arm_obj)
        if act_name in bpy.data.actions:
            arm_obj.animation_data.action = bpy.data.actions[act_name]
            scene.frame_set(frame)
        point_camera_at(cam_obj, cam_pos, target)
        out_png = os.path.join(PREVIEW_DIR, f"{char_name}_{vname}.png")
        scene.render.filepath = out_png
        bpy.ops.render.render(write_still=True)
        rendered_paths.append(out_png)

    # Remove studio objects before saving .blend and exporting .fbx
    for obj in studio_objs:
        bpy.data.objects.remove(obj, do_unlink=True)

    # Reset to rest pose for clean FBX bind pose export
    arm_obj.animation_data.action = None
    reset_pose(arm_obj)
    scene.frame_set(1)
    return rendered_paths


# ==============================================================================
# 6. MAIN CHARACTER BUILD & EXPORT LOOP
# ==============================================================================

CHARACTER_CONFIGS = [
    {
        "name": "Agent_Researcher",
        "primary_mat": "Mat_RoleTrim_Researcher",
        "secondary_mat": "Mat_RoleTrim_Researcher_Soft",
        "builder": build_researcher_accessories,
    },
    {
        "name": "Agent_Developer",
        "primary_mat": "Mat_RoleTrim_Developer",
        "secondary_mat": "Mat_RoleTrim_Developer_Soft",
        "builder": build_developer_accessories,
    },
    {
        "name": "Agent_Tester",
        "primary_mat": "Mat_RoleTrim_Tester",
        "secondary_mat": "Mat_RoleTrim_Tester_Soft",
        "builder": build_tester_accessories,
    },
]


def generate_all_characters():
    summary = []
    for cfg in CHARACTER_CONFIGS:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        # Clear any lingering actions from previous iteration
        for act in list(bpy.data.actions):
            bpy.data.actions.remove(act)

        cname = cfg["name"]
        arm_obj = create_character_armature(cname)
        shared_parts = build_shared_body_parts(cfg["primary_mat"], cfg["secondary_mat"])
        role_parts = cfg["builder"]()
        mesh_obj = join_and_bind_to_armature(shared_parts + role_parts, arm_obj, f"{cname}_Body")

        # Count triangles and vertices
        depsgraph = bpy.context.evaluated_depsgraph_get()
        eval_obj = mesh_obj.evaluated_get(depsgraph)
        eval_mesh = eval_obj.to_mesh()
        eval_mesh.calc_loop_triangles()
        tri_count = len(eval_mesh.loop_triangles)
        vert_count = len(eval_mesh.vertices)
        eval_obj.to_mesh_clear()

        actions = build_shared_animation_library(arm_obj)
        renders = render_character_views(cname, arm_obj)

        # Save editable .blend source
        blend_path = os.path.join(BLEND_DIR, f"{cname}.blend")
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)

        # Export Unity-ready .fbx with all 39 animation clips baked
        fbx_path = os.path.join(FBX_DIR, f"{cname}.fbx")
        bpy.ops.export_scene.fbx(
            filepath=fbx_path,
            use_selection=False,
            object_types={'ARMATURE', 'MESH', 'EMPTY'},
            use_mesh_modifiers=True,
            mesh_smooth_type='FACE',
            add_leaf_bones=False,
            primary_bone_axis='Y',
            secondary_bone_axis='X',
            armature_nodetype='NULL',
            bake_anim=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=True,
            bake_anim_use_all_actions=False,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0,
            path_mode='AUTO',
            axis_forward='-Z',
            axis_up='Y',
        )

        summary.append({
            "character": cname,
            "blend": blend_path,
            "fbx": fbx_path,
            "fbx_bytes": os.path.getsize(fbx_path),
            "vertices": vert_count,
            "triangles": tri_count,
            "bones": len(arm_obj.data.bones),
            "clips": len(actions),
            "renders": renders,
        })

    report_path = os.path.join(PREVIEW_DIR, "characters_build_report.json")
    with open(report_path, "w") as f:
        json.dump(summary, f, indent=2)


if __name__ == "__main__":
    generate_all_characters()
