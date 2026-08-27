using BillWatch.ViewModels;

namespace BillWatch;

public sealed class ConnectBankPage : ContentPage
{
    private readonly ConnectBankPageViewModel
        _viewModel;

    public ConnectBankPage(
        ConnectBankPageViewModel viewModel)
    {
        _viewModel =
            viewModel;

        BindingContext =
            viewModel;

        Title =
            "Connect Bank";

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
                BackgroundColor =
                    Colors.Transparent,
                HorizontalOptions =
                    LayoutOptions.Start
            };

        backButton.Clicked +=
            async (_, _) =>
            {
                await Shell.Current
                    .GoToAsync("..");
            };

        var title =
            new Label
            {
                Text =
                    "Bank connections"
            };

        title.SetDynamicResource(
            StyleProperty,
            "PageTitleStyle");

        var subtitle =
            new Label
            {
                Text =
                    "Securely connect and manage the accounts BillWatch uses to monitor recurring bills."
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
                    "Connect another bank",

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
                    "BillWatch uses transaction history to detect recurring bills and monitor changes. Your bank credentials are handled by Plaid.",

                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        cardDescription.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var connectButton =
            new Button
            {
                Text =
                    "Connect bank",

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
            "PrimaryTextStyle");

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

        var connectionsTitle =
            new Label
            {
                Text =
                    "Your connections"
            };

        connectionsTitle.SetDynamicResource(
            StyleProperty,
            "SectionTitleStyle");

        var refreshButton =
            new Button
            {
                Text =
                    "Refresh",

                HorizontalOptions =
                    LayoutOptions.End,

                HeightRequest = 42,
                CornerRadius = 14
            };

        refreshButton.SetBinding(
            Button.CommandProperty,
            nameof(
                ConnectBankPageViewModel
                    .RefreshConnectionsCommand));

        var connectionsHeader =
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

        connectionsHeader.Add(
            connectionsTitle,
            0,
            0);

        connectionsHeader.Add(
            refreshButton,
            1,
            0);

        var noConnectionsLabel =
            new Label
            {
                Text =
                    "No bank connections yet.",

                HorizontalTextAlignment =
                    TextAlignment.Center,

                Margin =
                    new Thickness(
                        0,
                        8)
            };

        noConnectionsLabel.SetDynamicResource(
            StyleProperty,
            "MutedTextStyle");

        noConnectionsLabel.SetBinding(
            IsVisibleProperty,
            nameof(
                ConnectBankPageViewModel
                    .HasNoConnections));

        var connectionsList =
            new VerticalStackLayout
            {
                Spacing = 12
            };

        BindableLayout.SetItemsSource(
            connectionsList,
            viewModel.Connections);

        BindableLayout.SetItemTemplate(
            connectionsList,
            new DataTemplate(
                () =>
                {
                    var institutionName =
                        new Label
                        {
                            FontAttributes =
                                FontAttributes.Bold,

                            FontSize = 17
                        };

                    institutionName.SetDynamicResource(
                        StyleProperty,
                        "ItemTitleStyle");

                    institutionName.SetBinding(
                        Label.TextProperty,
                        nameof(
                            BankConnectionItemViewModel
                                .InstitutionName));

                    var status =
                        new Label();

                    status.SetDynamicResource(
                        StyleProperty,
                        "SecondaryTextStyle");

                    status.SetBinding(
                        Label.TextProperty,
                        nameof(
                            BankConnectionItemViewModel
                                .StatusText));

                    var lastSync =
                        new Label();

                    lastSync.SetDynamicResource(
                        StyleProperty,
                        "SmallTextStyle");

                    lastSync.SetBinding(
                        Label.TextProperty,
                        nameof(
                            BankConnectionItemViewModel
                                .LastSyncText));

                    var disconnectButton =
                        new Button
                        {
                            Text =
                                "Disconnect",

                            HeightRequest = 42,
                            CornerRadius = 14
                        };

                    disconnectButton.SetBinding(
                        IsVisibleProperty,
                        nameof(
                            BankConnectionItemViewModel
                                .CanDisconnect));

                    disconnectButton.Clicked +=
                        DisconnectButtonClicked;

                    var reconnectButton =
                        new Button
                        {
                            Text =
                                "Reconnect",

                            HeightRequest = 42,
                            CornerRadius = 14
                        };

                    reconnectButton.SetDynamicResource(
                        BackgroundColorProperty,
                        "BrandPrimary");

                    reconnectButton.TextColor =
                        Colors.White;

                    reconnectButton.SetBinding(
                        IsVisibleProperty,
                        nameof(
                            BankConnectionItemViewModel
                                .CanReconnect));

                    reconnectButton.Clicked +=
                        ReconnectButtonClicked;

                    var actions =
                        new HorizontalStackLayout
                        {
                            Spacing = 10,

                            Children =
                            {
                                reconnectButton,
                                disconnectButton
                            }
                        };

                    var content =
                        new VerticalStackLayout
                        {
                            Spacing = 7,

                            Children =
                            {
                                institutionName,
                                status,
                                lastSync,
                                actions
                            }
                        };

                    var card =
                        new Border
                        {
                            Padding = 18,
                            Content = content
                        };

                    card.SetDynamicResource(
                        StyleProperty,
                        "FlatCardStyle");

                    return card;
                }));

        var connectionsSection =
            new VerticalStackLayout
            {
                Spacing = 12,

                Children =
                {
                    connectionsHeader,
                    noConnectionsLabel,
                    connectionsList
                }
            };

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
                    "BillWatch never receives or stores your bank username or password. Authentication happens through Plaid. Disconnecting removes BillWatch's ability to continue syncing that bank connection."
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
                            connectionsSection,
                            privacyCard
                        }
                    }
            };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel
            .LoadConnectionsAsync();
    }

    private async void DisconnectButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext
                is not BankConnectionItemViewModel
                    connection)
        {
            return;
        }

        var confirmed =
            await DisplayAlertAsync(
                "Disconnect bank",
                $"Disconnect {connection.InstitutionName}? BillWatch will stop syncing this connection.",
                "Disconnect",
                "Cancel");

        if (!confirmed)
        {
            return;
        }

        await _viewModel
            .DisconnectAsync(
                connection);
    }

    private async void ReconnectButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext
                is not BankConnectionItemViewModel)
        {
            return;
        }

        await _viewModel
            .ConnectBankAsync();
    }
}