using BillWatch.ViewModels;

namespace BillWatch;

public sealed class TransactionsPage : ContentPage
{
    private readonly TransactionsPageViewModel _viewModel;

    public TransactionsPage(
        TransactionsPageViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;

        Title = "Transactions";

        SetDynamicResource(
            StyleProperty,
            "BillWatchPageStyle");

        var title =
            new Label
            {
                Text = "Transactions"
            };

        title.SetDynamicResource(
            StyleProperty,
            "PageTitleStyle");

        var subtitle =
            new Label
            {
                Text =
                    "Recent activity imported securely from your connected bank accounts."
            };

        subtitle.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        var summary =
            new Label();

        summary.SetDynamicResource(
            StyleProperty,
            "SecondaryTextStyle");

        summary.SetBinding(
            Label.TextProperty,
            nameof(
                TransactionsPageViewModel.Summary));

        var loading =
            new ActivityIndicator
            {
                HorizontalOptions =
                    LayoutOptions.Center
            };

        loading.SetBinding(
            ActivityIndicator.IsRunningProperty,
            nameof(
                TransactionsPageViewModel.IsLoading));

        loading.SetBinding(
            IsVisibleProperty,
            nameof(
                TransactionsPageViewModel.IsLoading));

        var error =
            new Label
            {
                HorizontalTextAlignment =
                    TextAlignment.Center
            };

        error.SetDynamicResource(
            StyleProperty,
            "IncreaseTextStyle");

        error.SetBinding(
            Label.TextProperty,
            nameof(
                TransactionsPageViewModel.ErrorMessage));

        error.SetBinding(
            IsVisibleProperty,
            nameof(
                TransactionsPageViewModel.HasError));

        var transactions =
            new CollectionView
            {
                SelectionMode =
                    SelectionMode.None,

                EmptyView =
                    new Label
                    {
                        Text =
                            "No transactions to display yet.",

                        HorizontalTextAlignment =
                            TextAlignment.Center,

                        Margin =
                            new Thickness(
                                0,
                                30)
                    },

                ItemTemplate =
                    new DataTemplate(
                        () =>
                        {
                            var merchant =
                                new Label();

                            merchant.SetDynamicResource(
                                StyleProperty,
                                "ItemTitleStyle");

                            merchant.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.DisplayName));

                            var amount =
                                new Label
                                {
                                    FontAttributes =
                                        FontAttributes.Bold,

                                    HorizontalOptions =
                                        LayoutOptions.End
                                };

                            amount.SetDynamicResource(
                                StyleProperty,
                                "PrimaryTextStyle");

                            amount.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.FormattedAmount));

                            var topRow =
                                new Grid
                                {
                                    ColumnDefinitions =
                                    {
                                        new ColumnDefinition(
                                            GridLength.Star),

                                        new ColumnDefinition(
                                            GridLength.Auto)
                                    },

                                    ColumnSpacing = 16
                                };

                            topRow.Add(
                                merchant,
                                0,
                                0);

                            topRow.Add(
                                amount,
                                1,
                                0);

                            var account =
                                new Label();

                            account.SetDynamicResource(
                                StyleProperty,
                                "SmallTextStyle");

                            account.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.AccountDescription));

                            var institution =
                                new Label();

                            institution.SetDynamicResource(
                                StyleProperty,
                                "SmallTextStyle");

                            institution.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.InstitutionName));

                            var date =
                                new Label();

                            date.SetDynamicResource(
                                StyleProperty,
                                "SmallTextStyle");

                            date.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.FormattedDate));

                            var status =
                                new Label
                                {
                                    HorizontalOptions =
                                        LayoutOptions.End
                                };

                            status.SetDynamicResource(
                                StyleProperty,
                                "SmallTextStyle");

                            status.SetBinding(
                                Label.TextProperty,
                                nameof(
                                    TransactionListItem.Status));

                            var bottomRow =
                                new Grid
                                {
                                    ColumnDefinitions =
                                    {
                                        new ColumnDefinition(
                                            GridLength.Star),

                                        new ColumnDefinition(
                                            GridLength.Auto)
                                    },

                                    ColumnSpacing = 16
                                };

                            bottomRow.Add(
                                date,
                                0,
                                0);

                            bottomRow.Add(
                                status,
                                1,
                                0);

                            var content =
                                new VerticalStackLayout
                                {
                                    Spacing = 6,

                                    Children =
                                    {
                                        topRow,
                                        account,
                                        institution,
                                        bottomRow
                                    }
                                };

                            var card =
                                new Border
                                {
                                    Padding = 16,
                                    Margin =
                                        new Thickness(
                                            0,
                                            0,
                                            0,
                                            12),

                                    Content = content
                                };

                            card.SetDynamicResource(
                                StyleProperty,
                                "CardStyle");

                            return card;
                        })
            };

        transactions.SetBinding(
            ItemsView.ItemsSourceProperty,
            nameof(
                TransactionsPageViewModel.Transactions));

        var header =
            new VerticalStackLayout
            {
                Spacing = 8,

                Children =
                {
                    title,
                    subtitle,
                    summary,
                    loading,
                    error
                }
            };

        var layout =
            new Grid
            {
                Padding =
                    new Thickness(
                        24,
                        22,
                        24,
                        18),

                RowDefinitions =
                {
                    new RowDefinition(
                        GridLength.Auto),

                    new RowDefinition(
                        GridLength.Star)
                },

                RowSpacing = 18
            };

        layout.Add(
            header,
            0,
            0);

        layout.Add(
            transactions,
            0,
            1);

        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync();
    }
}