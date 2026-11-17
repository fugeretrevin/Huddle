import os
import runpy

# ==============================================================================
# MuseDesk — Master Blender Asset Generation & Export Orchestrator
# Invokes all 4 procedural generators in sequence:
#   1. generate_characters.py   (3 rigged mascots + 39 shared clips)
#   2. generate_workstations.py (3 role workstations + props + anchors)
#   3. generate_props.py        (TaskBoard, Documents, Spatial Diagrams, Decorations)
#   4. generate_factory.py      (Miniature Software Factory + 10 clips)
# Followed by validate_assets.py to generate Docs/Art/ASSET_MANIFEST.json.
# ==============================================================================

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))

PIPELINE_SCRIPTS = [
    "generate_characters.py",
    "generate_workstations.py",
    "generate_props.py",
    "generate_factory.py",
    "validate_assets.py",
]


def run_all():
    for script_name in PIPELINE_SCRIPTS:
        script_path = os.path.join(SCRIPTS_DIR, script_name)
        print(f"[MuseDesk Export] Running {script_name}...")
        runpy.run_path(script_path, run_name="__main__")
    print("[MuseDesk Export] All asset groups generated, exported, and validated.")


if __name__ == "__main__":
    run_all()

