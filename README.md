# Laraue.Apps.Boards
The repository contains backend for Task-management Jira-like system.

## Interface example
<img width="1339" height="1102" alt="image" src="https://github.com/user-attachments/assets/815691a0-fac8-4e40-8bb7-4efa4f28d0bd" />

## App structure

### Laraue.Apps.Boards.DataAccess
The layer that contains models and enums associated with these models

### Laraue.Apps.Boards.Services
The layer with services that use other services. Required to encapsulate hard logic mostly in CRD operations.  
Example: issue creation may update `updated_at` property, add record to history changes etc.  
So the service provides the method to create issue.

**Note:** core services should not manage transactions, but may require them calling `context.Database.EnsureTransaction`
at the top of function.

### Laraue.Apps.Boards.WebApiServices
Services to call from WebApi

### Laraue.Apps.Boards.TelegramServices
Services to call from TelegramApi

## Local run
Check how to deal with the frontend in [Frontend Repository](https://github.com/win7user10/laraue-note-to-board)

Each host reads its own `appsettings.Development.json` (gitignored, not the checked-in
`appsettings.json`) for local overrides. A minimal one for `Laraue.Apps.Boards.WebApiHost` that
runs against a local Postgres with no Identity/Billing dependencies (AI summarization still
defaults to a local Ollama instance - see below):
```json
{
  "Auth": {
    "Key": "any-local-signing-key"
  },
  "MockExternalServices": true
}
```
`Laraue.Apps.Boards.TelegramHost`'s only needs the `MockExternalServices` line (it has its own
`Telegram:Token` to set instead of `Auth:Key`). See the subsections below for what each setting
does and how to point at a real AI/Identity/Billing instance instead.

### AI content summarization (local dev)
`appsettings.json`'s `AiSummarizer` section defaults to a local [Ollama](https://ollama.com/)
instance, since Ollama exposes an OpenAI-compatible `/v1/chat/completions` endpoint and the
summarizer talks to any OpenAI-compatible API. Install Ollama, then pull the model referenced
in config:
```
ollama pull gemma3:12b
```
Ollama serves on `http://localhost:11434` by default once installed. On prod, override
`AiSummarizer:BaseUrl`/`AiSummarizer:Model`/`AiSummarizer:ApiKey` to point at a real provider
(e.g. DeepSeek) instead.

### Running without Identity/Billing

Boards calls out to two other services over gRPC: `Laraue.Apps.Identity` (global user identity on
first login) and `Laraue.Apps.Billing` (AI token spend/subscription limits). Neither has to be
running locally - both hosts' `appsettings.Development.json` set `"MockExternalServices": true`,
which swaps in in-process fakes for these calls (always succeeds, generous fixed limits, no
network traffic) instead of the real gRPC clients. Set it back to `false` if you actually want to
exercise a locally-running Identity/Billing instance.

### Create a new user for Test
`POST: http://localhost:5200/api/test/user`
```json
{
    "username": "winDiezel",
    "languageCode": "ru",
    "firstName": null,
    "lastName": null
}
```

### Auth as user on Test
Make a request GET `http://localhost:5200/api/test/user/{userId}` to receive a bearer token.  
Set it in frontend `.env` file:
```
NUXT_PUBLIC_TEST_USER_TOKEN=Taken_Token
```

## Permissions arch
Application permissions are divided into two parts:
1. Entity permissions. Allows to set up access to organization spaces, epics, issues
    1. Organization level. Make set for all entities in one time.
    2. Flexible setup for each space separately. Can be combined with organization level.
2. Administrative permissions. They allow to set up access for administrative actions like renaming organization.
