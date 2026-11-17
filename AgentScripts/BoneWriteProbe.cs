// script eval --file: does the visitor's Animator rewrite Head/Neck/Chest/Spine every frame? (read-mostly: it nudges
// the bones once with PortalVisitorLife paused, waits, reads them back, then resumes the life component)
using System.Threading.Tasks;
using UnityEngine;

var life = Object.FindAnyObjectByType<MuseDesk.Wall.PortalVisitorLife>();
if (life == null) return "no visitor";
var an = life.GetComponentInChildren<Animator>();
life.enabled = false;
var names = new[] { "Head", "Neck", "Chest", "Spine" };
var bones = new Transform[4];
foreach (var t in life.GetComponentsInChildren<Transform>(true))
    for (int i = 0; i < 4; i++) if (t.name == names[i] && bones[i] == null) bones[i] = t;
await Task.Delay(100);
var set = new Quaternion[4];
for (int i = 0; i < 4; i++) if (bones[i] != null) { bones[i].localRotation = Quaternion.Euler(0f, 90f, 0f) * bones[i].localRotation; set[i] = bones[i].localRotation; }
await Task.Delay(300);
string s = "animator=" + (an != null && an.isActiveAndEnabled) + " state=" + (an != null ? an.GetCurrentAnimatorStateInfo(0).shortNameHash.ToString() : "-") + " |";
for (int i = 0; i < 4; i++)
    s += " " + names[i] + (bones[i] == null ? "=missing" : (Quaternion.Angle(bones[i].localRotation, set[i]) < 0.01f ? "=NOT rewritten" : "=rewritten (" + Quaternion.Angle(bones[i].localRotation, set[i]).ToString("F0") + " deg back)"));
life.enabled = true;
return s;
