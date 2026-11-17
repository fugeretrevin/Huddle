using System;
using System.Reflection;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Reads "is a hand holding this?" from an Oculus.Interaction.Grabbable without a compile-time
    /// dependency on the Interaction SDK. The property is bound ONCE to a typed delegate; calling
    /// PropertyInfo.GetValue every frame boxed an int per object per frame.
    /// </summary>
    public static class GrabState
    {
        /// <summary>Returns a getter for Grabbable.SelectingPointsCount, or null if the component
        /// has no such int property (wrong component / SDK change).</summary>
        public static Func<int> BindSelectingPoints(Component grabbable)
        {
            if (grabbable == null) return null;
            var prop = grabbable.GetType().GetProperty("SelectingPointsCount",
                BindingFlags.Instance | BindingFlags.Public);
            var getter = prop != null ? prop.GetGetMethod() : null;
            if (getter == null || prop.PropertyType != typeof(int)) return null;
            try { return (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), grabbable, getter); }
            catch (ArgumentException) { return null; }
        }
    }
}
