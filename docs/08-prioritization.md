# SmartRail prioritisation report

Status: **review draft**, 2026-10-05 (day 5 of 20). Input: [`07-feature-inventory.md`](07-feature-inventory.md). Checked against the brief and [`06-scope-decision.md`](06-scope-decision.md).

**6 October scope change:** The project owner asked for a train map. The pilot now includes a small route map on the passenger train-detail screen. It reuses the stop list and simulated train position, so it needs no GPS feed or map service. Row 36 is re-scored below; its number stays the same so earlier references still work.

**8 October passenger-screen decision:** Kasun's feedback puts a guest-accessible Gampaha → Colombo Fort train list first. It shows the next two or three trains, scheduled and estimated times, boarding platform, delay, and update age. Origin-platform changes and cancellations are prominent. Sign-in is requested only for saved commutes, personal in-app alerts and food orders. These are changes to the presentation and access rules of existing Must items 6, 7, 10 and 18, so their scores and MoSCoW buckets stay the same.

**8 October scope review:** Add a thin early-ending change so unreachable stops and food orders cannot remain valid. Add real Control acknowledgement states for driver reports. Prevent a second controller silently replacing a newer change. Prioritise these before an unfinished moving map marker or driver offline report queue. These are decisions from a simulated stakeholder discussion, not evidence that the features are already built, tested or reviewed by native readers.

This report scores the inventory openly so that the scope in doc 06 can be defended as "why X before Y". It does not replace doc 06. Where the scoring disagrees with doc 06, the difference is listed in §4 for you to accept or reject. Either way, log your decision in the AI log.

---

## 1. Framework used

**Weighted scoring to rank, then MoSCoW to cut.** The ranking feeds a timeboxed release with a fixed deadline of 20 Oct.

| Rejected | Why |
|---|---|
| RICE | Reach has no meaning here. The users are fictional and there is no analytics, so any Reach number would be made up. |
| ICE | ICE has no way to give weight to the brief's mandatory artefacts, which matter more than anything else. |
| Kano | Kano needs evidence of user reactions, and there was no user research. It is used once in §3, as a hypothesis, for push notifications. |

The real "customer" is the assessment panel, so the criteria follow the brief (§23 required artefacts, §26 marking areas). User value is still weighted, because §26 marks product thinking.

### Criteria (each scored 0–5)

| Code | Criterion | Weight | What a 5 means | Evidence type |
|---|---|---|---|---|
| **M** | Brief mandate | 30% | A §23 minimum artefact. 4 = named in the body of the brief (§3–§20); 2 = implied; 0 = absent. | **Finding**: the brief's text |
| **D** | Golden-thread dependency | 20% | The demo story in doc 06 §2 breaks without it | **Finding**: doc 06 §2 |
| **R** | Rubric breadth | 15% | Evidences 4 or more of the §26 marking areas | Judgement |
| **V** | User outcome and honesty | 15% | Directly answers a passenger or operator question, or prevents a false statement | **Assumption**: no user research |
| **E** | Ease | 20% | ≤ 0.25 day. 4 ≤ 0.5 d, 3 ≤ 1 d, 2 ≤ 2 d, 1 > 2 d, 0 = needs external approval or data | **Assumption**: the council's AI estimates (doc 06 §14), not yet validated |

Score = 6M + 4D + 3R + 3V + 4E, out of 100.

**MoSCoW rules:**

| Bucket | Rule |
|---|---|
| Must | Score ≥ 74, **or** M = 5 (a §23 artefact), **or** a Must depends on it; any manual override is named below |
| Should | 60–73 |
| Could | 45–59 |
| Won't | < 45, or E = 0 |

The ~250 inventory lines are grouped into 43 capabilities. Duplicates and variants (for example the four cutoff values) sit inside one capability. Scoring each line separately would be precision theatre.

---

## 2. Scored backlog

The last column records what **doc 06 plans**. Its “Built” labels mean **planned to build**, not completed or tested software. Only code and observed results can establish completion.

