using Laraue.Apps.Boards.Common;
using Laraue.Apps.Retro.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Retro.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/retro")]
public class RetroController(IRetrosService retrosService) : ControllerBase
{
    [HttpGet]
    public Task<RetroListItem[]> Get(CancellationToken cancellationToken = default) =>
        retrosService.Get(AuthData(), cancellationToken);

    [HttpGet("{id:long}")]
    public Task<GetRetroResponse> Get(
        [FromRoute] long id,
        CancellationToken cancellationToken = default) =>
        retrosService.Get(id, AuthData(), cancellationToken);

    [HttpPost]
    public Task<CreateRetroResponse> Create(
        [FromBody] CreateRetroRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.Create(request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/finish")]
    public Task Finish(
        [FromRoute] long id,
        CancellationToken cancellationToken = default) =>
        retrosService.Finish(id, AuthData(), cancellationToken);

    [HttpPost("{id:long}/settings")]
    public Task UpdateSettings(
        [FromRoute] long id,
        [FromBody] UpdateRetroSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.UpdateSettings(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/phase/next")]
    public Task AdvancePhase(
        [FromRoute] long id,
        [FromBody] SetRetroPhaseRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.AdvancePhase(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/phase/back")]
    public Task RevertPhase(
        [FromRoute] long id,
        [FromBody] SetRetroPhaseRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.RevertPhase(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/timer")]
    public Task SetPhaseTimer(
        [FromRoute] long id,
        [FromBody] SetRetroTimerRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetPhaseTimer(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/discussed-card")]
    public Task SetDiscussedCard(
        [FromRoute] long id,
        [FromBody] SetRetroDiscussedCardRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetDiscussedCard(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/reveal-mine")]
    public Task SetMyCardsRevealed(
        [FromRoute] long id,
        [FromBody] SetRetroRevealRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetMyCardsRevealed(id, request, AuthData(), cancellationToken);

    [HttpPost("{id:long}/cards")]
    public Task<CreateRetroCardResponse> CreateCard(
        [FromRoute] long id,
        [FromBody] CreateRetroCardRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.CreateCard(id, request, AuthData(), cancellationToken);

    [HttpPut("cards/{cardId:guid}")]
    public Task UpdateCard(
        [FromRoute] Guid cardId,
        [FromBody] UpdateRetroCardRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.UpdateCard(cardId, request, AuthData(), cancellationToken);

    [HttpPut("cards/{cardId:guid}/position")]
    public Task MoveCard(
        [FromRoute] Guid cardId,
        [FromBody] MoveRetroCardRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.MoveCard(cardId, request, AuthData(), cancellationToken);

    [HttpDelete("cards/{cardId:guid}")]
    public Task DeleteCard(
        [FromRoute] Guid cardId,
        CancellationToken cancellationToken = default) =>
        retrosService.DeleteCard(cardId, AuthData(), cancellationToken);

    [HttpPost("cards/{cardId:guid}/vote")]
    public Task SetCardVote(
        [FromRoute] Guid cardId,
        [FromBody] SetRetroCardVoteRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetCardVote(cardId, request, AuthData(), cancellationToken);

    [HttpPost("cards/{cardId:guid}/done")]
    public Task SetCardDone(
        [FromRoute] Guid cardId,
        [FromBody] SetRetroCardDoneRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetCardDone(cardId, request, AuthData(), cancellationToken);

    [HttpPost("cards/{cardId:guid}/assignee")]
    public Task SetCardAssignee(
        [FromRoute] Guid cardId,
        [FromBody] SetRetroCardAssigneeRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetCardAssignee(cardId, request, AuthData(), cancellationToken);

    [HttpPost("cards/{cardId:guid}/reveal")]
    public Task SetCardRevealed(
        [FromRoute] Guid cardId,
        [FromBody] SetRetroCardRevealedRequest request,
        CancellationToken cancellationToken = default) =>
        retrosService.SetCardRevealed(cardId, request, AuthData(), cancellationToken);

    private OrganizationAuthData AuthData() => HttpContext.User.GetOrganizationAuthData();
}
