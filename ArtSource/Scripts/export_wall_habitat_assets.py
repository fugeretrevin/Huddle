import os
import sys
import traceback

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import generate_wall_habitat_core as core_gen
import generate_wall_habitat_mechanical as mech_gen
import generate_wall_manager_agent as mgr_gen
import generate_wall_habitat_interactions as int_gen
import wall_habitat_common as wh


def main():
    core_gen.generate_all()
    mech_gen.generate_all()
    mgr_gen.generate_manager()
    int_gen.generate_all()
    print("[export_wall_habitat_assets] ALL Phase 3 Wall Habitat Blender models, animations, FBXs & Eevee previews completed.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        err_path = os.path.join(wh.PREVIEW_WALL_DIR, "export_wall_habitat_ERROR.txt")
        with open(err_path, "w") as f:
            traceback.print_exc(file=f)
        raise
