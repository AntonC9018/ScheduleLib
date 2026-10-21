# Historical Google scheduler WIP

Source snapshot from historical `ui` at `850d76a148459a7f6f44383ac0067053131f4db9` (August 19, 2026).
The isolated change range starts after `de6e4e131563d574f2343b14b0c5997cdfdad644` and spans seven commits.
This archival branch is based on cleaned published history `ba380a2ab51b265a014dfabf1792ebfb1cda0dc6`;
it does not republish the original branch ancestry or its private data.

The five retained C# files preserve their exact historical bytes and original
relative paths. `scheduler.patch` is the complete C#-only diff for that range,
including removal of `LimitedTaskRunner.cs`. Apply it only to a compatible
historical source tree; it is not a patch against today's implementation.

The experiment implements Google API scheduling/throttling and changes Calendar,
Drive and credential-provider wiring. It is unfinished (including an incomplete
provider factory), outside the current projects and not build-verified.
No integration or modernization is implied. Formatting settings and historical
domain notes are excluded. No credentials, spreadsheets or generated data are included.

Changed paths in the original range:

```text
M	src/Features/Integration/Tasks/GoogleCalendarLessons.cs
M	src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCalendarConfig.cs
M	src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCredentialsConfig.cs
M	src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleDriveConfig.cs
A	src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleScheduler.cs
D	src/ScheduleLib/ScheduleLib.Helper/Helper/LimitedTaskRunner.cs
```
