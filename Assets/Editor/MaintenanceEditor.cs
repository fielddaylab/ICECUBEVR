using UnityEngine;
using System.Collections;
using UnityEditor;

[CustomEditor(typeof(Maintenance))]
public class MaintenanceEditor : Editor
{
  public override void OnInspectorGUI()
  {
    Maintenance maint = (Maintenance)target;
    DrawDefaultInspector();
    if(GUILayout.Button("Perform Maintenance"))
    {
      maint.performMaintenance();
    }
  }
}

