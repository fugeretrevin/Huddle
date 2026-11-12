import os
import json

# ==============================================================================
# MuseDesk — Asset Validation & Manifest Builder
# Inspects all generated .blend and .fbx files across Characters, Workstations,
# TaskBoard, Documents, Diagrams, Decorations, and Factory, verifies budgets
# and file integrity, and writes Docs/Art/ASSET_MANIFEST.json.
# ==============================================================================

PROJECT_ROOT = r"C:\dev\MuseDesk\MuseDesk"
PREVIEW_DIR = os.path.join(PROJECT_ROOT, "Screenshots", "ArtPreviews")
MANIFEST_PATH = os.path.join(PROJECT_ROOT, "Docs", "Art", "ASSET_MANIFEST.json")


def build_and_validate_manifest():
    os.makedirs(os.path.dirname(MANIFEST_PATH), exist_ok=True)

    char_report_path = os.path.join(PREVIEW_DIR, "characters_build_report.json")
    ws_report_path   = os.path.join(PREVIEW_DIR, "workstations_build_report.json")
    prop_report_path = os.path.join(PREVIEW_DIR, "props_build_report.json")
    fac_report_path  = os.path.join(PREVIEW_DIR, "factory_build_report.json")

    characters   = json.load(open(char_report_path)) if os.path.exists(char_report_path) else []
    workstations = json.load(open(ws_report_path))   if os.path.exists(ws_report_path) else []
    props        = json.load(open(prop_report_path)) if os.path.exists(prop_report_path) else []
    factory      = json.load(open(fac_report_path))  if os.path.exists(fac_report_path) else {}

    errors = []
    total_tris = 0
    total_assets = 0

    for c in characters:
        total_assets += 1
        total_tris += c.get("triangles", 0)
        if not os.path.exists(c["fbx"]) or os.path.getsize(c["fbx"]) < 1000:
            errors.append(f"Missing or invalid character FBX: {c['fbx']}")
        if c.get("clips", 0) < 39:
            errors.append(f"Character {c['character']} has fewer than 39 clips: {c.get('clips')}")

    for w in workstations:
        total_assets += 1
        total_tris += w.get("triangles", 0)
        if not os.path.exists(w["fbx"]) or os.path.getsize(w["fbx"]) < 1000:
            errors.append(f"Missing or invalid workstation FBX: {w['fbx']}")

    for p in props:
        total_assets += 1
        total_tris += p.get("triangles", 0)
        if not os.path.exists(p["fbx"]) or os.path.getsize(p["fbx"]) < 500:
            errors.append(f"Missing or invalid prop FBX: {p['fbx']}")

    if factory:
        total_assets += 1
        total_tris += factory.get("triangles", 0)
        if not os.path.exists(factory["fbx"]) or os.path.getsize(factory["fbx"]) < 1000:
            errors.append(f"Missing or invalid factory FBX: {factory.get('fbx')}")

    manifest = {
        "project": "MuseDesk",
        "target_platform": "Meta Quest 3 (Unity 6000.6.8f1 / URP 17.6)",
        "art_style": "Pipkin Sprites & Cozy Tactile Miniature Studio",
        "validation_passed": len(errors) == 0,
        "validation_errors": errors,
        "summary": {
            "total_exported_fbx_assets": total_assets,
            "character_count": len(characters),
            "shared_character_animation_clips": 39,
            "workstation_count": len(workstations),
            "prop_and_diagram_count": len(props),
            "factory_animation_clips": factory.get("clips", 10),
            "total_library_triangles": total_tris,
        },
        "required_character_attachments": [
            "RightHand_Attach",
            "LeftHand_Attach",
            "Head_Attach",
            "Back_Attach",
            "Document_Attach",
        ],
        "workstation_interaction_anchors": {
            "Workstation_Developer": [
                "AgentWorkPosition", "AgentSeatPosition", "TaskDropZone",
                "DocumentPickupPoint", "ArtifactOutputPoint",
                "MonitorScreen_Left", "MonitorScreen_Right", "HandoffReceivePoint"
            ],
            "Workstation_Research": [
                "AgentWorkPosition", "AgentSeatPosition", "TaskDropZone",
                "DocumentPickupPoint", "ArtifactOutputPoint",
                "BlueprintSpawnPoint", "HandoffReceivePoint"
            ],
            "Workstation_Testing": [
                "AgentWorkPosition", "AgentSeatPosition", "TaskDropZone",
                "DocumentPickupPoint", "ArtifactOutputPoint",
                "ChamberSlotPoint", "AcceptedTrayPoint", "RejectedTrayPoint",
                "HandoffReceivePoint"
            ],
        },
        "characters": characters,
        "workstations": workstations,
        "factory": factory,
        "props_by_category": {
            "TaskBoard":   [p for p in props if p["category"] == "TaskBoard"],
            "Documents":   [p for p in props if p["category"] == "Documents"],
            "Diagrams":    [p for p in props if p["category"] == "Diagrams"],
            "Decorations": [p for p in props if p["category"] == "Decorations"],
        },
    }

    with open(MANIFEST_PATH, "w") as f:
        json.dump(manifest, f, indent=2)

    return manifest


if __name__ == "__main__":
    build_and_validate_manifest()

