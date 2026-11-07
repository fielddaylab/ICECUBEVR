#!/usr/bin/env python3
"""N17 before/after capture: frame time + screenshots for a served WebGL build.

Drives a headless Chrome (CDP on --port) at --url, waits for the Unity player
to boot, injects a requestAnimationFrame probe that records per-frame deltas,
walks the experience with SPACE presses (one per phase), and at every phase
records:

  * p50 / p95 of the rAF frame interval (ms) sampled over --phase-seconds
  * a PNG screenshot  (<out>/<NN>_<label>.png)

Run it once against the baseline build and once against the trimmed build
(same script, same cadence), then feed both dirs to tools/n17_diff.py.

SwiftShader numbers are NOT acceptance evidence (see tools/MEASURE.md); they
are a regression signal only. The owner's Chromebook run uses the N1
PERFPROBE console lines instead - this script also harvests any PERFPROBE
line it sees so the same JSON shape works for both.

Usage:
  python3 tools/n17_capture.py --url http://127.0.0.1:8046/ --out /tmp/n17/after
  python3 tools/n17_capture.py --url http://127.0.0.1:8042/ --out /tmp/n17/before
"""
import argparse, base64, json, os, time, urllib.request
import websocket

PROBE_JS = r"""
(function(){
  if (window.__n17) return "already";
  var s = {deltas: [], last: null};
  function tick(t){
    if (s.last !== null) { var d = t - s.last; if (d < 1000) s.deltas.push(d); }
    s.last = t;
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);
  window.__n17 = s;
  window.__n17_reset = function(){ s.deltas.length = 0; };
  window.__n17_stats = function(){
    var a = s.deltas.slice().sort(function(x,y){return x-y;});
    if (!a.length) return JSON.stringify({n:0});
    function q(p){ return a[Math.min(a.length-1, Math.floor(p*a.length))]; }
    var mean = a.reduce(function(x,y){return x+y;},0)/a.length;
    return JSON.stringify({n:a.length, p50_ms:q(0.5), p95_ms:q(0.95), mean_ms:mean});
  };
  return "installed";
})()
"""

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=9231)
    ap.add_argument("--boot-seconds", type=float, default=150)
    ap.add_argument("--phase-seconds", type=float, default=20)
    ap.add_argument("--phases", type=int, default=6)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    tabs = json.load(urllib.request.urlopen("http://127.0.0.1:%d/json" % args.port))
    page = next(t for t in tabs if t["type"] == "page")
    ws = websocket.create_connection(page["webSocketDebuggerUrl"], timeout=60,
                                     max_size=64 * 1024 * 1024)
    seq = [0]
    logs = []

    def send(method, **params):
        seq[0] += 1
        ws.send(json.dumps({"id": seq[0], "method": method, "params": params}))
        return seq[0]

    def pump(msg):
        if msg.get("method") == "Runtime.consoleAPICalled":
            logs.append(" ".join(str(a.get("value", "")) for a in msg["params"].get("args", [])))

    def wait(req_id):
        ws.settimeout(120)
        while True:
            msg = json.loads(ws.recv())
            pump(msg)
            if msg.get("id") == req_id:
                return msg.get("result", {})

    def drain(sec):
        end = time.time() + sec
        ws.settimeout(1.0)
        while time.time() < end:
            try:
                pump(json.loads(ws.recv()))
            except Exception:
                pass

    def evaluate(expr):
        r = wait(send("Runtime.evaluate", expression=expr, returnByValue=True))
        return r.get("result", {}).get("value")

    def shot(name):
        r = wait(send("Page.captureScreenshot", format="png"))
        data = r.get("data")
        if not data:
            return None
        p = os.path.join(args.out, name + ".png")
        with open(p, "wb") as f:
            f.write(base64.b64decode(data))
        return p

    def space():
        for t in ("keyDown", "char", "keyUp"):
            kw = dict(type=t, key=" ", code="Space",
                      windowsVirtualKeyCode=32, nativeVirtualKeyCode=32)
            if t == "char":
                kw["text"] = " "
            send("Input.dispatchKeyEvent", **kw)

    send("Runtime.enable"); send("Page.enable")
    send("Page.navigate", url=args.url)
    print("booting", args.url)
    drain(args.boot_seconds)
    print("probe:", evaluate(PROBE_JS))
    send("Input.dispatchMouseEvent", type="mousePressed", x=640, y=360, button="left", clickCount=1)
    send("Input.dispatchMouseEvent", type="mouseReleased", x=640, y=360, button="left", clickCount=1)
    drain(3)

    results = {"url": args.url, "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
               "phases": []}

    def phase(i, label):
        evaluate("window.__n17_reset()")
        drain(args.phase_seconds)
        st = json.loads(evaluate("window.__n17_stats()") or "{}")
        p = shot("%02d_%s" % (i, label))
        st.update(index=i, label=label, shot=os.path.basename(p) if p else None)
        results["phases"].append(st)
        print("%02d %-10s n=%-4s p50=%7.2f ms  p95=%7.2f ms" % (
            i, label, st.get("n"), st.get("p50_ms", float("nan")), st.get("p95_ms", float("nan"))))

    phase(0, "boot")
    for i in range(1, args.phases + 1):
        space()
        drain(6)
        phase(i, "space%d" % i)

    results["perfprobe"] = [l for l in logs if "PERFPROBE" in l]
    results["governor"] = sorted(set(l.split("\n")[0][:160] for l in logs if "GOVERNOR" in l))
    results["errors"] = [l[:200] for l in logs if "rror" in l][:20]
    with open(os.path.join(args.out, "frames.json"), "w") as f:
        json.dump(results, f, indent=2)
    print("wrote", os.path.join(args.out, "frames.json"))

if __name__ == "__main__":
    main()