| # | Capability | M | D | R | V | E | Score | MoSCoW | Doc 06 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Station displays: platform and entrance, si/ta/en stacked, offline fallback, clock | 5 | 5 | 5 | 5 | 3 | **92** | Must | Built ✓ |
| 2 | Order follows the train; auto-cancel on cancellation | 5 | 5 | 4 | 5 | 3 | **89** | Must | Built ✓ |
| 3 | Service-change composer: delay, cancel, platform, impact preview, audit | 5 | 5 | 5 | 5 | 2 | **88** | Must | Built ✓ |
| 4 | Driver briefing, cached, showing its age | 5 | 5 | 4 | 4 | 3 | **86** | Must | Built ✓ |
| 5 | Live status and ETA engine (four truths, never departs early) | 5 | 5 | 4 | 5 | 2 | **85** | Must | Built ✓ |
| 6 | Public next-train home; optional saved train and stop detail | 5 | 5 | 4 | 5 | 2 | **85** | Must | Built ✓ |
| 7 | Origin-platform and cancellation alerts; in-app feed | 4 | 5 | 3 | 5 | 4 | **84** | Must | Built / thin ✓ |
| 8 | Freshness and "unknown" rule, with shared test vectors | 4 | 5 | 4 | 5 | 3 | **83** | Must | Built ✓ |
| 9 | Ops corridor table | 5 | 5 | 3 | 4 | 3 | **83** | Must | Built ✓ |
| 10 | Public guest reads; Keycloak login/token validation, local role table, scoping and auth tests | 5 | 5 | 4 | 3 | 3 | **83** | Must | Built ✓ |
| 11 | Grilled service, contract, HMAC, outbox and idempotency | 5 | 5 | 5 | 4 | 1 | **81** | Must | Built ✓ |
| 12 | Curated network seed and `seed-demo --anchor` | 5 | 5 | 3 | 3 | 3 | **80** | Must | Built ✓ |
| 13 | Passenger food ordering: menu, order, code, status, cancel | 5 | 5 | 3 | 4 | 2 | **79** | Must | Built ✓ |
| 14 | Design system tokens, `StatusBadge`/`FreshnessLine`, accessibility checks | 5 | 3 | 4 | 4 | 3 | **78** | Must | Built ✓ |
| 15 | Driver report: not sent, sent, Control acknowledged | 4 | 5 | 3 | 4 | 3 | **77** | Must | Thin ✓ |
| 16 | Incidents (Open → Resolved) and field-report inbox | 4 | 5 | 3 | 3 | 3 | **74** | Must | Thin ✓ |
| 17 | Security evidence: threat model, ZAP, scans, IDOR/403 suites | 5 | 2 | 4 | 3 | 3 | 71 | Must (M=5) | ✓ |
| 18 | Next 2–3 trains from A to B on the opening screen | 4 | 3 | 2 | 5 | 3 | 69 | Must (dependency) | Built ✓ |
| 19 | Test pack: 5 E2E, domain and integration tests, defect log | 5 | 3 | 4 | 2 | 2 | 68 | Must (M=5) | ✓ |
| 20 | Train ends early: valid stops kept, later stops and orders removed | 4 | 2 | 2 | 4 | 4 | 66 | **Must (dependency)** | Build thin |
| 21 | Passenger offline cache with age labels | 4 | 2 | 3 | 4 | 3 | 65 | Should | Thin ✓ |
| 22 | Observability: OTel, local Aspire dashboard, Azure Application Insights, correlation ID, health, 4 metrics, runbook, SLOs | 5 | 2 | 3 | 2 | 3 | 65 | Must (M=5) | ✓ |
| 23 | Deterministic train simulator | 3 | 5 | 2 | 2 | 3 | 62 | Must (dependency) | Built ✓ |
| 24 | Driver offline outbox (non-urgent reasons) | 4 | 1 | 3 | 4 | 3 | 61 | Should | Should ✓ |
| 25 | Terminal wireframes and interaction spec | 3 | 1 | 2 | 2 | 5 | 54 | Could | Design only ✓ |
| 26 | Connections from another station (departures from X) | 4 | 0 | 1 | 3 | 4 | 52 | **Could** | ⚠ deferred |
| 27 | Station-closure and diversion change types | 4 | 0 | 2 | 3 | 3 | 51 | Could | ⚠ not explicit |
| 28 | Prevent two controllers silently overwriting a change (409) | 2 | 0 | 3 | 3 | 5 | 50 | **Must (manual override)** | Build thin; prove before claiming |
| 29 | Azure single environment and CI deploy, including Keycloak | 3 | 2 | 3 | 1 | 3 | 50 | Should (kept, see §3) | Built ✓ |
| 30 | `validUntil` temporary access | 3 | 0 | 2 | 2 | 4 | 46 | Could | Should ⚠ |
| 31 | Registration, social sign-in, data export/delete | 3 | 0 | 2 | 3 | 3 | 45 | Could | Cut ✓ |
| 32 | Push notifications (FCM) | 2 | 1 | 2 | 4 | 2 | 42 | Won't | Cut ✓ |
| 33 | Reporting / punctuality | 3 | 0 | 2 | 2 | 3 | 42 | Won't | Deferred ✓ |
| 34 | Multi-leg journey planner | 3 | 0 | 2 | 4 | 1 | 40 | Won't | Deferred ✓ |
| 35 | Interactive terminal (built) | 2 | 0 | 2 | 3 | 2 | 35 | Won't | Design only ✓ |
| 36 | Passenger route diagram; moving marker only if time permits | 4 | 2 | 3 | 5 | 3 | 68 | Should | Build thin; marker first cut |
| 37 | SSE "changed" hint | 1 | 0 | 2 | 2 | 4 | 34 | Won't | Should ⚠ |
| 38 | On-board food delivery ("receive", §12) | 3 | 0 | 1 | 3 | 1 | 34 | Won't | ⚠ not in the cut list |
| 39 | Payment (Stripe) | 2 | 0 | 2 | 3 | 1 | 31 | Won't | Cut ✓ |
| 40 | Driver chat, defect photos, engine condition | 1 | 0 | 1 | 2 | 2 | 23 | Won't | Cut ✓ |
| 41 | Product AI (drafter, chatbot, copilot) | 0 | 0 | 1 | 1 | 2 | 14 | Won't | Cut ✓ |
| 42 | Enterprise infrastructure (AKS, Redis, Service Bus, Key Vault, 3 environments) | 0 | 0 | 2 | 0 | 1 | 10 | Won't | Cut ✓ |
| 43 | Safety-critical driver functions | 0 | 0 | 0 | 0 | 0 | 0 | **Won't, ever** | Never ✓ |

