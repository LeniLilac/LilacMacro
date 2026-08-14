using LilacMacro.App.Notifications;

namespace LilacMacro.Tests;

public sealed class AppToastServiceTests
{
    [Fact]
    public void SuccessAndErrorUseSharedNotificationChannel()
    {
        List<AppToast> received = [];
        EventHandler<AppToast> handler = (_, toast) => received.Add(toast);
        AppToastService.Raised += handler;
        try
        {
            AppToastService.ShowSuccess(" saved ", " done ");
            AppToastService.ShowError(" failed ", " nope ");
        }
        finally
        {
            AppToastService.Raised -= handler;
        }

        Assert.Collection(
            received,
            toast => Assert.Equal(new AppToast("saved", "done", AppToastTone.Success), toast),
            toast => Assert.Equal(new AppToast("failed", "nope", AppToastTone.Error), toast));
    }
}
