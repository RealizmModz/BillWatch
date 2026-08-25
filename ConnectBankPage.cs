using BillWatch.ViewModels;

namespace BillWatch;

public sealed class ConnectBankPage : ContentPage
{
    public ConnectBankPage(
        ConnectBankPageViewModel viewModel)
    {
        BindingContext = viewModel;

        Title = "Connect Bank";

        SetDynamicResource(
            StyleProperty,
            "BillWatchPageStyle");

        var backButton =
            new Button
            {
                Text = "‹",
                FontSize = 28,
                WidthRequest = 48,
                HeightRequest = 48,
                Padding = 0,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Start
            };

        backButton.Clicked +=
            async (_, _) =>
            {
                await Shell.Current.GoToAsync("..");
            };

        var title =
            new Label
            {
                Text = "Connect your bank"
            };

        title.SetDynamicResource(
            StyleProperty,
            "PageTitleStyle");

        var subtitle =
            new Label
            {
                Text =
                    "BillWatch securely connects through Plaid to monitor transactions and identify recurring bills."
            };

        subtitle.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var header =
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    title,
                    subtitle
                }
            };

        var securityBadge =
            new Border
            {
                Padding =
                    new Thickness(
                        12,
                        8),

                StrokeThickness = 0,

                Content =
                    new Label
                    {
                        Text =
                            "🔒 Secure connection powered by Plaid",

                        FontSize = 13
                    }
            };

        securityBadge.SetDynamicResource(
            StyleProperty,
            "BrandBadgeStyle");

        var bankIcon =
            new Label
            {
                Text = "🏦",
                FontSize = 44,
                HorizontalOptions =
                    LayoutOptions.Center
            };

        var cardTitle =
            new Label
            {
                Text =
                    "See your bills automatically",

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        cardTitle.SetDynamicResource(
            StyleProperty,
            "SectionTitleStyle");

        var cardDescription =
            new Label
            {
                Text =
                    "BillWatch will use your transaction history to detect recurring bills, track changes, and help explain where your money is going.",

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        cardDescription.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var connectButton =
            new Button
            {
                Text = "Connect bank",
                HeightRequest = 54,
                CornerRadius = 16,
                FontAttributes =
                    FontAttributes.Bold
            };

        connectButton.SetBinding(
            Button.CommandProperty,
            nameof(
                ConnectBankPageViewModel
                    .ConnectBankCommand));

        connectButton.SetDynamicResource(
            BackgroundColorProperty,
            "BrandPrimary");

        connectButton.TextColor =
            Colors.White;

        var activityIndicator =
            new ActivityIndicator
            {
                HorizontalOptions =
                    LayoutOptions.Center
            };

        activityIndicator.SetBinding(
            ActivityIndicator.IsRunningProperty,
            nameof(
                ConnectBankPageViewModel
                    .IsBusy));

        activityIndicator.SetBinding(
            IsVisibleProperty,
            nameof(
                ConnectBankPageViewModel
                    .IsBusy));

        var statusLabel =
            new Label
            {
                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        statusLabel.SetDynamicResource(
            StyleProperty,
            "SecondaryTextStyle");

        statusLabel.SetBinding(
            Label.TextProperty,
            nameof(
                ConnectBankPageViewModel
                    .StatusMessage));

        var connectedInstitution =
            new Label
            {
                HorizontalTextAlignment =
                    TextAlignment.Center,
                FontAttributes =
                    FontAttributes.Bold
            };

        connectedInstitution.SetDynamicResource(
            StyleProperty,
            "SuccessTextStyle");

        connectedInstitution.SetBinding(
            Label.TextProperty,
            nameof(
                ConnectBankPageViewModel
                    .ConnectedInstitution));

        connectedInstitution.SetBinding(
            IsVisibleProperty,
            nameof(
                ConnectBankPageViewModel
                    .HasConnectedInstitution));

        var connectionCard =
            new Border
            {
                Padding = 24,

                Content =
                    new VerticalStackLayout
                    {
                        Spacing = 18,

                        Children =
                        {
                            bankIcon,
                            cardTitle,
                            cardDescription,
                            connectButton,
                            activityIndicator,
                            statusLabel,
                            connectedInstitution
                        }
                    }
            };

        connectionCard.SetDynamicResource(
            StyleProperty,
            "CardStyle");

        var privacyTitle =
            new Label
            {
                Text =
                    "Your banking credentials stay private"
            };

        privacyTitle.SetDynamicResource(
            StyleProperty,
            "ItemTitleStyle");

        var privacyDescription =
            new Label
            {
                Text =
                    "BillWatch never receives or stores your bank username or password. Authentication happens through Plaid's secure connection."
            };

        privacyDescription.SetDynamicResource(
            StyleProperty,
            "SmallTextStyle");

        var privacyCard =
            new Border
            {
                Padding = 18,

                Content =
                    new VerticalStackLayout
                    {
                        Spacing = 6,

                        Children =
                        {
                            privacyTitle,
                            privacyDescription
                        }
                    }
            };

        privacyCard.SetDynamicResource(
            StyleProperty,
            "FlatCardStyle");

        Content =
            new ScrollView
            {
                Content =
                    new VerticalStackLayout
                    {
                        Padding =
                            new Thickness(
                                24,
                                18,
                                24,
                                40),

                        Spacing = 22,

                        Children =
                        {
                            backButton,
                            header,
                            securityBadge,
                            connectionCard,
                            privacyCard
                        }
                    }
            };
    }
}