**Outside the scoring.** These are fixed Musts because §23 requires them, and they aren't product features: requirements set, personas, journeys, stories and acceptance criteria, edge cases, flows, wireframes, ADRs, threat model, AI-usage record, final demo.

**14 October architecture update:** Keycloak now owns sign-in and refresh tokens; Aspire AppHost runs the local stack. The scores above are the original prioritization scores, not a fresh effort estimate. Keycloak adds a service, database and deployment work, so recheck the Azure cost and delivery risk before relying on item 29's old score. See [identity and local development](architecture/identity-and-local-development.md).

**Sanity check: five overrides, all deliberate.**
- **Simulator (62)** and **Next trains (69)** are Must by dependency. Without the simulator nothing moves in the demo. Without a public route search, a passenger cannot check the next train before deciding whether to follow or save it.
- **Observability (65)** is Must by mandate. The scoring underrates "invisible" mandatory work because it scores low on D and V. That is a known bias of this weighting, not a sign the work is optional.
- **Early ending (66)** is Must by dependency: the passenger status and food-order Musts must not claim the train reaches a station after it ends. Preserve valid earlier stops and cancel existing orders beyond the new endpoint. The earlier score assumed this was a small composer option; actual effort is unmeasured.
- **Controller edit conflict (50)** is a manual Must because the current viva answer claims a 409 and a silent overwrite would make published changes unreliable. Keep the rule small, prove it with two editors, and remove the viva claim if it is not implemented. The old under-one-hour estimate is unverified.

