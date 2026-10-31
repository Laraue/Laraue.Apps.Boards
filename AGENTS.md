# AGENTS.md

Conventions and gotchas for AI agents in this repo that aren't obvious from the code.

## What this project is

Backend for "Boards", a Jira-like tracker: organizations contain spaces, spaces contain epics/statuses, epics contain
issues ("cards"). Two surfaces:

- A web API for the separate frontend (Mini App / web board UI): organizations, spaces, epics, statuses, issues,
  attachments, entity-level and administrative permissions.
- A Telegram bot that links a chat to an organization/epic/status so messages become issues:
  - `SaveMode.EachMessage` - every message in a linked chat is saved as a card (an edit updates it). For personal chats.
  - `SaveMode.BotMentionedMessages` - nothing is saved automatically; replying `/save` (optionally with a note) turns
    a message into a card. For busy group chats.
  - Card creation is gated by `IAccessService`, not just by the chat being linked.
  - `/info` - reply to a tracked message to get its card's preview/link without changing anything
    (`TelegramSaveMessageService.GetInfoByReply`, `InfoCommandService`); mostly for `EachMessage` mode, where saving is
    silent.
  - Inline search (`@bot query`) searches issues across every readable space (`SearchService`,
    `TelegramServices/Services/Search`): `key:value` tokens (assignee, organization, space, updated date, direct issue
    key - the `*TokenFilter` classes, `TokenFilterRegistry`) plus free text matched against content with a highlighted
    snippet (`ContentFragment`).
  - Search results and `/save` replies share one "issue preview card" (key · org · snippet, plus "💬 chat · sender ·
    date" for issues from Telegram): `IssuePreviewFormatter`.
  - An issue's source (BRD-324): the web issue and MCP `get_issue` return the Telegram message it was created from
    (`IssueSourceReader`), with a t.me link built only by `TelegramMessageUrlBuilder` from the message's own chat id
    and `LinkedTelegramChat.Username` (set on link, refreshed from every group message by `GroupChatService`):
    public supergroup `t.me/{username}/{id}`, private `t.me/c/{id without -100}/{id}`, none for basic groups and the
    private chat with the bot. A basic group → supergroup upgrade (`ChatMigrationService`) moves only the link:
    the supergroup numbers messages anew, so earlier messages keep the old chat id and get no link.

See [README.md](README.md) for the human overview, local run and permissions model.

## Issue hierarchy

`Organization` → `Space` (short `Key`, e.g. `UNC`) → `Epic` → `Status` → `Issue`. `IssueNumber` is a per-space
sequential number; with the space key it forms the `IssueKey` shown everywhere (`UNC-24`). Every space has an
undeletable `IsDefault` epic (the backlog). Spaces have no default one (BRD-293): sign-up and organization creation make a
regular "Default Space" (`DEF`), which can be renamed, moved and deleted like any other.

`Epic.Status` (New/Active/Done) is derived from the epic's active issues and never set by hand (BRD-323): New with none
or all Created, Done with all Completed, Active otherwise. It's a stored column, so every write that can change it
(issue create/move/delete, a status's category edit or deletion) calls `ICoreEpicStateService.RecalculateStatuses` in the
same transaction - do the same in any new such write. It locks the epics in id order (`LockEpics`) before computing, so
concurrent writes to one epic recalculate one after another.

## Permissions

Two layers, both scoped by `OrganizationAuthData` (organization + user):

- **Organization-level** (`OrganizationUser`): `CanRead`, `Can{Create,Update,Delete}{Spaces,Epics,Issues}`, plus a
  separate `AdminAccessLevel` for administrative actions (e.g. renaming the organization).
- **Space-level** (`DirectSpacePermission`): the same flags for one space (no `CanCreateSpace`).

`IAccessService.GetAccessLevelsBySpaceId` merges both with a boolean OR (`AccessLevels.Merge`). Epic and issue checks
(`GetAccessLevelsByEpicId`/`ByIssueId`) resolve to the owning space - there's no epic/issue permission table. Always go
through `IAccessService`, never read `OrganizationUser`/`DirectSpacePermission` directly. From an issue key use
`IAccessService.GetIssueAccessByKey` (issue id + `AccessLevels`, null only when the key doesn't exist) rather than an
`IssueNumbers` lookup plus `GetAccessLevelsByIssueId` (older services still have their own copies).

Permission checks belong at the **host level** (WebApiHost controllers, TelegramHost/TelegramServices handlers), before
calling a core service. Core services (`Boards.Services`) trust the ids they're given and never call `IAccessService`.

## Choosing 404 vs 403

`IAccessService.GetAccessLevelsBy*` returns null only when the entity doesn't exist; zero access is a real all-false
`AccessLevels`. Map them so existence never leaks:

- **Missing, or exists but not readable → 404 `NotFoundException`.** A 403 would tell a member with no access that the
  entity exists.
- **Readable, but this action is denied → 403 `ForbiddenException`.**

So always two checks, in this order:
```csharp
var accessLevels = await accessService.GetAccessLevelsByXxxId(authData, id, includeDeleted: false, ct)
    .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Xxx", id))
    .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Xxx", id, "read"));
```
then a separate check for the action (`CanUpdateIssue`, comment ownership, ...) - see `IssueMcpService.EditComment`
(McpHost), and `IAccessService.GetAccessLevelsByCommentId`, which resolves a comment through its issue for this reason.
Never go straight to `EnsureOrThrowForbidden(a => a.CanUpdateIssue, ...)`.

## Project layout

Solution: `Laraue.Apps.Boards.sln`.

- `Boards.DataAccess` - EF Core `DatabaseContext`, entities, migrations.
- `Boards.Common` - minimal, dependency-free identity types: `OrganizationAuthData`, `AuthSchemas`,
  `ClaimsPrincipalExtensions`, `Actor`. Referenced (transitively) by every host, so keep it small: a type belongs here
  only if it's dependency-free (or needs only `Laraue.Core.Exceptions`-style base packages) and every consumer would
  plausibly need it, and has real logic to share. A plain DTO shape is duplicated instead - `VisibleUser`
  (`WebApiServices`) and `RetroUser` (`Retro.WebApiServices`) are separate copies on purpose. `IAuthService`/
  `AuthService`/`AuthOptions` (JWT) stay in `Boards.WebApiServices`; `Retro.WebApiHost` references that project for
  them (a separate `Boards.Auth` project was tried and reverted). Revisit if this becomes a shared package.
- `Boards.Services` - shared host-agnostic Boards services: DI extensions, the `IAccessService` permission engine, file
  storage, etc.
- `Boards.TelegramHost` / `Boards.TelegramServices` - the bot webhook host, and its logic (chat linking, `/save`, inline
  search, preview formatting).
- `Boards.WebApiHost` / `Boards.WebApiServices` - the web/Mini App REST host and its logic.
- `Boards.McpHost` - an independently deployable MCP server over HTTP (`/mcp`) for machine callers authenticated by API
  key. See "API keys and MCP access".
- `Retro.Services`, `Retro.WebApiServices`, `Retro.WebApiHost` - the retro board as its **own deployable** (own
  `Program.cs`, port, `appsettings`, DI wiring - nothing shared with Boards' `AddCoreServices()`/
  `AddDatabaseServices()`). It's the only SignalR/WebSocket feature (`RetroHub`, `/hubs/retro`), kept apart so nginx
  doesn't proxy WebSockets for everything and a retro incident can't take Boards down. Keep retro work here.
  - Shares `Boards.DataAccess` (`Retro`, `RetroSection`, `RetroCard`, `RetroCardVote`, `RetroParticipant`); no
    `Retro.DataAccess`.
  - `Retro.Services` references only `Boards.DataAccess`/`Boards.Common`. `Retro.WebApiServices` references
    `Boards.Services` for `IAccessService.CanManageRetros` (org grant OR a space grant on any space); the
    membership/owner-bypass check before it is an inline `OrganizationUsers` query, since `IAccessService` doesn't model
    owner bypass. `Retro.WebApiHost`'s `AddDatabaseServices()`/`AddApplicationServices()`/`AddAuthentication()` are its
    own copies, registering only `ICoreRetrosService`/`IRetrosService`/`IAccessService`/`DatabaseContext`/JWT auth.
  - The organization JWT is minted by Boards and validated by both hosts, so `Auth:Key` must match in both.
- `StructuredMessages.DataAccess`, `StructuredMessages.Services` - an unrelated app in the same solution.
- `tests/Laraue.Apps.Boards.IntegrationTests` - the only test project. `Laraue.Telegram.NET.Testing`
  (`AppTelegramTestHost`) for Telegram flows, an in-process host for the web API. `RetroControllerTests` runs
  `WebApiTestHost` (seeding via `WebApiTestHostScope`/`OrganizationInitializer`) and `RetroWebApiTestHost` side by side
  on one test database; `RetroWebApiTestHost.Controller<T>()` takes the Boards host's `Services` as `authServices` to
  mint the JWT, since Retro has no `IAuthService`.

## Service layering

- `Boards.Services` holds **core** logic shared by the hosts (`CoreIssuesService`, `CoreFilesService`,
  `CoreMassMovementService`, ...). `WebApiServices`/`TelegramServices` hold surface-specific logic (request/response
  shaping, Telegram formatting, surface permission checks) and call core rather than duplicating it. Telegram concerns
  stay out of core; shared business logic doesn't live in a host's services.
- **Core services are for mutations, not reads.** A `Core*Service` exposes only data-changing methods (or
  validate-and-touch like `ICoreApiKeysService.ValidateAsync`). Plain reads inject `DatabaseContext` directly in the
  host service - e.g. `WebApiServices.IssuesService` uses `ICoreIssuesService` for writes and `DatabaseContext` for
  `GetIssues`. Exceptions, for a non-trivial read mapping that two hosts must share exactly: `Services.History.
  OrganizationHistoryReader` (WebApiHost history and McpHost `get_issue_history`; its DTOs `OrganizationHistoryItem`,
  `HistoryItemChange`, `UserDetails` moved with it), `Services.Links.IssueLinksReader` and
  `Services.Sources.IssueSourceReader`. They're named `*Reader`, do no
  permission checks, and take the ids/spaces the caller already checked (`WebApiServices.OrganizationHistoryService`,
  `IssueMcpService.GetIssueHistory`).
- Core services never open/commit/roll back transactions - the caller owns the scope. A core method may require one
  (`EnsureTransactionStarted()`).
- **Thin controllers**: parse the request, call a service, shape the response.
- **Route segments are kebab-case** (`/api/api-keys`).
- **Extension methods take no DI dependencies** beyond the extended type. Needing a logger/DbContext/service means a
  real DI service (e.g. `EphemeralReplySender`); `IssuePreviewReplySender.SendIssuePreviewReply` stays an extension.
- **No tuples in public signatures** - use a named record (`ApiKeyCreationResult(Guid Id, string RawKey)`). Tuples are
  fine as locals.
- **Wrapping calls**: when a call doesn't fit on one line or an argument is a LINQ chain, one argument per line and one
  LINQ call per line:
  ```csharp
  await memberProfileReader.EnrichUsers(
      authData.OrganizationId,
      result.Data
          .Select(x => x.Assignee),
      cancellationToken);
  ```

## Workflow for new features

Build step by step from the data model up (entities/migration, services, wiring, UI/bot text, tests), stopping after
each step for the user's review instead of one big diff.

## Writing tests

- Telegram: `GetTelegramTestHost()` → `host.SendUpdateAsync(new Update { Message = ... })`, then inspect
  `host.Requests().OfType<SendMessageRequest>()` (or `.Single<T>()`). `host.CreateTestScope()`/`CreateScope()` give a
  `DatabaseContext`.
- **Stale change tracker**: reading an entity, mutating it through another scope (e.g. another `SendUpdateAsync`), then
  re-reading on the same context returns cached data. Use `.AsNoTracking()` for reads before a later mutation, e.g.
  `db.Issues.AsNoTracking().ToListAsyncLinqToDB()`.
- Assert on user-visible output (sent text, buttons) over DB shape where both matter.
- Naming: `{Handler}_Should{ExpectedBehavior}_When{Condition}`, e.g.
  `HandleSave_ShouldReplyWithExistingLink_WhenMessageWasAlreadySaved`.
- One `[Fact]` per case, unless the setup is long and the cases are a sequence of actions on it.
- Don't assert on logging (`Mock<ILogger>.Verify`) - assert observable behavior.

## Database safety

- Migrations apply on startup in every host and the test host (no need for `dotnet ef database update` by hand); tests
  use their own database.
- **Never run `dotnet ef database drop`** - `--startup-project` decides the connection string and may be the dev DB.
- Before any destructive DB operation confirm which database it targets, and ask the user if unsure.

## Build-lock protocol

`MSB3026`/`MSB3027` file-lock errors mean a host is running and holding the DLLs. Don't kill it - ask the user to stop it,
then retry.

## Options and configuration

- Bind every options class with `builder.AddValidatedOptions<TOptions>("Section")`
  (`Boards.Services.WebApplicationBuilderExtensions`), not `Configure<T>(GetSection(...))`. It always binds; **only in
  Production** it validates data annotations and `ValidateOnStart()`s, so a missing setting stops the deployed host
  while local development needn't configure everything. Mark required values `[Required]` (`[Url]` for URLs). Checks
  annotations can't express go in the helper's `validation`/`failureMessage` parameters, not a chained `.Validate(...)`
  (that would run outside Production) - e.g. `Telegram:FilesChatId != 0`, or `TelegramNetOptions`, which has no
  annotations. Nested objects aren't validated automatically - see `AppOptions.Validate`.
- Register an options class only in hosts that use it. Core-service options go in `AddCoreServices()`
  (`TelegramOptions`, `FileStorageOptions`, `IdentityOptions`, `BillingOptions`); the AI summarizer is opt-in via
  `AddAiContentSummarizer()` (WebApiHost, TelegramHost). `WebAppOptions` (web app URL for `IIssueUrlBuilder`) is
  registered by TelegramHost and McpHost from their `AppOptions` section, so `AppOptions:Url` is configured once.
- The integration tests' `appsettings.json` must satisfy validation - add every new required setting there.
  `WebApplicationFactory` hosts run as Development (no validation), but `TelegramIntegrationTest` uses a bare
  `WebApplication.CreateBuilder()` (Production). `OptionsValidationTests` covers both.

## Metrics

Prometheus on every host's `/_metrics` (`System.Diagnostics.Metrics`, meter `Laraue.Apps.Boards`; McpHost also has
`McpToolMetrics`). Same conventions as Billing and Identity.

- **Naming**: `boards.<noun>.<verb or state>`, exported as `boards_<noun>_<verb>_total`. New counters go in
  `BoardsMetrics` (`Services/Metrics`) with a typed `Record...` method. `Retro.Services` can't reference it, so its one
  counter is in `RetroMetrics` on the same meter name (keep them equal).
- **Counters are events**, recorded after the save by the host handling the event (each `AddMeter`s it):
  `boards_issues_created_total{source}` (`telegram` with a Telegram message, `mcp` with an API key, else `web`),
  `boards_issues_completed_total` (moved into a `Completed` status), `boards_organizations_created_total{type}`
  (`organization`, or `personal` at sign-up), `boards_retros_started_total`, `boards_issues_deleted_total` (single
  deletes; cascaded ones only show in the gauge). Every counter records zero for each known label value at start
  (`BoardsMetrics`, `RetroMetrics`) so `increase()` sees the first event - do the same for new counters/labels.
- **Gauges are state**, read from the database every 60 s (`BoardsStateMetrics`, WebApiServices, WebApiHost only):
  `boards_issues{state}` (active, completed), `boards_issues_soft_deleted`, `boards_epics{status}`,
  `boards_organizations{type}`, `boards_retros{state}` (running, finished), `boards_active_users{window}` (1d, 7d, 30d:
  distinct users with an `OrganizationLog` row - edits, not reads). Soft-deleted rows aren't counted. Query with `max()`.
- **Telegram**: TelegramHost also exports the `Laraue.Telegram.NET` meter (`LaraueTelegramTelemetry.SourceName`):
  `telegram_requests_started_total{telegram_update_type}`, `telegram_requests_failed_total{telegram_status}`,
  `telegram_request_duration_milliseconds` - the same for every bot host, so one dashboard covers them.
- **Labels are low-cardinality** (source, type, state, status, window) - never a user/organization/issue id.
- **Tests** scrape `/_metrics` (`BoardsMetricsTests`) and assert a series exists, not its count; test hosts disable the
  exporter's scrape cache.

## Logging

Always `ILogger<T>`, never bare `ILogger` - including generic helpers (`SendEphemeralNotice<T>(..., ILogger<T> logger)`).

## EF Core vs LinqToDB

Default to EF Core (`ToListAsync`, `FirstOrDefaultAsync`). Use LinqToDB (`ToListAsyncLinqToDB`, ...) only where EF can't
translate the query or translates it badly.

## Per-organization names

A person is shown per organization (BRD-220): `OrganizationUser.DisplayName`/`Initials`/`Color`, copied from their
Identity profile on joining (Identity is the source of truth; `User` has no name), then editable by the member. The row
stays after they leave (`LeftAt`, permissions cleared) so their name keeps showing; rejoining revives it. Use
`ActiveOrganizationUsers()`, never raw `OrganizationUsers`, for membership/permission checks.

Don't join the membership table to get a name. Project the user id onto a DTO implementing `IEnrichableUser`
(`Boards.Services.Members`: `UserId` + settable `DisplayName`/`Initials`/`Color`; map other property names with an
explicit implementation) and fill the page in one query with `IMemberProfileReader.EnrichUsers`:

```csharp
var issue = new IssueDetailDto { Owner = new UserDetails { UserId = ownerId }, ... };
await memberProfileReader.EnrichUsers(
    organizationId,
    [issue.Owner],
    ct);
```

`UserDetails` is the general implementation: for a person in a response, a person property on a row
(`IssueListDtoData.Assignee = new UserDetails { UserId = x.AssigneeId }`), or with no row (a name in history, an MCP
record). Implement the interface on another DTO only when it must keep its own shape (`RetroUser`,
`AdminBillingTransaction`). Use the organization the shown thing belongs to; cross-organization reads (Telegram search)
pass organization/user selectors. Queries already over `OrganizationUser` read its fields directly. A user with no row
gets `MemberProfile.Unknown`.

## Soft delete

`Organization`, `Space`, `Epic`, `Status`, `Issue`, `IssueComment` and `User` have nullable `DeletedAt`/
`DeletedByUserId`; deleting sets them and cascades the flag down that list (a space soft-deletes its epics, statuses,
issues). `User` isn't in that cascade: it's soft-deleted only when account linking moves its last sign-in method away
(BRD-218), leaving its personal organization unreachable. `OrganizationUser` uses `LeftAt` instead. Everything else is
hard-deleted.

There's deliberately **no global query filter** - every query chooses:

- Normal reads: `context.ActiveIssues()`/`ActiveSpaces()`/`ActiveEpics()`/`ActiveStatuses()`/`ActiveOrganizations()`/
  `ActiveIssueComments()`/`ActiveUsers()` (`DatabaseContextActiveEntityExtensions`). Older `context.Users` queries
  weren't switched (a merged user has no sign-in id or memberships to be found by); use `ActiveUsers()` in new user
  queries where a deleted user could appear (`UserService.GetUser` does). A merged user's tokens are revoked anyway
  (see "Session tokens").
- Audit/history reads (e.g. `OrganizationHistoryService`): the raw DbSet.
- `IAccessService.GetAccessLevelsBy{Space,Epic,Issue}Id`/`GetAvailableSpaces` take an explicit `includeDeleted` (no
  default): `true` only from audit/history callers.

Why: it was never verified that LinqToDB's bridge honors `HasQueryFilter`, while an explicit `Where(x => x.DeletedAt ==
null)` works under both providers.

## No unpaginated list endpoints

Any collection sized by user data is paginated, never a bare `.ToListAsync()`: the request implements
`IPaginatedRequest` (`Pagination`), the endpoint is `[HttpPost("search")]` with the request as body, and the query ends
in `.ShortPaginateEFAsync(request.Pagination, ct)` (or `ShortPaginateLinq2DbAsync`) returning `ShortPaginatedResult<T>`
(`Laraue.Core.DataAccess.Contracts`) - see `IssuesController.Search`, `BillingController.GetTransactions`,
`EpicsController.SearchEpicsWithStatuses`, `ApiKeysController.GetAll`.

## Query shape: project, don't load-then-map

- Don't `Include` a graph to read a few fields: `.Select(...)` the needed columns (no `AsSplitQuery()` needed - that's
  for `Include` graphs).
- No `AsNoTracking()` on queries projecting to a non-entity type - it's a no-op there.
- Project straight into the response DTO, not an anonymous type mapped afterwards.
- For a response combining several collections of one aggregate (a retro's sections, cards, participants), run one
  focused query per collection and assemble, rather than nested `Select`s off the root. Scalars from an earlier query
  (e.g. the retro's `Phase`) can be used in a later `Select` - EF parameterizes them. See
  `RetrosService.Get(long, OrganizationAuthData, CancellationToken)`.
- Core services checking a scalar (existence, a flag, a count) query just that scalar - e.g.
  `CoreRetrosService.SetVote` selects `Phase`/`VoteEndsAt`/`VotesPerUser` and looks up the caller's vote directly.

## AI content summarization

`Services.Ai` (core): `IAiContentSummarizer`/`OpenAiCompatibleContentSummarizer` calls any OpenAI-compatible
`POST {BaseUrl}chat/completions` to rewrite notes into `title\n---\ncontent` markdown without inventing content (see its
system prompt for the "beautify, don't invent" and title rules).

- **Config, not code, picks the provider**: `AiSummarizerOptions` (`ApiKey`, `BaseUrl`, `Model`, `Thinking`) has no code
  defaults; base `appsettings.json` points at local Ollama (`http://localhost:11434/v1/`), production overrides it (e.g.
  DeepSeek). `Thinking` defaults to `false` (`"thinking": {"type": "disabled"}`) - reasoning isn't needed and is slow.
