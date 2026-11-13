import os
import json
import runpy

SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))

MODULES = [
    "generate_spatial_floor_and_preview.py",
    "generate_spatial_ceiling_pod.py",
    "generate_spatial_beacon_and_mailbox.py",
    "generate_spatial_courier_and_perch.py",
]


def run_all():
    combined = []
    for mod in MODULES:
        path = os.path.join(SCRIPTS_DIR, mod)
        print(f"[SpatialExport] Running {mod}...")
        runpy.run_path(path, run_name="__main__")

    preview_dir = os.path.abspath(os.path.join(SCRIPTS_DIR, "..", "..", "Screenshots", "SpatialWorkspace", "Blender"))
    for rname in [
        "floor_and_preview_report.json",
        "ceiling_pod_report.json",
        "beacon_and_mailbox_report.json",
        "courier_and_perch_report.json",
    ]:
        rp = os.path.join(preview_dir, rname)
        if os.path.exists(rp):
            with open(rp, "r") as f:
                combined.extend(json.load(f))

    out_path = os.path.join(preview_dir, "spatial_blender_master_report.json")
    with open(out_path, "w") as f:
        json.dump(combined, f, indent=2)
    print(f"[SpatialExport] Complete! {len(combined)} assets exported -> {out_path}")


if __name__ == "__main__":
    run_all()

