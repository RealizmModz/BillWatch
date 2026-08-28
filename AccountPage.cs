using Microsoft.Maui.Controls.Shapes;

namespace BillWatch;

public sealed class AccountPage : ContentPage
{
    public AccountPage()
    {
        Title =
            "Account";

        SetDynamicResource(
            StyleProperty,
            "BillWatchPageStyle");

        Content =
            new ScrollView
            {
                Content =
                    new VerticalStackLayout
                    {
                        Padding =
                            new Thickness(
                                28,
                                28,
                                28,
                                48),

                        MaximumWidthRequest =
                            760,

                        HorizontalOptions =
                            LayoutOptions.Center,

                        Spacing =
                            24,

                        Children =
                        {
                            CreateHeader(),

                            CreateNavigationCard(
                                title:
                                    "Connected banks",

                                description:
                                    "Review the financial institutions BillWatch is monitoring and manage connection health.",

                                actionText:
                                    "Manage connections",

                                onClicked:
                                    OpenConnectionsAsync),

                            CreateNavigationCard(
                                title:
                                    "Transactions",

                                description:
                                    "Review the bank transactions BillWatch uses to discover recurring bills.",

                                actionText:
                                    "View transactions",

                                onClicked:
                                    OpenTransactionsAsync),

                            CreateTrustCard()
                        }
                    }
            };
    }

    private static View CreateHeader()
    {
        var title =
            new Label
            {
                Text =
                    "Account",

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
                    "Connections, privacy, and the data BillWatch uses to keep watching your bills."
            };

        subtitle.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        return new VerticalStackLayout
        {
            Spacing =
                6,

            Children =
            {
                title,
                subtitle
            }
        };
    }

    private static View CreateNavigationCard(
        string title,
        string description,
        string actionText,
        Func<Task> onClicked)
    {
        var titleLabel =
            new Label
            {
                Text =
                    title,

                FontSize =
                    21,

                FontAttributes =
                    FontAttributes.Bold
            };

        titleLabel.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        var descriptionLabel =
            new Label
            {
                Text =
                    description,

                LineBreakMode =
                    LineBreakMode.WordWrap
            };

        descriptionLabel.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var button =
            new Button
            {
                Text =
                    actionText,

                HorizontalOptions =
                    LayoutOptions.Start,

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

        button.SetDynamicResource(
            BackgroundColorProperty,
            "BrandPrimary");

        button.Clicked +=
            async (_, _) =>
            {
                button.IsEnabled =
                    false;

                try
                {
                    await onClicked();
                }
                finally
                {
                    button.IsEnabled =
                        true;
                }
            };

        var card =
            new Border
            {
                Padding =
                    new Thickness(
                        22),

                StrokeThickness =
                    1,

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
                            12,

                        Children =
                        {
                            titleLabel,
                            descriptionLabel,
                            button
                        }
                    }
            };

        card.SetDynamicResource(
            BackgroundColorProperty,
            "CardBackground");

        card.SetDynamicResource(
            Border.StrokeProperty,
            "CardBorder");

        return card;
    }

    private static View CreateTrustCard()
    {
        var heading =
            new Label
            {
                Text =
                    "Your financial connections",

                FontSize =
                    18,

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
                    "BillWatch uses connected financial data to detect recurring bills and meaningful changes. Financial connections can be reviewed or disconnected from Connected banks.",

                LineBreakMode =
                    LineBreakMode.WordWrap
            };

        detail.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

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
                            8,

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

    private static Task OpenConnectionsAsync()
    {
        return Shell.Current.GoToAsync(
            nameof(ConnectBankPage));
    }

    private static Task OpenTransactionsAsync()
    {
        return Shell.Current.GoToAsync(
            nameof(TransactionsPage));
    }
}