using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MyEpisodes.TestHarness.Cli;

public class StubUserDataManager : IUserDataManager
{
    public void SaveUserData(User user, BaseItem item, UserItemData userData, UserDataSaveReason reason,
        CancellationToken cancellationToken)
    {
        UserDataSaved?.Invoke(this, new UserDataSaveEventArgs
        {
            UserId = user.Id,
            Item = item,
            UserData = userData,
            SaveReason = reason
        });
    }

    public void SaveUserData(User user, BaseItem item, UpdateUserItemDataDto userDataDto, UserDataSaveReason reason)
    {
        
    }

    public UserItemData? GetUserData(User user, BaseItem item)
    {
        return null;
    }

    public UserItemDataDto? GetUserDataDto(BaseItem item, User user)
    {
        return null;
    }

    public UserItemDataDto? GetUserDataDto(BaseItem item, BaseItemDto? itemDto, User user, DtoOptions options)
    {
        return null;
    }

    public bool UpdatePlayState(BaseItem item, UserItemData data, long? reportedPositionTicks)
    {
        return false;
    }

    public event EventHandler<UserDataSaveEventArgs>? UserDataSaved;
}