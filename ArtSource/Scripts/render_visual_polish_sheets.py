import bpy
import math
import mathutils
import os
import sys

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
SCRIPTS_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Scripts")
if SCRIPTS_DIR not in sys.path:
    sys.path.append(SCRIPTS_DIR)

import generate_visual_polish_assets as vpa

PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "VisualPolish")
os.makedirs(PREVIEW_DIR, exist_ok=True)


def setup_studio(cam_pos, target, lens=45, res=(1440, 900)):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = res[0]
    scene.render.resolution_y = res[1]

    world = bpy.data.worlds.new("PolishWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.13, 0.15, 0.19, 1.0)
        bg.inputs[1].default_value = 1.15

    # Studio pedestal floor
    vpa.make_box("Studio_Floor", (1.8, 1.2, 0.01), (0, 0, -0.01), "Mat_FeltSlate", bevel_width=0.002)

    lights = [
        ("Key",  ( 0.65, -0.85, 0.85), 75.0, (1.0, 0.97, 0.92)),
        ("Fill", (-0.65, -0.65, 0.60), 40.0, (0.88, 0.94, 1.0)),
        ("Rim",  ( 0.00,  0.65, 0.70), 45.0, (1.0, 0.95, 0.88)),
    ]
    for lname, loc, energy, col in lights:
        ld = bpy.data.lights.new(lname, 'POINT')
        ld.energy = energy
        ld.color = col
        lo = bpy.data.objects.new(lname, ld)
        lo.location = loc
        bpy.context.collection.objects.link(lo)

    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = lens
    cam_o = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam_o)
    scene.camera = cam_o
    cam_o.location = cam_pos
    cam_o.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(cam_pos)).to_track_quat('-Z', 'Y').to_euler()
    return scene


def render_sheet_1_documents():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Place Expanded Decision Paper in center, Small Decision Paper on left, Stamps + RestPad on right
    exp = vpa.build_decision_paper_expanded("Doc_DecisionPaper_Expanded")
    exp.location = (-0.04, 0.0, 0.16)
    exp.rotation_euler = (math.radians(-10), 0, 0)

    sm = vpa.build_decision_paper_small("Doc_DecisionPaper_Small")
    sm.location = (-0.28, -0.04, 0.09)
    sm.rotation_euler = (math.radians(-12), 0, math.radians(10))

    stamps = vpa.build_polished_stamp_assets()
    pad = stamps["Doc_StampRestPad"]()
    pad.location = (0.22, -0.05, 0.0)

    st_app = stamps["Doc_Stamp_Approve"]()
    st_app.location = (0.248, -0.05, 0.009)
    st_rej = stamps["Doc_Stamp_Reject"]()
    st_rej.location = (0.192, -0.05, 0.009)

    # Also show a 3D Poke Key (Housing + Cap) at 4 cm scale on the front desk
    ui = vpa.build_ui_poke_key_assets()
    for idx, (mat_name, bx) in enumerate([
        ("Mat_Tester_Mint",      -0.145),
        ("Mat_Tester_SlateBlue", -0.075),
        ("Mat_WarmBeige",        -0.005),
        ("Mat_Developer_Coral",   0.065),
    ]):
        h = ui["UI_PokeKey_Housing"]()
        h.scale = (0.042, 0.042, 0.032)
        h.location = (bx, -0.008, 0.048)
        c = ui["UI_PokeKey_Cap"]()
        c.scale = (0.042, 0.042, 0.032)
        c.location = (bx, -0.018, 0.048)
        for child in c.children:
            if child.type == 'MESH':
                child.data.materials.clear()
                child.data.materials.append(vpa.get_mat(mat_name))

    scene = setup_studio(cam_pos=(0.0, -0.68, 0.34), target=(-0.02, 0.0, 0.14), lens=44)
    out = os.path.join(PREVIEW_DIR, "ContactSheet_DecisionPaper_And_Stamps.png")
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)


def render_sheet_2_affordances_and_feedback():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    aff = vpa.build_affordance_assets()
    fb = vpa.build_mascot_feedback_assets()

    h1 = aff["Affordance_AgentHaloRing"]()
    h1.location = (-0.26, 0.02, 0.0)

    pad = aff["Affordance_DeskReceivePad"]()
    pad.location = (0.0, -0.04, 0.0)

    sock = aff["Affordance_CardSocket"]()
    sock.location = (0.14, -0.04, 0.04)

    puck = aff["Affordance_StatePuck"]()
    puck.location = (0.24, -0.04, 0.0)

    fb_names = [
        "Feedback_Listening",
        "Feedback_Speaking",
        "Feedback_Thinking",
        "Feedback_NeedsAttention",
        "Feedback_Error",
        "Feedback_Success",
    ]
    for idx, fname in enumerate(fb_names):
        fobj = fb[fname]()
        fobj.scale = (1.8, 1.8, 1.8)
        fobj.location = (-0.25 + idx * 0.10, 0.0, 0.14)

    scene = setup_studio(cam_pos=(0.0, -0.58, 0.28), target=(0.0, 0.0, 0.07), lens=44)
    out = os.path.join(PREVIEW_DIR, "ContactSheet_Affordances_And_Feedback.png")
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    render_sheet_1_documents()
    render_sheet_2_affordances_and_feedback()

