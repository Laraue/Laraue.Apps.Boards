# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

Backend for a Jira-like task-management system ("Boards"): organizations contain spaces, spaces
contain epics/statuses, epics contain issues ("cards"). Exposed via two surfaces:

- A web API consumed by a separate frontend (Mini App / web board UI) — organizations, spaces,
  epics, statuses, issues, attachments, entity-level and administrative permissions.
- A Telegram bot that lets a chat be linked to an organization/epic/status, so chat messages can
  become issues:
  - `SaveMode.EachMessage` — every message in a linked chat is auto-saved as a card (or updates
    one, on edit). Meant for solo/personal chats.
  - `SaveMode.BotMentionedMessages` — nothing is saved automatically; a user replies to a message
    with `/save` (optionally with a note) to turn it into a card. Meant for group chats with a
    lot of unrelated discussion.
  - Card creation is gated by `IAccessService` permission checks, not just by the chat being
    linked.
  - `/info` — reply to a tracked message to get its card's preview/link back without changing
    anything. Read-only counterpart to `/save`, mainly useful in `EachMessage` mode where saving
    is silent (just a reaction) and there's otherwise no way to grab the link
    (`TelegramSaveMessageService.GetInfoByReply`, `InfoCommandService`).
  - Inline search: typing `@bot query` in any Telegram chat searches issues across every space
    the user has read access to (`SearchService`, `src/.../TelegramServices/Services/Search`).
    The query is parsed into `key:value` filter tokens (assignee, organization, space, updated
    date, plus a direct issue-key lookup — see the `*TokenFilter` classes and
    `TokenFilterRegistry`) and leftover free text, which is matched against issue content with a
    highlighted snippet (`ContentFragment`). Results render as the same "issue preview card" that
    `/save` replies use.
  - Both inline search results and `/save` replies render the same "issue preview card" format
    (key · org · content snippet, plus a "💬 chat · sender · date" footer when the issue
    originated from Telegram) via the shared `IssuePreviewFormatter`.

See [README.md](README.md) for the human-facing overview, local-run steps, and permissions model.

## Issue hierarchy

`Organization` → `Space` (has a short `Key`, e.g. `UNC`) → `Epic` → `Status` → `Issue`. An
`IssueNumber` gives an issue a per-space sequential number; combined with the space's `Key` that
forms the human-facing `IssueKey` shown everywhere (`UNC-24`). Every space and every epic has an
`IsDefault` one that can't be deleted (backlog-equivalent, so there's always somewhere for a
space/epic to have issues).

## Permissions

Two independent layers, both scoped by `OrganizationAuthData` (organization + user):

- **Organization-level** (`OrganizationUser`): `CanRead`, `Can{Create,Update,Delete}Spaces`,
  `Can{Create,Update,Delete}Epics`, `Can{Create,Update,Delete}Issues`, plus a separate
  `AdminAccessLevel` for administrative actions (e.g. renaming the organization) that isn't part
  of the entity-permission set at all.
