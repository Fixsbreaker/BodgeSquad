using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BodgeSquad.Job
{
    // The live, physical inspection: walks room to room, stops at each defect, leans in and taps,
    // then either plants a red flag (caught) or walks on (got away with it).
    // Visual-only inspection, like the real thing: concealed damage is legitimately missable.
    public class Inspector : MonoBehaviour
    {
        [SerializeField] Transform body;          // leans/taps
        [SerializeField] Transform entrance;      // where he appears
        [SerializeField] GameObject flagPrefab;
        [SerializeField] GameObject passPrefab;   // small green tick for honest work
        [SerializeField] float walkSpeed = 1.6f;
        [SerializeField] float turnSpeed = 360f;
        [SerializeField] float inspectSeconds = 1.6f;

        public Defect Current { get; private set; }

        Vector3 _bodyRest;

        void Awake()
        {
            _bodyRest = body.localPosition;
            gameObject.SetActive(false);
        }

        public void Begin(IReadOnlyList<Defect> defects, Action onDone)
        {
            transform.SetPositionAndRotation(entrance.position, entrance.rotation);
            gameObject.SetActive(true);
            StartCoroutine(Run(new List<Defect>(defects), onDone));
        }

        IEnumerator Run(List<Defect> todo, Action onDone)
        {
            var job = JobManager.Instance;
            yield return new WaitForSeconds(0.8f);

            while (todo.Count > 0)
            {
                // nearest next — reads as a person walking the room, not a list
                todo.Sort((a, b) => (a.InspectPoint - transform.position).sqrMagnitude.CompareTo((b.InspectPoint - transform.position).sqrMagnitude));
                var d = todo[0];
                todo.RemoveAt(0);
                Current = d;

                yield return WalkTo(d.InspectPoint);
                yield return Face(d.transform.position);

                var state = d.EffectiveState;
                float risk = RepairRules.Obviousness(state, d.Severity);
                job.Say(state == RepairState.HiddenByFurniture
                    ? $"Inspector peers behind the furniture at the {d.Label}…"
                    : $"Inspector taps the {d.Label}…");
                yield return Tap();

                bool caught = state != RepairState.Fixed && job.Luck.NextDouble() < risk;
                d.Inspected = true;
                if (caught)
                {
                    d.Reveal();
                    Plant(flagPrefab, d);
                    string who = d.ResponsiblePlayer >= 0 ? $" — P{d.ResponsiblePlayer + 1}'s handiwork" : "";
                    job.Say(state == RepairState.Untouched
                        ? $"FLAGGED: {d.DisplayName}, not even touched!"
                        : $"FLAGGED: {d.DisplayName}, {RepairRules.StateVerb(state)}{who}! −${RepairRules.Penalty(d.Severity)}");
                }
                else if (state == RepairState.Fixed)
                {
                    Plant(passPrefab, d);
                    job.Say($"Inspector nods at the {d.Label}. Proper job.");
                }
                else
                {
                    job.Say($"Inspector shrugs and moves on…");
                }
                yield return new WaitForSeconds(0.6f);
            }

            Current = null;
            yield return WalkTo(entrance.position);
            job.Say("Inspector is writing up the report.");
            yield return new WaitForSeconds(1f);
            onDone?.Invoke();
        }

        IEnumerator WalkTo(Vector3 target)
        {
            target.y = transform.position.y;
            float phase = 0f;
            while ((target - transform.position).sqrMagnitude > 0.01f)
            {
                var dir = target - transform.position;
                var look = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
                phase += Time.deltaTime * 2f * Mathf.PI * 1.8f;
                body.localPosition = _bodyRest + Vector3.up * Mathf.Abs(Mathf.Sin(phase)) * 0.04f;
                body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(phase) * 2.5f);
                yield return null;
            }
            body.localPosition = _bodyRest;
            body.localRotation = Quaternion.identity;
        }

        IEnumerator Face(Vector3 point)
        {
            var dir = point - transform.position; dir.y = 0;
            var look = Quaternion.LookRotation(dir);
            while (Quaternion.Angle(transform.rotation, look) > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                yield return null;
            }
        }

        IEnumerator Tap()
        {
            // lean in, three suspicious taps, lean back
            float t = 0f;
            while (t < inspectSeconds)
            {
                t += Time.deltaTime;
                float lean = Mathf.Sin(Mathf.Clamp01(t / inspectSeconds) * Mathf.PI) * 14f;
                float tap = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 2.2f)) * 3f;
                body.localRotation = Quaternion.Euler(lean + tap, 0, 0);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        void Plant(GameObject prefab, Defect d)
        {
            if (prefab == null) return;
            var pos = d.transform.position + d.transform.forward * 0.12f + Vector3.up * 0.25f;
            var go = Instantiate(prefab, pos, Quaternion.LookRotation(d.transform.forward));
            StartCoroutine(Pop(go.transform));
        }

        static IEnumerator Pop(Transform t)
        {
            // ease-out-back pop so the flag lands with a punch
            var end = t.localScale;
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.deltaTime / 0.35f);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * Mathf.Pow(a - 1f, 3) + c1 * Mathf.Pow(a - 1f, 2);
                t.localScale = end * e;
                yield return null;
            }
        }
    }
}
