// N6 — pacing governor decision policy.
//
// Pure C#: no UnityEngine reference, no clocks of its own, no allocation after
// construction. Everything is driven by Tick(nowSeconds, workMs), which makes
// the whole hysteresis/cooldown state machine testable without a player.
//
// Why work time and not frame delta: with the half-rate rAF shim the frame
// delta is pinned at ~33.3 ms whether the tick did 5 ms or 32 ms of work, so a
// delta-based (or Time.smoothDeltaTime-based) controller is blind until it is
// already dropping frames. Work time is the headroom signal: at a locked 30 fps
// the budget is 33.3 ms, we start shedding fill at 28 ms (84 % of budget, i.e.
// before frames are actually lost) and only claw resolution back under 20 ms
// (60 % of budget) so that one step up cannot immediately re-breach 28 ms.
using System;

namespace Icecube.Perf
{
  public enum GovernorAction
  {
    None = 0,
    StepDown = 1,
    StepUp = 2
  }

  /// Reason a Tick produced no decision. Diagnostics only; no behaviour depends
  /// on it, but it makes "why didn't it move" answerable from a log line.
  public enum GovernorIgnoreReason
  {
    Active = 0,
    Warmup = 1,
    PostChange = 2,
    Cooldown = 3,
    BadSample = 4,
    NotEnoughSamples = 5
  }

  public sealed class GovernorPolicy
  {
    // --- tunables (card N6) -------------------------------------------------
    public const double Alpha = 0.1;                 // EWMA weight on the newest sample
    public const double P90WindowSeconds = 3.0;      // rolling p90 window
    public const double EvalIntervalSeconds = 1.0;   // decisions are made on 1 s windows
    public const double DownThresholdMs = 28.0;
    public const double UpThresholdMs = 20.0;
    public const int DownSecondsRequired = 3;
    public const int UpSecondsRequired = 8;
    public const double WarmupSeconds = 10.0;        // ignore after load
    public const double PostChangeIgnoreSeconds = 2.0;  // ignore after scale change / scene swap
    public const double CooldownSeconds = 5.0;       // minimum spacing between changes
    public const double MaxPlausibleWorkMs = 2000.0; // tab-hidden / breakpoint outlier guard

    /// Index 0 must be 1.0 (native) — SceneCompositor has the same contract.
    public static float[] DefaultLadder()
    {
      return new float[] { 1.0f, 0.85f, 0.71f, 0.6f, 0.5f };
    }

    // --- sample ring --------------------------------------------------------
    // 3 s at 60 Hz is 180 samples; 512 leaves room for an un-gated 120 Hz panel.
    const int Capacity = 512;
    readonly double[] sampleTime = new double[Capacity];
    readonly double[] sampleWork = new double[Capacity];
    readonly double[] scratch = new double[Capacity];
    int head;        // next write position
    int count;       // live samples in the ring

    readonly int ladderLength;

    double ewma;
    bool ewmaPrimed;
    double lastP90;
    double nextEvalTime;
    bool evalScheduled;

    int highSeconds;
    int lowSeconds;
    int index;

    double ignoreUntil;        // post change / scene swap
    double cooldownUntil;      // minimum spacing between changes
    double lastChangeTime = double.NegativeInfinity;

    public GovernorPolicy(int ladderLength)
    {
      if (ladderLength < 1) ladderLength = 1;
      this.ladderLength = ladderLength;
      ignoreUntil = WarmupSeconds;   // "ignore the first 10 s after load"
    }

    public GovernorPolicy() : this(DefaultLadder().Length) { }

    // --- observable state ---------------------------------------------------
    public int LadderIndex { get { return index; } }
    public int LadderLength { get { return ladderLength; } }
    public double Ewma { get { return ewma; } }
    public double LastP90 { get { return lastP90; } }
    public int SampleCount { get { return count; } }
    public int HighSeconds { get { return highSeconds; } }
    public int LowSeconds { get { return lowSeconds; } }
    public double LastChangeTime { get { return lastChangeTime; } }
    public GovernorIgnoreReason LastReason { get; private set; }
    public int ChangeCount { get; private set; }

    public bool IsIgnoring(double now) { return now < ignoreUntil; }
    public bool IsCoolingDown(double now) { return now < cooldownUntil; }

    // --- external events ----------------------------------------------------

    /// Skybox / scene swap: the frames straight after a swap are dominated by
    /// material + texture upload, not by fill, so they must not drive the
    /// ladder. Also used for anything else that invalidates the recent history.
    public void NotifySceneSwap(double now)
    {
      BeginIgnoreWindow(now, PostChangeIgnoreSeconds);
    }

    /// Someone changed the scale behind our back (manual console SetScale, N5
    /// A/B toggle). Re-sync without pretending we decided it.
    public void NotifyExternalScaleChange(double now, int newIndex)
    {
      index = Clamp(newIndex);
      BeginIgnoreWindow(now, PostChangeIgnoreSeconds);
      cooldownUntil = now + CooldownSeconds;
      lastChangeTime = now;
    }

    void BeginIgnoreWindow(double now, double seconds)
    {
      double until = now + seconds;
      if (until > ignoreUntil) ignoreUntil = until;
      // History collected before the discontinuity is meaningless.
      count = 0;
      head = 0;
      ewmaPrimed = false;
      ewma = 0.0;
      highSeconds = 0;
      lowSeconds = 0;
      evalScheduled = false;
    }

    // --- main entry point ---------------------------------------------------