- **Failures** only ever throw `AiContentSummarizationException`; each host catches it, logs a warning and translates
  it: `IssuesService.SummarizeContent` throws `AiSummarizationUnavailableException` (a `HttpException` subclass → 503;
  its constructor is protected but subclassable), `SaveCommandService` sends `Phrases.AiSummarizationUnavailable`.
- **Tests** override `IAiContentSummarizer` with a `Mock` in `WebApiTestHost`/`TelegramIntegrationTest`
  (last-registered-wins) and configure it per case.
- `/save` and `/aisave` are one command with a `Summarize` flag (`SaveCommandService` →
  `SaveByReplyRequest.Summarize`), routed from one `SaveController`. Don't give a "same flow plus one step" variant its
  own command service (tried and reverted).

## Session tokens

`AuthService` mints user and organization JWTs with **no expiry** (`ValidateLifetime = false`, 365-day cookie), so
sessions end only by revocation (BRD-222): each token has a `tv` claim (`AuthService.TokenVersionClaim`) = the user's
`User.TokenVersion` at issue, checked by every JWT scheme of WebApiHost and Retro.WebApiHost in `OnTokenValidated`
(`TokenVersionValidation` → `ITokenVersionService`); a lower version or missing user is 401. No claim counts as 0.

- Revoke all of a user's sessions by incrementing `TokenVersion` - today only account linking does
  (`CoreUserService.SoftDeleteMergedUserAsync`); future uses: sign out everywhere, leaving an organization, account
  deletion.
