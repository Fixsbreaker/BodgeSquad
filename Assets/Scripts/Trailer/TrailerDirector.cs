using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BodgeSquad.Art;
using BodgeSquad.Job;
using BodgeSquad.Players;
using BodgeSquad.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BodgeSquad.Trailer
{
    // Records the itch.io trailer from the real game: `-bodgeHostLan -bodgeTrailer <dir>`.
    // Hosts a job, stages each scene with stand-in bodgers (copies of the player model) and the real
    // inspector AI, films it with its own camera at a fixed 30 fps into numbered JPGs, and logs sound
    // cues (events.txt) for the soundtrack. Captions are drawn in-game. Never runs in normal play.
    public class TrailerDirector : MonoBehaviour
    {
        const int W = 1920, H = 1080, Fps = 30;

        string _dir;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _grab;
        int _frame;
        bool _recording;
        StreamWriter _events;
        Inspector _insp;
        JobManager _job;
        NetworkBodger _host;
        FirstPersonController _hostFpc;
        RectTransform _ui;
        TextMeshProUGUI _caption, _feed, _stamp;
        RectTransform _captionStrip;
        Coroutine _captionAnim, _stampAnim;
        float _feedUntil;
        readonly Actor[] _actors = new Actor[4];

        float Now => _frame / (float)Fps;

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-bodgeTrailer");
            _dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "trailer");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        // ================================================================ the cut
        IEnumerator Run()
        {
            while (NetworkBodger.Local == null || JobManager.Instance == null || !JobManager.Instance.IsSpawned || Inspector.Instance == null || !Inspector.Instance.IsSpawned)
                yield return null;
            yield return new WaitForSeconds(1.5f);
            _job = JobManager.Instance;
            _insp = Inspector.Instance;
            _host = NetworkBodger.Local;
            _hostFpc = _host.GetComponent<FirstPersonController>();
            _insp.ScriptHoldOutside();
            Inspector.IgnoreWork = true;
            Setup();
            Park();

            // the cast: P1 orange hard hat … P4 lilac goggles (P1 is a stand-in too; the real host hides)
            _actors[0] = MakeActor(0, new Vector3(-1.35f, 0, -1.2f), 180 - 15);
            _actors[1] = MakeActor(1, new Vector3(-0.45f, 0, -1.2f), 180 - 5);
            _actors[2] = MakeActor(2, new Vector3(0.45f, 0, -1.2f), 180 + 5);
            _actors[3] = MakeActor(3, new Vector3(1.35f, 0, -1.2f), 180 + 15);
            yield return null;

            Time.captureFramerate = Fps;
            _recording = true;
            StartCoroutine(Capture());
            StartCoroutine(Watch());
            Cue("music");

            // 1 — title: the squad, slow push in --------------------------------------------- 6 s
            Title("BODGE SQUAD", "a co-op renovation game for 1–4 bodgers", 5.4f);
            foreach (var a in _actors) a.Cheer(5.5f, UnityEngine.Random.Range(0f, 1f));
            yield return Shot(6f, u => (Vector3.Lerp(new Vector3(0.1f, 1.7f, -5.2f), new Vector3(0.1f, 1.5f, -4.0f), Ease(u)), new Vector3(0, 1.45f, -1.2f)));
            foreach (var a in _actors) a.Stand();

            // 2 — the damage: three quick close-ups -------------------------------------------- 6.6 s
            Caption("THE HOUSE IS A WRECK", 6.2f);
            var crack = DefectAt("north wall"); var mould = DefectAt("north corner"); var wiring = DefectAt("west wall");
            yield return CloseUp(crack, 2.2f, 2.1f);
            yield return CloseUp(mould, 2.2f, 1.7f);
            yield return CloseUp(wiring, 2.2f, 1.9f);

            // 3 — paint over it ------------------------------------------------------------ 6 s
            var p2 = _actors[1];
            p2.Place(crack.transform.position + crack.transform.forward * 0.75f, crack.transform.position);
            _actors[0].Place(new Vector3(-4.8f, 0, 4.2f), Vector3.zero); _actors[2].Place(new Vector3(5.2f, 0, -4.2f), Vector3.zero); _actors[3].Place(new Vector3(-5f, 0, -4f), Vector3.zero);
            Caption("NO MONEY TO FIX IT PROPERLY?", 2.6f);
            StartCoroutine(PaintOver(p2, crack, 5.6f, 1));
            yield return Shot(6f, u =>
            {
                if (Mathf.Abs(u - 0.47f) < 0.01f) Caption("PAINT OVER IT.", 3f);
                return (Vector3.Lerp(new Vector3(4.6f, 1.6f, 2.3f), new Vector3(4.2f, 1.5f, 2.9f), Ease(u)), new Vector3(2.4f, 1.3f, 4.7f)); // from the side: him and the wall
            });

            // 4 — or just hide it behind the furniture ------------------------------------- 5 s
            var shelf = GameObject.Find("Bookshelf");
            var hole = DefectAt("east wall");
            Caption("OR SHOVE A SHELF IN FRONT OF IT", 4.6f);
            if (shelf != null) StartCoroutine(Shove(shelf.transform, new Vector3(5.72f, 0f, hole.transform.position.z), _actors[2], 3.2f));
            yield return Shot(5f, u => (Vector3.Lerp(new Vector3(2.2f, 1.9f, 3.0f), new Vector3(2.6f, 1.8f, 2.6f), Ease(u)), new Vector3(4.9f, 0.8f, 0.1f)));

            // 5 — the inspector drops in -------------------------------------------------------- 5 s
            Caption("BUT THE INSPECTOR DROPS IN EARLY...", 4.6f);
            p2.Place(new Vector3(-1.3f, 0, 1.9f), new Vector3(-3.5f, 0, 5f));
            _actors[0].Place(new Vector3(-0.6f, 0, 1.4f), new Vector3(-3.5f, 0, 5f));
            _insp.ScriptArriveNow();
            yield return Shot(5f, u => (Vector3.Lerp(new Vector3(0.6f, 1.75f, 3.9f), new Vector3(0.1f, 1.65f, 3.7f), Ease(u)), new Vector3(-2.9f, 1.05f, 3.4f))); // along the north wall: the door and him coming through it

            // 6 — his view, drawn on the floor -------------------------------------------------- 4 s
            Caption("DON'T LET HIM SEE YOU BODGE", 4f);
            _insp.ScriptGoTo(new Vector3(-3.6f, 0, 1.8f));
            p2.WalkTo(new Vector3(1.2f, 0, 1f));
            _actors[0].WalkTo(new Vector3(0.4f, 0, 3.6f));
            yield return Shot(4f, u =>
            {
                var ip = _insp.transform.position;
                return (ip + new Vector3(2.8f, 3.4f, -2.2f), ip + _insp.transform.forward * 2.2f);
            });

            // 7 — caught red-handed ----------------------------------------------------------- 6 s
            var p1 = _actors[0];
            var paintSpot = new Vector3(-5.05f, 0, 1.8f);
            p1.Place(paintSpot, wiring.transform.position);
            _hostFpc.Teleport(paintSpot, Quaternion.LookRotation(Vector3.left)); // the real bodger he actually sees
            _insp.ScriptLookAt(wiring.transform.position, 9f);
            StartCoroutine(PaintOver(p1, wiring, 5.5f, 0));
            int finesBefore = _job.Fines;
            bool stamped = false;
            yield return Shot(6f, u =>
            {
                if (u > 0.12f && Inspector.IgnoreWork) Inspector.IgnoreWork = false; // a beat of painting first
                if (!stamped && _job.Fines > finesBefore) { stamped = true; Stamp("CAUGHT RED-HANDED!\n<size=60%>−$100</size>"); p1.Stop(); }
                return (Vector3.Lerp(new Vector3(-2.3f, 1.85f, 3.0f), new Vector3(-2.6f, 1.75f, 2.7f), Ease(u)), new Vector3(-4.9f, 1.1f, 1.7f));
            });
            Inspector.IgnoreWork = true;
            Park();

            // 8 — distract him: the radio ------------------------------------------------------- 7 s
            Caption("DISTRACT HIM", 6.5f);
            var p3 = _actors[2]; var p4 = _actors[3];
            p3.Place(Inspector.RadioSpot + new Vector3(0.75f, 0, 0.6f), Inspector.RadioSpot);
            var southMould = DefectAt("south wall"); var highCrack = DefectAt("east wall, up high");
            p2.Place(southMould.transform.position + southMould.transform.forward * 0.7f, southMould.transform.position);
            p4.Place(highCrack.transform.position + highCrack.transform.forward * 0.8f, highCrack.transform.position);
            p1.Place(new Vector3(-1.5f, 0, 3.6f), Vector3.zero);
            bool radioOn = false;
            yield return Shot(7f, u =>
            {
                if (!radioOn && u > 0.1f) { radioOn = true; p3.Press(); _insp.ToggleRadioRpc(2); StartCoroutine(PaintOver(p2, southMould, 5f, 1)); StartCoroutine(PaintOver(p4, highCrack, 5f, 3)); }
                return (Vector3.Lerp(new Vector3(4.6f, 2.9f, 3.6f), new Vector3(4.0f, 2.6f, 3.0f), Ease(u)), new Vector3(-1.2f, 0.5f, -2.4f));
            });

            // 8b — everyone gets a knack ---------------------------------------------------------- 4 s
            Caption("EVERY BODGER GETS A RANDOM KNACK", 3.8f);
            // park him by the west wall for the next two shots, staring at the radio
            var chatSpot = new Vector3(-3.6f, 0f, -0.8f);
            _insp.ScriptWarp(chatSpot, Inspector.RadioSpot);
            _insp.ScriptLookAt(Inspector.RadioSpot, 20f);
            for (int k = 0; k < 4; k++)
            {
                _actors[k].Place(new Vector3(-0.9f + k * 0.85f, 0, -1.6f), new Vector3(-0.9f + k * 0.85f, 0, -5f));
                _actors[k].Cheer(4f, k * 0.15f);
            }
            KnackCards(3.9f);
            yield return Shot(4f, u => (Vector3.Lerp(new Vector3(0.4f, 1.2f, -4.7f), new Vector3(0.4f, 1.15f, -4.2f), Ease(u)), new Vector3(0.4f, 1.35f, -1.6f)));
            foreach (var a in _actors) a.Stand();

            // 8c — Smooth Talker keeps him chatting while a mate bodges behind his back ------------ 6 s
            var lowHole = DefectAt("west wall, low");
            var ip8 = _insp.transform.position;
            var talkSpot = Clamp(ip8 + new Vector3(1.35f, 0, 0.15f)); talkSpot.y = 0f; // east of him: he turns his back to the west wall
            p1.Place(talkSpot, ip8);
            _hostFpc.Teleport(talkSpot, Quaternion.LookRotation(ip8 - talkSpot)); // the real bodger does the talking
            _host.SetKnack(Knack.SmoothTalker);
            p2.Place(lowHole.transform.position + lowHole.transform.forward * 0.7f, lowHole.transform.position);
            Caption("SMOOTH TALKER: KEEP HIM CHATTING...", 2.9f);
            bool talked = false, mates = false;
            var talkCam = new Vector3(-1.7f, 1.75f, 1.9f);
            yield return Shot(6f, u =>
            {
                if (!talked && u > 0.08f)
                {
                    talked = true;
                    _host.GetComponent<KnackAbility>().TryUse();
                    p1.Talk(5.5f);
                    StartCoroutine(PaintOver(p2, lowHole, 5f, 1));
                    Cue("talk");
                }
                if (!mates && u > 0.5f) { mates = true; Caption("...WHILE YOUR MATES BODGE", 2.9f); }
                var mid = Vector3.Lerp(_insp.transform.position, lowHole.transform.position, 0.35f) + Vector3.up * 0.9f;
                return (Vector3.Lerp(talkCam, talkCam + new Vector3(-0.35f, -0.1f, -0.35f), Ease(u)), mid);
            });
            _insp.ScriptLookAt(_insp.transform.position + _insp.transform.forward, 4f); // stays put for the paint tin
            Park();

            // 9 — blind him: a paint tin to the face, then he skids in it ---------------------- 7 s
            Caption("BLIND HIM", 3f);
            p3.Place(new Vector3(2.4f, 0, 3.4f), Vector3.zero); // clear the stage: only the thrower and him
            p1.Place(new Vector3(3.2f, 0, 3.0f), Vector3.zero);
            p2.Place(new Vector3(1.6f, 0, 3.8f), Vector3.zero);
            var thrower = _actors[3];
            var ipos = _insp.transform.position;
            var behind = ipos - _insp.transform.forward * 2.0f + _insp.transform.right * 0.3f; behind.y = 0; behind = Clamp(behind);
            thrower.Place(behind, ipos);
            _hostFpc.Teleport(behind, Quaternion.LookRotation(ipos - behind)); // "did he see who threw it?" — no
            bool thrown = false, slipCued = false;
            var camFrom = SideOf(ipos, 3.1f);
            yield return Shot(7f, u =>
            {
                if (!thrown && u > 0.08f) { thrown = true; StartCoroutine(Lob(thrower, KitType.PaintTin)); }
                if (!slipCued && _insp.CurrentMode == Inspector.Mode.Slipped) { slipCued = true; Stamp("WHOA!"); Caption("WATCH YOUR STEP...", 2.6f); }
                if (u > 0.52f && !slipCued && _insp.CurrentMode != Inspector.Mode.Slipped && Spills.Instance != null && _insp.CurrentMode != Inspector.Mode.Blinded)
                {
                    _insp.ScriptAllowSlip();
                    Spills.Instance.SpillRpc(Floor(_insp.transform.position + _insp.transform.forward * 0.3f), 0.8f); // make sure he finds it
                }
                var at = _insp.transform.position + Vector3.up * (_insp.CurrentMode == Inspector.Mode.Slipped ? 0.4f : 1.2f);
                return (Safe(at, Vector3.Lerp(camFrom, camFrom + (at - camFrom).normalized * 0.6f, Ease(u))), at);
            });
            Park();

            // 10 — or just bonk him -------------------------------------------------------------- 5 s
            Caption("...OR JUST BONK HIM", 4.6f);
            float wait = 0f;
            while (_insp.CurrentMode == Inspector.Mode.Slipped && wait < 2f) { wait += Time.deltaTime; yield return null; } // let him get up (on camera)
            _insp.ScriptClearImmunity();
            ipos = _insp.transform.position;
            behind = Clamp(ipos - _insp.transform.forward * 2.0f - _insp.transform.right * 0.4f); behind.y = 0;
            p2.Place(behind, ipos);
            _hostFpc.Teleport(behind, Quaternion.LookRotation(ipos - behind));
            bool bonked = false;
            camFrom = SideOf(ipos, 2.9f);
            yield return Shot(5f, u =>
            {
                if (!bonked && u > 0.1f) { bonked = true; StartCoroutine(Lob(p2, KitType.Filler)); }
                var at = _insp.transform.position + Vector3.up * 1.4f;
                return (Safe(at, camFrom), at);
            });
            Park();

            // 11 — and watch your own step ------------------------------------------------------- 4.5 s
            Caption("FRESH PAINT IS SLIPPERY", 4.2f);
            _insp.ScriptWarp(new Vector3(-3.4f, 0f, 1.6f), new Vector3(0.9f, 0f, -2.4f)); // out of the way, watching from the side
            var puddleAt = Floor(new Vector3(0.9f, 0f, -2.4f));
            Spills.Instance.SpillRpc(puddleAt, 0.85f);
            p3.Place(new Vector3(-2.6f, 0, -2.4f), new Vector3(3f, 0, -2.4f));
            p3.Run(new Vector3(3.2f, 0, -2.4f), 4.6f, puddleAt);
            yield return Shot(4.5f, u => (new Vector3(0.6f, 0.75f, -4.6f), Vector3.Lerp(new Vector3(-1.4f, 0.6f, -2.4f), new Vector3(1.4f, 0.4f, -2.4f), Ease(Mathf.Clamp01(u * 1.6f)))));

            // 12 — the whole squad, all the chaos -------------------------------------------------- 4.5 s
            Caption("1–4 PLAYERS  ·  ONLINE CO-OP", 4.2f);
            _insp.ScriptClearImmunity();
            _insp.ScriptWarp(new Vector3(0.4f, 0f, 0.6f), new Vector3(0.4f, 0f, -3f));
            yield return null;
            ipos = _insp.transform.position;
            for (int k = 0; k < 4; k++)
            {
                float ang = k * Mathf.PI * 0.5f + 0.4f;
                var spot = Clamp(ipos + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 1.6f); spot.y = 0;
                _actors[k].Place(spot, ipos);
                _actors[k].Cheer(4.5f, k * 0.23f);
            }
            _insp.ServerHit(NetworkManager.Singleton.LocalClientId, KitType.Filler);
            Cue("bonk_extra");
            yield return Shot(4.5f, u =>
            {
                float a = Mathf.Lerp(-40f, 40f, u) * Mathf.Deg2Rad;
                var c = ipos + new Vector3(0, 1.0f, 0);
                return (Safe(c, c + new Vector3(Mathf.Sin(a) * 3.6f, 1.4f, -Mathf.Cos(a) * 3.6f)), c);
            });

            // 13 — end card ---------------------------------------------------------------------- 5.5 s
            EndCard();
            Cue("end");
            yield return Shot(5.5f, u =>
            {
                float a = Mathf.Lerp(40f, 70f, u) * Mathf.Deg2Rad;
                var c = ipos + new Vector3(0, 1.0f, 0);
                return (Safe(c, c + new Vector3(Mathf.Sin(a) * 3.8f, 1.5f, -Mathf.Cos(a) * 3.8f)), c);
            });

            _recording = false;
            yield return null;
            _events.Flush(); _events.Close();
            Debug.Log($"[Trailer] Done: {_frame} frames ({Now:0.0}s) in {_dir}");
            Time.captureFramerate = 0;
            Application.Quit();
        }

        // ================================================================ staging helpers
        static float Ease(float u) => u * u * (3f - 2f * u);
        static Vector3 Clamp(Vector3 p) => new(Mathf.Clamp(p.x, -5.4f, 5.4f), p.y, Mathf.Clamp(p.z, -4.4f, 4.4f));

        static Vector3 Floor(Vector3 p)
        {
            p.y = 0f;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var h, 3f, ~((1 << 2) | (1 << NetworkBodger.LocalBodyLayer)), QueryTriggerInteraction.Ignore)
                && h.collider.GetComponentInParent<Inspector>() == null) p = h.point;
            return p;
        }

        // A camera spot beside him, towards the middle of the room (never inside a wall)
        Vector3 SideOf(Vector3 target, float dist)
        {
            var toCentre = new Vector3(-target.x, 0, -target.z).normalized;
            var side = Vector3.Cross(Vector3.up, toCentre) * 0.6f;
            var p = target + (toCentre + side).normalized * dist + Vector3.up * 1.6f;
            return Clamp(p);
        }

        // Pull the camera in front of anything solid between it and what it's looking at
        static Vector3 Safe(Vector3 target, Vector3 cam)
        {
            var d = cam - target;
            float len = d.magnitude;
            if (len < 0.01f) return cam;
            var mask = ~((1 << 2) | (1 << NetworkBodger.LocalBodyLayer));
            foreach (var h in Physics.SphereCastAll(target, 0.25f, d / len, len, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.distance <= 0f || h.collider.GetComponentInParent<Inspector>() != null) continue;
                if (h.distance < len) len = h.distance;
            }
            return target + d.normalized * Mathf.Max(0.8f, len - 0.15f);
        }

        // The real host bodger stays out of shot unless a scene needs it
        void Park() => _hostFpc.Teleport(new Vector3(5.3f, 0f, 4.3f), Quaternion.LookRotation(Vector3.back));

        Defect DefectAt(string label)
        {
            foreach (var d in _job.Defects) if (d.Label == label) return d;
            return _job.Defects[0];
        }

        IEnumerator Shot(float seconds, Func<float, (Vector3 pos, Vector3 look)> path)
        {
            float t = 0f;
            while (t < seconds)
            {
                var (p, l) = path(Mathf.Clamp01(t / seconds));
                var dir = l - p;
                if (dir.sqrMagnitude > 0.0001f) _cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir));
                yield return null;
                t += Time.deltaTime;
            }
        }

        IEnumerator CloseUp(Defect d, float seconds, float dist)
        {
            var n = d.transform.forward;
            var target = d.transform.position;
            var from = target + n * dist + Vector3.up * 0.15f + d.transform.right * 0.35f;
            var to = target + n * (dist * 0.72f) + Vector3.up * 0.05f;
            yield return Shot(seconds, u => (Vector3.Lerp(from, to, Ease(u)), target));
        }

        // Brush paint all over a defect like a player would: marks everyone sees + covered cells
        IEnumerator PaintOver(Actor a, Defect d, float seconds, int slot)
        {
            a.Paint(true);
            float t = 0f, next = 0f;
            var tr = d.transform;
            while (t < seconds && a.Painting)
            {
                if (t >= next)
                {
                    next = t + 0.06f;
                    var p = tr.position + tr.right * Mathf.Sin(t * 5.1f) * 0.42f + tr.up * Mathf.Cos(t * 3.3f) * 0.3f;
                    if (Physics.Raycast(p + tr.forward * 0.5f, -tr.forward, out var hit, 1f, ~((1 << 2) | (1 << NetworkBodger.LocalBodyLayer)), QueryTriggerInteraction.Ignore))
                    {
                        SurfaceMarks.Instance.Place(KitType.PaintTin, hit, 0.27f * UnityEngine.Random.Range(0.9f, 1.15f), UnityEngine.Random.Range(0f, 360f));
                        if (d.State != RepairState.Fixed) d.AddCells(RepairRules.Layer.Paint, d.BrushMask(hit.point, 1.4f), slot);
                    }
                }
                yield return null;
                t += Time.deltaTime;
            }
            a.Paint(false);
        }

        // Shove a piece of furniture flat against the wall, with a bodger leaning into it
        IEnumerator Shove(Transform item, Vector3 to, Actor a, float seconds)
        {
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            var from = item.position; to.y = from.y;
            var dir = (to - from); dir.y = 0; dir.Normalize();
            float t = 0f;
            a.Push(true);
            while (t < seconds)
            {
                var p = Vector3.Lerp(from, to, Ease(t / seconds));
                if (rb != null) rb.MovePosition(p); else item.position = p;
                a.Place(p - dir * 0.85f, p);
                yield return null;
                t += Time.deltaTime;
            }
            a.Push(false);
            if (rb != null) rb.isKinematic = false;
        }

        // Lob something at the inspector's head; on contact it hits him for real
        IEnumerator Lob(Actor a, KitType kit)
        {
            a.Throw();
            Cue("throw");
            yield return new WaitForSeconds(0.12f);
            var go = kit == KitType.PaintTin ? TinModel() : PlankModel();
            var from = a.transform.position + a.transform.forward * 0.35f + a.transform.right * 0.35f + Vector3.up * 1.35f;
            float t = 0f, dur = 0.42f;
            var spin = UnityEngine.Random.onUnitSphere * 900f;
            while (t < dur)
            {
                var to = _insp.transform.position + Vector3.up * 1.55f;
                float u = t / dur;
                go.transform.position = Vector3.Lerp(from, to, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.35f;
                go.transform.Rotate(spin * Time.deltaTime, Space.World);
                yield return null;
                t += Time.deltaTime;
            }
            Destroy(go);
            _insp.ServerHit(NetworkManager.Singleton.LocalClientId, kit);
            if (kit == KitType.PaintTin)
            {
                Stamp("SPLAT!");
                Spills.Instance.SpillRpc(Floor(_insp.transform.position + _insp.transform.forward * 0.25f), 0.9f);
            }
            else Stamp("BONK!");
        }

        GameObject TinModel()
        {
            var root = new GameObject("TrailerTin");
            Prim(root.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.2f, 0.12f, 0.2f), Palette.FreshPaint);
            Prim(root.transform, PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0), new Vector3(0.21f, 0.01f, 0.21f), Palette.PlayerTints[2]);
            return root;
        }

        GameObject PlankModel()
        {
            var root = new GameObject("TrailerPlank");
            Prim(root.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.22f, 1.1f, 0.06f), Palette.Wood);
            return root;
        }

        static Material _flat;
        static void Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            var g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>();
            if (_flat == null) _flat = new Material(FindAnyObjectByType<Inspector>().GetComponentInChildren<Renderer>(true).sharedMaterial); // a lit material the build has
            r.sharedMaterial = _flat;
            var b = new MaterialPropertyBlock(); b.SetColor("_BaseColor", c); r.SetPropertyBlock(b);
        }

        // ================================================================ cast
        Actor MakeActor(int slot, Vector3 pos, float yaw)
        {
            var src = _host.transform.Find("Visual");
            var go = Instantiate(src.gameObject);
            go.name = $"Actor_P{slot + 1}";
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb); // BodgerMotion would pin it to the player
            go.SetActive(true);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            go.transform.localScale = Vector3.one;
            var hats = go.transform.Find("Hats");
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", Palette.PlayerTints[slot % Palette.PlayerTints.Length]);
            var tinted = new HashSet<string> { "Dome", "Brim", "Ridge", "Crown", "Bill", "Knit", "Cuff", "Pompom", "Hair", "Bun" };
            if (hats != null)
                for (int h = 0; h < hats.childCount; h++)
                {
                    var hat = hats.GetChild(h);
                    hat.gameObject.SetActive(h == slot % hats.childCount);
                    foreach (var r in hat.GetComponentsInChildren<Renderer>(true)) if (tinted.Contains(r.name)) r.SetPropertyBlock(tint);
                }
            var a = go.AddComponent<Actor>();
            a.Init();
            a.Place(pos, pos + Quaternion.Euler(0, yaw, 0) * Vector3.forward);
            // the real host body hides; the stand-ins do the acting
            if (slot == 0) src.gameObject.SetActive(false);
            return a;
        }

        // ================================================================ camera, captions, capture
        void Setup()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, name = "TrailerRT" };
            _grab = new Texture2D(W, H, TextureFormat.RGB24, false);
            var cg = new GameObject("TrailerCamera");
            _cam = cg.AddComponent<Camera>();
            _cam.fieldOfView = 50f;
            _cam.nearClipPlane = 0.05f;
            _cam.targetTexture = _rt;
            _cam.depth = 50;
            var data = cg.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var canvas = UiKit.Canvas("TrailerCaptions", null, RenderMode.ScreenSpaceCamera, _cam, 100);
            canvas.planeDistance = 0.3f;
            _ui = (RectTransform)canvas.transform;

            _captionStrip = UiKit.Panel("CaptionTape", _ui, new Vector2(.5f, 0), new Vector2(.5f, .5f), new Vector2(0, 150), new Vector2(1100, 118), Palette.TintYellow).rectTransform;
            _captionStrip.localRotation = Quaternion.Euler(0, 0, -1.6f);
            _caption = UiKit.Text("Caption", _captionStrip, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -2), new Vector2(1600, 120), 70, TextAlignmentOptions.Center, Palette.UiInk);
            _caption.fontStyle = FontStyles.Bold;
            _caption.outlineWidth = 0f;
            _caption.textWrappingMode = TextWrappingModes.NoWrap;
            _captionStrip.gameObject.SetActive(false);

            _feed = UiKit.Text("Feed", _ui, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(1700, 60), 36, TextAlignmentOptions.Bottom);
            _feed.fontStyle = FontStyles.Italic;

            _stamp = UiKit.Text("Stamp", _ui, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 210), new Vector2(1600, 300), 150, TextAlignmentOptions.Center, Palette.Accent);
            _stamp.fontStyle = FontStyles.Bold;
            _stamp.outlineWidth = 0.28f;
            _stamp.textWrappingMode = TextWrappingModes.NoWrap;
            _stamp.gameObject.SetActive(false);

            _events = new StreamWriter(Path.Combine(_dir, "events.txt"));
            Inspector.Yelled += k => Cue($"yell{k}");
            _job.Announce += OnAnnounce;
        }

        void OnAnnounce(string line)
        {
            // his own words make good subtitles; the scoreboard lines are covered by the stamps
            if (line.StartsWith("SPLAT") || line.StartsWith("BONK") || line.StartsWith("CAUGHT") || line.StartsWith("WHOA") || line.StartsWith("Knacks")) return;
            if (line.StartsWith("P") && !line.Contains("asks the inspector")) return;
            if (line.Contains("asks the inspector")) line = "\"So, inspector, " + line.Substring(line.IndexOf("about") + 6).Replace(". He can't stop talking!", "") + "?\"  He can't stop talking.";
            if (line.StartsWith("The inspector let himself")) line = "The inspector let himself in for a \"quick look around\".";
            _feed.text = line;
            _feedUntil = Now + 2.6f;
        }

        void Cue(string what) => _events?.WriteLine($"{Now:0.000} {what}");

        void Caption(string text, float hold)
        {
            if (_captionAnim != null) StopCoroutine(_captionAnim);
            _captionAnim = StartCoroutine(CaptionAnim(text, hold));
        }

        IEnumerator CaptionAnim(string text, float hold)
        {
            _caption.text = text;
            _caption.ForceMeshUpdate();
            _captionStrip.sizeDelta = new Vector2(_caption.preferredWidth + 110f, 118f);
            _captionStrip.gameObject.SetActive(true);
            float t = 0f;
            while (t < hold)
            {
                float inU = Mathf.Clamp01(t / 0.22f), outU = Mathf.Clamp01((hold - t) / 0.2f);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float back = 1f + c3 * Mathf.Pow(inU - 1f, 3) + c1 * Mathf.Pow(inU - 1f, 2);
                _captionStrip.localScale = new Vector3(back, 1f, 1f) * Mathf.Lerp(0.85f, 1f, outU);
                _captionStrip.anchoredPosition = new Vector2(Mathf.Lerp(-900f, 0f, back), 150f - (1f - outU) * 30f);
                yield return null;
                t += Time.deltaTime;
            }
            _captionStrip.gameObject.SetActive(false);
        }

        void Stamp(string text)
        {
            Cue("stamp");
            if (_stampAnim != null) StopCoroutine(_stampAnim);
            _stampAnim = StartCoroutine(StampAnim(text));
        }

        IEnumerator StampAnim(string text)
        {
            _stamp.text = text;
            _stamp.gameObject.SetActive(true);
            float t = 0f, hold = 1.5f;
            float tilt = UnityEngine.Random.Range(-9f, -4f);
            while (t < hold)
            {
                float slam = Mathf.Clamp01(t / 0.13f);
                float s = Mathf.Lerp(2.4f, 1f, slam * slam);
                float shake = t < 0.35f ? Mathf.Sin(t * 90f) * (0.35f - t) * 30f : 0f;
                _stamp.rectTransform.localScale = Vector3.one * s;
                _stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, tilt);
                _stamp.rectTransform.anchoredPosition = new Vector2(shake, 210f);
                _stamp.alpha = Mathf.Clamp01((hold - t) / 0.25f);
                yield return null;
                t += Time.deltaTime;
            }
            _stamp.gameObject.SetActive(false);
        }

        // Four knack cards popping in one after another, in the players' colours
        void KnackCards(float hold) => StartCoroutine(KnackCardsAnim(hold));

        IEnumerator KnackCardsAnim(float hold)
        {
            string[] names = { "SMOOTH TALKER", "SPEED PAINTER", "STRONGBACK", "LOOKOUT" };
            string[] lines = { "keeps him chatting", "paints twice as fast", "lifts the sofa alone", "sees him through walls" };
            var cards = new List<RectTransform>();
            for (int i = 0; i < 4; i++)
            {
                var card = UiKit.Panel($"KnackCard{i}", _ui, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-645 + i * 430, 345), new Vector2(400, 190), UiKit.WithAlpha(Palette.UiInk, 0.92f)).rectTransform;
                var tag = UiKit.Text("Who", card, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -14), new Vector2(380, 40), 30, TextAlignmentOptions.Center, Palette.PlayerTints[i]);
                tag.fontStyle = FontStyles.Bold; tag.text = $"P{i + 1}";
                var nm = UiKit.Text("Knack", card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 4), new Vector2(380, 60), 40, TextAlignmentOptions.Center, Palette.TintYellow);
                nm.fontStyle = FontStyles.Bold; nm.text = names[i];
                var ln = UiKit.Text("What", card, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(380, 40), 26, TextAlignmentOptions.Center, Palette.UiPaper);
                ln.fontStyle = FontStyles.Italic; ln.text = lines[i];
                card.localScale = Vector3.zero;
                cards.Add(card);
            }
            float t = 0f;
            var popped = new bool[4];
            while (t < hold)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    float u = Mathf.Clamp01((t - 0.25f - i * 0.28f) / 0.3f);
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float back = u <= 0f ? 0f : 1f + c3 * Mathf.Pow(u - 1f, 3) + c1 * Mathf.Pow(u - 1f, 2);
                    float fade = Mathf.Clamp01((hold - t) / 0.25f);
                    cards[i].localScale = Vector3.one * back * fade;
                    cards[i].localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? -2f : 2f));
                    if (u > 0f && !popped[i]) { popped[i] = true; Cue("card"); }
                }
                yield return null;
                t += Time.deltaTime;
            }
            foreach (var c in cards) Destroy(c.gameObject);
        }

        void Title(string logo, string tagline, float hold) => StartCoroutine(TitleAnim(logo, tagline, hold));

        IEnumerator TitleAnim(string logo, string tagline, float hold)
        {
            var big = UiKit.Text("Logo", _ui, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 330), new Vector2(1800, 240), 180, TextAlignmentOptions.Center, Palette.TintYellow);
            big.fontStyle = FontStyles.Bold; big.outlineWidth = 0.3f; big.characterSpacing = 4f;
            var sub = UiKit.Text("Tagline", _ui, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 195), new Vector2(1800, 90), 50, TextAlignmentOptions.Center, Palette.UiPaper);
            sub.fontStyle = FontStyles.Italic; sub.outlineWidth = 0.25f;
            big.text = logo; sub.text = tagline;
            float t = 0f;
            while (t < hold)
            {
                float a = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((hold - t) / 0.4f);
                big.alpha = a; sub.alpha = Mathf.Clamp01((t - 0.6f) / 0.5f) * Mathf.Clamp01((hold - t) / 0.4f);
                big.rectTransform.localScale = Vector3.one * (1.06f - 0.06f * Mathf.Clamp01(t / hold));
                yield return null;
                t += Time.deltaTime;
            }
            Destroy(big.gameObject); Destroy(sub.gameObject);
        }

        void EndCard()
        {
            var panel = UiKit.Panel("EndCard", _ui, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1080), UiKit.WithAlpha(Palette.UiInk, 0f));
            var menu = FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
            var field = typeof(MainMenu).GetField("backdrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var art = field != null && menu != null ? field.GetValue(menu) as Sprite : null;
            UnityEngine.UI.Image img = null;
            if (art != null)
            {
                img = UiKit.Panel("Art", panel.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1920 * art.rect.height / art.rect.width), Color.white);
                img.sprite = art; img.preserveAspect = true;
                if (img.rectTransform.sizeDelta.y < 1080) img.rectTransform.sizeDelta = new Vector2(1080 * art.rect.width / art.rect.height, 1080);
            }
            var shade = UiKit.Panel("Shade", panel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(1920, 470), UiKit.WithAlpha(Palette.UiInk, 0.82f));
            var logo = UiKit.Text("EndLogo", shade.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -20), new Vector2(1800, 200), 170, TextAlignmentOptions.Center, Palette.TintYellow);
            logo.fontStyle = FontStyles.Bold; logo.outlineWidth = 0.3f; logo.characterSpacing = 4f; logo.text = "BODGE SQUAD";
            var line1 = UiKit.Text("EndLine1", shade.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -215), new Vector2(1800, 80), 50, TextAlignmentOptions.Center, Palette.UiPaper);
            line1.text = "Fix it cheap. Hide the rest. Don't get caught.";
            var line2 = UiKit.Text("EndLine2", shade.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -300), new Vector2(1800, 70), 44, TextAlignmentOptions.Center, Palette.TintYellow);
            line2.text = "1–4 players  ·  online & LAN co-op  ·  <b>aybekk.itch.io/bodge-squad</b>";
            _captionStrip.gameObject.SetActive(false);
            _feed.text = "";
            StartCoroutine(Fade(panel, img));
        }

        IEnumerator Fade(UnityEngine.UI.Image panel, UnityEngine.UI.Image art)
        {
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < 0.6f) { group.alpha = t / 0.6f; panel.color = UiKit.WithAlpha(Palette.UiInk, 1f); yield return null; t += Time.deltaTime; }
            group.alpha = 1f;
            while (art != null) { art.rectTransform.localScale = Vector3.one * (1f + (t - 0.6f) * 0.012f); yield return null; t += Time.deltaTime; }
        }

        IEnumerator Watch()
        {
            bool radio = false;
            var mode = _insp.CurrentMode;
            while (_recording)
            {
                if (_insp.RadioOn != radio) { radio = _insp.RadioOn; Cue(radio ? "radio_on" : "radio_off"); }
                if (_insp.CurrentMode != mode) { mode = _insp.CurrentMode; if (mode == Inspector.Mode.Slipped) Cue("slip_inspector"); if (mode == Inspector.Mode.Patrol && _insp.Present) { } }
                if (!string.IsNullOrEmpty(_feed.text) && Now > _feedUntil) _feed.text = "";
                yield return null;
            }
        }

        IEnumerator Capture()
        {
            var eof = new WaitForEndOfFrame();
            while (_recording)
            {
                yield return eof;
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _grab.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                _grab.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(_dir, $"f{_frame:00000}.jpg"), _grab.EncodeToJPG(92));
                _frame++;
            }
        }

        // ================================================================ a stand-in bodger
        class Actor : MonoBehaviour
        {
            enum State { Idle, Walk, Run, Paint, Push, Cheer, Fall, Throw, Press, Talk }
            State _state;
            Transform _armL, _armR, _gloveL, _gloveR;
            Vector3 _armLRest, _armRRest, _gloveLRest, _gloveRRest;
            Quaternion _armLRot, _armRRot;
            Vector3 _target, _slipAt;
            float _speed, _t, _until, _phase, _tip, _tipVel, _delay;
            bool _slips;
            public bool Painting => _state == State.Paint;

            public void Init()
            {
                _armL = transform.Find("Arm_L"); _armR = transform.Find("Arm_R");
                _gloveL = transform.Find("Glove_L"); _gloveR = transform.Find("Glove_R");
                _armLRest = _armL.localPosition; _armRRest = _armR.localPosition; _gloveLRest = _gloveL.localPosition; _gloveRRest = _gloveR.localPosition;
                _armLRot = _armL.localRotation; _armRRot = _armR.localRotation;
            }

            public void Place(Vector3 pos, Vector3 lookAt)
            {
                pos.y = 0f;
                transform.position = pos;
                var d = lookAt - pos; d.y = 0;
                if (d.sqrMagnitude > 0.01f && lookAt != Vector3.zero) transform.rotation = Quaternion.LookRotation(d);
                _state = State.Idle; _tip = 0; _tipVel = 0;
            }

            public void Stand() => _state = State.Idle;
            public void Stop() { if (_state == State.Paint) _state = State.Idle; }
            public void WalkTo(Vector3 p) { _target = p; _target.y = 0; _speed = 1.8f; _state = State.Walk; }
            public void Run(Vector3 p, float speed, Vector3 slipAt) { _target = p; _target.y = 0; _speed = speed; _state = State.Run; _slips = true; _slipAt = slipAt; }
            public void Paint(bool on) => _state = on ? State.Paint : State.Idle;
            public void Push(bool on) => _state = on ? State.Push : State.Idle;
            public void Cheer(float seconds, float delay) { _state = State.Cheer; _until = Time.time + seconds; _delay = delay; _t = 0; }
            public void Throw() { _state = State.Throw; _t = 0; }
            public void Press() { _state = State.Press; _t = 0; }
            public void Talk(float seconds) { _state = State.Talk; _t = 0; _until = Time.time + seconds; }

            void Arm(Transform arm, Transform glove, Vector3 shoulder, float pitch, float roll)
            {
                var rot = Quaternion.Euler(pitch, 0, roll);
                arm.localRotation = rot;
                arm.localPosition = shoulder + rot * new Vector3(0, -0.21f, 0);
                glove.localPosition = shoulder + rot * new Vector3(0, -0.46f, 0.02f);
            }

            void Update()
            {
                _t += Time.deltaTime;
                var sL = _armLRest + new Vector3(0, 0.21f, 0); var sR = _armRRest + new Vector3(0, 0.21f, 0);
                float bob = 0f, tipTarget = 0f;
                switch (_state)
                {
                    case State.Walk:
                    case State.Run:
                    {
                        var to = _target - transform.position; to.y = 0;
                        if (to.magnitude < 0.05f) { _state = State.Idle; break; }
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 540f * Time.deltaTime);
                        transform.position += to.normalized * Mathf.Min(_speed * Time.deltaTime, to.magnitude);
                        _phase += Time.deltaTime * _speed * 3.2f;
                        bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f;
                        float swing = Mathf.Sin(_phase) * (_state == State.Run ? 55f : 30f);
                        Arm(_armL, _gloveL, sL, swing, -10); Arm(_armR, _gloveR, sR, -swing, 10);
                        if (_slips && _state == State.Run && Vector3.Distance(Flat(transform.position), Flat(_slipAt)) < 0.5f)
                        {
                            _slips = false; _state = State.Fall; _t = 0; _target = transform.position + transform.forward * 1.6f;
                            FindAnyObjectByType<TrailerDirector>()?.Cue("slip_player");
                        }
                        break;
                    }
                    case State.Paint:
                    {
                        float stroke = Mathf.Sin(_t * 9f);
                        Arm(_armR, _gloveR, sR, -110f + stroke * 25f, 10 + Mathf.Cos(_t * 6f) * 15f);
                        Arm(_armL, _gloveL, sL, 0, -10);
                        bob = Mathf.Abs(stroke) * 0.015f;
                        break;
                    }
                    case State.Push:
                        Arm(_armL, _gloveL, sL, -85f, -6); Arm(_armR, _gloveR, sR, -85f, 6);
                        _phase += Time.deltaTime * 6f; bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.03f;
                        break;
                    case State.Cheer:
                    {
                        float tt = Mathf.Max(0f, _t - _delay);
                        bob = Mathf.Abs(Mathf.Sin(tt * 7f)) * 0.3f;
                        float wave = Mathf.Sin(tt * 14f) * 20f;
                        Arm(_armL, _gloveL, sL, -160f + wave, -25); Arm(_armR, _gloveR, sR, -160f - wave, 25);
                        if (Time.time > _until) _state = State.Idle;
                        break;
                    }
                    case State.Throw:
                        Arm(_armR, _gloveR, sR, Mathf.Lerp(-170f, -40f, Mathf.Clamp01(_t / 0.25f)), 10);
                        if (_t > 0.6f) _state = State.Idle;
                        break;
                    case State.Talk:
                        // big chatty hand gestures
                        Arm(_armL, _gloveL, sL, -60f + Mathf.Sin(_t * 5f) * 30f, -25f + Mathf.Sin(_t * 3.1f) * 15f);
                        Arm(_armR, _gloveR, sR, -75f + Mathf.Sin(_t * 4.3f + 1f) * 35f, 25f + Mathf.Cos(_t * 2.7f) * 15f);
                        bob = Mathf.Abs(Mathf.Sin(_t * 4f)) * 0.02f;
                        if (Time.time > _until) _state = State.Idle;
                        break;
                    case State.Press:
                        Arm(_armR, _gloveR, sR, -70f + Mathf.Sin(Mathf.Clamp01(_t / 0.4f) * Mathf.PI) * 20f, 10);
                        if (_t > 0.6f) _state = State.Idle;
                        break;
                    case State.Fall:
                    {
                        tipTarget = _t < 2.1f ? -92f : 0f;
                        var to = _target - transform.position; to.y = 0;
                        transform.position += to * Mathf.Min(1f, Time.deltaTime * 2.5f);
                        Arm(_armL, _gloveL, sL, -150f, -40); Arm(_armR, _gloveR, sR, -150f, 40);
                        if (_t > 2.8f) _state = State.Idle;
                        break;
                    }
                    default:
                        _armL.localRotation = _armLRot; _armR.localRotation = _armRRot;
                        _armL.localPosition = _armLRest; _armR.localPosition = _armRRest;
                        _gloveL.localPosition = _gloveLRest; _gloveR.localPosition = _gloveRRest;
                        break;
                }
                _tipVel += ((tipTarget - _tip) * 160f - _tipVel * 14f) * Time.deltaTime;
                _tip += _tipVel * Time.deltaTime;
                var p = transform.position; p.y = bob; transform.position = p;
                var e = transform.eulerAngles; transform.rotation = Quaternion.Euler(_tip, e.y, 0);
            }

            static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);
        }
    }
}
