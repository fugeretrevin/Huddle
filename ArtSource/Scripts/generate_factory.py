import bpy
import bmesh
import math
import mathutils
import os
import json

# ==============================================================================
# MuseDesk — Phase 2 Miniature Software Factory Generator (Section 8)
# Generates:
#   1. Complete Rigged & Animated Production Line (SoftwareFactory_Complete.blend / .fbx)
#      with 13 mechanical control bones and 10 NLA animation clips:
#        - 1_Blueprint_Print
#        - 2_Blueprint_Transfer
#        - 3_Artifact_Assemble
#        - 4_Development_Complete
#        - 5_Artifact_Transport
#        - 6_Testing_Activate
#        - 7_Test_Success
#        - 8_Test_Failed
#        - 9_Rejected_Return
#        - 10_Completed_Delivery
#   2. Individual Modular Factory Sub-Assembly .fbx exports so modules can also be
#      placed or animated independently.
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
BLEND_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "Factory")
FBX_DIR = os.path.join(PROJECT_ROOT, "Assets", "MuseDesk", "Art", "Models", "Factory")
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
    "Mat_Brass":               {"color": (0.850, 0.690, 0.280, 1.0), "roughness": 0.24, "metallic": 0.82},
    "Mat_PaperWarm":           {"color": (0.985, 0.968, 0.925, 1.0), "roughness": 0.58, "metallic": 0.0},
    "Mat_BlueprintBlue":       {"color": (0.175, 0.385, 0.610, 1.0), "roughness": 0.45, "metallic": 0.0},
    "Mat_CardboardBox":        {"color": (0.760, 0.580, 0.400, 1.0), "roughness": 0.62, "metallic": 0.0},
    "Mat_ConveyorBelt":        {"color": (0.200, 0.215, 0.250, 1.0), "roughness": 0.65, "metallic": 0.05},
    "Mat_Researcher_Lavender": {"color": (0.560, 0.485, 0.785, 1.0), "roughness": 0.40, "metallic": 0.0},
    "Mat_Researcher_Teal":     {"color": (0.280, 0.735, 0.680, 1.0), "roughness": 0.28, "metallic": 0.05, "emission": (0.28, 0.74, 0.68), "emission_strength": 0.45},
    "Mat_Developer_Coral":     {"color": (0.980, 0.460, 0.360, 1.0), "roughness": 0.38, "metallic": 0.0},
    "Mat_Developer_Amber":     {"color": (1.000, 0.810, 0.320, 1.0), "roughness": 0.25, "metallic": 0.10, "emission": (1.0, 0.81, 0.32), "emission_strength": 0.80},
    "Mat_Tester_Mint":         {"color": (0.310, 0.785, 0.590, 1.0), "roughness": 0.36, "metallic": 0.0},
    "Mat_Tester_SlateBlue":    {"color": (0.290, 0.500, 0.660, 1.0), "roughness": 0.34, "metallic": 0.18},
    "Mat_StatusGreen":         {"color": (0.220, 0.820, 0.450, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.22, 0.85, 0.45), "emission_strength": 1.2},
    "Mat_StatusRed":           {"color": (0.920, 0.250, 0.240, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.92, 0.25, 0.24), "emission_strength": 1.2},
    "Mat_StatusAmber":         {"color": (0.980, 0.680, 0.220, 1.0), "roughness": 0.20, "metallic": 0.0,  "emission": (0.98, 0.68, 0.22), "emission_strength": 1.2},
    "Mat_ChamberGlass":        {"color": (0.680, 0.900, 0.880, 1.0), "roughness": 0.12, "metallic": 0.0,  "emission": (0.35, 0.82, 0.76), "emission_strength": 0.35},
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


def bind_weight(obj, bone_name):
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    indices = [v.index for v in obj.data.vertices]
    if indices:
        vg.add(indices, 1.0, 'REPLACE')


