using System;
using System.Reflection;
using UnityEngine;

namespace Icecube.Perf
{
  /// Applies a ladder index to the N5 compositor (SceneCompositor.SetScale) and
  /// reports the resulting RenderTexture size.
  ///
  /// Reflection rather than a direct reference on purpose: N5 and N6 are
  /// separate cards on separate branches, and this file must compile (and the
  /// governor must stay harmlessly inert) whether or not SceneCompositor.cs is
  /// present in the tree. When it is present the behaviour is identical to
  /// calling SceneCompositor.Instance.SetScale(i) — the lookups are cached and
  /// only ever touched on a ladder change, never per frame.
  ///
  /// The sink also pushes the N6 ladder {1.0,0.85,0.71,0.6,0.5} onto the
  /// compositor's own scaleLadder field, so index i means the same thing on both
  /// sides. N5 ships a shorter default ladder; without this the governor's
  /// index 4 would clamp to the compositor's last entry and the logged RT size
  /// would not match the logged index.
  public sealed class GovernorScaleSink
  {
    readonly float[] ladder;

    Type compositorType;
    Component instance;
    MethodInfo setScale;
    PropertyInfo targetProp;
    PropertyInfo currentIndexProp;
    PropertyInfo isActiveProp;
    FieldInfo ladderField;
    bool ladderPushed;
    bool resolveFailedLogged;

    public GovernorScaleSink(float[] ladder)
    {
      this.ladder = (ladder != null && ladder.Length > 0) ? ladder : new float[] { 1.0f };
    }

    public bool Available { get { return Resolve(); } }

    /// Index the compositor currently reports, or -1 when unavailable.
    public int CurrentIndex
    {
      get
      {
        if (!Resolve() || currentIndexProp == null) return -1;
        try { return (int)currentIndexProp.GetValue(instance, null); }
        catch (Exception) { return -1; }
      }
    }

    public bool Apply(int index)
    {
      if (!Resolve()) return false;
      try
      {
        PushLadder();
        setScale.Invoke(instance, new object[] { index });
        return true;
      }
      catch (Exception e)
      {
        Debug.LogWarning("[GOV] SetScale(" + index + ") failed: " + e.Message);
        return false;
      }
    }

    /// Current 3D RenderTexture size. Falls back to the native drawing buffer
    /// scaled by the ladder entry when the compositor is not wired yet, so the
    /// log line is never blank.
    public void GetRenderSize(int index, out int w, out int h)
    {
      w = 0; h = 0;
      if (Resolve() && targetProp != null)
      {
        try
        {
          var rt = targetProp.GetValue(instance, null) as RenderTexture;
          if (rt != null) { w = rt.width; h = rt.height; return; }
        }
        catch (Exception) { /* fall through to the estimate */ }
      }
      float s = ladder[Mathf.Clamp(index, 0, ladder.Length - 1)];
      w = Mathf.Max(1, Mathf.RoundToInt(Screen.width * s));
      h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * s));
    }

    void PushLadder()
    {
      if (ladderPushed || ladderField == null) return;
      try
      {
        var existing = ladderField.GetValue(instance) as float[];
        if (!SameLadder(existing))
        {
          var copy = new float[ladder.Length];
          Array.Copy(ladder, copy, ladder.Length);
          ladderField.SetValue(instance, copy);
        }
        ladderPushed = true;
      }
      catch (Exception e)
      {
        ladderPushed = true;   // do not retry every change
        Debug.LogWarning("[GOV] could not install the N6 ladder on the compositor: " + e.Message);
      }
    }

    bool SameLadder(float[] other)
    {
      if (other == null || other.Length != ladder.Length) return false;
      for (int i = 0; i < ladder.Length; i++)
        if (Mathf.Abs(other[i] - ladder[i]) > 0.0005f) return false;
      return true;
    }

    bool Resolve()
    {
      if (instance != null && setScale != null) return true;

      if (compositorType == null)
      {
        compositorType = FindType("SceneCompositor");
        if (compositorType == null)
        {
          if (!resolveFailedLogged)
          {
            resolveFailedLogged = true;
            Debug.Log("[GOV] SceneCompositor (N5) not present; governor is inert");
          }
          return false;
        }
        setScale = compositorType.GetMethod("SetScale", new Type[] { typeof(int) });
        targetProp = compositorType.GetProperty("Target");
        currentIndexProp = compositorType.GetProperty("CurrentLadderIndex");
        isActiveProp = compositorType.GetProperty("IsActive");
        ladderField = compositorType.GetField("scaleLadder");
        if (setScale == null)
        {
          if (!resolveFailedLogged)
          {
            resolveFailedLogged = true;
            Debug.LogWarning("[GOV] SceneCompositor has no SetScale(int); governor is inert");
          }
          return false;
        }
      }

      instance = UnityEngine.Object.FindObjectOfType(compositorType) as Component;
      return instance != null && setScale != null;
    }

    public bool CompositorActive
    {
      get
      {
        if (!Resolve() || isActiveProp == null) return Resolve();
        try { return (bool)isActiveProp.GetValue(instance, null); }
        catch (Exception) { return true; }
      }
    }

    static Type FindType(string name)
    {
      var t = Type.GetType(name);
      if (t != null) return t;
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
      for (int i = 0; i < assemblies.Length; i++)
      {
        try
        {
          t = assemblies[i].GetType(name, false);
          if (t != null) return t;
        }
        catch (Exception) { /* dynamic/unloadable assembly */ }
      }
      return null;
    }
  }
}
