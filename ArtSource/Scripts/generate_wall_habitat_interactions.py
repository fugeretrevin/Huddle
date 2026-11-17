import bpy
import math
import os
import sys
import json

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import wall_habitat_common as wh
import spatial_common as sc


def _make_arm_bone(arm_data, name, head, tail, parent=None):
    b = arm_data.edit_bones.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = parent
    return b


def _setup_anim(arm_obj):
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = 'XYZ'


def _key_clip(arm_obj, clip_name, duration, frames):
    if clip_name in bpy.data.actions:
        bpy.data.actions.remove(bpy.data.actions[clip_name])
    act = bpy.data.actions.new(name=clip_name)
    act.use_fake_user = True
    arm_obj.animation_data.action = act

    for frame, bone_map in frames:
        for pb in arm_obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("rotation_euler", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
        for bname, xf in bone_map.items():
            pb = arm_obj.pose.bones.get(bname)
            if not pb:
                continue
            if "loc" in xf:
                pb.location = xf["loc"]
                pb.keyframe_insert("location", frame=frame)
            if "rot" in xf:
                pb.rotation_euler = tuple(math.radians(a) for a in xf["rot"])
                pb.keyframe_insert("rotation_euler", frame=frame)
            if "scale" in xf:
                pb.scale = xf["scale"]
                pb.keyframe_insert("scale", frame=frame)

    sc.push_action_to_nla(arm_obj, clip_name, 1, duration)
    return clip_name


def _build_desk_visit_perch():
    """
    Miniature Wooden-and-Brass Desk Visit Coaster / Perch (diameter 0.140m, height 0.018m):
    - Holds a temporarily ray-grabbed `/btw` agent projection cleanly on the user's desk!
    - Articulated status trim ring + collapsible rear nameplate backrest.
    - Exposes SeatPose / AgentHomePose / WorkPose / DocumentAttach / HeadLookTarget.
    """
    name = "WallHabitat_DeskVisitPerch"
    wh.reset_scene()

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_base = _make_arm_bone(arm_data, "Bone_PerchBase", (0, 0, 0), (0, 0, 0.02))
    _make_arm_bone(arm_data, "Bone_StatusRing", (0, 0, 0.010), (0, 0.04, 0.010), b_base)
    _make_arm_bone(arm_data, "Bone_BackTab",    (0, -0.058, 0.015), (0, -0.058, 0.055), b_base)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # Main wood & ceramic low-profile coaster base
    wh.make_cylinder(f"{name}_WoodCoaster", 0.070, 0.012, (0, 0, 0.006),
                     "Mat_WarmWood", vertices=28, bevel_caps=0.0015,
                     bone_name="Bone_PerchBase", parts_list=parts)
    wh.make_cylinder(f"{name}_CeramicRim", 0.060, 0.004, (0, 0, 0.013),
                     "Mat_CreamCeramic", vertices=26, bone_name="Bone_PerchBase", parts_list=parts)
    wh.make_cylinder(f"{name}_CushionPad", 0.052, 0.003, (0, 0, 0.0155),
                     "Mat_FeltSlate", vertices=24, bone_name="Bone_PerchBase", parts_list=parts)
    # Articulated status torus rim (MPB-controllable teal / amber / emerald pulse)
    wh.make_torus(f"{name}_GlowRing", 0.064, 0.0028, (0, 0, 0.012),
                  "Mat_Researcher_Teal", maj_seg=28, min_seg=8,
                  bone_name="Bone_StatusRing", parts_list=parts)
    # Tiny hinged rear backrest tab + brass trim for role/status badge card
    wh.make_beveled_box(f"{name}_BackCardTab", (0.058, 0.006, 0.034), (0, -0.058, 0.032),
                        "Mat_CreamCeramic", bevel_width=0.0012,
                        bone_name="Bone_BackTab", parts_list=parts)
    wh.make_beveled_box(f"{name}_BackBrassTrim", (0.048, 0.007, 0.008), (0, -0.058, 0.046),
                        "Mat_Brass", bevel_width=0.0008,
                        bone_name="Bone_BackTab", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    clips = [
        _key_clip(arm_obj, "Perch_Deploy", 20, [
            (1,  {"Bone_StatusRing": {"scale": (0.8, 0.8, 0.8)}, "Bone_BackTab": {"rot": (-85, 0, 0)}}),
            (12, {"Bone_StatusRing": {"scale": (1.06, 1.06, 1.06)}, "Bone_BackTab": {"rot": (6, 0, 0)}}),
            (20, {}),
        ]),
        _key_clip(arm_obj, "Perch_Active_Idle", 40, [
            (1,  {}),
            (20, {"Bone_StatusRing": {"scale": (1.03, 1.03, 1.0)}}),
            (40, {}),
        ]),
        _key_clip(arm_obj, "Perch_Conversing_Idle", 36, [
            (1,  {}),
            (12, {"Bone_StatusRing": {"scale": (1.05, 1.05, 1.15)}, "Bone_BackTab": {"rot": (3, 0, 0)}}),
            (24, {"Bone_StatusRing": {"scale": (1.01, 1.01, 1.0)}}),
            (36, {}),
        ]),
        _key_clip(arm_obj, "Perch_Dismiss", 18, [
            (1,  {}),
            (18, {"Bone_StatusRing": {"scale": (0.75, 0.75, 0.75)}, "Bone_BackTab": {"rot": (-88, 0, 0)}}),
        ]),
    ]
    arm_obj.animation_data.action = bpy.data.actions.get("Perch_Active_Idle")
    bpy.context.scene.frame_set(1)

    wh.make_anchor("AgentHomePose",  (0, 0, 0.017), arm_obj)
    wh.make_anchor("SeatPose",       (0, 0, 0.017), arm_obj)
    wh.make_anchor("WorkPose",       (0, 0, 0.017), arm_obj)
    wh.make_anchor("EntryPose",      (0, +0.070, 0.017), arm_obj)
    wh.make_anchor("HandoffPose",    (+0.050, +0.045, 0.017), arm_obj)
    wh.make_anchor("DocumentAttach", (0, +0.036, 0.058), arm_obj)
    wh.make_anchor("HeadLookTarget", (0, 0, 0.115), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.14, 0.22, 0.14), target=(0, 0, 0.02))


def _build_selection_halo():
    """
    Lightweight Distance-Select Visual Marker (`Select Agents` mode):
    - Ground selection halo ring (0.088m diameter around agent capsule base)
    - Overhead gentle targeting chevron / crown ring above Head_Attach (z=0.22m native)
    - Uses single shared opaque/emissive material hookable via MaterialPropertyBlock
      for `home`, `selected`, `carried_proxy`, `desk_visit`, `large_portal`, `returning`!
    """
    name = "WallHabitat_SelectionHalo"
    wh.reset_scene()
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)

    ring_parts = []
    chevron_parts = []

    # Ground ring
    wh.make_torus(f"{name}_FloorRing", 0.046, 0.0026, (0, 0, 0.004),
                  "Mat_Researcher_Teal", maj_seg=28, min_seg=8,
                  parent=root, parts_list=ring_parts)
    # 4 cardinal tick accents around ground ring
    for idx in range(4):
        ang = idx * 90.0
        rad = math.radians(ang)
        wh.make_beveled_box(f"{name}_Tick_{idx}", (0.008, 0.004, 0.004),
                            (math.cos(rad) * 0.052, math.sin(rad) * 0.052, 0.004),
                            "Mat_Brass", bevel_width=0.0005, rot_deg=(0, 0, ang),
                            parent=root, parts_list=ring_parts)

    # Overhead friendly inverted pointer diamond + brass crown accent above mascot hat/sprout
    wh.make_cone(f"{name}_PointerCone", 0.015, 0.002, 0.026, (0, 0, 0.245),
                 "Mat_Researcher_Teal", vertices=12, rot_deg=(180, 0, 0),
                 parent=root, parts_list=chevron_parts)
    wh.make_torus(f"{name}_PointerRing", 0.017, 0.0022, (0, 0, 0.258),
                  "Mat_Brass", maj_seg=18, min_seg=6,
                  parent=root, parts_list=chevron_parts)

    wh.join_static_parts(ring_parts, f"{name}_GroundRing", parent=root)
    wh.join_static_parts(chevron_parts, f"{name}_Pointer", parent=root)

    wh.make_anchor("AgentHomePose",  (0, 0, 0), root)
    wh.make_anchor("HeadLookTarget", (0, 0, 0.16), root)

    return wh.export_wall_static(name, cam_pos=(0.24, 0.48, 0.26), target=(0, 0, 0.13))