    /// Feed one frame. nowSeconds must be monotonic seconds since load
    /// (Time.realtimeSinceStartup on the player side); workMs is the previous
    /// main-loop tick duration from GovWorkTime().
    /// Returns the action to apply this frame (at most one step).
    public GovernorAction Tick(double nowSeconds, double workMs)
    {
      // Hidden tab / first frames / jslib fallback: GovWorkTime returns 0, and a
      // multi-second value means the page was suspended. Neither is a signal.
      bool goodSample = workMs > 0.0 && !double.IsNaN(workMs) && !double.IsInfinity(workMs)
                        && workMs <= MaxPlausibleWorkMs;

      if (nowSeconds < ignoreUntil)
      {
        LastReason = nowSeconds < WarmupSeconds
          ? GovernorIgnoreReason.Warmup
          : GovernorIgnoreReason.PostChange;
        return GovernorAction.None;
      }

      if (!goodSample)
      {
        LastReason = GovernorIgnoreReason.BadSample;
        return GovernorAction.None;
      }

      // EWMA (diagnostics + the "is it even close" sanity signal).
      if (!ewmaPrimed) { ewma = workMs; ewmaPrimed = true; }
      else ewma = Alpha * workMs + (1.0 - Alpha) * ewma;

      Push(nowSeconds, workMs);

      if (!evalScheduled)
      {
        // First accepted sample after a (re)start: the first verdict lands one
        // second later, so a single spike can never move the ladder.
        nextEvalTime = nowSeconds + EvalIntervalSeconds;
        evalScheduled = true;
        LastReason = GovernorIgnoreReason.Active;
        return GovernorAction.None;
      }

      if (nowSeconds < nextEvalTime)
      {
        LastReason = GovernorIgnoreReason.Active;
        return GovernorAction.None;
      }

      // One verdict per elapsed second, even if frames were long enough that we
      // skipped past several boundaries.
      nextEvalTime += EvalIntervalSeconds;
      if (nextEvalTime <= nowSeconds) nextEvalTime = nowSeconds + EvalIntervalSeconds;

      int n = CollectWindow(nowSeconds - P90WindowSeconds);
      // At a locked 30 fps a full 3 s window is ~90 samples. Require a third of
      // that so a half-filled window right after an ignore window cannot decide.
      if (n < 10)
      {
        LastReason = GovernorIgnoreReason.NotEnoughSamples;
        return GovernorAction.None;
      }

      lastP90 = Percentile90(n);

      if (lastP90 > DownThresholdMs)
      {
        lowSeconds = 0;
        if (highSeconds < DownSecondsRequired) highSeconds++;
      }
      else if (lastP90 < UpThresholdMs)
      {
        highSeconds = 0;
        if (lowSeconds < UpSecondsRequired) lowSeconds++;
      }
      else
      {
        // 20..28 ms is the dead band: this is the state we want to sit in.
        highSeconds = 0;
        lowSeconds = 0;
      }

      if (nowSeconds < cooldownUntil)
      {
        LastReason = GovernorIgnoreReason.Cooldown;
        return GovernorAction.None;
      }

      LastReason = GovernorIgnoreReason.Active;

      if (highSeconds >= DownSecondsRequired && index < ladderLength - 1)
      {
        index++;
        Commit(nowSeconds);
        return GovernorAction.StepDown;
      }

      if (lowSeconds >= UpSecondsRequired && index > 0)
      {
        index--;
        Commit(nowSeconds);
        return GovernorAction.StepUp;
      }

      // Pinned at an end of the ladder: stay put. Counters are left saturated so
      // the instant the ladder gains headroom (index changes) we do not have to
      // re-earn it, but no change is emitted and nothing oscillates.
      return GovernorAction.None;
    }

    void Commit(double now)
    {
      ChangeCount++;
      lastChangeTime = now;
      cooldownUntil = now + CooldownSeconds;
      BeginIgnoreWindow(now, PostChangeIgnoreSeconds);
    }

    int Clamp(int i)
    {
      if (i < 0) return 0;
      if (i > ladderLength - 1) return ladderLength - 1;
      return i;
    }

    // --- ring helpers -------------------------------------------------------

    void Push(double t, double w)
    {
      sampleTime[head] = t;
      sampleWork[head] = w;
      head = (head + 1) % Capacity;
      if (count < Capacity) count++;
    }

    /// Copies the work values with timestamp >= cutoff into scratch[0..n).
    int CollectWindow(double cutoff)
    {
      int n = 0;
      for (int k = 0; k < count; k++)
      {
        int i = head - 1 - k;
        if (i < 0) i += Capacity;
        if (sampleTime[i] < cutoff) break;   // ring is ordered newest-first here
        scratch[n++] = sampleWork[i];
      }
      return n;
    }

    /// p90 of scratch[0..n) — insertion sort, n is ~90 and nearly sorted runs
    /// are common, so this is cheaper than allocating for Array.Sort<double>.
    double Percentile90(int n)
    {
      for (int i = 1; i < n; i++)
      {
        double v = scratch[i];
        int j = i - 1;
        while (j >= 0 && scratch[j] > v) { scratch[j + 1] = scratch[j]; j--; }
        scratch[j + 1] = v;
      }
      int idx = (int)Math.Ceiling(0.9 * n) - 1;
      if (idx < 0) idx = 0;
      if (idx > n - 1) idx = n - 1;
      return scratch[idx];
    }
  }
}
