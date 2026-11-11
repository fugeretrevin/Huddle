"""Validate provided Phase 2 example schema instances (not full production integration tests)."""
from pathlib import Path
import json
from jsonschema import Draft202012Validator, FormatChecker
ROOT = Path(__file__).resolve().parents[1]
PAIRS = [
 ("event_v2.schema.json", "phase2_event_example.json"),
 ("command_v2.schema.json", "phase2_assign_command_example.json"),
 ("artifact_v2.schema.json", "phase2_artifact_example.json"),
 ("diagram_v2.schema.json", "phase2_diagram_example.json"),
 ("snapshot_v2.schema.json", "phase2_demo_snapshot.json")
]
for s, example in PAIRS:
    schema=json.loads((ROOT/"contracts"/s).read_text())
    item=json.loads((ROOT/"examples"/example).read_text())
    Draft202012Validator.check_schema(schema)
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(item)
    print("PASS", example)
print("Sample schema validation passed; not a runtime/Quest verification")
