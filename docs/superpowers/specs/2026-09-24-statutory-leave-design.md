# Philippine statutory leave - design

Date: 2026-09-24. Status: approved in brainstorming; awaiting spec review.

## Why

PeopleCore's leave module has no Philippine statutory logic. Leave types are flat records; only
the gender restriction and `IsPaid` change behaviour. Days are counted Monday to Friday, leave
can only be filed against a yearly balance, and `MaxDaysPerYear`, `RequiresDocument` and
`IsActive` are stored but never enforced. There is no page to manage leave types.

This plan adds the government-mandated leaves - Service Incentive Leave, Expanded Maternity Leave,
Paternity, Solo Parent, VAWC and the Magna Carta special leave for women - with their entitlements,
eligibility and proof. Maternity pay in payroll (the SSS benefit advance, the salary differential
and the SSS reimbursement) is the next plan.

## Entitlement kinds

`LeaveType.EntitlementKind`:

| Kind | Balance | Examples |
|---|---|---|
| `Accrued` | Yearly `LeaveBalance` built by accrual policies, as today | VL, SL, SIL |
| `YearlyAllowance` | A fixed number of days a year (`MaxDaysPerYear`). The year's balance is created the first time an eligible employee files in that year, with `TotalDays = MaxDaysPerYear`. No accrual run. | Solo Parent (7), VAWC (10) |
| `PerEvent` | A limit per occurrence (`DaysPerEvent`), no yearly balance. One request is one event. | Maternity, Paternity, Magna Carta, maternity days allocated to the father |

Existing leave types become `Accrued` with every new rule off, so VL and SL behave as before
(apart from the fixes listed under "Filing").

## New leave-type settings

Added to `LeaveType` (one migration), all editable on the new Leave Types page:

- `EntitlementKind` (default `Accrued`).
- `CountsCalendarDays` (default false). Calendar-day types count every date from start to end.
  All other types count working days (see "Day counting").
- `DaysPerEvent` (decimal?, PerEvent only).
- `MinServiceMonths` (int?, service from `HireDate` to the leave's start date).
- `RequiresMarried` (bool) - the employee's `CivilStatus` must be `Married`.
- `RequiresSoloParentId` (bool) - the employee must hold a solo parent ID valid on the start date.
- `MaxEvents` (int?, PerEvent only) - the most approved requests an employee may ever have of
  this type, counted from approved requests in PeopleCore.
- `IsConfidential` (bool) - see "Confidential types".

Existing settings, now enforced:

- `MaxDaysPerYear`: the allowance for `YearlyAllowance` types. Accrued types keep using their
  accrual policies; the field is informational for them.
- `RequiresDocument`: a request must carry a document.
- `GenderRestriction`: unchanged.
- `IsActive`: inactive types can't be filed or listed for filing. `IsActive` becomes editable
  through the update DTO.

## Maternity (RA 11210)

A request of a maternity type (see `IsMaternity` below; nothing keys off the code "ML") carries
`MaternityCase`:

- `LiveBirth`: `DaysPerEvent` (105) days, plus 15 when the employee holds a solo parent ID valid
  on the start date.
- `MiscarriageOrEmergencyTermination`: 60 days.

For `LiveBirth`, `DaysAllocatedToFather` (0-7) reduces the mother's limit by the same number.
The father files those days under the "Maternity Leave Allocated to Father" type (AML, PerEvent,
7 calendar days). The two requests aren't linked in data; the AML type requires a document (the
mother's allocation form).

The maternity case applies to a leave type through `LeaveType.IsMaternity` (bool). A type with
`IsMaternity` requires `MaternityCase` on its requests and uses the 105/120/60 rule in place of a
flat `DaysPerEvent`; its `DaysPerEvent` holds the live-birth base (105). The 60-day figure and
the 15-day solo-parent addition are constants from RA 11210 (`StatutoryLeave` in the Domain).

The optional 30-day unpaid extension is out of scope.

## Employee fields

`Employee` gains `SoloParentIdNumber` (string?, up to 50) and `SoloParentIdValidUntil`
(DateOnly?). An ID counts when both are set and `ValidUntil >= ` the leave's start date. An ID
that expires during the leave doesn't shorten it. HR edits both on the employee form (the same
permission that edits the rest of the employee record).

