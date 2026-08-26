using Laraue.Apps.Boards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Laraue.Apps.Boards.WebApiServices;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
public class RetroHub(IRetrosService retrosService) : Hub
{
    private const string RetroIdKey = "retroId";
    private const string MemberKey = "member";

    public static string GroupName(long retroId) => $"retro:{retroId}";

    public async Task Join(long retroId)
    {
        var member = await retrosService.JoinRealtime(
            retroId,
            Context.User!.GetOrganizationAuthData(),
            Context.ConnectionAborted);

        Context.Items[RetroIdKey] = retroId;
        Context.Items[MemberKey] = member;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(retroId), Context.ConnectionAborted);
        await Others().SendAsync("join", member, Context.ConnectionAborted);
    }

    /// <summary>Answers a newcomer so they learn who is already here.</summary>
    public Task Announce() => Others().SendAsync("presence", Member(), Context.ConnectionAborted);

    public Task Cursor(double x, double y) =>
        Others().SendAsync("cursor", Member(), x, y, Context.ConnectionAborted);

    public Task MoveCard(Guid cardId, double x, double y) =>
        Others().SendAsync("card-move", cardId, x, y, Context.ConnectionAborted);

    public Task SetCardText(Guid cardId, string text) =>
        Others().SendAsync("card-text", cardId, text, Context.ConnectionAborted);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RetroIdKey, out var retroId)
            && Context.Items.TryGetValue(MemberKey, out var member))
        {
            await Clients
                .OthersInGroup(GroupName((long)retroId!))
                .SendAsync("leave", member);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private IClientProxy Others() => Clients.OthersInGroup(GroupName(Joined<long>(RetroIdKey)));

    private VisibleUser Member() => Joined<VisibleUser>(MemberKey);

    private TValue Joined<TValue>(string key) =>
        Context.Items.TryGetValue(key, out var value) && value is TValue joined
            ? joined
            : throw new HubException("Join the retro before sending anything to it.");
}