def _build_large_agent_portal():
    """
    Restrained Architectural Floor Portal for Large-Agent Presentation (~0.9-1.2m tall avatar):
    - Quest 3 safe: NO heavy multi-layer transparency, NO passthrough-blocking walls, NO strobe VFX!
    - Sleek 0.54m circular stepped wood-and-brass Floor Threshold Ring (inner open step pad 0.42m dia)
    - Graceful slim curved CreamCeramic + Brass Partial Arch Halo rising behind/around shoulders
      (0.58m W x 1.24m H arch peak, center stays 100% open & readable for real-room passthrough!)
    - Single thin animated Brass/Teal Aperture Threshold Shimmer Rim inside arch!
    - Exposes DialoguePanelSocket safely offset beside (+X) the enlarged avatar so panel never overlaps face/body!
    """
    name = "WallHabitat_LargeAgentPortal"
    wh.reset_scene()

    arm_data = bpy.data.armatures.new(f"Rig_{name}")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')

    b_base = _make_arm_bone(arm_data, "Bone_PortalBase", (0, 0, 0), (0, 0, 0.12))
    _make_arm_bone(arm_data, "Bone_ThresholdRim", (0, 0, 0.02), (0, 0.10, 0.02), b_base)
    _make_arm_bone(arm_data, "Bone_ArchCrown",    (0, -0.06, 0.02), (0, -0.06, 1.22), b_base)
    _make_arm_bone(arm_data, "Bone_ShimmerTrim",  (0, -0.055, 0.04), (0, -0.055, 1.18), b_base)

    bpy.ops.object.mode_set(mode='OBJECT')
    _setup_anim(arm_obj)

    parts = []
    # 1. Low-profile stepped wood-and-cream circular floor threshold plinth (bottom flush at Z=0)
    wh.make_cylinder(f"{name}_FloorStepPlinth", 0.270, 0.018, (0, 0, 0.009),
                     "Mat_WarmWood", vertices=36, bevel_caps=0.002,
                     bone_name="Bone_PortalBase", parts_list=parts)
    wh.make_cylinder(f"{name}_InnerStandPad", 0.220, 0.006, (0, 0, 0.020),
                     "Mat_FeltSlate", vertices=32, bone_name="Bone_PortalBase", parts_list=parts)
    # Brass beveled threshold ring + teal active rim indicator
    wh.make_torus(f"{name}_OuterBrassRing", 0.258, 0.007, (0, 0, 0.020),
                  "Mat_Brass", maj_seg=36, min_seg=10, bone_name="Bone_ThresholdRim", parts_list=parts)
    wh.make_torus(f"{name}_InnerTealRim", 0.232, 0.005, (0, 0, 0.022),
                  "Mat_Researcher_Teal", maj_seg=36, min_seg=10, bone_name="Bone_ThresholdRim", parts_list=parts)

    # 2. Restrained slim architectural rear semi-arch pillars & canopy crown (sits slightly at -Y behind avatar!)
    arch_y = -0.095
    arch_half_w = 0.275
    arch_spring_z = 0.880
    for slbl, sx in [("L", +1.0), ("R", -1.0)]:
        wh.make_cylinder(f"{name}_ArchFoot_{slbl}", 0.026, 0.028, (sx * arch_half_w, arch_y, 0.014),
                         "Mat_CreamCeramic", vertices=18, bone_name="Bone_PortalBase", parts_list=parts)
        wh.make_cylinder(f"{name}_ArchPillar_{slbl}", 0.014, arch_spring_z,
                         (sx * arch_half_w, arch_y, arch_spring_z * 0.5 + 0.020),
                         "Mat_WarmWood", vertices=16, bone_name="Bone_ArchCrown", parts_list=parts)
        wh.make_cylinder(f"{name}_BrassCollar_{slbl}", 0.018, 0.016,
                         (sx * arch_half_w, arch_y, arch_spring_z + 0.018),
                         "Mat_Brass", vertices=16, bone_name="Bone_ArchCrown", parts_list=parts)

    # Curved segmented upper portal arch beam (open hollow frame!)
    arch_segs = 18
    r_arch = arch_half_w
    for i in range(arch_segs):
        a0 = math.pi * (i / arch_segs)
        a1 = math.pi * ((i + 1) / arch_segs)
        am = (a0 + a1) * 0.5
        seg_len = r_arch * (math.pi / arch_segs) + 0.006
        sx = math.cos(am) * r_arch
        sz = arch_spring_z + 0.020 + math.sin(am) * r_arch * 0.92
        tilt = -math.degrees(am)
        wh.make_beveled_box(f"{name}_ArchSeg_{i}", (0.026, 0.022, seg_len),
                            (sx, arch_y, sz), "Mat_CreamCeramic", bevel_width=0.003,
                            rot_deg=(0, tilt, 0), bone_name="Bone_ArchCrown", parts_list=parts)

    # Single slim translucent inner shimmer arc ribbon (one clean pass, Quest-safe!)
    for i in range(14):
        a0 = math.pi * (i / 14)
        a1 = math.pi * ((i + 1) / 14)
        am = (a0 + a1) * 0.5
        r_in = arch_half_w - 0.024
        seg_len = r_in * (math.pi / 14) + 0.004
        sx = math.cos(am) * r_in
        sz = arch_spring_z + 0.020 + math.sin(am) * r_in * 0.90
        tilt = -math.degrees(am)
        wh.make_beveled_box(f"{name}_ShimmerArc_{i}", (0.008, 0.004, seg_len),
                            (sx, arch_y + 0.010, sz), "Mat_Wall_PortalRim", bevel_width=0.0008,
                            rot_deg=(0, tilt, 0), bone_name="Bone_ShimmerTrim", parts_list=parts)

    # Top keyhole brass medallion at peak
    wh.make_cylinder(f"{name}_PeakCrest", 0.028, 0.018, (0, arch_y + 0.012, arch_spring_z + r_arch * 0.92 + 0.028),
                     "Mat_Brass", vertices=18, rot_deg=(90, 0, 0), bone_name="Bone_ArchCrown", parts_list=parts)
    wh.make_sphere(f"{name}_PeakOrb", (0.016, 0.012, 0.016),
                   (0, arch_y + 0.018, arch_spring_z + r_arch * 0.92 + 0.028),
                   "Mat_StatusCyan", segments=14, rings=10, bone_name="Bone_ShimmerTrim", parts_list=parts)

    sc.join_parts_to_armature(parts, arm_obj, f"{name}_Mesh")

    clips = [
        _key_clip(arm_obj, "Portal_Hidden_Idle", 24, [
            (1,  {"Bone_ThresholdRim": {"scale": (0.15, 0.15, 0.15)}, "Bone_ArchCrown": {"scale": (0.05, 0.05, 0.05)}, "Bone_ShimmerTrim": {"scale": (0.05, 0.05, 0.05)}}),
            (24, {"Bone_ThresholdRim": {"scale": (0.15, 0.15, 0.15)}, "Bone_ArchCrown": {"scale": (0.05, 0.05, 0.05)}, "Bone_ShimmerTrim": {"scale": (0.05, 0.05, 0.05)}}),
        ]),
        _key_clip(arm_obj, "Portal_Open_Expand", 28, [
            (1,  {"Bone_ThresholdRim": {"scale": (0.2, 0.2, 0.2)}, "Bone_ArchCrown": {"scale": (0.2, 0.2, 0.1)}, "Bone_ShimmerTrim": {"scale": (0.1, 0.1, 0.1)}}),
            (16, {"Bone_ThresholdRim": {"scale": (1.05, 1.05, 1.05)}, "Bone_ArchCrown": {"scale": (1.02, 1.02, 1.04)}, "Bone_ShimmerTrim": {"scale": (1.06, 1.06, 1.06)}}),
            (28, {}),
        ]),
        _key_clip(arm_obj, "Portal_Active_Idle", 48, [
            (1,  {}),
            (24, {"Bone_ThresholdRim": {"scale": (1.02, 1.02, 1.0)}, "Bone_ShimmerTrim": {"scale": (1.03, 1.03, 1.02)}}),
            (48, {}),
        ]),
        _key_clip(arm_obj, "Portal_Close_Collapse", 24, [
            (1,  {}),
            (10, {"Bone_ShimmerTrim": {"scale": (0.5, 0.5, 0.5)}, "Bone_ArchCrown": {"scale": (0.85, 0.85, 0.65)}}),
            (24, {"Bone_ThresholdRim": {"scale": (0.15, 0.15, 0.15)}, "Bone_ArchCrown": {"scale": (0.05, 0.05, 0.05)}, "Bone_ShimmerTrim": {"scale": (0.05, 0.05, 0.05)}}),
        ]),
    ]
    arm_obj.animation_data.action = bpy.data.actions.get("Portal_Active_Idle")
    bpy.context.scene.frame_set(1)

    wh.make_anchor("PortalCenterPose",    (0, 0, 0.023), arm_obj)
    wh.make_anchor("EmergeStartPose",     (0, -0.075, 0.023), arm_obj)
    wh.make_anchor("AgentHomePose",       (0, +0.025, 0.023), arm_obj)
    wh.make_anchor("WorkPose",            (0, +0.025, 0.023), arm_obj)
    wh.make_anchor("SeatPose",            (0, +0.025, 0.023), arm_obj)
    wh.make_anchor("EntryPose",           (0, -0.075, 0.023), arm_obj)
    wh.make_anchor("HandoffPose",         (0, +0.110, 0.023), arm_obj)
    wh.make_anchor("DocumentAttach",      (0, +0.160, 0.550), arm_obj)
    wh.make_anchor("HeadLookTarget",      (0, +0.025, 0.880), arm_obj)
    # Safe DialoguePanelSocket positioned cleanly beside (+X in Unity / -X in Blender) so text never blocks avatar!
    wh.make_anchor("DialoguePanelSocket", (-0.420, +0.100, 0.850), arm_obj)
    wh.make_anchor("PortalSource_Floor",  (0.0, +0.025, 0.023), arm_obj)

    return wh.export_wall_animated(name, clips, cam_pos=(0.78, 2.40, 0.78), target=(0, 0, 0.58))


def generate_all():
    results = [
        _build_desk_visit_perch(),
        _build_selection_halo(),
        _build_large_agent_portal(),
    ]
    report_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_interactions_report.json")
    with open(report_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"[generate_wall_habitat_interactions] Generated {len(results)} interaction assets -> {report_path}")


if __name__ == "__main__":
    try:
        generate_all()
    except Exception as ex:
        import traceback
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "wall_habitat_interactions_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