- **Space-level** (`DirectSpacePermission`): the same `Can{Read,Update,Delete}` /
  `Can{Create,Update,Delete}Epics` / `Can{Create,Update,Delete}Issues` flags, but scoped to one
  specific space (no `CanCreateSpace` — that's inherently organization-wide).

`IAccessService.GetAccessLevelsBySpaceId` computes the effective `AccessLevels` for a space by
merging both layers with a boolean **OR** (`AccessLevels.Merge`) — an organization-wide grant
applies everywhere, a space-level grant applies only there, and either is enough. Epic- and
issue-level checks (`GetAccessLevelsByEpicId`, `GetAccessLevelsByIssueId`) resolve to the owning
space and delegate to the same space-level check — there's no separate epic/issue permission
table. Always go through `IAccessService` for a permission check rather than reading
`OrganizationUser`/`DirectSpacePermission` directly.

Permission checks belong at the **host level** (controllers in `WebApiHost`, command/message
handlers in `TelegramHost`/`TelegramServices`) — call `IAccessService` there, before invoking a
core service. Core services (`Laraue.Apps.Boards.Services`) don't check permissions themselves;
they trust the data/ids they're given and just act on them. Don't add `IAccessService` calls
inside a core service — if a new core service needs a permission check, that check belongs in
its caller.

## Choosing 404 vs 403

`IAccessService.GetAccessLevelsBy*` returns null only when the entity itself doesn't exist -
if it exists but the caller has zero access to it, it still returns a real (all-flags-false)
`AccessLevels`. That distinction matters for which exception to throw, because the two failure
modes must map to different HTTP statuses:

- **Entity doesn't exist, or exists but the caller can't even read it → 404 `NotFoundException`.**
  Returning 403 here would leak that the entity exists to someone who isn't supposed to know
  that - an org member with zero access to a private space shouldn't be able to tell "no such
  issue" apart from "an issue I'm not allowed to see" by watching for a 403 instead of a 404.
- **Entity is readable, but the specific action is denied → 403 `ForbiddenException`.** Only
  reachable once the caller has already been shown the entity exists (they can read it), so
  there's nothing left to leak.

The correct pattern is therefore always two checks, in this order:
```csharp
var accessLevels = await accessService.GetAccessLevelsByXxxId(authData, id, includeDeleted: false, ct)
    .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Xxx", id))
    .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Xxx", id, "read"));
```
then a *second*, separate check for the specific action (`CanUpdateIssue`, comment-ownership,
etc.) once `CanRead` is confirmed true - see `IssueMcpService.EditComment`
(`Laraue.Apps.Boards.McpHost`) for the full shape, including
`IAccessService.GetAccessLevelsByCommentId` resolving a comment's access through its issue for
exactly this reason. Don't skip straight to `EnsureOrThrowForbidden(a => a.CanUpdateIssue, ...)`
without confirming `CanRead` first - that path returns 403 (not 404) for someone with no access
at all, which is exactly the leak this two-step check exists to close.

## Project layout

Solution: `Laraue.Apps.Boards.sln`

- `src/Laraue.Apps.Boards.DataAccess` — EF Core `DatabaseContext`, entity models, migrations.
- `src/Laraue.Apps.Boards.Common` — generic, host-agnostic identity types with no business logic and
  no ASP.NET-specific dependencies: `OrganizationAuthData`, `AuthSchemas`,
  `ClaimsPrincipalExtensions`. Referenced by `Boards.Services` (so effectively every host, including
  `TelegramHost`, gets it transitively) — kept intentionally minimal so that transitive reach doesn't
  drag in anything a consumer might not need. Notably `VisibleUser` (the "person as seen by another
  org member" DTO — id/display name/initials/color/isCurrentUser) is **not** here: it's a plain DTO
  with zero logic, so Boards (`WebApiServices.VisibleUser`) and Retro
  (`Retro.WebApiServices.RetroUser`) each keep their own copy rather than sharing one through
  `Common`. Prefer duplicating a shape like this over adding a cross-feature dependency for it —
  reach for `Common` when there's actual behavior/logic to share, not just an identical shape.
  **Rule of thumb for what belongs in `Common`**: a type belongs here only if it's dependency-free
  (or only depends on `Laraue.Core.Exceptions`-style base packages) and every consumer would
  plausibly need it. `IAuthService`/`AuthService`/`AuthOptions` (JWT creation/validation config) are
  **not** here, on purpose — they live in `Boards.WebApiServices` (their original, pre-split home)
  even though `Retro.WebApiHost` also needs them for JWT validation. A dedicated `Boards.Auth`
  project was tried and reverted: it's not worth a fourth shared project yet for one class used by
  only three consumers (`Boards.WebApiHost`, `Retro.WebApiHost`, `Boards.WebApiServices`'s login
  token issuance) — `Retro.WebApiHost` just takes a direct `ProjectReference` to
  `Boards.WebApiServices` for it instead. Revisit this (and `Common` in general) if/when this
  becomes an actual shared package, per the original ask that started this restructuring.
- `src/Laraue.Apps.Boards.Services` — shared, host-agnostic **Boards-domain** services (DI setup
  extensions, `IAccessService`'s space/epic/issue permission engine, file storage, etc.) used by
  both Boards hosts.
- `src/Laraue.Apps.Boards.TelegramHost` — ASP.NET host for the Telegram bot webhook: routes,
  middleware pipeline, `appsettings`.
- `src/Laraue.Apps.Boards.TelegramServices` — Telegram-specific business logic (group chat
  linking, `/save`, inline search, issue preview formatting) consumed by `TelegramHost`.
- `src/Laraue.Apps.Boards.WebApiHost` — ASP.NET host for the web/Mini App REST API.
- `src/Laraue.Apps.Boards.WebApiServices` — business logic consumed by `WebApiHost`.
- `src/Laraue.Apps.Boards.McpHost` — a fourth, independently deployable ASP.NET host exposing an
  MCP server over HTTP (`/mcp`), so a program (Claude via a remote MCP connector) can read/act on
  a user's own data machine-to-machine, authenticated by a long-lived API key instead of a browser
  session's JWT. See "API keys and MCP access" below.
- `src/Laraue.Apps.Retro.Services`, `src/Laraue.Apps.Retro.WebApiServices`,
  `src/Laraue.Apps.Retro.WebApiHost` — the retro-board feature, split into its **own deployable**
  from `Boards.WebApiHost` (its own `Program.cs`, port, `appsettings`, and its own DI-wiring
  extension methods — not shared with Boards' `AddCoreServices()`/`AddDatabaseServices()`, each host
  configures its own container from scratch). It's the only feature that needs SignalR/WebSockets
  (`RetroHub`, `/hubs/retro`) — bundling that into the main API host would mean nginx has to proxy
  WebSocket traffic for everything else too, and a retro-specific incident (e.g. a runaway
  connection storm) would take down unrelated Boards endpoints. Keep new retro-adjacent work in
  these three projects, not back in `Boards.WebApiHost`/`WebApiServices`.
  - These still share `Boards.DataAccess`'s `DatabaseContext`/migrations and the retro entities
    (`Retro`, `RetroSection`, `RetroCard`, `RetroCardVote`, `RetroParticipant`) — there's no
    separate `Retro.DataAccess`.
  - `Retro.Services` deliberately does **not** reference `Boards.Services` — only
    `Boards.DataAccess` and `Boards.Common`. `Retro.WebApiServices`, however, *does* reference
    `Boards.Services`, specifically for `IAccessService`: `RetrosService.CanCreate` reuses
    `IAccessService.CanManageRetros` (org-wide grant, OR'd with a `DirectSpacePermission` grant on
    any space) instead of duplicating that merge logic, since retro management permission is
    just another flag on the same `OrganizationUser`/`DirectSpacePermission` entities Boards'
    permission engine already understands. The org-membership-and-owner-bypass check ahead of it
    stays a trivial inline query against `DatabaseContext.OrganizationUsers`, since owner bypass
    isn't something `IAccessService` models. `Retro.WebApiHost`'s
    `AddDatabaseServices()`/`AddApplicationServices()`/`AddAuthentication()` are its own local
    copies (not calls into Boards' equivalents) — registering `ICoreRetrosService`/`IRetrosService`/
    `IAccessService`/`DatabaseContext`/JWT auth, not Boards' Issue/Epic/Space/AI-summarizer stack.
    `Retro.WebApiHost` *does* reference `Boards.WebApiServices` directly, but only for
    `AuthService`/`IAuthService` — see the `Common` entry above for why that one dependency is kept
    rather than duplicated or split out further.
  - The organization JWT is minted by Boards' login flow and validated by the retro host too (same
    `AuthService` from `Boards.WebApiServices`, `AuthSchemas` from `Boards.Common`), so `Auth:Key`
    must be identical in both hosts' `appsettings`.
- `src/Laraue.Apps.StructuredMessages.DataAccess`, `src/Laraue.Apps.StructuredMessages.Services`
  — a separate app living in the same solution; unrelated to the Boards/Telegram feature set
  unless a task says otherwise.
- `tests/Laraue.Apps.Boards.IntegrationTests` — the only test project. Uses
  `Laraue.Telegram.NET.Testing` (`AppTelegramTestHost`) for Telegram-flow tests and a similar
  in-process host for web API tests. Retro tests (`RetroControllerTests`) use two in-process hosts
  side by side — `WebApiTestHost` (Boards) for seeding users/organizations via
  `WebApiTestHostScope`/`OrganizationInitializer` (which need Boards-only core services like
  `ICoreOrganizationsService`), and `RetroWebApiTestHost` for the actual `Proxy<RetroController>`
  calls — both point at the same test database. `RetroWebApiTestHost.Controller<TController>()`
  takes an optional `authServices` `IServiceProvider`: Retro's own container has no `IAuthService`
  (it never mints tokens, only validates them), so `RetroControllerTests` passes the Boards
  `WebApiTestHost`'s `Services` there to mint the test JWT while still calling through Retro's
  `HttpClient`.

## Service layering

- `Laraue.Apps.Boards.Services` holds **core** business logic shared by both hosts (e.g.
  `CoreIssuesService`, `CoreFilesService`, `CoreMassMovementService`) — anything that isn't
  specific to how the web API or the Telegram bot happens to expose it.
- **Core services are for mutations, not reads.** A `Core*Service` should only expose methods that
  change data (create/update/delete, or a validate-and-touch-a-timestamp method like
  `ICoreApiKeysService.ValidateAsync`). A plain read (list/get/search) doesn't belong there, even
  if both hosts need it — inject `DatabaseContext` directly into the `WebApiServices`/
  `TelegramServices` class instead and query it there. See `IssuesService` (`WebApiServices`),
  which injects both `ICoreIssuesService` (for its mutating calls) and `DatabaseContext` (for
  `GetIssues` and its other reads) side by side in the same class. Reads have no transaction/
  cross-entity-consistency concerns a shared core method would protect, so there's nothing to gain
  from routing them through core, and duplicating a `Core*Service`'s read method for TelegramServices/
  WebApiServices when only one host actually calls it is dead-weight API surface.
- `WebApiServices` and `TelegramServices` sit on top of core and hold logic specific to their own
  surface (request/response shaping, Telegram formatting and commands, permission checks tied to
  that surface's flow, etc.). They call into core services rather than duplicating their logic.
- Keep this boundary: don't put Telegram-specific concerns into `Services`/core, and don't put
  shared business logic directly into `WebApiServices`/`TelegramServices` — promote it to core
  instead so both surfaces can use it.
- **Keep controllers clean**: a controller action should parse the request, call into a service,
  and shape the response — no business logic in the controller itself. If a controller method is
  doing more than that, move the logic into the appropriate service.
- **Route segments are kebab-case**, not camelCase — `/api/api-keys`, not `/api/apiKeys`. A
  single-word segment (`/api/spaces`, `/api/billing`) has no casing to get wrong; the rule matters
  once a segment is more than one word.
- Core services don't open/commit/rollback transactions themselves — that's the caller's call to
  make, since only the caller knows the full scope of what needs to be atomic. A core service can
  require that it's called within an already-open transaction, but it doesn't manage the
  transaction's lifecycle.
- **Extension methods must not take DI dependencies** (`ILogger`, a `DbContext`, an injected
  service, etc.) beyond the type they extend. If a static method needs a dependency injected,
  it isn't an extension anymore — make it a proper DI-registered service (interface + class)
  instead. E.g. `EphemeralReplySender` is a real service (constructor-injects
  `ITelegramBotClient`/`ILogger<T>`) rather than an extension on `ITelegramBotClient`, precisely
  because it needs a logger; `IssuePreviewReplySender.SendIssuePreviewReply` stays a plain
  extension because it only needs the `ITelegramBotClient` it's called on plus its own arguments.
- **No tuples in public method signatures** (params or return type) — use a named `record`
  instead, even for a throwaway two-field shape. A tuple's `Item1`/`Item2` (or unlabeled
  deconstruction) forces every call site to re-derive what each value means; a record gives it a
  name once. E.g. `ICoreApiKeysService.CreateAsync` returns `ApiKeyCreationResult(Guid Id, string
  RawKey)`, not `(Guid, string)`. Tuples are fine as a private/internal implementation detail
  (e.g. a local variable inside a method body) — the rule is about what a public signature exposes.

## Workflow for new features

Don't implement a whole feature in one shot. Move step-by-step, starting from the data model
(entities/migration) and building up from there (services, wiring, UI/bot-facing text, tests).
Stop after each step and let the user review and approve before continuing to the next — don't
pile up a large diff they then have to review all at once.

## Writing tests

- Test host patterns: `GetTelegramTestHost()` → `host.SendUpdateAsync(new Update { Message = ... })`
  to simulate an incoming Telegram update, `host.Requests().OfType<SendMessageRequest>()` (or
  `.Single<T>()`) to inspect what the bot sent back. `host.CreateTestScope()` /
  `host.CreateScope()` give a `DatabaseContext` for seeding/asserting DB state directly.
- **Stale change-tracker trap**: if a test reads an entity, then triggers a write to that same row
  through a *different* `DbContext` scope (e.g. via another `SendUpdateAsync` call), and then reads
  it again on the *same* tracked context, EF's identity map can silently return the old cached
  data even though the DB was actually updated. Use `.AsNoTracking()` on any read that happens
  before a later mutation of the same rows, e.g. `db.Issues.AsNoTracking().ToListAsyncLinqToDB()`.
- Prefer asserting on user-visible output (sent message text, buttons) over internal DB shape
  where both are meaningful — it catches regressions closer to what a real user would notice.
- **Naming convention**: `{Handler}_Should{ExpectedBehavior}_When{Condition}`, e.g.
  `HandleSave_ShouldReplyWithExistingLink_WhenMessageWasAlreadySaved`,
  `HandleTextMessage_ShouldNotAutoUpdateCard_WhenEditedInBotMentionedMode`.
- Prefer a separate `[Fact]` per case over one big test covering several scenarios — failures
  point straight at the broken case instead of requiring someone to read asserts to find it.
  Exception: when the shared setup is long/expensive and the cases are just a sequence of
  actions against that same setup, it can be more readable to call them in order within one test
  (e.g. "first /save creates the card, second /save on the same message returns the existing
  link") rather than duplicating the setup across several tests.
- Don't assert on whether/what something logged (e.g. `Mock<ILogger>.Verify(...)`). It's rarely
  worth the brittleness - assert on the actual observable behavior (what got sent, what changed
  in the DB) instead.

## Database safety

- Migrations are applied automatically on startup — both hosts (`TelegramHost`, `WebApiHost`) and
  the integration test host run `Migrate()`/`MigrateAsync()` at boot, so there's normally no need
  to run `dotnet ef database update` by hand. Tests run against their own separate database, not
  the dev database.
- **Never run `dotnet ef database drop`.** `--startup-project` determines which
  `appsettings.json` connection string is used, and it does *not* necessarily point at the test
  database — it's easy to accidentally drop the dev DB by mistake.
- Before any destructive DB operation, confirm which database (dev vs. test) the command will
  actually target, and ask the user first if there's any ambiguity.

## Build-lock protocol

`dotnet build` can fail with `MSB3026`/`MSB3027` file-lock errors if a `TelegramHost` (or other
host) process is already running locally and holding the output DLLs open. Don't kill the process
yourself — ask the user to stop it, then retry the build once they confirm.

## Logging

- Always use `ILogger<T>` (the generic, type-scoped interface), never the bare non-generic
  `ILogger`, including on generic helper methods/extensions — e.g.
  `SendEphemeralNotice<T>(..., ILogger<T> logger, ...)` rather than taking a plain `ILogger`
  parameter. Keeps log category names meaningful instead of defaulting to whatever type happened
  to resolve it.

## EF Core vs LinqToDB

- Default to plain EF Core (`ToListAsync`, `FirstOrDefaultAsync`, etc. via
  `Microsoft.EntityFrameworkCore`) for queries.
- Reach for the LinqToDB extension methods (`ToListAsyncLinqToDB`, `FirstOrDefaultAsyncLinqToDB`,
  etc., via `LinqToDB.EntityFrameworkCore`) only where EF Core's LINQ provider can't translate the
  query (or translates it inefficiently) and LinqToDB can. Don't reach for LinqToDB by default —
  it's the fallback, not the first choice.

## Soft delete

`Organization`, `Space`, `Epic`, `Status`, `Issue`, and `IssueComment` are soft-deletable (nullable
`DeletedAt`/`DeletedByUserId` columns) — deleting one of these sets `DeletedAt` instead of removing
the row, and cascades the same flag down to its descendants in that list (e.g. deleting a `Space`
also soft-deletes its `Epic`s, `Status`es, `Issue`s). Nothing else in the schema is soft-deletable;
everything else stays hard-deleted.

There is deliberately **no EF Core global query filter** (`HasQueryFilter`) for this — every query
against one of these six entities states its own choice explicitly:

- For normal reads that should hide soft-deleted rows, query `context.ActiveIssues()`/
  `ActiveSpaces()`/`ActiveEpics()`/`ActiveStatuses()`/`ActiveOrganizations()`/`ActiveIssueComments()`
  (`Laraue.Apps.Boards.DataAccess.DatabaseContextActiveEntityExtensions`) instead of the raw
  `context.Issues`/etc. DbSet.
- For audit/history features that must keep working after the row is soft-deleted (e.g.
  `OrganizationHistoryService`), query the raw `context.Issues`/etc. DbSet directly - the row is
  still there, so an ordinary join/read finds it exactly as before.
- `IAccessService.GetAccessLevelsBySpaceId`/`GetAccessLevelsByEpicId`/`GetAccessLevelsByIssueId`/
  `GetAvailableSpaces` take an explicit `includeDeleted` parameter (no default value) for the same
  reason - pass `true` only from an audit/history caller, `false` everywhere else.

This was chosen over a global filter specifically because this repo also queries through
`LinqToDB.EntityFrameworkCore`, and it was never verified whether LinqToDB's bridge honors EF's
`HasQueryFilter` model metadata - an explicit `.Where(x => x.DeletedAt == null)` (which is what the
`Active*()` helpers do) has no such question mark, since it's an ordinary predicate already baked
into the query before either provider translates it.

## No unpaginated list endpoints

Every endpoint that returns a collection whose size depends on user data (not a small fixed set)
must be paginated - never return a bare array/`.ToListAsync()` result straight to the client, even
if today's data volumes make it seem harmless. Follow the existing `PaginationData`/
`ShortPaginatedResult<T>` (`Laraue.Core.DataAccess.Contracts`) convention already used throughout
(`IssuesController.Search`, `BillingController.GetTransactions`, `EpicsController.SearchEpicsWithStatuses`,
`ApiKeysController.GetAll`): the request implements `IPaginatedRequest` (a `Pagination` property),
the endpoint is a `[HttpPost("search")]` taking that request as its body (a `GET` can't carry a
JSON body for pagination params), and the query ends in `.ShortPaginateEFAsync(request.Pagination,
cancellationToken)` (or the LinqToDB equivalent, `ShortPaginateLinq2DbAsync`) instead of
`.ToListAsync()`.

## Query shape: project, don't load-then-map

- Don't `Include`/`ThenInclude` a full entity graph just to read a handful of fields off it.
  Project straight to the shape the caller needs with `.Select(...)` — pull only the columns
  actually used. This avoids over-fetching (whole `User`/navigation entities when only
  `DisplayName`/`Initials`/`Color` are needed) and skips `AsSplitQuery()` entirely, since EF
  already issues one query per projected collection when you `Select` into nested arrays/DTOs —
  `AsSplitQuery()` is only relevant for `Include`-based graphs.
- Don't add `AsNoTracking()` to a query that ends in `.Select(...)` into a non-entity type (a DTO,
  an anonymous type, etc.) — EF Core never tracks projected results in the first place, so it's a
  no-op there. `AsNoTracking()` only matters when the query's result is the entity type itself
  (e.g. returned via `Include` or a bare `Where(...).ToListAsync()` with no projection).
- Project directly into the final response DTO inside the `Select` (e.g. `new VisibleUser {
  UserId = p.UserId, DisplayName = p.User!.DisplayName, ... }`) instead of projecting to an
  anonymous type first and mapping it to the DTO in a second, separate step — that second step is
  usually redundant work once the query already has everything the DTO needs.
- When one response combines rows from several unrelated collections off the same aggregate root
  (e.g. a retro's sections, cards, and participants), don't fetch it as a single query with nested
  `Select`s off the root — issue one focused, independent query per collection instead (e.g.
  `context.RetroSections.Where(x => x.RetroId == id)...`, `context.RetroCards.Where(x =>
  x.Section!.RetroId == id)...`, `context.RetroParticipants.Where(x => x.RetroId == id)...`) and
  assemble the response from the results. Scalar/root fields needed by a later projection (like a
  parent's `Phase` used to decide whether a card's vote count/text should be hidden) can be
  captured from an earlier query and referenced as a local variable in a later query's `Select` —
  EF Core parameterizes it, it's not a client-eval issue. See
  `RetrosService.Get(long id, OrganizationAuthData, CancellationToken)` for the pattern.
- Core services that only need to check a scalar condition (existence, a status flag, a count)
  shouldn't load the owning entity graph to get it — query directly for that scalar instead. E.g.
  `CoreRetrosService.SetVote` queries `Phase`/`VoteEndsAt`/`VotesPerUser` via a `Select` and looks
  up the caller's own vote with a targeted `FirstOrDefaultAsync`, rather than `Include`-ing the
  card's `Section`, `Retro`, and entire `Votes` collection.

## AI content summarization

`Laraue.Apps.Boards.Services.Ai` (core, shared by both hosts) provides `IAiContentSummarizer` /
`OpenAiCompatibleContentSummarizer`, which calls any OpenAI-compatible chat-completions API
(`POST {BaseUrl}chat/completions`) to rewrite chaotic notes into `title\n---\ncontent` markdown
without inventing new content — see the system prompt in `OpenAiCompatibleContentSummarizer` for
the exact "beautify, don't invent" contract, including how it decides whether to keep an existing
title vs. derive one.

- **Config-driven provider, not code-driven**: `AiSummarizerOptions` (`ApiKey`, `BaseUrl`, `Model`,
  `Thinking`) has no defaults in code — both hosts' base `appsettings.json` bind an `AiSummarizer`
  section pointing at a local Ollama instance (`http://localhost:11434/v1/`, OpenAI-compatible),
  since that's free to run for local dev. Production overrides those three settings to point at a
  real provider (e.g. DeepSeek) instead of changing any code. `Thinking` defaults to `false`
  (`"thinking": {"type": "disabled"}` in the request) — extended chain-of-thought reasoning isn't
  needed for this task and meaningfully slows down models that support toggling it.
- **Failure handling**: `OpenAiCompatibleContentSummarizer` only ever throws
  `AiContentSummarizationException` (defined alongside it in `Services.Ai`) on any failure mode
  (non-success HTTP status, empty response, no completion content) — never a raw
  `HttpRequestException`/`InvalidOperationException`. Each host-level caller catches it, logs a
  `LogWarning`, and translates it into its own surface's error convention rather than letting it
  bubble as a generic 500/unhandled exception: `WebApiServices.IssuesService.SummarizeContent`
  rethrows `AiSummarizationUnavailableException` (a `HttpException` subclass → 503, since
  `Laraue.Core.Exceptions.Web` doesn't ship one for "downstream dependency failed" out of the box —
  `HttpException`'s constructor is `protected`, so it's still subclassable from application code);
  `TelegramServices.SaveCommandService` catches it and sends the `Phrases.AiSummarizationUnavailable`
  ephemeral notice instead of leaving the user hanging.
- **Testing without a real AI call**: both `WebApiTestHost` and `TelegramIntegrationTest` register
  a `Mock<IAiContentSummarizer>` as a DI override (last-registered-wins, same pattern as the
  Telegram bot client mock) — tests reconfigure it per-case via `Mock.Get(...)`/the exposed mock
  property rather than hitting a real provider.
- `/save` and `/aisave` are the same command with an extra step, not two features — they're both
  handled by `SaveCommandService`/`ISaveCommandService` (one `Summarize` bool flag threaded through
  to `TelegramSaveMessageService.SaveByReply`'s `SaveByReplyRequest.Summarize`), routed from a
  single `SaveController` with two `[TelegramMessageRoute]` actions. Resist the urge to give a
  "same flow, one extra step" variant its own command-service class — that was tried and reverted
  in favor of this shared-method approach.

## External services (Identity, Billing) and local mocking

Boards calls two sibling services over gRPC:

- `Laraue.Apps.Identity` — resolves/creates the global Laraue identity for a Telegram account on
  first login (`CoreUserService`, via `UserIdentityService.UserIdentityServiceClient` injected
  directly — there's no Boards-side wrapper interface for it since it's a single call with a
  single caller).
- `Laraue.Apps.Billing` — AI token reserve/commit/cancel and subscription/limit lookups, wrapped
  behind `IBillingTokenClient`/`IBillingSubscriptionClient`
  (`Laraue.Apps.Boards.Services.Billing`) rather than exposing the generated gRPC clients
  directly, since callers need Boards' own personal-vs-team `Organization` resolution layered on
  top (see the XML doc on `IBillingTokenClient` for why that resolution lives here and not as a
  caller-supplied flag).

**Local run without either service actually running**: `AddCoreServices()` always registers the
real gRPC-backed clients — that's harmless even when unused, since `AddGrpcClient`/
`AddLaraueGrpcClient` only open a channel lazily, on the first call a real client would make. A
`"MockExternalServices": true` config flag (set in both hosts' `appsettings.Development.json`,
`false` everywhere else) then registers `FakeUserIdentityServiceClient`/`FakeBillingTokenClient`/
`FakeBillingSubscriptionClient` **after** the real ones — last-registered-wins, the same override
pattern the integration tests already use for the Telegram bot client/AI summarizer mocks. This is
deliberately a single `if (mockExternalServices) { ... }` block wrapping three registrations, not
an `if`/`else` duplicating the real registrations under a negated condition — the real registrations
above always run unconditionally.

- `FakeUserIdentityServiceClient` subclasses `UserIdentityService.UserIdentityServiceClient`
  (generated gRPC clients are designed to be subclassed for exactly this — Moq does the same thing
  in tests) and overrides its one virtual RPC method to mint a fresh `Guid` instead of calling out.
- `FakeBillingTokenClient`/`FakeBillingSubscriptionClient` implement the wrapper interfaces
  directly (no gRPC involved at all) and report a generous fixed balance/unlimited subscription,
  so `IUsageLimitService` never blocks a local run for lack of a real plan.
- Don't use these fakes for anything test-project-scoped — they're for running a host
  (`WebApiHost`/`TelegramHost`) locally without external dependencies. The integration tests have
  their own separate `Mock<IBillingTokenClient>`/`Mock<IBillingSubscriptionClient>`/
  `Mock<UserIdentityService.UserIdentityServiceClient>` overrides in `WebApiTestHost`/
  `TelegramIntegrationTest` and don't reference these fakes.

## API keys and MCP access

Lets a program (Claude, via a remote MCP connector) act as an organization member without a
browser session. Three pieces:

- **`ApiKey`** (`DataAccess.Models.ApiKey`) — a long-lived credential scoped to one organization
  and one member (`CreatedByUserId`). Its authority is never snapshotted: every use resolves
  `CreatedByUserId`'s access **live**, through the exact same `IAccessService` checks a normal JWT
  request goes through - if that member later loses a permission or is removed from the org, the
  key silently loses it too, for free. `ICoreApiKeysService` (`Boards.Services`) owns
  create/revoke/validate; it only covers mutations (per "Core services are for mutations, not
  reads" above) - listing a caller's own keys is a plain read living in
  `WebApiServices.ApiKeysService`, querying `DatabaseContext` directly.
- **Self-service, not admin-gated**: a key only ever grants what its own creator could already do,
  so there's no `AdminAccessLevel` flag for managing keys - a member creates/lists/revokes only
  their own keys (`WHERE CreatedByUserId == <caller>`), via `POST/GET/DELETE /api/api-keys` on the
  existing `AuthSchemas.Organization` JWT scheme. Org-wide admin visibility into every member's
  keys is a deliberately deferred future ask, not an oversight.
- **`AuthSchemas.ApiKey`** (`Boards.Common`) + `ApiKeyAuthenticationHandler`
  (`Boards.Services.Auth`) — a second authentication scheme, independent of the JWT ones. Reads an
  `X-Api-Key` header, calls `ICoreApiKeysService.ValidateAsync`, and on success builds a
  `ClaimsPrincipal` with the *same* `orgId`/`id` claim types the JWT schemes use - so
  `GetOrganizationAuthData()` and every existing `IAccessService`/controller-level check work
  completely unchanged regardless of which scheme authenticated the caller. Only `McpHost`
  registers this scheme; no existing `WebApiHost`/`TelegramHost` endpoint accepts an API key.
- **`Laraue.Apps.Boards.McpHost`** — the fourth host (see "Project layout"), built on
  `ModelContextProtocol.AspNetCore`. `AddCoreServices()` is called here same as any host, which
  means `ICoreFilesService`'s `ITelegramBotClient` dependency has to be satisfied too even though
  no MCP tool touches file attachments - `Program.cs` registers a real `TelegramBotClient` purely
  to satisfy ASP.NET's build-time DI validation, the same way `WebApiHost` already does.
  `McpServerOptions.ServerInstructions` (`McpServerInstructions.cs`) is sent to every connecting
  client, telling it to reach for these tools instead of asking the user to paste issue content in.
  Tool types (`Tools/IssueTools.cs`, `[McpServerToolType]`) are thin adapters, same shape as a
  controller: resolve the caller's `OrganizationAuthData` from
  `IHttpContextAccessor.HttpContext!.User` (populated by the API key handler above), call into a
  plain service, return the result - no query/permission/mutation logic in the tool type itself.
  That logic lives in `Services/IssueMcpService.cs` (`IIssueMcpService`), which delegates straight
  into `IAccessService`/`ICoreIssuesService` - the exact same permission checks and mutation path
  the REST API uses, no new logic. Tests construct `IssueMcpService` directly against the
  integration test database (`IssueMcpServiceTests.cs`), passing a plain `OrganizationAuthData`,
  rather than driving the real MCP HTTP/SSE transport - not worth the effort for what's otherwise
  already-covered `IAccessService`/core-service behavior. `IssueTools` itself has no dedicated
  tests, same reason a controller doesn't usually get tested separately from the service it calls.
  Tools cover `list_issues`/`get_issue`/`update_issue_status` plus `create_issue`/`edit_issue`/
  `add_comment`/`edit_comment`/`get_attachment` and the discovery tools `list_spaces`/
  `list_statuses`/`list_attributes`/`list_members` - each still just the REST API's own
  permission/mutation path (`CanCreateIssue` off the target status's epic, `CanUpdateIssue` for
  edits/comments, owner-only for editing a comment - same as `IssuesService.UpdateIssueComment`,
  not gated by `CanUpdateIssue`).
  `update_issue_status`/`create_issue` take a **`statusId`** (matching the REST API's own shape -
  `IssuesService.Create` also just takes a raw `StatusId`, no separate space concept at all) - and
  `list_statuses` exists to make that id discoverable, since an MCP caller has no status-picker UI
  the way the REST API's frontend does. `update_issue_status` accepts *any* status id the caller
  can move issues to, not necessarily one in the issue's current epic - moving an issue to a
  different epic's status is real REST API behavior too (an issue's epic is entirely derived from
  its `StatusId`), not something worth artificially restricting just because MCP takes an id.
  `create_issue` has **no `spaceKey` parameter at all**, matching the REST API's `Create` exactly
  - an earlier revision added one plus a "does `statusId` belong to `spaceKey`" cross-check,
  reasoning that `CanCreateIssue` needed a caller-supplied space to check against. That was
  unnecessary: `IssuesService.Create` proves permission can be derived directly from `statusId`'s
  own epic (`GetAccessLevelsByEpicId`), with nothing left to cross-validate once there's no second
  space parameter to disagree with it. `create_issue`'s `statusId` is **required**, not defaulted
  - `list_statuses` always has to be called first, which also means a caller always knows and
  states exactly which status a new issue lands in, rather than relying on an implicit "space's
  default" a caller can't see without a separate lookup anyway. `list_attributes` plays the
  equivalent discovery role for `create_issue`/`edit_issue`'s `attributes` map, whose keys are
  attribute **ids** (`list_attributes` returns each attribute's id, and for `AttributeType.List`,
  each allowed value's own id too) - matching `statusId`'s id-based shape rather than the
  name-based alternative once used here. Names were briefly tried since attribute names are
  already unique per org (unlike a status name, which needs epic-scoping to disambiguate), but
  ids won: an agentic caller already has `list_attributes`' full output in context right before
  calling `create_issue`/`edit_issue`, so there's no real memorization cost, and ids let the whole
  validate-and-build step be shared with the REST API instead of duplicated (see below).
- **Attributes** (custom per-organization fields - `Attribute`/`AttributeListValue`,
  `Laraue.Apps.Boards.DataAccess.Models`) are flat and org-wide, never scoped to a space/epic -
  `list_attributes` and `create_issue`/`edit_issue`'s `attributes` map (attribute id → plain text
  value; for `AttributeType.List`, the value is one of that attribute's list value ids, also as
  plain text) both just filter `context.Attributes` by `OrganizationId`.
  `Laraue.Apps.Boards.Services.AttributeRequests.AttributeValue` (+ its 6 typed subtypes -
  `StringAttributeValue`, `IntegerAttributeValue`, `DecimalAttributeValue`, `DateAttributeValue`,
  `DateTimeAttributeValue`, `EnumAttributeValue`) is the **shared** representation both hosts
  build before handing off to `ICoreIssueAttributesService.BuildSetRequests` - the one place that
  validates each value against the attribute's real type (batched, one query for attribute types
  + one for list-value-id existence, not N+1) and builds the corresponding
  `SetIssueAttributeRequest`s, collecting every problem found (not just the first) into a single
  `BadRequestException`. The REST API's client already sends an already-typed `AttributeValue`
  per attribute (see `IssuesService.CreateIssueRequest`/`UpdateIssueRequest`, `[JsonModelBinder]`
  picking the right derived type) and calls `BuildSetRequests` directly. MCP callers can only
  produce plain text, so `IssueMcpService.ParseAttributeValue` does the one genuinely
  MCP-specific step: parsing raw text into the right typed `AttributeValue` per `AttributeType`
  (`long`/`decimal`/`DateOnly`/`DateTime.TryParse`, and for `List`, just `long.TryParse` into a
  `ValueId` now that the caller passes an id instead of matching display text) - then hands the
  result to the same shared `BuildSetRequests`. `IssueMcpService.ResolveAttributeRequests` still
  has to merge MCP's own parse-time errors with whatever `BuildSetRequests` itself throws (rather
  than short-circuiting on the first parse failure) so a caller sees every problem across every
  attribute in one response, matching the batched-error guarantee `BuildSetRequests` already gives
  REST callers. `CreateIssue`/`EditIssue` both check `attributes is not null` (not
  `attributeRequests.Count > 0`, which was 0 in both the omitted and the explicitly-empty case and
  could never actually reach the empty-`SetAttributes` clear path) - a bug caught and fixed
  mid-session, not a design choice to preserve: `null`/omitted `attributes` leaves every attribute
  untouched, while an explicit empty object (`{}`) clears every attribute the issue currently has.
- **File attachments** (`create_issue`/`edit_issue`'s optional `files` param, a
  `FileAttachment(FileName, ContentType, Base64Content)[]`) - MCP has no multipart upload channel
  the way the REST API's `IFormFile[]` does, so a caller sends each file base64-encoded instead;
  `IssueMcpService.UploadFiles` decodes it, then calls the exact same
  `ICoreFilesService.UploadFile` the REST API uses - same storage path, same restriction to
  `SystemMimeTypes.Supported` (images only today), same `SystemMimeTypes.MaxFileSizeBytes` (3MB)
  cap. Validation (unsupported type, invalid base64, too large) follows the same
  batched-all-errors-at-once pattern as attributes. `edit_issue`'s `files` are added alongside the
  issue's existing attachments; `removeAttachmentIds` (a `Guid[]`, matching REST's own
  `RemoveAttachmentIds`/`IssueUpdateRequest.UnlinkAttachments`) removes existing ones by id, and
  both can be given in the same call to replace one attachment with another. `get_issue`'s
  `IssueDetail` includes `Attachments` (id + file name) so a caller has a way to discover those
  ids. `get_attachment` (id from that same list) goes the other direction - downloads one
  attachment's original file content, returned as a real MCP `ImageContentBlock` rather than a
  JSON field with a base64 string wedged into it. `IssueMcpService.GetAttachmentContent` resolves
  `IssueAttachment` -> `Attachment.FileId`, permission-checks `CanRead` on the owning issue, then
  delegates to `ICoreFilesService.GetFileContent(fileId)` - a read, so it lives on
  `Boards.Services`' `CoreFilesService` even though "core services are for mutations" above.
  `GetFileContent` returns a `FileContent(Stream Content, string MimeType)` record whose `Content`
  is a live local-file or HTTP-response stream, not a pre-buffered `byte[]` - `IssueTools.
  GetAttachment` is the only place that actually needs bytes (an `ImageContentBlock.Data` is a
  `ReadOnlyMemory<byte>`), so it's the only place that buffers the stream into memory, right
  before building the response, and disposes the stream immediately after. `get_attachment`
  enforces the same 3MB cap uploads use, in two layers: `IssueMcpService.GetAttachmentContent`
  rejects a file whose DB-recorded `File.Size` already exceeds it before ever opening a stream,
  and `IssueTools.GetAttachment` separately enforces a hard runtime cap while copying the stream
  into memory, so a missing/wrong `File.Size` still can't cause unbounded buffering - the DB check
  is an optimization, the runtime cap is the actual OOM guard. The DB lookup (local-cache path +
  mime type) and the Telegram download-URL construction are shared with `FilesController.
  GetFileById` too, via `ICoreFilesService.ResolveFileLocation`/`ResolveTelegramDownloadUrl` - the
  controller still owns its own Range-forwarding and `IMemoryCache` URL-caching (genuinely
  HTTP-response-specific concerns `GetFileContent` doesn't need).
- **`list_issues`' `assigneeId`** (a `Guid`) replaced an earlier `assigneeName` display-name
  substring filter, matching the id-based convention `statusId`/attribute ids already use - a
  display name filter can silently match zero or several people, while an id is unambiguous.
  `list_members` (`IssueMcpService.ListMembers`, wrapping `IAccessService.GetAvailableSpaces`/
  `GetVisibleUsers` the same way REST's `OrganizationsController.GetMembers` does with no
  `spaceKey` given) exists to make `assigneeId` discoverable - the same "call this first" role
  `list_statuses`/`list_attributes` play. `list_spaces` (`IssueMcpService.ListSpaces`, wrapping
  `IAccessService.GetAvailableSpaces`) exists for the same reason - `spaceKey` (used by
  `list_issues`/`list_statuses`) had no MCP discovery path before it. Neither is paginated, same
  as their REST equivalents - organization membership/space count are naturally small.

## User-facing text

- Don't put string literals directly in `throw new SomeException("...")`/ephemeral-notice calls.
  Every host/surface keeps its **own** `Resources/ErrorMessages.resx` (`WebApiServices`,
  `McpHost`) or `Resources/Phrases.resx`/`Phrases.ru.resx` (EN/RU, `TelegramServices`) rather than
  sharing one across projects — there's no cross-project resx reference in this codebase, so a new
  host adds its own copy. Reuse an existing **template shape** when it matches exactly, even
  across surfaces: `EntityNotFound = "{0}: {1} is not found"`, `EntityNotFoundOrNotAccessible =
  "{0}: {1} is not found or not accessible"`, `EntityActionForbidden = "{0}: {1} {2} is
  forbidden"` (see "Choosing 404 vs 403" above for `EntityActionForbidden`'s `"...", "read"` case)
  all exist verbatim in both `WebApiServices` and `McpHost`'s resx files - copy the key+value into
  the new surface's own `.resx` rather than inventing a differently-worded equivalent. Add a new
  resx key only when the message shape is genuinely different (e.g. `McpHost`'s
  `StatusNotFoundInIssueEpic`, which needs two placeholders in a shape none of the generic
  templates cover).
- **`.Designer.cs` isn't auto-regenerated by `dotnet build`** on this machine — it's a
  Visual-Studio-only single-file-generator step. After adding a `<data>` entry to a `.resx`, also
  hand-add the matching `internal static string Foo { get { return
  ResourceManager.GetString("Foo", resourceCulture); } }` property to the paired `.Designer.cs`,
  mirroring an existing entry's shape. Wire a brand-new resx into its `.csproj` too - an
  `<EmbeddedResource Update="Resources\ErrorMessages.resx">` with `Generator`/`LastGenOutput`, and
  a `<Compile Update="Resources\ErrorMessages.Designer.cs">` with `DesignTime`/`AutoGen`/
  `DependentUpon` - copy both `ItemGroup`s from an existing project (e.g. `WebApiServices.csproj`)
  rather than retyping them.

## Task flow
- Create branch with pattern feature/task-number-task-description, like feature/BRD-120-add-assignee-api for new task