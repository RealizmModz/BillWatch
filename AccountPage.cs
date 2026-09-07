using BillWatch.Services;
using Microsoft.Maui.Controls.Shapes;

namespace BillWatch;

public sealed class AccountPage : ContentPage
{
    private readonly AuthenticationService _authenticationService;
    private bool _isWorking;

    public AccountPage(AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
        Title = "Account";
        SetDynamicResource(StyleProperty, "BillWatchPageStyle");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(28, 28, 28, 48),
                MaximumWidthRequest = 760,
                HorizontalOptions = LayoutOptions.Center,
                Spacing = 24,
                Children =
                {
                    CreateHeader(),
                    CreateNavigationCard(
                        "Connected banks",
                        "Review the financial institutions BillWatch is monitoring, connection health, and disconnect controls.",
                        "Manage connections",
                        () => Shell.Current.GoToAsync(nameof(ConnectBankPage))),
                    CreateNavigationCard(
                        "Transactions",
                        "Review the bank transactions BillWatch uses to discover recurring bills.",
                        "View transactions",
                        () => Shell.Current.GoToAsync(nameof(TransactionsPage))),
                    CreateInfoCard(),
                    CreateSessionCard(),
                    CreateDangerCard()
                }
            }
        };
    }

    private static View CreateHeader()
    {
        var title = CreateTitleLabel("Account");
        title.FontSize = 34;

        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                title,
                CreateBodyLabel("Connections, privacy, security, and your BillWatch account.")
            }
        };
    }

    private static View CreateNavigationCard(
        string title,
        string description,
        string actionText,
        Func<Task> onClicked)
    {
        var button = CreatePrimaryButton(actionText);
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await onClicked();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        return CreateCard(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                CreateTitleLabel(title),
                CreateBodyLabel(description),
                button
            }
        });
    }

    private static View CreateInfoCard()
    {
        return CreateCard(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                CreateTitleLabel("Your financial data"),
                CreateBodyLabel("BillWatch uses connected transaction data to identify recurring bills and meaningful changes. Plaid access tokens stay protected on the server and are never returned to this app."),
                CreateBodyLabel("Uploaded provider statements are stored privately by BillWatch and used as evidence for bill history and change explanations.")
            }
        });
    }

    private View CreateSessionCard()
    {
        var signOut = CreatePrimaryButton("Sign out");
        signOut.Clicked += async (_, _) =>
        {
            if (_isWorking)
            {
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Sign out",
                "Sign out of BillWatch on this device? Automatic monitoring will continue in the background.",
                "Sign out",
                "Cancel");

            if (confirmed)
            {
                _authenticationService.Logout();
            }
        };

        return CreateCard(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                CreateTitleLabel("Sign-in session"),
                CreateBodyLabel("Signing out removes BillWatch authentication tokens from this device. Server-side monitoring continues for connected banks until you disconnect them or delete your account."),
                signOut
            }
        });
    }

    private View CreateDangerCard()
    {
        var deleteButton = new Button
        {
            Text = "Delete account permanently",
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.IndianRed,
            BorderColor = Colors.IndianRed,
            BorderWidth = 1,
            CornerRadius = 14,
            Padding = new Thickness(18, 10),
            FontAttributes = FontAttributes.Bold
        };

        deleteButton.Clicked += async (_, _) =>
            await DeleteAccountAsync(deleteButton);

        return CreateCard(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                CreateTitleLabel("Delete BillWatch account"),
                CreateBodyLabel("Permanently deleting your account removes your BillWatch financial data, detected bills, alerts, bill history, subscription state, and stored statement files. BillWatch first attempts to revoke active bank connections."),
                new Label
                {
                    Text = "This cannot be undone.",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.IndianRed
                },
                deleteButton
            }
        });
    }

    private async Task DeleteAccountAsync(
        Button deleteButton)
    {
        if (_isWorking)
        {
            return;
        }

        var first = await DisplayAlertAsync(
            "Delete your account?",
            "BillWatch will permanently remove your account and financial history after safely revoking connected banks.",
            "Continue",
            "Cancel");

        if (!first)
        {
            return;
        }

        var credentials =
            await PromptForDeletionCredentialsAsync();

        if (credentials is null)
        {
            return;
        }

        try
        {
            _isWorking = true;
            deleteButton.IsEnabled = false;
            deleteButton.Text = "Deleting securely…";

            await _authenticationService.DeleteAccountAsync(
                credentials.CurrentPassword,
                credentials.TwoFactorCode);
        }
        catch (AccountDeletionException exception)
        {
            await DisplayAlertAsync(
                "Account not deleted",
                exception.Message,
                "OK");
        }
        catch (SessionExpiredException)
        {
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Account not deleted",
                "BillWatch could not reach the server. Your account was not deleted. Check your connection and try again.",
                "OK");
        }
        catch
        {
            await DisplayAlertAsync(
                "Account not deleted",
                "BillWatch could not safely complete account deletion. Your account was not deleted.",
                "OK");
        }
        finally
        {
            _isWorking = false;
            deleteButton.IsEnabled = true;
            deleteButton.Text = "Delete account permanently";
        }
    }

    private async Task<DeletionCredentials?> PromptForDeletionCredentialsAsync()
    {
        var completion =
            new TaskCompletionSource<DeletionCredentials?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var passwordEntry = new Entry
        {
            Placeholder = "Current password",
            IsPassword = true,
            MaxLength = 256,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };

        var twoFactorEntry = new Entry
        {
            Placeholder = "Authenticator code (if enabled)",
            Keyboard = Keyboard.Numeric,
            MaxLength = 32,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };

        var confirmationEntry = new Entry
        {
            Placeholder = "DELETE",
            MaxLength = 6,
            AutoCapitalization = TextTransform.None,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };

        var deleteButton = new Button
        {
            Text = "Delete permanently",
            BackgroundColor = Colors.IndianRed,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            IsEnabled = false
        };

        var cancelButton = new Button
        {
            Text = "Keep my account",
            BackgroundColor = Colors.Transparent,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14
        };

        void RefreshDeleteButton()
        {
            deleteButton.IsEnabled =
                !string.IsNullOrWhiteSpace(passwordEntry.Text) &&
                string.Equals(
                    confirmationEntry.Text,
                    "DELETE",
                    StringComparison.Ordinal);
        }

        passwordEntry.TextChanged += (_, _) =>
            RefreshDeleteButton();

        confirmationEntry.TextChanged += (_, _) =>
            RefreshDeleteButton();

        var modal = new ContentPage
        {
            Title = "Confirm account deletion",
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = new Thickness(28, 36),
                    Spacing = 18,
                    Children =
                    {
                        new Label
                        {
                            Text = "Confirm permanent deletion",
                            FontSize = 28,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = "Re-enter your current credentials. If two-factor authentication is enabled, enter a current authenticator code. Then type DELETE exactly.",
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        new Label
                        {
                            Text = "Current password",
                            FontAttributes = FontAttributes.Bold
                        },
                        passwordEntry,
                        new Label
                        {
                            Text = "Authenticator code (optional unless 2FA is enabled)",
                            FontAttributes = FontAttributes.Bold
                        },
                        twoFactorEntry,
                        new Label
                        {
                            Text = "Type DELETE to confirm",
                            FontAttributes = FontAttributes.Bold
                        },
                        confirmationEntry,
                        new Label
                        {
                            Text = "This cannot be undone.",
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.IndianRed
                        },
                        deleteButton,
                        cancelButton
                    }
                }
            }
        };

        var completed = false;

        async Task CloseAsync(
            DeletionCredentials? result)
        {
            if (completed)
            {
                return;
            }

            completed = true;

            var password = passwordEntry.Text;
            var twoFactor = twoFactorEntry.Text;

            passwordEntry.Text = string.Empty;
            twoFactorEntry.Text = string.Empty;
            confirmationEntry.Text = string.Empty;

            await Navigation.PopModalAsync();

            completion.TrySetResult(
                result is null
                    ? null
                    : new DeletionCredentials(
                        password ?? string.Empty,
                        twoFactor));
        }

        deleteButton.Clicked += async (_, _) =>
        {
            if (!deleteButton.IsEnabled)
            {
                return;
            }

            await CloseAsync(
                new DeletionCredentials(
                    passwordEntry.Text ?? string.Empty,
                    twoFactorEntry.Text));
        };

        cancelButton.Clicked += async (_, _) =>
            await CloseAsync(null);

        modal.Disappearing += (_, _) =>
        {
            if (!completed)
            {
                passwordEntry.Text = string.Empty;
                twoFactorEntry.Text = string.Empty;
                confirmationEntry.Text = string.Empty;
                completion.TrySetResult(null);
                completed = true;
            }
        };

        await Navigation.PushModalAsync(modal);

        return await completion.Task;
    }

    private static Label CreateTitleLabel(
        string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 21,
            FontAttributes = FontAttributes.Bold
        };

        label.SetDynamicResource(
            StyleProperty,
            "PrimaryTextStyle");

        return label;
    }

    private static Label CreateBodyLabel(
        string text)
    {
        var label = new Label
        {
            Text = text,
            LineBreakMode = LineBreakMode.WordWrap
        };

        label.SetDynamicResource(
            StyleProperty,
            "BodyTextStyle");

        return label;
    }

    private static Button CreatePrimaryButton(
        string text)
    {
        var button = new Button
        {
            Text = text,
            HorizontalOptions = LayoutOptions.Start,
            CornerRadius = 14,
            Padding = new Thickness(18, 10),
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

        button.SetDynamicResource(
            BackgroundColorProperty,
            "BrandPrimary");

        return button;
    }

    private static Border CreateCard(
        View content)
    {
        var card = new Border
        {
            Padding = new Thickness(22),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(22)
            },
            Content = content
        };

        card.SetDynamicResource(
            BackgroundColorProperty,
            "CardBackground");

        card.SetDynamicResource(
            Border.StrokeProperty,
            "CardBorder");

        return card;
    }

    private sealed record DeletionCredentials(
        string CurrentPassword,
        string? TwoFactorCode);
}
