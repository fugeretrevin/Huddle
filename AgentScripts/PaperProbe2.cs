// script eval --file: call RefreshPlanPaper once by reflection and report what happens (diagnostic).
using System.Reflection;
using UnityEngine;
using MuseDesk.Wall;

var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
var t = typeof(WallHabitatController);
var f = BindingFlags.Instance | BindingFlags.NonPublic;
string s = "now=" + Time.unscaledTime.ToString("F1") + " refreshAt=" + ((float)t.GetField("_paperRefreshAt", f).GetValue(c)).ToString("F1")
         + " enabled=" + c.enabled + " active=" + c.isActiveAndEnabled;
try
{
    t.GetMethod("RefreshPlanPaper", f).Invoke(c, null);
    s += " | refresh ok rows=" + c.Paper.RowCount + " stamp='" + c.Paper.StampWord + "'";
}
catch (TargetInvocationException e) { s += " | THROWS " + e.InnerException.GetType().Name + ": " + e.InnerException.Message + " @ " + e.InnerException.StackTrace.Split('\n')[0]; }
return s;