---

## 3. Trade-off analysis

**Close calls near the cut lines.** One point on one criterion moves a score by 3–6. Each item below is within that distance of a boundary.

| Item | Score | What would move it |
|---|---|---|
| Concurrent-edit protection | 50 | **Now a manual Must:** a second controller must not silently overwrite a newer change. The exact implementation and earlier 30–60 minute estimate need validation. Show the two-editor result before claiming 409 in the viva. |
| Connections from another station | 52 | **Up**, if it reuses `/stations/{id}/board`, which the displays need anyway. A "departures from X" screen in the passenger app then costs about 0.25 d (E → 5, score 56). §3 of the brief names this passenger question explicitly, and it is the only §3 question with no answer in the build. |
| Azure deploy | 50 | Kept as **Should** despite its score. §19 asks for "operable in production", and a public health URL is evidence. The local fallback is now Aspire plus a backup video. Keycloak makes the cost and deployment estimate larger than this original score suggests. **Down to Could** if G1 slips. |
| `validUntil` | 46 | Doc 06 rates it *Should*. The scoring puts it at Could: no demo dependency, and only §9 implies it. This is consistent with doc 06, which drops it first at G3 anyway. |
| Early ending | 66 | **Now a dependency Must:** the composer says where the train ends. Earlier valid stops remain; later arrival estimates and new food orders disappear; existing food orders beyond the new endpoint are cancelled with a reason. Do not describe the entire train as cancelled when earlier stops still operate. |

**Different risk profiles behind similar scores.**
- **Grilled (81) vs passenger food ordering (79).** Grilled is the highest-effort Must (E = 1), with two runtimes, HMAC and an outbox. Food ordering is mostly UI. If day 12–13 slips, the doc 06 cut line protects the right thing: keep the outbox and the ETA push, cut the staff page. The integration is what the panel marks (§26 "Integration"). The ordering screens are not.
- **Push notifications (42).** As a **Kano hypothesis**, background alerts are *Must-Be* for a real commuter, because the app is useless if you have to keep it open. For the assessment they are only a delivery channel. The ranking holds **only because** the in-app feed plus "deciding who gets notified is built and tested" carries §13. If the panel's view of §13 is "notify" in the push sense, push moves to Should (M 2 → 4 gives 54, still Could). Low risk to the ranking, but worth one line in the demo.

**Weight sensitivity.** The 16 top-scoring Musts all have M ≥ 4 and D = 5. Any weighting that gives mandate and demo dependency together at least 40% keeps them in the Must set, so the top of the ranking is stable. Setting ease to 0% (pure value) only reorders the Could and Won't tiers: the planner and push rise, but neither crosses 60.

---

## 4. Recommendation

The top of the backlog matches doc 06. **The weighted scoring confirms the council's Must set.** Every capability scoring 74 or more is already in doc 06 §3's built or built-thin columns. The recommendations are therefore about the edges:

1. **Protect the propagation core (items 1–16).** The guest screen, update age, unknown state, shared changes and checked wording remain the first priorities.
2. **Include a thin early-ending change (item 20)** because destinations and food orders cannot remain valid beyond the new final station. Show the whole effect, not just a new composer choice.
3. **Include controller edit protection (item 28)** and distinguish a service-change conflict from the existing food-order cutoff 409. Do not claim either behaviour complete until demonstrated.
4. **Promote "departures from another station" to Could-plus**, and do it on day 10 if G2 is green. It closes the last unanswered §3 question at almost no cost by reusing the board endpoint.
5. **Add on-board delivery to the cut list** with a viva line, for example: *"Collection at a station only. On-board delivery needs onboard staff, train-coach location and a handover protocol the provider doesn't have."* §12 says "receive **or** collect", so the panel may ask.

