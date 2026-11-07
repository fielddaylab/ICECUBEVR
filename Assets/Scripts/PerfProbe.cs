using System;
using UnityEngine;

#if UNITY_WEBGL
using System.Runtime.InteropServices;

// N1 — Real-hardware measurement harness.
// Self-instantiating WebGL-only probe: logs environment (renderer/DPR/buffer
// size/compression extensions) once at load and one frame timestamp per
// Update. Output is tagged "PERFPROBE" so it can be grep'd from headless
// build logs. Does not alter rendering, text, or governor behaviour.
public class PerfProbe : MonoBehaviour
{
  [DllImport("__Internal")]
  private static extern void PerfProbe_Env();

  [DllImport("__Internal")]
  private static extern void PerfProbe_Frame();

  private static bool s_envLogged = false;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void AutoCreate()
  {
    if (s_envLogged) { return; }
    var go = new GameObject("[PerfProbe]");
    UnityEngine.Object.DontDestroyOnLoad(go);
    go.AddComponent<PerfProbe>();
    s_envLogged = true;
  }

  private void Start()
  {
    try
    {
      PerfProbe_Env();
    }
    catch (Exception e)
    {
      Debug.Log("PERFPROBE env error: " + e.Message);
    }
  }

  private void Update()
  {
    try
    {
      PerfProbe_Frame();
    }
    catch (Exception e)
    {
      Debug.Log("PERFPROBE frame error: " + e.Message);
    }
  }
}
#endif
