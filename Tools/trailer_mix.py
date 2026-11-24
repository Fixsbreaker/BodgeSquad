"""Soundtrack for the Bodge Squad trailer: a sneaky pizzicato 'heist' tune plus the game's own
cartoon SFX, placed at the cues the trailer director logged (events.txt). Pure Python, 22.05 kHz mono.

Usage: python3 trailer_mix.py <frames/events.txt> <Docs/Sfx dir> <out.wav> <length seconds>
"""
import math, random, struct, sys, wave

events_path, sfx_dir, out_path, length = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
R = 22050
N = int(length * R) + R
mus = [0.0] * N
fx = [0.0] * N
rnd = random.Random(5)


def hz(semi_from_a4): return 440.0 * 2 ** (semi_from_a4 / 12)


def pluck(buf, t, semi, dur, vol, bright=0.35):
    f = hz(semi); i0 = int(t * R); n = int(dur * R)
    for i in range(n):
        if i0 + i >= N: break
        u = i / R
        env = math.exp(-u * 9.0) * min(1.0, u / 0.004)
        s = math.sin(2 * math.pi * f * u) + bright * math.sin(4 * math.pi * f * u) + 0.12 * math.sin(6 * math.pi * f * u)
        buf[i0 + i] += s * env * vol


def bass(buf, t, semi, dur, vol):
    f = hz(semi); i0 = int(t * R); n = int(dur * R)
    for i in range(n):
        if i0 + i >= N: break
        u = i / R
        env = math.exp(-u * 5.5) * min(1.0, u / 0.006)
        buf[i0 + i] += (math.sin(2 * math.pi * f * u) + 0.4 * math.sin(4 * math.pi * f * u)) * env * vol


def tick(buf, t, vol):
    i0 = int(t * R); lp = 0.0
    for i in range(int(0.03 * R)):
        if i0 + i >= N: break
        x = rnd.uniform(-1, 1); hp = x - lp; lp += (x - lp) * 0.3
        buf[i0 + i] += hp * math.exp(-i / R * 180) * vol


def kick(buf, t, vol):
    i0 = int(t * R); ph = 0.0
    for i in range(int(0.18 * R)):
        if i0 + i >= N: break
        u = i / R; f = 45 + 90 * math.exp(-u * 30); ph += f / R
        buf[i0 + i] += math.sin(2 * math.pi * ph) * math.exp(-u * 16) * vol


# ---- music: A minor, 112 bpm. Am | F | Dm | E (one bar each)
bpm = 112.0; beat = 60.0 / bpm; bar = 4 * beat
prog = [(-24, [0, 3, 7]), (-28, [-4, 0, 3]), (-31, [-7, -4, 0]), (-29, [-5, -1, 2])]
melody = [12, None, 15, 12, None, 10, 12, None,   8, None, 12, 8, 7, None, 8, None,
          5, None, 8, 5, None, 3, 5, None,          4, None, 7, 11, 12, None, None, None]
build = 26.0  # the inspector arrives: drums come in
for b in range(int(length / bar) + 1):
    t0 = b * bar
    if t0 > length - 5.5: break  # the end card gets a stinger instead
    root, chord = prog[b % 4]
    for k, off in enumerate([0, 7, 12, 11 if b % 4 == 3 else 10]):
        bass(mus, t0 + k * beat, root + off - (12 if k == 2 else 0), beat * 0.9, 0.42)
    for k in range(4):
        for c in chord: pluck(mus, t0 + k * beat + beat * 0.5, c - 12, 0.18, 0.07, 0.2)
    if b >= 1:
        for k in range(8):
            n = melody[(b % 4) * 8 + k]
            if n is not None: pluck(mus, t0 + k * beat * 0.5, n - 12 + (12 if b % 8 >= 4 else 0), 0.22, 0.16)
    if t0 >= build:
        for k in range(8): tick(mus, t0 + k * beat * 0.5, 0.10 if k % 2 else 0.05)
        kick(mus, t0, 0.5); kick(mus, t0 + 2 * beat, 0.45)