The route diagram stays. If time runs short, remove its moving marker and the optional driver offline report queue before cutting freshness, early-ending behaviour, report acknowledgement or reviewed language. A stopped feed makes position and ETA unknown but leaves a separately confirmed platform change visible. SmartRail blocks new food orders when status is unknown; Grilled does not need a new freshness event. Payment happens at collection, so the pilot has no automatic refund.

---

## 5. Sensitivity notes

| Most uncertain | Why | Effect if wrong |
|---|---|---|
| **E (ease)** across the board | AI estimates from doc 06's time budget. No build work had been timed when this was written. | E carries 20% of the weight. If the Flutter work runs 2× over (items 6, 13, 15), those items stay Must because of M and D. The pressure lands on Should items 21 and 24, which is what doc 06's cut lines already plan for. |
| **V (user outcome)** | No interviews. Personas are invented. | Changes the order within the Could and Won't tiers, never the Must tier. |
| **M for §10 change types** | "Should provide an appropriate mechanism" could mean all 7 situations are expected, or only a representative set. | If all 7 are expected, M for station closure and diversion rises to 5 (+6, giving 57). That is still Could, but closer. Mitigation: document the full state model even where the composer doesn't build it. |
| **Azure M = 3** | The brief never says "cloud". | If the panel expects a live URL, Azure becomes a Must. The G1 cut line covers it. |

---

## 6. Decision frame

- **Top recommended investment:** finish and demonstrate the propagation core, thin early-ending change and controller edit protection. Consider departures-from-X only after those work.
- **Trade-off:** depth over breadth. One loop proven end to end across 5 surfaces, instead of more features each built thinly. This is what §25 asks for: "A smaller, coherent, well-engineered solution is preferable".
- **Confidence: medium-high on the ranking, low on the effort figures.** Mandate and dependency scores trace to the brief and doc 06, which counts as strong evidence. Ease and user value are assumptions, which is weak evidence.
- **Owner:** you, as the sole engineer and product owner.
- **Decision date:** Mon 5 Oct (day 5), before the day-5 build starts.
- **Revisit triggers:**
  - G2 (Sat 10 Oct): if it's green, take departures-from-X. If it's red, apply doc 06's cut lines and drop all Could items.
  - G3 (Thu 15 Oct): freeze. Nothing moves up after this point.
  - Any message from the assessors about the stack, iOS, or which change types are expected.

## 7. Unknowns and evidence gaps

- **Scored below 50% confidence:** every V score, and E for items 11, 13 and 24 (the highest-effort, never-timed items).
- **Assumptions that would change the ranking if wrong:**
  1. The panel accepts a representative subset of §10's change types. If not, items 20 and 27 rise.
  2. The panel accepts the in-app feed as meeting §13. If not, push rises to Could.
  3. Weekend work happens (doc 06 §14). If not, about 4 days disappear, and everything below 74 except the M = 5 items becomes a cut candidate.
  4. Nobody expects iOS or a Node backend (doc 06 §14). If they do, this report is moot, because the effort base changes.
- **Validation step:** time the day-5 to day-7 work against the budget (backend core 2.25 d, simulator 1 d). Re-score E at G1 using actual figures.

## 8. Pass/fail readiness

**PASS, provisional for the prioritisation argument only.** This is not a pass for implementation readiness.
- Every score names its evidence type.
- Mandate and dependency are findings, traced to the brief and doc 06.
- Rubric breadth is judgement. User value and ease are labelled assumptions.
- No score was inflated to protect an item. The five Must overrides are stated with their reasons.
- Low-confidence areas lead to named re-scoring points (G1, G2), not commitments.

## 9. Recommended next artefact

The Shipwright skill suite isn't installed here, so there is no skill to chain to. The useful next artefacts are:

1. **A dated change log in doc 06**, recording which of the five recommendations in §4 you accepted or rejected, and why. This is direct §21 evidence of your judgement over AI output.
2. **Acceptance criteria for early ending, controller edit conflict and driver acknowledgement**, with requirement IDs in the traceability matrix. Capture real code, tests and native-reader feedback before marking anything complete; the simulated agents' statements are not proof.