- Token minting takes the user's current version (`IAuthService` stays a pure singleton); a stale one is rejected.
- Versions are cached in `ITokenVersionCache` (`MemoryTokenVersionCache`, 30 s per host). The bumping host removes the
  entry after commit (`ConnectedAccountsService`); other hosts catch up within 30 s. A shared cache (Redis) would make it
  immediate.
- API keys (McpHost) are validated live against the database and unaffected.

## External services (Identity, Billing, Feedback) and local mocking

Boards calls sibling services over gRPC:

- **`Laraue.Apps.Identity`** - the global identity. `CoreUserService` calls `UserIdentityService.UserIdentityServiceClient`
  directly (no wrapper: one call, one caller).
  - **Identity owns the profile** (Telegram username/names/language, Google email/name). `User` stores only identifiers
    (`GlobalUserId`, `TelegramId` - null for Google-only users - and `GoogleSubject`). Display names live on
    `OrganizationUser`, copied from `GetUserProfile` on joining (see "Per-organization names"). No interface language is
    stored (BRD-239): the web app keeps its own, the bot uses `DefaultCultureInfoProvider` (the sender's Telegram app
    language, or English); the sign-in language only names the personal organization. Don't re-add profile columns to
    `users` - ask whether a new value is a Boards setting or belongs in Identity.
  - `User` implements no Telegram.NET interface: `ITelegramUserQueryService<Guid>` (Telegram.NET 5.0) only finds a user
    by Telegram id; `TelegramUserQueryService` maps `TelegramData` to `TelegramUserProfile` for
    `ICoreUserService.ResolveTelegramIdentity`.
  - Sign-up is two core steps so gRPC never runs inside a DB transaction: `Resolve{Telegram,Google}Identity` (Identity
    only), then `CreateIf{TelegramId,GoogleSubject}NotExists` (Boards writes, asserts `EnsureTransactionStarted()`),
    in the host's transaction.
  - **Editing the global profile** (BRD-238, `GET`/`PUT /api/user/profile`, `UserService`): a pass-through to
    `GetUserProfile`/`UpdateUserProfile`, no Boards writes. Validated in Boards (data annotations, Identity's column
    lengths) first; any Identity failure is a 503 (`ProfileServiceUnavailableException`). Initials aren't sent -
    Identity derives them. Existing organizations keep the name copied on joining.
  - **Connecting the other sign-in method** (BRD-218, `POST /api/user/connected-accounts/telegram|google`,
    `ConnectedAccountsController` → `ConnectedAccountsService` → `ICoreUserService.Link…AccountInIdentity`/
    `Link…AccountInBoards`): refusals are a 200 with an `AccountLinkOutcome` so the frontend can word each. If another
    Boards user owns the account and has no data (`CoreUserService.HasDataAsync`) it's moved; with data, Identity isn't
    called (its contract requires that check). `Link…InIdentity` runs outside a transaction; only on `Linked` does
    `Link…InBoards` run in `ConnectedAccountsService`'s transaction. It locks the user and the current owner
    (`DatabaseExtensions.LockUsers`, `SELECT ... FOR UPDATE` in id order) so concurrent connects serialize, re-finds the
    previous owner and skips an existing personal chat, so a retry after a failure between the steps completes
    (Identity's link is idempotent). If the moved account was the owner's last sign-in method, that user is
    soft-deleted (`DeletedByUserId` = the taker) and their `TokenVersion` bumped; their personal organization stays.
    The merge target is recorded only in Identity (`merged_into`). Telegram data is verified by
    `TelegramAuthService.ConnectTelegram` like login. Known gaps (token replay, the no-data check racing the move):
    BRD-231.
  - **Google sign-in** (`POST /api/user/auth-via-google`, `GoogleAuthService`): Boards verifies the ID token itself
    (`GoogleIdTokenValidator`, Google.Apis.Auth) against `GoogleAuth:ClientId` and sends only verified claims to
    Identity (`CreateUserIfNotExistsByGoogle`). The validator throws on an empty `ClientId` on purpose - without an
    audience Google.Apis.Auth accepts tokens for any client. The frontend uses Google's popup (code client) and sends a
    `Code`, exchanged via `GoogleIdTokenValidator.ExchangeCodeAsync` with `GoogleAuth:ClientSecret` (redirect URI
    `postmessage`); `/api/user/auth-via-google` and `/api/user/connected-accounts/google` both accept `Code` or
    `IdToken`. Google and Telegram sign-ins are separate users unless connected; a Google-only user has no `TelegramId`
    and no personal Telegram chat.
- **`Laraue.Apps.Billing`** - AI token reserve/commit/cancel and subscription/limits, behind `IBillingTokenClient`/
  `IBillingSubscriptionClient` (`Services.Billing`), which add Boards' personal-vs-team organization resolution (see
  `IBillingTokenClient`'s XML doc).
- **`Laraue.Apps.Feedback`** (BRD-224) - stores web feedback and notifies the team in Telegram. WebApiHost only:
  `AddFeedbackClient()` registers `FeedbackService.FeedbackServiceClient` and `FeedbackOptions`
  (`FakeFeedbackServiceClient` when mocking). `POST /api/feedback` (`UserFeedbackService`) accepts the user or
  organization token, sends the global user id and sets the `organization` context key to the current organization
  (overriding the frontend's). The optional contact goes in the typed `contact` field (trimmed, blank = unset). Limits
  are Feedback's (INVALID_ARGUMENT → 400 via Laraue.Grpc.Client); other gRPC failures are 503.

**Running locally without them**: `AddCoreServices()` always registers the real clients (channels open lazily). With
`"MockExternalServices": true` (both hosts' `appsettings.Development.json`, `false` elsewhere) one
`if (mockExternalServices) { ... }` block registers `FakeUserIdentityServiceClient` (subclasses the generated client,
mints a fresh `Guid`), `FakeBillingTokenClient`/`FakeBillingSubscriptionClient` (generous balance, unlimited plan)
**after** them - last-registered-wins, no `if`/`else` duplication. These fakes are for running hosts only; integration
tests use their own `Mock<...>` overrides in `WebApiTestHost`/`TelegramIntegrationTest`.

## API keys and MCP access

Lets a program (e.g. Claude via a remote MCP connector) act as an organization member without a browser session.

- **`ApiKey`** (`DataAccess.Models.ApiKey`, `ICoreApiKeysService`) - a long-lived credential for one organization and
  one member (`CreatedByUserId`). Never snapshotted: every use checks that member's access live through
  `IAccessService`, so losing access revokes the key's access too. Self-service: members create/list/revoke only their
  own keys via `/api/api-keys` (Organization JWT); no admin view of others' keys. Listing (`ApiKeysService`, a plain
  read) shows active keys unless `IncludeRevoked` is set.
- **`AuthSchemas.ApiKey` + `ApiKeyAuthenticationHandler`** (`Services.Auth`, registered only by McpHost) - reads
  `X-Api-Key`, calls `ValidateAsync`, and builds a principal with the same `orgId`/`id` claims as the JWTs (plus
  `apiKeyId`), so `GetOrganizationAuthData()` and all checks work unchanged.
- **`Actor`** (`Common`, `readonly record struct Actor(Guid UserId, Guid? ApiKeyId = null)`) - who made a change, for
  history. `ICoreIssuesService`'s history-writing methods (`Create`/`Update`/`Delete`/`AddComment`/`UpdateComment`/
  `DeleteComment`/`UpdateIssuesStatus`) and `IIssueHistoryService.Record`/`RecordIfChanged` take it. The implicit
  `Guid → Actor` conversion (the codebase's only implicit operator, deliberate) kept web/Telegram call sites unchanged;
  McpHost builds one via `OrganizationAuthData.ToActor()`. Persisted as `OrganizationLog.ApiKeyId` (nullable,
  `SetNull`), read back as `OrganizationHistoryItem.ApiKeyName`. `UpdateIssuesStatus` writes `OrganizationLog` rows
  inline (many issues per call) - a new history-writing method may do the same, so don't assume everything goes
  through `IIssueHistoryService.Record`.

**`Boards.McpHost`** (`ModelContextProtocol.AspNetCore`):

- Tool types (`Tools/IssueTools.cs`, `[McpServerToolType]`) are thin adapters: resolve `OrganizationAuthData` from
  `IHttpContextAccessor`, call a service, return. All logic is in `Services/IssueMcpService.cs` (`IIssueMcpService`),
  on the same `IAccessService`/`ICoreIssuesService` path as REST.
- `McpServerOptions.ServerInstructions` (`McpServerInstructions.cs`) goes to every client - keep it a short overview;
  per-tool details belong in `[Description]`.
- `server.json` (MCP registry manifest, published with `mcp-publisher`) is the single source of name/title/description/
  version/auth header: embedded as `ServerManifest` and served by `GET /mcp/server-card` (URL from
  `ServerCard:PublicMcpUrl`). Bump the version there - major for breaking tool changes (2.0.0 renames, 3.0.0 comment
  split).
- Tests call `IssueMcpService` directly against the test DB (`IssueMcpServiceTests.cs`); `IssueTools` is untested like
  a thin controller, except `get_attachment`, which builds the content block itself (`ImageContentBlock.FromBytes` -
  `Data` is base64-encoded bytes; raw bytes send broken base64, BRD-229). To see exactly what a client receives (errors,
  metrics) use a real client: `McpHostTestHost.ConnectMcpClientAsync` with an API key from the test DB
  (`McpToolErrorTests`, `McpMetricsTests`).

**Tools**: `list_issues`, `get_issue`, `list_issue_comments`, `get_issue_history`, `edit_issue_status`, `create_issue`,
`edit_issue`, `delete_issue`, `create_comment`, `edit_comment`, `delete_comment`, `create_issue_link`,
`delete_issue_link`, `get_attachment`; discovery: `get_me`, `get_space`, `list_spaces`, `list_epics`, `list_statuses`,
`list_attributes`, `list_members`; agent runs: `create_agent_run`, `list_agent_runs`, `claim_agent_run`,
`start_agent_run`, `finish_agent_run`, `cancel_agent_run`. Reads need `CanRead` (except `get_attachment`); mutations use REST's checks
(`CanCreateIssue` from the target status's epic, `CanUpdateIssue` for edits/comments, `CanDeleteIssue`, author-only for
editing/deleting a comment). Guardrails:

- **Issue links are their own tools**: `edit_issue` needs the whole content and title, so linking through it risks
  overwriting. `get_issue` returns `links` (relation from the issue's side, key, title, status category) via
  `IssueLinksReader`; `list_issues` takes `excludeBlocked` rather than returning a flag, so "ready to start" pages are
  full. An unreadable issue is a 404 on either end and left out of `links`.
- **No attachment tool**: attach via `create_issue`/`edit_issue`/`create_comment`/`edit_comment`'s `files`, remove via
  `edit_issue`/`edit_comment`'s `removeAttachmentIds`.
- **No `spaceKey` on `create_issue`**: permission comes from `statusId`'s epic (`GetAccessLevelsByEpicId`).
- **Ids, not names**, for whatever can't be typed unambiguously: `statusId`, attribute and list-value ids from
  `list_attributes`, `assigneeId` and the assignee filter. `list_statuses`/`list_attributes`/`list_members` exist to
  discover them.
- **Capability flags**: `list_issues`/`get_issue` return `canEdit`/`canDelete`, `list_spaces` `canCreateIssue`,
  `list_issue_comments` `canManage`, so a caller knows upfront. Permissions are space-scoped, so `ListIssues` computes
  flags once per space on the page (`IAccessService.GetSpacesWithAllowedIssuesUpdate`/`...Delete`/
  `GetSpacesWithAllowedIssueCreation`, batched); `GetIssue` reuses its `CanRead` lookup; `CanManage` is
  `OwnerId == authData.UserId` (one flag: edit and delete share the rule).
- **Names**: `edit_issue_status`/`create_comment` (renamed from `update_issue_status`/`add_comment` for consistency - a
  deliberate breaking change).
- **`assigneeId`** on `create_issue`/`edit_issue` is checked to belong to the organization
  (`IssueMcpService.EnsureUserBelongsToOrganization`) - no UI stops an arbitrary Guid.
- **`edit_issue_status` and `POST /api/issues/status` take an optional `comment`**, added to every issue actually moved
  (`ICoreIssuesService.UpdateIssuesStatus` → `AddComment`, same transaction).
- **Comments aren't in `get_issue`** - only `commentCount`; `list_issue_comments` pages them oldest first with their
  attachments (breaking split, BRD-230).
- **`get_me` returns no permissions** - just user (id, display name) and organization (id, name, personal);
  organization-wide flags would miss space grants and mislead.
- **Issues carry their web `url`**, built by `IIssueUrlBuilder` (`Boards.Services`, shared with Telegram previews).
- **`get_issue_history`** uses `OrganizationHistoryReader` and flattens changes into lines (`status: To Do -> Done`,
  `attachment added: x.png`; content cut to 200 chars). Existing issues only.

**Agent sign-in** (BRD-309, `AgentSignInRequest`, `ICoreAgentSignInService`, `AgentSignInService`/
`AgentSignInController`, `/api/agent-sign-in`) - how `boards-agent setup` gets an API key without copying one:

- The runner starts a sign-in (anonymous) and polls `POST /api/agent-sign-in/token` with its secret device code (stored
  only as a SHA-256 via `SecretHash`, like `ApiKey.KeyHash`). The person opens the web app's Allow page
  (`AgentSignInOptions.AgentLoginPagePath`) by the short user code, picks an organization and allows or denies
  (`AuthSchemas.User`, since the organization is chosen there; non-member → 404).
- The first poll after Allow creates the key `<clientName> (<machine>)` (e.g. `Claude Code agent (dev-laptop)`) and
  returns it once; the raw key is never stored. The client sends its own name (`clientName`) - nothing is tied to one
  agent. One key = one organization.
- Signing in again revokes nothing by name: the runner sends the key it held (`replacesApiKey`, proof of possession),
  and only that key is revoked, only if it's the approving user's key in the same organization.
- Refusals (pending, slow down, denied, expired) are a 200 with `AgentSignInPollOutcome`, like `AccountLinkOutcome`.
- Shaped like the OAuth device grant, but not OAuth: a general sign-in server belongs in Laraue.Apps.Identity; Boards
  only issues its organization-scoped keys.

**How an issue was created and agent authors** (BRD-325): `Issue.CreatedVia` (`Web | Telegram | Mcp | Agent`) and
`Issue.CreatedByApiKeyId` are stored on create by `CoreIssuesService` (a Telegram message wins; a key issued by agent
sign-in - one with an `AgentSignInRequest` - is Agent, any other key Mcp) and backfilled by the `AddIssueCreatedVia`
migration from the issue's create entry in history, so lists and boards return `createdVia`/`createdByApiKeyName` without
reading history. Comments and history (`OrganizationHistoryReader`, `GetCommentApiKeys`) return `AuthorKind` (`User |
Agent`) and the agent's client/machine (`AgentDetails`) next to `ApiKeyName`: actions made with an agent sign-in key read as
the agent's, on behalf of the key's owner; keys made by hand stay `User`.

**Agent runs** (BRD-305, `IssueAgentRun`, `ICoreAgentRunsService`, `AgentRunMcpService`/`AgentRunTools`, REST
`AgentRunsController`) - handing an issue to a coding agent on the user's machine:

- One row per hand-off: Ready → Working → Done/Failed, or Cancelled by a person; a retry is a new row. A partial unique
  index (`Constraints.OpenAgentRunIndexName`, `state IN (0, 1)`) allows one open run per issue; a second hand-off is
  detected by the violation (400).
- Every transition is one conditional `UPDATE ... WHERE State = expected` (`ExecuteUpdateAsync`), returning false to
  the loser - two claims get exactly one `true`, no row lock. The runner moves the issue itself after claiming.
- Each transition is an `OrganizationLog` item (`PropertyType.AgentRun`, run id in `ParentId`, states in the value
  ids), so history shows the runner's API key. Actions need `CanUpdateIssue` (reads `CanRead`). `get_issue` and the
  web card/detail return the latest run.
- A run belongs to the assignee: only they hand off, and only their key can claim (`AgentRunMcpService.EnsureIsAssignee`,
  `AgentRunsService.Create`; 403 otherwise).
- While a run is open the issue is locked: `ICoreIssuesService.Update`/`Delete` throw `IssueLockedByAgentRunException`
  (409) before writing (Telegram's edit sync calls them without a transaction), so every surface refuses field edits
  and deletion, and the assignee stays fixed. Status changes, comments and cancel stay open to editors; cancelling
  unlocks.
- A hand-off needs the issue in a Created status and its epic to have Created, InProgress and Review statuses
  (`AgentRunBoardRequirements`), else 400. The web hand-off (`POST /api/agent-runs`) also needs an active API key in the
  organization (400 otherwise); MCP callers always have one. `IAgentRunsService.GetHandOff` decides once - available,
  or `NotAllowed`, `RunOpen`, `NoApiKey`, `BoardNotReady` (with the missing categories), `IssueStarted` - returned on
  the issue detail as `AgentHandOff` and re-checked by the endpoint. Since the issue can still move, the claim
  re-checks the board and fails the run if it no longer qualifies.
- Started runs (BRD-320, `start_agent_run`, `ICoreAgentRunsService.Start`): `lba continue` - the person working with
  the agent directly - opens a run already Working, claimed by the caller's key, in one step. No status or board
  requirements and no status change (nothing is handed off), but otherwise a claim's rules: assignee only, one open run
  (400), the issue locked, a `none -> Working` history item with the API key. It ends like any run.
- Send back (BRD-315, `POST /api/agent-runs/send-back`, `IAgentRunsService.SendBack`): for an issue the agent already
  worked on, one transaction posts the assignee's comment (`ICoreIssuesService.AddComment`), moves the issue to its
  board's first Created status and creates a new run. `AgentHandOff.CanSendBack` says when it's offered - the same checks
  as a hand-off except the status, which send back changes itself.
- Issue links (BRD-308): a run is `isBlocked` while a readable issue that blocks it isn't Completed (same rule as
  `excludeBlocked` and the web "blocked" mark). Blocked issues can be handed off; `list_agent_runs(excludeBlocked: true)`
  hides the run and `claim_agent_run` returns false (checked in `AgentRunMcpService` via `IIssueLinksReader`, since
  "readable" is a permission question), so the run becomes claimable once blockers complete.

**Argument validation**: free-text arguments (title, content, comment text, `edit_issue_status`'s comment) carry
`[MaxLength(Constraints.*)]` and state the limit in the description; the SDK emits `maxLength` and
`McpArgumentValidator` (run by `McpToolCallFilter`) rejects longer values as a readable `BadRequest`. The attribute is
the single source - don't re-check in `IssueMcpService`; add it to every new text argument. Fixed value sets are enum
parameters (the schema lists them, input is case-insensitive, an unknown value gets
`- state: Must be one of: Done, Failed.`); a tool accepting only some values gets its own enum (`AgentRunFinishState`).

**Tool errors** go through `McpToolCallFilter` (registered in `Program.cs`), McpHost's counterpart to
`ExceptionHandleMiddleware` - a tool's exception never leaves the MCP request, and the SDK would turn it into a bare
`An error occurred invoking '<tool>'.` logged as unhandled. The filter maps `HttpException`s (`NotFoundException`,
`ForbiddenException`, `BadRequestException` with field errors, ...) to `CallToolResult { IsError = true }` with
`"{StatusCode}: {message}"` plus `- field: error` lines, logged at Information (Warning for 5xx). So throw the same
`HttpException`s as REST.

**Metrics**: every MCP call is `POST /mcp` → 200, so HTTP metrics say little. McpHost exports the SDK's meter
(`Experimental.ModelContextProtocol`: `mcp_server_operation_duration_seconds` per method, `error_type="tool_error"` on
failed calls; `mcp_server_session_duration_seconds`) and `boards_mcp_tool_duration_seconds{tool, status}`
(`McpToolMetrics`, from `McpToolCallFilter`; `status` = `ok`, the expected error's status code, or `unhandled`). The
SDK meter name is experimental - check dashboards when bumping `ModelContextProtocol`. Meters are process-wide, so
`McpMetricsTests` asserts a series exists, not a count.

**Attributes** (`Attribute`/`AttributeListValue`, organization-wide): MCP sends plain text, which
`IssueMcpService.ParseAttributeValue` turns into REST's `AttributeRequests.AttributeValue`; both paths then share
`ICoreIssueAttributesService.BuildSetRequests` (batched validation, all errors at once). Omitted/`null` `attributes`
leaves them untouched, `{}` clears all - check `attributes is not null`, not `.Count > 0`.

**Attachment types** (BRD-259, `AttachmentFileTypes`, `Boards.Services`): the one list of what attachments accept, for
REST and MCP. The type comes from the file's content (magic bytes; docx = a zip with `word/document.xml` and no VBA
project), never the declared mime type; md/txt/json have no signature, so the extension decides provided the content is
UTF-8 with no NUL bytes, and a .doc needs both the OLE signature and the extension. Images (JPEG, PNG, WebP, GIF) are
limited to 3 MB, documents (PDF, DOCX, DOC, Markdown, TXT, JSON) to 10 MB - under the Bot API's 20 MB download limit,
since Telegram is the storage. SVG, HEIC/AVIF and anything else are rejected. JPEG/PNG go through `sendPhoto` (Telegram
makes the thumbnail); the rest through `sendDocument` with content-type detection off (GIF/WebP are their own preview,
documents have none: `AttachmentType.Document`). Telegram documents (BRD-319) aren't re-uploaded: `TelegramSaveMessageService`
downloads the file to `Detect` it, then keeps it by its own file id. It downloads only when nothing cheaper decides: a
file unique id already stored or skipped keeps its verdict, and `FileSize` or a name and declared type that are both
unsupported (`AttachmentFileTypes.MayBeSupported`) skip it unread. A skipped one (type or size) is recorded on
`TelegramMessage.SkippedFile{Name,Reason,UniqueId}` so `/save` can report it later, and in `EachMessage` mode is reported only
when it was the message's only content (no card is made). `GET /api/files/{id}` sends `nosniff` and a sandbox CSP for every file
and serves anything but images/videos as `Content-Disposition: attachment` with the sanitized upload name.

**File attachments**: no multipart in MCP, so `files` is `FileAttachment(FileName, ContentType, Base64Content)[]`,
decoded by `IssueMcpService.UploadFiles` into REST's `ICoreFilesService.UploadFile` (same types and limits).
`get_attachment` returns an `ImageContentBlock` for images, a `TextContentBlock` for md/txt/json and an
`EmbeddedResourceBlock` (base64 blob) for pdf/doc/docx, with `AttachmentFileTypes.MaxFileSizeBytes` enforced twice: the
recorded size before streaming, and a hard cap while buffering (the real OOM guard if `File.Size` is wrong). It takes
any attachment id (issue or comment) and checks **no permission**, like REST's unauthenticated `GET /api/files/{id}`:
ids are unguessable UUIDv7s learned only from readable data. An id stays usable after access is lost.

**Space description and links** (BRD-312, `Space.Description`, `SpaceLink`, `ICoreSpaceLinksService`,
`SpacesService.GetAbout`): a space's markdown overview and its links to things outside Boards (repositories, docs, designs),
each `Url` + `Description`. A URL is listed once per space (unique index, violation → 400); the same URL can be in other
spaces with its own description. At most `Constraints.MaxSpaceLinksPerSpace`, so they're returned in one list. Read with
`GET /api/spaces/{key}/about` (`CanRead`, else 404) and MCP `get_space`; saved with the space's other settings in
`PUT /api/spaces/{key}` (`description`, and `links` as the whole list, which replaces the saved one; null leaves either
as is) in one transaction, through `ICoreSpaceLinksService.Replace`. Links are rewritten, so their ids aren't stable -
nothing refers to them; a URL the space already had keeps its `CreatedAt`/`CreatedByUserId`. No link kind: the agent runner recognizes repositories by matching URLs against local git remotes.
Descriptions are context for agents, never instructions.

**`list_epics`** (BRD-234) gives usable epic ids (`list_issues`' `epicId`, `list_statuses`' groups). With `spaceKey`:
that space (404 if missing or unreadable); without: every readable space via `IAccessService.GetAvailableEpics`.
Paginated, since epics accumulate. **`list_members`/`list_spaces`** wrap `GetVisibleUsers`/`GetAvailableSpaces` (like
`OrganizationsController.GetMembers`); `list_members`' `spaceKey` narrows to members who can see that space. Not
paginated - naturally small.

## User-facing text

- No string literals in `throw new SomeException("...")` or ephemeral notices. Each surface has its **own**
  `Resources/ErrorMessages.resx` (`WebApiServices`, `McpHost`) or `Phrases.resx`/`Phrases.ru.resx` (`TelegramServices`)
  - no cross-project resx. Reuse an existing template shape verbatim (copy key and value) when it fits:
  `EntityNotFound = "{0}: {1} is not found"`, `EntityNotFoundOrNotAccessible = "{0}: {1} is not found or not
  accessible"`, `EntityActionForbidden = "{0}: {1} {2} is forbidden"`. Add a key only for a genuinely different shape
  (e.g. McpHost's `StatusNotFoundInIssueEpic`).
- **`.Designer.cs` isn't regenerated by `dotnet build`** here (a Visual Studio generator). After adding a `<data>`
  entry, hand-add `internal static string Foo { get { return ResourceManager.GetString("Foo", resourceCulture); } }`
  to the `.Designer.cs`. A new resx also needs its `.csproj` `ItemGroup`s (`EmbeddedResource` with `Generator`/
  `LastGenOutput`, `Compile` with `DesignTime`/`AutoGen`/`DependentUpon`) - copy them from `WebApiServices.csproj`.

## Task flow

- Branch per task: `feature/<task-number>-<description>`, e.g. `feature/BRD-120-add-assignee-api`.
