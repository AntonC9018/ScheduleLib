# Employment document generation

This context describes the employment records and documents produced by DocsTemplates.

## Language

**Document date**:
The date stored for a person and used for document signing or issue dates. It defaults to 1 September of the current year and is distinct from an employment-period end date.
_Avoid_: Date, signing date

**Repeating employment documents**:
The hire request and individual employment contract generated from one person record. A workload above one and at most two units produces a second pair for the remainder, using internal cumulation.
_Avoid_: Repeating documents, copies

**Assistant-specific document**:
A document generated only for a person whose function denotes an assistant university position. The assistant job description is an assistant-specific document.
_Avoid_: Assistant template

**Supported academic function**:
One of `asistent universitar`, `lector universitar`, `conferențiar universitar`, or `profesor universitar`. Each supported academic function has a CORM occupation code.
_Avoid_: Arbitrary function name

**Manual completion field**:
A blank intentionally retained for later completion by the employee or university staff, such as a signature, approval, visa, receipt acknowledgement, or contract number.
_Avoid_: Skipped field, missing field

**Template output field**:
A fixed-position value placed over the template's existing blank or underlined area. It follows the legacy templates: the source paragraph, wording, underscores, spacing, and line breaks remain intact, while a white, borderless anchored text box carries the generated value and does not reflow the document.
_Avoid_: Inline placeholder, rewritten paragraph

**Person packet**:
The complete set of generated employment documents for one person. Its folder contains a `docs` subfolder with the individual documents and a `print.pdf` bundle of the printable ones.
_Avoid_: Output folder