## Leave request changes

`LeaveRequest` gains:

- `MaternityCase` (enum?, maternity types only).
- `DaysAllocatedToFather` (int, default 0).
- A document: `DocumentFileName`, `DocumentStorageKey`, `DocumentContentType`,
  `DocumentSizeBytes`, `DocumentUploadedBy`, `DocumentUploadedAt` (all nullable).

## Documents

- Stored through the existing `IStorageService`, as employee documents are, and opened through a
  presigned short-lived link.
- One file per request. PDF, JPEG or PNG, at most 10 MB. Anything else: "Attach a PDF, JPG or PNG
  of at most 10 MB."
- Filing stays a JSON request (the demo seed and any API client file leave that way). The file is
  uploaded right after, with `PUT api/leave-requests/{id}/document` (multipart). My Leave won't
  submit a document-requiring type without a file, and uploads it straight after filing. Approving
  a request of a document-requiring type without a file is refused: "{Type} needs a supporting
  document."
- The employee who filed may replace the file while the request is Pending.
- Who may open it: the employee who filed; anyone who may decide the request; users with
  `approvals.all`. For confidential types: the employee and `approvals.all` only.

## Confidential types

For `IsConfidential` types (VAWC, per RA 9262's confidentiality):

- Only `approvals.all` users see the request in Leave Approvals and can decide it. A manager with
  `approvals.team` never sees it, even for a direct report.
- Anywhere else a manager could see the employee's leave (team views, calendars), the request
  shows as "Leave" with no type and no reason. Anything that lists leave for people other than the
  employee applies this rule.

## Day counting

- Calendar-day types: every date from start to end, inclusive.
- Other types: the employee's scheduled working days from their shift assignments (the same
  source the payroll attendance bridge uses), falling back to Monday to Friday when there is no
  assignment, and skipping holidays. This replaces the hardcoded Monday-Friday count for every
  type, VL and SL included.
- A request with 0 counted days is refused: "There are no working days in that range."

## Filing rules

Checked in this order when filing, and all re-checked when approving:

| Check | Message |
|---|---|
| Type inactive | "{Type} is no longer available." |
| Gender | the existing message |
| Service | "{Type} needs {n} months of service; you'll qualify on {MMM d, yyyy}." |
| Married | "{Type} is for married employees." |
| Solo parent ID | "{Type} needs a valid solo parent ID on your record; ask HR to add it." |
| Maternity case missing | "Choose whether this is a live birth or a miscarriage or emergency termination." |
| Father allocation outside 0-7 or on a non-live-birth case | "Up to 7 days can be allocated to the father, for a live birth only." |
| Event limit | "You've used {Type} {n} times, the most allowed." |
| Overlap with a Pending or Approved request | the existing message |
| Per-event limit | "{Type} is up to {n} days each time; this request is {m}." Maternity words it by case, e.g. "Maternity leave for a live birth is up to 105 days; this request is 110." |
| Balance / allowance | "You have {n} days of {Type} left for {year}." |
| Document (on approval only) | "{Type} needs a supporting document." |

Pending requests now hold their days: the balance available for a new request is
`RemainingDays` minus the days of that employee's Pending requests of the same type and year.

A request that spans two years charges each year's days to that year's balance (Accrued and
YearlyAllowance). Every year touched must have enough. PerEvent requests are one event whatever
their length.

Approving an Accrued or YearlyAllowance request adds to `UsedDays` per year, as today. Approving
a PerEvent request touches no balance; the event count comes from approved requests. Cancelling
an approved request gives the days back (or frees the event). Changing a type's rules affects new
requests; approving a pending one re-checks it against today's rules.

## Leave Types page

A new page at `/leave-types`, needing `leave.manage`, with a nav entry:

- A list: name, code, kind, limits, calendar/working days, active, and a summary of rules.
- Create and edit forms with every setting. Fields irrelevant to the chosen kind are hidden.
- For Accrued types, their accrual policies: tenure range, days a year, Monthly or Annual, active.
  Add, edit and deactivate. (The API for policies exists; the Web client gains the calls.)
