# BIR Form 2316 — Exact Official Form Output

**Date:** 2026-09-06
**Status:** Approved for planning
**Follows:** the BIR Form 2316 phase

## Context

The 2316 phase ported PayZen's QuestPDF `Bir2316Document` — a 372-line hand-drawn reproduction of
the form. It renders every Part and every item, but it is a reproduction: the boxes, rules and legal
captions are approximations of a document the BIR prescribes exactly.

It also has one measurable defect. The official form is **612 × 936 points — 8.5" × 13", Philippine
folio**. The ported document uses `PageSizes.Legal`, which QuestPDF defines as 8.5" × 14" (612 ×
1008). It is a full inch too tall, so nothing aligns on official stock.

**Goal:** produce output that *is* the official form — BIR's own file with values stamped onto it —
rather than a drawing of it.

## Approach

Embed the blank official PDF as a resource and overlay values at measured coordinates.

This was proven feasible before committing to it. A spike confirmed:

- PDFsharp 6.2.4 opens the supplied form and reports `612 x 936 pts`.
- `XGraphics.FromPdfPage` draws onto the existing page and saves valid output.
- Stamped values are recoverable with a text extractor — so the output can be **verified
  automatically**, not merely eyeballed.
- The form carries **no AcroForm fields** (`get_fields()` returns none), so it cannot be filled; it
  must be stamped.

### Why not keep drawing it

A reproduction can be made to look close, but it can never be the form. A BIR examiner comparing
side by side sees differences, and every future revision of the form means re-drawing it. Stamping
means a new BIR revision is a new embedded file plus a coordinate pass — not a rewrite.

### What this replaces, and what it does not

`Bir2316Document` (QuestPDF) and its test are deleted. **QuestPDF stays** — `PayslipDocument` is a
document PeopleCore designs itself, where drawing is the right tool. Only the government form moves
to stamping, because only the government form has an authoritative original.

## Architecture

```
src/PeopleCore.Reports/
  Forms/bir-2316-2021-encs.pdf        the blank official form, embedded resource
  Bir2316Stamper.cs                   opens it, stamps, returns bytes
  Bir2316FieldMap.cs                  every field's page coordinates
  Fonts/                              an embedded TTF for deterministic output
```

`IBir2316Renderer` — the interface Application already declares — keeps its signature. Only the
implementation changes, so `Bir2316Service`, the controller, the client and the page are untouched.

### Coordinates

The map is a static table: form field → `(x, y)` in PDF points, origin bottom-left on the 612 × 936
page, plus an alignment for money columns, which are right-aligned on the official form.

They are derived, not guessed. `pypdf` yields 220 positioned text runs from the blank form, giving
each printed label's coordinates; a value sits at a known offset from its label. The derivation is
done once, written into `Bir2316FieldMap` as literal constants with the label each was measured
from, and then verified visually. Constants rather than runtime extraction: the blank form never
changes between builds, and parsing it at render time would turn a layout question into a
production failure mode.

### Fonts

PDFsharp requires an explicit `IFontResolver` and has no default. The spike used
`FailsafeFontResolver`, which substitutes silently — acceptable for a spike, wrong for output that
must look identical everywhere. A real TTF is embedded and resolved from the assembly, so a payroll
run in a Linux container produces the same bytes as one on a developer's Windows machine.

## Testing

Stamped values are text-extractable, which makes this testable properly rather than by asserting a
byte count:

- Every value the service produces appears in the rendered output's extracted text.
- A money figure lands in the right column — assert the extracted text run's x-coordinate falls
  within the amount column's bounds, so a value stamped into the wrong box fails.
- The output page is 612 × 936, guarding the size defect that prompted this work.
- Output is deterministic: rendering the same DTO twice yields identical bytes, proving font
  resolution is not machine-dependent.
- The existing `Bir2316ServiceTests` are untouched — this changes rendering, not aggregation.

Visual confirmation is still required once, against the official form, because no automated test
proves a value sits in the box a human would call correct. That check is part of the work, not a
substitute for the tests above.

## Risks

**The embedded form is a BIR public document.** It is redistributed inside the application to
produce that same document, which is what it exists for. If legal review objects, the alternative is
shipping it as a configured file path rather than an embedded resource — a small change, and worth
knowing before it is a surprise.

**A BIR revision changes coordinates.** The map is keyed to the September 2021 ENCS revision, and the
file name records it. A later revision needs a new file and a new map, side by side, chosen by the
year being certified — out of scope here, but the naming leaves room for it.

## Acceptance criteria

1. Generated output is the official form with values on it, at 612 × 936 points.
2. Every figure the service computes appears in the output, asserted by test.
3. Money figures land in the amount column, asserted by coordinate.
4. Rendering the same certificate twice produces identical bytes.
5. `Bir2316Document` and its QuestPDF test are gone; `PayslipDocument` still renders.
6. `Bir2316Service`, the controller, the client and the page are unchanged.
7. `dotnet build` is clean and the full suite is green.
