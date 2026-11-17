// script eval --file: the Manager paper's visible row texts (read-only).
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using MuseDesk.Wall;

var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
if (c == null || c.Paper == null) return "no paper";
var rows = (List<TMPro.TMP_Text>)typeof(WallPlanPaper).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c.Paper);
var sb = new System.Text.StringBuilder("visible=" + c.Paper.Visible + " rows=" + c.Paper.RowCount + "\n");
for (int i = 0; i < c.Paper.RowCount && i < rows.Count; i++) sb.Append("  ").Append(rows[i].text).Append('\n');
return sb.ToString();