- Delete is refused once a type has any request or balance: "{Type} has been used; deactivate it
  instead."
- "Add Philippine statutory leave": creates whichever of the types below are missing, matched by
  code (trimmed, case-insensitive), and reports what it added and what it skipped. Running it
  twice changes nothing. For SIL it also creates the accrual policy.

### The statutory set

All paid.

| Code | Name | Kind | Limit | Days | Rules |
|---|---|---|---|---|---|
| SIL | Service Incentive Leave | Accrued | Monthly policy of 5 days a year from 12 months' service, credited on a cumulative rounding (0.42, 0.41, 0.42, ... adding up to exactly 5) | Working | Convertible to cash; counts as vacation for de minimis |
| ML | Maternity Leave | PerEvent, IsMaternity | 105 (120 solo parent), 60 miscarriage | Calendar | Female; document required |
| AML | Maternity Leave Allocated to Father | PerEvent | 7 | Calendar | Male; document required |
| PL | Paternity Leave | PerEvent | 7, at most 4 events | Working | Male; married; document required |
| SPL | Solo Parent Leave | YearlyAllowance | 7 a year | Working | Solo parent ID; 6 months' service |
| VAWC | VAWC Leave | YearlyAllowance | 10 a year | Working | Female; confidential; document required |
| SLW | Special Leave for Women (Magna Carta) | PerEvent | 60 | Calendar | Female; 6 months' service; document required |

## My Leave page

- The type list shows every active type the employee may file: Accrued types with a balance,
  YearlyAllowance and PerEvent types the employee is eligible for. Ineligible types are hidden.
- Next to the chosen type: "4 of 7 days left this year", "Up to 7 days per delivery (2 of 4
  used)" and so on.
- Maternity shows the case picker and, for a live birth, the father allocation, and states the
  resulting limit.
- Types that require a document show a required file picker.
- A Pending request with a document offers "Replace document".

## Leave Approvals page

- Shows the maternity case and father allocation, and a "View document" link.
- Confidential requests appear only for `approvals.all` users.
- A decision refused by a re-check shows the message.

## Payroll

All statutory types are paid, so the payroll attendance bridge doesn't deduct their days.
Maternity is therefore paid as ordinary salary until the next plan splits it into the SSS
maternity benefit (non-taxable) and the salary differential; until then the SSS portion is taxed.
The Leave Types page shows that note on maternity types.

## Demo seed

The seeder presses "Add Philippine statutory leave" (through the API) after creating VL and SL.

## Out of scope

- Maternity pay in payroll, the SSS reimbursement, and the SSS sickness benefit (next plan).
- Half-day leave.
- The 30-day unpaid maternity extension.
- A scheduled carry-over job.
- Linking the father's AML request to the mother's ML request.

## Known limitations

- Unused SIL isn't converted to cash at year-end yet. Labor Code Art. 95 makes it commutable;
  the Leave Types page says so on SIL's row and form, and conversion is left to a later plan.
- Event limits count requests, not deliveries. Paternity Leave's "at most 4" counts approved
  requests of the type, so one delivery filed as two requests uses two of the four.
- The mask still identifies VAWC. A confidential request shown to someone without
  `approvals.all` reads "Leave", but while VAWC is the only confidential type, "Leave" can only
  mean VAWC to anyone who knows the set.

## Testing

- Service: every filing rule and message; calendar vs shift-based day counts with holidays; pending
  holds; the two-year split; maternity cases, the solo-parent addition and the father allocation;
  the event count ignoring rejected and cancelled requests; the YearlyAllowance balance created on
  first filing; re-checks on approval; cancel restoring days.
- The statutory button: creates only missing types, respects existing codes, idempotent, SIL's
  policy.
- Documents: type and size limits, replace while Pending only, who may open (including the
  confidential case), a presigned link.
- Confidential: hidden from `approvals.team`, decidable only by `approvals.all`.
- Postgres: the migration, the new request and employee columns, and per-event counting.
- bUnit: Leave Types (list, forms, policies, button), My Leave (type list, limits, maternity
  picker, required file, replace), Leave Approvals (document link, confidential hidden), the
  employee form's solo parent fields.
- `PermissionEquivalenceTests` for the new endpoints.
