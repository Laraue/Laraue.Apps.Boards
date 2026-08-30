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
  - `Retro.Services`/`Retro.WebApiServices` deliberately do **not** reference `Boards.Services`
    — only `Boards.DataAccess` and `Boards.Common`. `RetrosService` doesn't take an `IAccessService`
    dependency at all; it inlines its own trivial "is this user an org member" check directly
    against `DatabaseContext.OrganizationUsers` rather than pulling in Boards' full space/epic/issue
    permission engine for two methods it doesn't need. `Retro.WebApiHost`'s
    `AddDatabaseServices()`/`AddApplicationServices()`/`AddAuthentication()` are its own local
    copies (not calls into Boards' equivalents) — registering only `ICoreRetrosService`/
    `IRetrosService`/`DatabaseContext`/JWT auth, not Boards' Issue/Epic/Space/AI-summarizer stack.
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
- `WebApiServices` and `TelegramServices` sit on top of core and hold logic specific to their own
  surface (request/response shaping, Telegram formatting and commands, permission checks tied to
  that surface's flow, etc.). They call into core services rather than duplicating their logic.
- Keep this boundary: don't put Telegram-specific concerns into `Services`/core, and don't put
  shared business logic directly into `WebApiServices`/`TelegramServices` — promote it to core
  instead so both surfaces can use it.
- **Keep controllers clean**: a controller action should parse the request, call into a service,
  and shape the response — no business logic in the controller itself. If a controller method is
  doing more than that, move the logic into the appropriate service.
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

## User-facing text

- Don't put string literals directly in `throw new SomeException("...")`/ephemeral-notice calls.
  `WebApiServices` uses `Resources/ErrorMessages.resx` (+ `.Designer.cs`, `string.Format(...)` for
  placeholders — reuse an existing template like `EntityNotFound = "{0}: {1} is not found"` or
  `EntityActionForbidden = "{0}: {1} {2} is forbidden"` when the message shape matches exactly,
  add a new resx key otherwise). `TelegramServices` uses `Resources/Phrases.resx` +
  `Phrases.ru.resx` (EN/RU) for anything sent back to a Telegram user.
- **`.Designer.cs` isn't auto-regenerated by `dotnet build`** on this machine — it's a
  Visual-Studio-only single-file-generator step. After adding a `<data>` entry to a `.resx`, also
  hand-add the matching `internal static string Foo { get { return
  ResourceManager.GetString("Foo", resourceCulture); } }` property to the paired `.Designer.cs`,
  mirroring an existing entry's shape.

## Task flow
- Create branch with pattern feature/task-number-task-description, like feature/BRD-120-add-assignee-api for new task