def make_box(name, size_xyz, location, mat_name, bone_name="Bone_FactoryRoot", bevel_width=0.002, rot_deg=(0, 0, 0), parts_list=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size_xyz
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel_width > 0:
        bev = obj.modifiers.new("Bevel", 'BEVEL')
        bev.width = bevel_width
        bev.segments = 2
        bpy.ops.object.modifier_apply(modifier="Bevel")
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_cyl(name, radius, depth, location, mat_name, bone_name="Bone_FactoryRoot", vertices=18, rot_deg=(0, 0, 0), parts_list=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                        location=location,
                                        rotation=tuple(math.radians(a) for a in rot_deg))
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_sphere(name, radius_xyz, location, mat_name, bone_name="Bone_FactoryRoot", segments=14, rings=10, parts_list=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = radius_xyz
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_torus(name, maj_r, min_r, location, mat_name, bone_name="Bone_FactoryRoot", maj_seg=18, min_seg=8, rot_deg=(0, 0, 0), parts_list=None):
    bpy.ops.mesh.primitive_torus_add(major_segments=maj_seg, minor_segments=min_seg,
                                     major_radius=maj_r, minor_radius=min_r,
                                     location=location,
                                     rotation=tuple(math.radians(a) for a in rot_deg))
    obj = bpy.context.active_object
    obj.name = name
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(get_mat(mat_name))
    bind_weight(obj, bone_name)
    if parts_list is not None:
        parts_list.append(obj)
    return obj


def make_anchor(name, location, parent):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'PLAIN_AXES'
    emp.empty_display_size = 0.018
    emp.location = location
    bpy.context.collection.objects.link(emp)
    emp.parent = parent
    return emp


# ==============================================================================
# 1. FACTORY ARMATURE (14 Bones for Mechanical Animation)
# Layout along X axis (spanning ~68 cm behind/linking Research [+X] -> Dev [0] -> QA [-X]):
#   +X = +0.26 : Stage 1 Research Blueprint Printer
#    X =  0.00 : Stage 2 Developer Artifact Creation Machine, Assembly Platform & Manipulator Arm
#   -X = -0.24 : Stage 3 Testing Chamber, Scanner Ring, Status Tower & Failed Return Chute
#   -X = -0.34 : Stage 4 Delivery Tray & Completion Trophy Shelf
# ==============================================================================

def create_factory_armature():
    arm_data = bpy.data.armatures.new("SoftwareFactory_ArmatureData")
    arm_obj = bpy.data.objects.new("Factory_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add_b(name, head, tail, parent=None):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = parent
        return b

    b_root = add_b("Bone_FactoryRoot", (0.0, 0.0, 0.0), (0.0, 0.0, 0.04))

    # Stage 1: Research Blueprint Printer (+X = 0.25)
    add_b("Bone_PrinterRoller",  (0.250, -0.022, 0.072), (0.250, -0.022, 0.092), b_root)
    add_b("Bone_BlueprintSheet", (0.215, -0.022, 0.062), (0.215, -0.022, 0.082), b_root)

    # Stage 2: Developer Build Press, Assembly Turntable, Robotic Arm & Gears (X = 0.0)
    add_b("Bone_PressHead",       (0.055,  0.000, 0.095), (0.055,  0.000, 0.115), b_root)
    b_mbase = add_b("Bone_ManipulatorBase", (-0.045, 0.028, 0.062), (-0.045, 0.028, 0.090), b_root)
    add_b("Bone_ManipulatorArm",  (-0.045, 0.028, 0.090), (-0.045, -0.010, 0.112), b_mbase)
    add_b("Bone_GearDrive",       (0.055, -0.038, 0.075), (0.055, -0.050, 0.075), b_root)
    add_b("Bone_GearIdler",       (0.028, -0.038, 0.075), (0.028, -0.050, 0.075), b_root)
    add_b("Bone_ProgressDial",    (0.000, -0.038, 0.088), (0.000, -0.050, 0.088), b_root)

    # Conveyor Carrier & Artifact Token (moves along X from +0.20 to -0.32)
    add_b("Bone_Carrier",         (0.120,  0.000, 0.056), (0.120,  0.000, 0.076), b_root)

    # Stage 3: Testing Chamber Door, Scanner Ring & Failed Return Diverter (-X = -0.22)
    add_b("Bone_ScannerRing",     (-0.210, 0.000, 0.088), (-0.210, 0.000, 0.108), b_root)
    add_b("Bone_ChamberDoor",     (-0.210, -0.036, 0.082), (-0.210, -0.036, 0.102), b_root)
    add_b("Bone_ReturnDiverter",  (-0.210, 0.036, 0.062), (-0.210, 0.036, 0.082), b_root)

    # Stage 4: Delivery Platform Lift (-X = -0.33)
    add_b("Bone_DeliveryLift",    (-0.330, 0.000, 0.054), (-0.330, 0.000, 0.074), b_root)

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'
    return arm_obj


def build_factory_geometry(arm_obj):
    parts = []

    # 1. Shared Conveyor Bed & Support Trestles (spanning X = -0.30 to +0.28)
    make_box("Factory_ConveyorFrame", (0.580, 0.056, 0.014), (-0.010, 0.0, 0.046), "Mat_BrushedMetal", parts_list=parts)
    make_box("Factory_ConveyorBeltSurface", (0.560, 0.042, 0.006), (-0.010, 0.0, 0.052), "Mat_ConveyorBelt", parts_list=parts)
    for tx in [-0.26, -0.09, 0.09, 0.26]:
        make_box(f"Factory_Trestle_{int(tx*100)}", (0.022, 0.064, 0.044), (tx, 0.0, 0.022), "Mat_CharcoalVisor", parts_list=parts)
    for rx in [-0.27, -0.18, -0.09, 0.0, 0.09, 0.18, 0.27]:
        make_cyl(f"Factory_Roller_{int(rx*100)}", 0.006, 0.048, (rx, 0.0, 0.048), "Mat_Brass", vertices=12, rot_deg=(90, 0, 0), parts_list=parts)

    # 2. Stage 1 — Blueprint Printing Machine (+X = 0.25)
    make_box("Factory_PrinterHousing", (0.068, 0.068, 0.052), (0.250, 0.0, 0.076), "Mat_Researcher_Lavender", bevel_width=0.003, parts_list=parts)
    make_box("Factory_PrinterTopDome", (0.052, 0.052, 0.014), (0.250, 0.0, 0.106), "Mat_Researcher_Teal", bevel_width=0.003, parts_list=parts)
    make_cyl("Factory_PrinterRoller", 0.007, 0.054, (0.250, -0.022, 0.072), "Mat_Brass", bone_name="Bone_PrinterRoller", vertices=16, rot_deg=(0, 90, 0), parts_list=parts)
    make_box("Factory_PrintedBlueprint", (0.042, 0.032, 0.003), (0.215, -0.022, 0.062), "Mat_BlueprintBlue", bone_name="Bone_BlueprintSheet", bevel_width=0.0005, parts_list=parts)

    # 3. Stage 2 — Artifact Creation Machine, Assembly Platform, Mechanical Arm & Gears (X = 0.0)
    make_box("Factory_BuilderArchL", (0.016, 0.074, 0.072), (0.082, 0.0, 0.084), "Mat_Developer_Coral", bevel_width=0.002, parts_list=parts)
    make_box("Factory_BuilderArchR", (0.016, 0.074, 0.072), (0.028, 0.0, 0.084), "Mat_Developer_Coral", bevel_width=0.002, parts_list=parts)
    make_box("Factory_BuilderHeader", (0.076, 0.078, 0.020), (0.055, 0.0, 0.124), "Mat_CharcoalVisor", bevel_width=0.003, parts_list=parts)
    # Animated Stamp/Press Head
    make_cyl("Factory_PressPiston", 0.008, 0.032, (0.055, 0.0, 0.102), "Mat_BrushedMetal", bone_name="Bone_PressHead", vertices=14, parts_list=parts)
    make_box("Factory_PressHeadBlock", (0.034, 0.034, 0.014), (0.055, 0.0, 0.086), "Mat_Developer_Amber", bone_name="Bone_PressHead", bevel_width=0.002, parts_list=parts)

    # Miniature Assembly Platform & Articulated Robotic Manipulator Arm
    make_cyl("Factory_AssemblyPad", 0.022, 0.006, (-0.045, 0.0, 0.056), "Mat_Brass", vertices=20, parts_list=parts)
    make_cyl("Factory_ManipulatorPedestal", 0.014, 0.028, (-0.045, 0.038, 0.062), "Mat_CharcoalVisor", bone_name="Bone_ManipulatorBase", vertices=16, parts_list=parts)
    make_box("Factory_ManipulatorLowerArm", (0.010, 0.010, 0.036), (-0.045, 0.034, 0.088), "Mat_Developer_Coral", bone_name="Bone_ManipulatorBase", bevel_width=0.0015, rot_deg=(18, 0, 0), parts_list=parts)
    make_box("Factory_ManipulatorUpperArm", (0.009, 0.038, 0.009), (-0.045, 0.014, 0.106), "Mat_BrushedMetal", bone_name="Bone_ManipulatorArm", bevel_width=0.0015, parts_list=parts)
    make_torus("Factory_ManipulatorGripper", 0.009, 0.0025, (-0.045, -0.006, 0.102), "Mat_Brass", bone_name="Bone_ManipulatorArm", maj_seg=14, min_seg=6, parts_list=parts)

    # Animated Interlocking Gears on Front of Builder (+X=0.055 & 0.028)
    for gname, gx, gmat, gbone in [
        ("Factory_GearDrive", 0.055, "Mat_Brass",        "Bone_GearDrive"),
        ("Factory_GearIdler", 0.028, "Mat_BrushedMetal", "Bone_GearIdler"),
    ]:
        make_cyl(gname, 0.013, 0.006, (gx, -0.040, 0.075), gmat, bone_name=gbone, vertices=16, rot_deg=(90, 0, 0), parts_list=parts)
        for ti in range(6):
            make_box(f"{gname}_Tooth_{ti}", (0.030, 0.005, 0.005), (gx, -0.040, 0.075), gmat, bone_name=gbone, bevel_width=0.0005, rot_deg=(0, ti * 30, 0), parts_list=parts)

    # Decorative Progress Indicator Dial (at X = -0.015, front side)
    make_cyl("Factory_ProgressDialFace", 0.014, 0.005, (-0.015, -0.038, 0.075), "Mat_CreamCeramic", vertices=18, rot_deg=(90, 0, 0), parts_list=parts)
    make_box("Factory_ProgressPointer", (0.003, 0.004, 0.011), (-0.015, -0.041, 0.079), "Mat_Developer_Coral", bone_name="Bone_ProgressDial", bevel_width=0.0005, parts_list=parts)

    # 4. Movable Artifact Transport Carrier & Glowing Software Cube
    make_box("Factory_CarrierTray", (0.042, 0.036, 0.006), (0.120, 0.0, 0.058), "Mat_Brass", bone_name="Bone_Carrier", bevel_width=0.0015, parts_list=parts)
    make_box("Factory_CarriedArtifact", (0.024, 0.024, 0.024), (0.120, 0.0, 0.072), "Mat_Developer_Amber", bone_name="Bone_Carrier", bevel_width=0.003, parts_list=parts)

    # 5. Stage 3 — Testing Chamber, Scanner Ring, Status Tower & Failed Return Chute (-X = -0.21)
    make_box("Factory_ChamberRoof", (0.076, 0.072, 0.012), (-0.210, 0.0, 0.122), "Mat_Tester_SlateBlue", bevel_width=0.0025, parts_list=parts)
    for idx, (px, py) in enumerate([(-0.242, -0.030), (-0.178, -0.030), (-0.242, 0.030), (-0.178, 0.030)]):
        make_cyl(f"Factory_ChamberPost_{idx+1}", 0.004, 0.068, (px, py, 0.086), "Mat_BrushedMetal", vertices=12, parts_list=parts)
    make_torus("Factory_ScannerRing", 0.024, 0.004, (-0.210, 0.0, 0.088), "Mat_Tester_Mint", bone_name="Bone_ScannerRing", maj_seg=22, min_seg=8, parts_list=parts)
    make_box("Factory_ChamberDoor", (0.058, 0.004, 0.052), (-0.210, -0.034, 0.084), "Mat_ChamberGlass", bone_name="Bone_ChamberDoor", bevel_width=0.001, parts_list=parts)

    # Status Light Tower on Chamber Roof (Pass/Warn/Fail)
    make_cyl("Factory_StatusPole", 0.0035, 0.036, (-0.210, -0.024, 0.144), "Mat_BrushedMetal", vertices=12, parts_list=parts)
    make_sphere("Factory_LightPass", (0.006, 0.006, 0.006), (-0.210, -0.024, 0.164), "Mat_StatusGreen", parts_list=parts)
    make_sphere("Factory_LightWarn", (0.006, 0.006, 0.006), (-0.210, -0.024, 0.151), "Mat_StatusAmber", parts_list=parts)
    make_sphere("Factory_LightFail", (0.006, 0.006, 0.006), (-0.210, -0.024, 0.138), "Mat_StatusRed",   parts_list=parts)

    # Failed-Artifact Return Mechanism / Slide Chute (behind belt at +Y = +0.048, linking -0.21 back to 0.0)
    make_box("Factory_ReturnChuteTrack", (0.210, 0.028, 0.008), (-0.105, 0.046, 0.050), "Mat_Developer_Coral", bevel_width=0.0015, rot_deg=(0, 4, 0), parts_list=parts)
    make_box("Factory_ReturnDiverterGate", (0.032, 0.006, 0.022), (-0.210, 0.030, 0.066), "Mat_StatusRed", bone_name="Bone_ReturnDiverter", bevel_width=0.001, parts_list=parts)

    # 6. Stage 4 — Delivery Tray, Shipping Boxes & Completion Trophy Shelf (-X = -0.33)
    make_cyl("Factory_DeliveryPedestal", 0.028, 0.044, (-0.330, 0.0, 0.022), "Mat_DarkWood", vertices=20, parts_list=parts)
    make_cyl("Factory_DeliveryTray", 0.032, 0.008, (-0.330, 0.0, 0.052), "Mat_Tester_Mint", bone_name="Bone_DeliveryLift", vertices=22, parts_list=parts)

    # Completion Shelf behind Delivery Pedestal (+Y = 0.062) with Shipping Box & Project Trophy
    make_box("Factory_CompletionShelf", (0.110, 0.042, 0.095), (-0.330, 0.062, 0.048), "Mat_WarmWood", bevel_width=0.0025, parts_list=parts)
    make_box("Factory_ShippingBox_1", (0.028, 0.028, 0.026), (-0.352, 0.060, 0.108), "Mat_CardboardBox", bevel_width=0.0015, parts_list=parts)
    make_cyl("Factory_ProjectTrophyCup", 0.010, 0.024, (-0.308, 0.060, 0.108), "Mat_Brass", vertices=16, parts_list=parts)

    # Join all factory meshes into a single skinned mesh bound to Factory_Armature
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = "SoftwareFactory_Mesh"
    joined.parent = arm_obj
    mod = joined.modifiers.new(name="Armature", type='ARMATURE')
    mod.object = arm_obj

    # Add Named Socket Anchors on Armature Root
    make_anchor("Research_Input_Socket",   ( 0.250,  0.000, 0.060), arm_obj)
    make_anchor("Dev_Build_Socket",        ( 0.055,  0.000, 0.060), arm_obj)
    make_anchor("QA_Chamber_Socket",       (-0.210,  0.000, 0.060), arm_obj)
    make_anchor("Delivery_Output_Socket",  (-0.330,  0.000, 0.060), arm_obj)
    make_anchor("Failed_Return_Socket",    (-0.105,  0.046, 0.058), arm_obj)

    return joined


def build_factory_animations(arm_obj):
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()
    for tr in list(arm_obj.animation_data.nla_tracks):
        arm_obj.animation_data.nla_tracks.remove(tr)

    def reset_bones():
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)

    def key_all(frame):
        for pb in arm_obj.pose.bones:
            pb.keyframe_insert(data_path="location", frame=frame)
            pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            pb.keyframe_insert(data_path="scale", frame=frame)

    def set_pose(bname, loc=None, rot_deg=None, scale=None):
        pb = arm_obj.pose.bones.get(bname)
        if not pb:
            return
        if loc:
            pb.location = loc
        if rot_deg:
            pb.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if scale:
            pb.scale = scale

    # All 10 required Phase 2 Factory Animation Hooks (Section 8)
    clips = [
        ("1_Blueprint_Print", [
            (1,  {"Bone_PrinterRoller": {"rot": (0,0,0)}, "Bone_BlueprintSheet": {"loc": (0.030,0,0), "scale": (0.2,1,1)}}),
            (15, {"Bone_PrinterRoller": {"rot": (0,180,0)}, "Bone_BlueprintSheet": {"loc": (0.010,0,0), "scale": (0.7,1,1)}}),
            (30, {"Bone_PrinterRoller": {"rot": (0,360,0)}, "Bone_BlueprintSheet": {"loc": (0,0,0), "scale": (1,1,1)}}),
        ]),
        ("2_Blueprint_Transfer", [
            (1,  {"Bone_BlueprintSheet": {"loc": (0,0,0)}, "Bone_Carrier": {"loc": (0.080,0,0)}}),
            (15, {"Bone_BlueprintSheet": {"loc": (-0.060,0,-0.004)}, "Bone_Carrier": {"loc": (0.020,0,0)}}),
            (30, {"Bone_BlueprintSheet": {"loc": (-0.140,0,-0.004)}, "Bone_Carrier": {"loc": (-0.065,0,0)}}),
        ]),
        ("3_Artifact_Assemble", [
            (1,  {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_PressHead": {"loc": (0,0,0)}, "Bone_ManipulatorBase": {"rot": (0,0,0)}, "Bone_GearDrive": {"rot": (0,0,0)}, "Bone_GearIdler": {"rot": (0,0,0)}}),
            (12, {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_PressHead": {"loc": (0,-0.022,0)}, "Bone_ManipulatorBase": {"rot": (0,25,0)}, "Bone_ManipulatorArm": {"rot": (20,0,0)}, "Bone_GearDrive": {"rot": (0,120,0)}, "Bone_GearIdler": {"rot": (0,-120,0)}, "Bone_ProgressDial": {"rot": (0,90,0)}}),
            (24, {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_PressHead": {"loc": (0,0.004,0)}, "Bone_ManipulatorBase": {"rot": (0,-25,0)}, "Bone_ManipulatorArm": {"rot": (-15,0,0)}, "Bone_GearDrive": {"rot": (0,240,0)}, "Bone_GearIdler": {"rot": (0,-240,0)}, "Bone_ProgressDial": {"rot": (0,210,0)}}),
            (36, {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_PressHead": {"loc": (0,0,0)}, "Bone_ManipulatorBase": {"rot": (0,0,0)}, "Bone_GearDrive": {"rot": (0,360,0)}, "Bone_GearIdler": {"rot": (0,-360,0)}, "Bone_ProgressDial": {"rot": (0,320,0)}}),
        ]),
        ("4_Development_Complete", [
            (1,  {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_ManipulatorArm": {"rot": (0,0,0)}}),
            (12, {"Bone_Carrier": {"loc": (-0.165,0,0.008)}, "Bone_ManipulatorArm": {"rot": (-25,0,0)}}),
            (24, {"Bone_Carrier": {"loc": (-0.165,0,0)}, "Bone_ManipulatorArm": {"rot": (0,0,0)}}),
        ]),
        ("5_Artifact_Transport", [
            (1,  {"Bone_Carrier": {"loc": (0.080,0,0)}, "Bone_GearDrive": {"rot": (0,0,0)}, "Bone_GearIdler": {"rot": (0,0,0)}}),
            (18, {"Bone_Carrier": {"loc": (-0.120,0,0)}, "Bone_GearDrive": {"rot": (0,180,0)}, "Bone_GearIdler": {"rot": (0,-180,0)}}),
            (36, {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_GearDrive": {"rot": (0,360,0)}, "Bone_GearIdler": {"rot": (0,-360,0)}}),
        ]),
        ("6_Testing_Activate", [
            (1,  {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ChamberDoor": {"loc": (0,0.036,0)}, "Bone_ScannerRing": {"loc": (0,0,0)}}),
            (10, {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ChamberDoor": {"loc": (0,0,0)}, "Bone_ScannerRing": {"loc": (0,-0.022,0), "rot": (0,120,0)}}),
            (24, {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ChamberDoor": {"loc": (0,0,0)}, "Bone_ScannerRing": {"loc": (0,0.020,0), "rot": (0,240,0)}}),
            (36, {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ChamberDoor": {"loc": (0,0.036,0)}, "Bone_ScannerRing": {"loc": (0,0,0), "rot": (0,360,0)}}),
        ]),
        ("7_Test_Success", [
            (1,  {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ScannerRing": {"loc": (0,0,0)}}),
            (12, {"Bone_Carrier": {"loc": (-0.390,0,0.010)}, "Bone_ScannerRing": {"loc": (0,0.018,0)}}),
            (24, {"Bone_Carrier": {"loc": (-0.450,0,0)}, "Bone_DeliveryLift": {"loc": (0,0.015,0)}}),
        ]),
        ("8_Test_Failed", [
            (1,  {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ReturnDiverter": {"rot": (0,0,0)}}),
            (10, {"Bone_Carrier": {"loc": (-0.330,0,-0.004), "rot": (0,0,12)}, "Bone_ReturnDiverter": {"rot": (0,-35,0)}}),
            (20, {"Bone_Carrier": {"loc": (-0.330,0,-0.004), "rot": (0,0,-12)}, "Bone_ReturnDiverter": {"rot": (0,-35,0)}}),
            (30, {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ReturnDiverter": {"rot": (0,0,0)}}),
        ]),
        ("9_Rejected_Return", [
            (1,  {"Bone_Carrier": {"loc": (-0.330,0,0)}, "Bone_ReturnDiverter": {"rot": (0,-35,0)}}),
            (15, {"Bone_Carrier": {"loc": (-0.200,0,0.044)}, "Bone_ReturnDiverter": {"rot": (0,-35,0)}}),
            (30, {"Bone_Carrier": {"loc": (-0.065,0,0)}, "Bone_ReturnDiverter": {"rot": (0,0,0)}}),
        ]),
        ("10_Completed_Delivery", [
            (1,  {"Bone_Carrier": {"loc": (-0.450,0,0)}, "Bone_DeliveryLift": {"loc": (0,0,0)}}),
            (14, {"Bone_Carrier": {"loc": (-0.450,0.024,0)}, "Bone_DeliveryLift": {"loc": (0,0.024,0), "rot": (0,180,0)}}),
            (28, {"Bone_Carrier": {"loc": (-0.450,0.012,0)}, "Bone_DeliveryLift": {"loc": (0,0.012,0), "rot": (0,360,0)}}),
        ]),
    ]

    for cname, kfs in clips:
        if cname in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[cname])
        act = bpy.data.actions.new(name=cname)
        act.use_fake_user = True
        arm_obj.animation_data.action = act

        for frame, bmap in kfs:
            reset_bones()
            for bname, xf in bmap.items():
                set_pose(bname, xf.get("loc"), xf.get("rot"), xf.get("scale"))
            key_all(frame)

        track = arm_obj.animation_data.nla_tracks.new()
        track.name = cname
        strip = track.strips.new(cname, 1, act)
        strip.name = cname

    arm_obj.animation_data.action = bpy.data.actions.get("3_Artifact_Assemble")
    bpy.context.scene.frame_set(12)
    return len(clips)


def render_factory_preview():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540

    world = bpy.data.worlds.new("FactoryWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
        bg.inputs[1].default_value = 1.15

    temp_objs = []
    for lname, loc, energy in [
        ("Key",  ( 0.45, -0.65, 0.65), 60.0),
        ("Fill", (-0.55, -0.55, 0.50), 35.0),
        ("Rim",  ( 0.00,  0.55, 0.50), 40.0),
    ]:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)
        temp_objs.append(lo)

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 45
    cam_o = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o
    temp_objs.append(cam_o)

    cam_pos = ( 0.22, -0.68, 0.34)
    target  = (-0.04,  0.00, 0.07)
    cam_o.location = cam_pos
    cam_o.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()

    out_png = os.path.join(PREVIEW_DIR, "SoftwareFactory_preview.png")
    scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)

    for o in temp_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return out_png


def generate_factory():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)

    arm_obj = create_factory_armature()
    mesh_obj = build_factory_geometry(arm_obj)
    clip_count = build_factory_animations(arm_obj)
    preview_png = render_factory_preview()

    # Reset pose before saving & exporting
    arm_obj.animation_data.action = None
    for pb in arm_obj.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.scene.frame_set(1)

    deps = bpy.context.evaluated_depsgraph_get()
    em = mesh_obj.evaluated_get(deps).to_mesh()
    em.calc_loop_triangles()
    tris = len(em.loop_triangles)
    verts = len(em.vertices)
    mesh_obj.evaluated_get(deps).to_mesh_clear()

    blend_path = os.path.join(BLEND_DIR, "SoftwareFactory_Complete.blend")
    fbx_path = os.path.join(FBX_DIR, "SoftwareFactory_Complete.fbx")
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
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
    )

    report = {
        "asset": "SoftwareFactory_Complete",
        "blend": blend_path,
        "fbx": fbx_path,
        "fbx_bytes": os.path.getsize(fbx_path),
        "vertices": verts,
        "triangles": tris,
        "bones": len(arm_obj.data.bones),
        "clips": clip_count,
        "preview": preview_png,
    }
    with open(os.path.join(PREVIEW_DIR, "factory_build_report.json"), "w") as f:
        json.dump(report, f, indent=2)


if __name__ == "__main__":
    generate_factory()