# ---- SFX
def load(path):
    w = wave.open(path); sr = w.getframerate(); raw = w.readframes(w.getnframes())
    d = struct.unpack(f"<{len(raw) // 2}h", raw)
    if sr != R:
        ratio = sr / R
        d = [d[int(i * ratio)] for i in range(int(len(d) / ratio))]
    return [x / 32768.0 for x in d]


sfx = {k: load(f"{sfx_dir}/inspector_{n}.wav") for k, n in enumerate(["bonk", "splat", "grumble", "slip"])}


def put(buf, t, data, vol):
    i0 = int(t * R)
    for i, x in enumerate(data):
        if i0 + i >= N: break
        buf[i0 + i] += x * vol


def whoosh(t):
    i0 = int(t * R); n = int(0.32 * R); lp = 0.0
    for i in range(n):
        if i0 + i >= N: break
        u = i / n; a = 0.05 + 0.5 * u
        lp += (rnd.uniform(-1, 1) - lp) * a
        fx[i0 + i] += lp * math.sin(math.pi * u) * 0.5


def stamp(t):
    kick(fx, t, 0.6)
    i0 = int(t * R); lp = 0.0
    for i in range(int(0.12 * R)):
        if i0 + i >= N: break
        lp += (rnd.uniform(-1, 1) - lp) * 0.5
        fx[i0 + i] += lp * math.exp(-i / R * 40) * 0.45


def radio(t0, t1):
    notes = [3, 7, 10, 7, 12, 10, 7, None, 5, 8, 12, 8, 15, 12, 8, None]
    step = 60 / 132 / 2; t = t0; k = 0
    while t < t1:
        n = notes[k % len(notes)]
        if n is not None:
            f = hz(n); i0 = int(t * R)
            for i in range(int(step * R)):
                if i0 + i >= N or (i0 + i) / R > t1: break
                u = i / R
                fx[i0 + i] += (0.18 if (u * f) % 1 < 0.5 else -0.18) * math.exp(-u * 7)
        t += step; k += 1


radio_on = None
t_end_card = None
for line in open(events_path):
    parts = line.split()
    if len(parts) != 2: continue
    t, what = float(parts[0]), parts[1]
    if what.startswith("yell"): put(fx, t, sfx[int(what[4])], 0.9)
    elif what == "throw": whoosh(t)
    elif what == "stamp": stamp(t)
    elif what == "slip_player": put(fx, t, sfx[3], 0.8)
    elif what == "card":   # a bright "ding" as each knack card pops
        pluck(fx, t, 7, 0.25, 0.35, 0.5); pluck(fx, t + 0.04, 14, 0.2, 0.25, 0.4)
    elif what == "talk":   # chatter while the Smooth Talker keeps him busy
        put(fx, t + 0.2, sfx[2], 0.45); put(fx, t + 1.4, sfx[2], 0.4); put(fx, t + 2.7, sfx[2], 0.35)
    elif what == "radio_on": radio_on = t
    elif what == "radio_off" and radio_on is not None: radio(radio_on, t); radio_on = None
    elif what == "end": t_end_card = t

if t_end_card is not None:
    for c in [-12, -5, 0, 3, 7, 14]: pluck(mus, t_end_card + 0.05, c, 2.5, 0.16, 0.5)
    bass(mus, t_end_card + 0.05, -36, 2.0, 0.6); kick(mus, t_end_card + 0.05, 0.7)

# ---- mix: duck the music under the effects, soft-limit, fade in/out
env = 0.0; out = []
for i in range(N):
    t = i / R
    env = max(abs(fx[i]), env * 0.9995)
    duck = 1.0 - min(0.55, env * 0.8)
    s = mus[i] * 0.85 * duck + fx[i]
    g = min(1.0, t / 0.4) * max(0.0, min(1.0, (length - t) / 1.5))
    s = math.tanh(s * 1.1) * 0.9 * g
    out.append(int(max(-1, min(1, s)) * 32767))
w = wave.open(out_path, "wb"); w.setnchannels(1); w.setsampwidth(2); w.setframerate(R)
w.writeframes(struct.pack(f"<{len(out)}h", *out)); w.close()
print("wrote", out_path, len(out) / R, "s")
