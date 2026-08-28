using BillWatch.Services;
using Microsoft.Maui.Controls.Shapes;

namespace BillWatch;

public sealed class ActivityPage : ContentPage
{
    private readonly BillAlertService
        _billAlertService;

    private readonly BillStreamService
        _billStreamService;

    private readonly VerticalStackLayout
        _content;

    private readonly ActivityIndicator
        _loadingIndicator;

    private bool
        _isLoading;

    public ActivityPage(
        BillAlertService billAlertService,
        BillStreamService billStreamService)
    {
        _billAlertService =
            billAlertService;

        _billStreamService =
            billStreamService;

        Title =
            "Activity";

        SetDynamicResource(
            StyleProperty,
            "BillWatchPageStyle");

        _loadingIndicator =
            new ActivityIndicator
            {
                IsRunning =
                    false,

                IsVisible =
                    false,

                HorizontalOptions =
                    LayoutOptions.Center,

                Margin =
                    new Thickness(
                        0,
                        40)
            };

        _loadingIndicator.SetDynamicResource(
            ActivityIndicator.ColorProperty,
            "BrandPrimary");

        _content =
            new VerticalStackLayout
            {
                MaximumWidthRequest =
                    760,

                HorizontalOptions =
                    LayoutOptions.Center,

                Spacing =
                    16
            };

        var mainLayout =
            new VerticalStackLayout
            {
                Padding =
                    new Thickness(
                        28,
                        28,
                        28,
                        48),

                Spacing =
                    28,

                Children =
                {
                    CreateHeader(),
                    _loadingIndicator,
                    _content
                }
            };

        Content =
            new RefreshView
            {
                Command =
                    new Command(
                        async () =>
                            await LoadAsync()),

                Content =
                    new ScrollView
                    {
                        Content =
                            mainLayout
                    }
            };
    }

    protected override async void
        OnAppearing()
    {
        base.OnAppearing();

        await LoadAsync();
    }

    private View CreateHeader()
    {
        var logo =
            new Border
            {
                WidthRequest =
                    44,

                HeightRequest =
                    44,

                StrokeThickness =
                    0,

                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(
                                14)
                    },

                Content =
                    new Label
                    {
                        Text =
                            "!",

                        FontSize =
                            23,

                        FontAttributes =
                            FontAttributes.Bold,

                        TextColor =
                            Colors.White,

                        HorizontalTextAlignment =
                            TextAlignment.Center,

                        VerticalTextAlignment =
                            TextAlignment.Center
                    }
            };

        logo.SetDynamicResource(
            BackgroundColorProperty,
            "BrandPrimary");

        var brand =
            new VerticalStackLayout
            {
                Spacing =
                    0,

                VerticalOptions =
                    LayoutOptions.Center
            };

        var brandName =
            new Label
            {
                Text =
                    "BillWatch",

                FontSize =
                    21,

                FontAttributes =
                    FontAttributes.Bold
            };

        brandName.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var brandSubtitle =
            new Label
            {
                Text =
                    "Automated Bill Intelligence"
            };

        brandSubtitle.SetDynamicResource(
            StyleProperty,
            "SmallTextStyle");

        brand.Children.Add(
            brandName);

        brand.Children.Add(
            brandSubtitle);

        var brandRow =
            new HorizontalStackLayout
            {
                Spacing =
                    14,

                Children =
                {
                    logo,
                    brand
                }
            };

        var title =
            new Label
            {
                Text =
                    "Activity",

                FontSize =
                    34,

                FontAttributes =
                    FontAttributes.Bold
            };

        title.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var subtitle =
            new Label
            {
                Text =
                    "Important changes BillWatch has detected across your bills."
            };

        subtitle.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        return new VerticalStackLayout
        {
            Spacing =
                28,

            Children =
            {
                brandRow,

                new VerticalStackLayout
                {
                    Spacing =
                        6,

                    Children =
                    {
                        title,
                        subtitle
                    }
                }
            }
        };
    }

    private async Task LoadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            _isLoading =
                true;

            _loadingIndicator.IsVisible =
                true;

            _loadingIndicator.IsRunning =
                true;

            _content.Clear();

            var alerts =
                await _billAlertService
                    .GetAlertsAsync(
                        includeDismissed:
                            false,

                        unreadOnly:
                            false,

                        take:
                            100);

            ShowAlerts(
                alerts);
        }
        catch (SessionExpiredException)
        {
            ShowError(
                "Your BillWatch session expired. Please sign in again.");
        }
        catch (HttpRequestException)
        {
            ShowError(
                "BillWatch couldn't load your activity right now.");
        }
        catch (Exception)
        {
            ShowError(
                "Something went wrong while loading activity.");
        }
        finally
        {
            _loadingIndicator.IsRunning =
                false;

            _loadingIndicator.IsVisible =
                false;

            _isLoading =
                false;
        }
    }

    private void ShowAlerts(
        IReadOnlyList<BillAlertResult> alerts)
    {
        _content.Clear();

        if (alerts.Count ==
            0)
        {
            ShowEmptyState();

            return;
        }

        var unreadCount =
            alerts.Count(
                alert =>
                    !alert.IsRead);

        _content.Children.Add(
            CreateSummaryCard(
                alerts.Count,
                unreadCount));

        foreach (var alert in
                 alerts)
        {
            _content.Children.Add(
                CreateAlertCard(
                    alert));
        }
    }

    private View CreateSummaryCard(
        int totalCount,
        int unreadCount)
    {
        var heading =
            new Label
            {
                Text =
                    unreadCount ==
                    0
                        ? "You're all caught up"
                        : $"{unreadCount} new {(unreadCount == 1 ? "alert" : "alerts")}",

                FontSize =
                    22,

                FontAttributes =
                    FontAttributes.Bold
            };

        heading.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var detail =
            new Label
            {
                Text =
                    $"{totalCount} recent meaningful {(totalCount == 1 ? "event" : "events")}"
            };

        detail.SetDynamicResource(
            StyleProperty,
            "SecondaryTextStyle");

        var card =
            new Border
            {
                Padding =
                    new Thickness(
                        22),

                StrokeThickness =
                    0,

                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(
                                22)
                    },

                Content =
                    new VerticalStackLayout
                    {
                        Spacing =
                            4,

                        Children =
                        {
                            heading,
                            detail
                        }
                    }
            };

        card.SetDynamicResource(
            BackgroundColorProperty,
            "CardBackground");

        return card;
    }

    private View CreateAlertCard(
        BillAlertResult alert)
    {
        var status =
            new Label
            {
                Text =
                    GetStatusText(
                        alert),

                FontSize =
                    12,

                FontAttributes =
                    FontAttributes.Bold
            };

        status.SetDynamicResource(
            Label.TextColorProperty,
            GetSeverityResource(
                alert));

        var timestamp =
            new Label
            {
                Text =
                    alert.CreatedAtUtc
                        .ToLocalTime()
                        .ToString(
                            "MMM d • h:mm tt"),

                HorizontalOptions =
                    LayoutOptions.End
            };

        timestamp.SetDynamicResource(
            StyleProperty,
            "SmallTextStyle");

        var topRow =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star),

                    new ColumnDefinition(
                        GridLength.Auto)
                }
            };

        topRow.Add(
            status,
            0,
            0);

        topRow.Add(
            timestamp,
            1,
            0);

        var title =
            new Label
            {
                Text =
                    alert.Title,

                FontSize =
                    20,

                FontAttributes =
                    alert.IsRead
                        ? FontAttributes.None
                        : FontAttributes.Bold
            };

        title.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var message =
            new Label
            {
                Text =
                    alert.Message,

                LineBreakMode =
                    LineBreakMode.WordWrap
            };

        message.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var buttonRow =
            new HorizontalStackLayout
            {
                Spacing =
                    10
            };

        if (alert.BillStreamId.HasValue)
        {
            var viewBillButton =
                new Button
                {
                    Text =
                        "View bill",

                    CornerRadius =
                        14,

                    Padding =
                        new Thickness(
                            18,
                            10),

                    FontAttributes =
                        FontAttributes.Bold,

                    TextColor =
                        Colors.White
                };

            viewBillButton.SetDynamicResource(
                BackgroundColorProperty,
                "BrandPrimary");

            viewBillButton.Clicked +=
                async (_, _) =>
                {
                    await OpenBillAsync(
                        alert);
                };

            buttonRow.Children.Add(
                viewBillButton);
        }

        var dismissButton =
            new Button
            {
                Text =
                    "Dismiss",

                CornerRadius =
                    14,

                Padding =
                    new Thickness(
                        18,
                        10),

                BackgroundColor =
                    Colors.Transparent
            };

        dismissButton.SetDynamicResource(
            Button.TextColorProperty,
            "SecondaryText");

        dismissButton.Clicked +=
            async (_, _) =>
            {
                await DismissAsync(
                    alert);
            };

        buttonRow.Children.Add(
            dismissButton);

        var cardContent =
            new VerticalStackLayout
            {
                Spacing =
                    12,

                Children =
                {
                    topRow,
                    title,
                    message,
                    buttonRow
                }
            };

        var card =
            new Border
            {
                Padding =
                    new Thickness(
                        22),

                StrokeThickness =
                    alert.IsRead
                        ? 1
                        : 2,

                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(
                                22)
                    },

                Content =
                    cardContent
            };

        card.SetDynamicResource(
            BackgroundColorProperty,
            "CardBackground");

        card.SetDynamicResource(
            Border.StrokeProperty,
            alert.IsRead
                ? "CardBorder"
                : "BrandPrimary");

        return card;
    }

    private async Task OpenBillAsync(
        BillAlertResult alert)
    {
        if (!alert.BillStreamId.HasValue)
        {
            return;
        }

        try
        {
            if (!alert.IsRead)
            {
                await _billAlertService
                    .MarkReadAsync(
                        alert.Id);
            }

            var detailPage =
                new BillDetailPage(
                    _billStreamService)
                {
                    BillStreamId =
                        alert.BillStreamId
                            .Value
                            .ToString()
                };

            await Navigation
                .PushModalAsync(
                    new NavigationPage(
                        detailPage));
        }
        catch (SessionExpiredException)
        {
            ShowError(
                "Your BillWatch session expired. Please sign in again.");
        }
        catch
        {
            ShowError(
                "BillWatch couldn't open this bill.");
        }
    }

    private async Task DismissAsync(
        BillAlertResult alert)
    {
        try
        {
            await _billAlertService
                .DismissAsync(
                    alert.Id);

            await LoadAsync();
        }
        catch (SessionExpiredException)
        {
            ShowError(
                "Your BillWatch session expired. Please sign in again.");
        }
        catch
        {
            ShowError(
                "BillWatch couldn't dismiss this alert.");
        }
    }

    private void ShowEmptyState()
    {
        _content.Clear();

        var icon =
            new Label
            {
                Text =
                    "✓",

                FontSize =
                    42,

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        icon.SetDynamicResource(
            Label.TextColorProperty,
            "BrandPrimary");

        var title =
            new Label
            {
                Text =
                    "Nothing needs your attention",

                FontSize =
                    22,

                FontAttributes =
                    FontAttributes.Bold,

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        title.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var message =
            new Label
            {
                Text =
                    "When BillWatch detects a meaningful bill change, you'll see it here.",

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        message.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var card =
            new Border
            {
                Padding =
                    new Thickness(
                        30),

                StrokeThickness =
                    0,

                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(
                                24)
                    },

                Content =
                    new VerticalStackLayout
                    {
                        Spacing =
                            12,

                        Children =
                        {
                            icon,
                            title,
                            message
                        }
                    }
            };

        card.SetDynamicResource(
            BackgroundColorProperty,
            "CardBackground");

        _content.Children.Add(
            card);
    }

    private void ShowError(
        string message)
    {
        _content.Clear();

        var title =
            new Label
            {
                Text =
                    "Couldn't load activity",

                FontSize =
                    21,

                FontAttributes =
                    FontAttributes.Bold
            };

        title.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var detail =
            new Label
            {
                Text =
                    message
            };

        detail.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var retry =
            new Button
            {
                Text =
                    "Try again",

                CornerRadius =
                    14,

                TextColor =
                    Colors.White
            };

        retry.SetDynamicResource(
            BackgroundColorProperty,
            "BrandPrimary");

        retry.Clicked +=
            async (_, _) =>
            {
                await LoadAsync();
            };

        _content.Children.Add(
            new VerticalStackLayout
            {
                Spacing =
                    12,

                Children =
                {
                    title,
                    detail,
                    retry
                }
            });
    }

    private static string GetStatusText(
        BillAlertResult alert)
    {
        var prefix =
            alert.IsRead
                ? string.Empty
                : "● ";

        return alert.AlertType switch
        {
            "BillIncrease" =>
                $"{prefix}BILL INCREASE",

            "BillDecrease" =>
                $"{prefix}BILL DECREASE",

            "NewFee" =>
                $"{prefix}NEW FEE",

            "RemovedDiscount" =>
                $"{prefix}DISCOUNT REMOVED",

            "PaymentDue" =>
                $"{prefix}PAYMENT DUE",

            "ConnectionIssue" =>
                $"{prefix}CONNECTION ISSUE",

            _ =>
                $"{prefix}ACTIVITY"
        };
    }

    private static string GetSeverityResource(
        BillAlertResult alert)
    {
        return alert.Severity switch
        {
            "Critical" =>
                "DangerText",

            "Warning" =>
                "BrandPrimary",

            _ =>
                "SecondaryText"
        };
    }
}