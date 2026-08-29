using BillWatch.Services;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public enum LoginPageDestination
{
    None = 0,
    Home = 1,
    ConnectBank = 2
}

public sealed class LoginPageViewModel : INotifyPropertyChanged
{
    private readonly AuthenticationService _authenticationService;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isCreateAccount;

    public LoginPageViewModel(AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email == value) return;
            _email = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (_confirmPassword == value) return;
            _confirmPassword = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool IsCreateAccount
    {
        get => _isCreateAccount;
        private set
        {
            if (_isCreateAccount == value) return;
            _isCreateAccount = value;
            NotifyModeChanged();
        }
    }

    public string CardTitle => IsCreateAccount ? "Create your account" : "Welcome back";
    public string CardSubtitle => IsCreateAccount
        ? "Start monitoring recurring bills and the changes that cost you money."
        : "Sign in to continue monitoring your bills.";
    public string PrimaryActionText => IsCreateAccount ? "Create account" : "Sign in";
    public string TogglePromptText => IsCreateAccount ? "Already have an account?" : "New to BillWatch?";
    public string ToggleActionText => IsCreateAccount ? "Sign in" : "Create account";
    public string PasswordHelpText => "12+ characters · uppercase · lowercase · number · symbol";

    public void ToggleMode()
    {
        if (IsBusy) return;
        IsCreateAccount = !IsCreateAccount;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        ErrorMessage = string.Empty;
    }

    public async Task<bool> TryResumeSessionAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;

        try
        {
            IsBusy = true;
            return await _authenticationService.IsAuthenticatedAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<LoginPageDestination> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return LoginPageDestination.None;

        ErrorMessage = string.Empty;
        var email = Email.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter your email address and password.";
            return LoginPageDestination.None;
        }

        if (!email.Contains('@', StringComparison.Ordinal))
        {
            ErrorMessage = "Enter a valid email address.";
            return LoginPageDestination.None;
        }

        if (IsCreateAccount && !ValidateNewPassword())
        {
            return LoginPageDestination.None;
        }

        var accountWasCreated = false;

        try
        {
            IsBusy = true;

            if (IsCreateAccount)
            {
                await _authenticationService.RegisterAsync(email, Password, cancellationToken);
                accountWasCreated = true;
                await _authenticationService.LoginAsync(email, Password, cancellationToken);
                ClearPasswords();
                return LoginPageDestination.ConnectBank;
            }

            await _authenticationService.LoginAsync(email, Password, cancellationToken);
            ClearPasswords();
            return LoginPageDestination.Home;
        }
        catch (AccountRegistrationException exception)
        {
            ErrorMessage = exception.Message;
            return LoginPageDestination.None;
        }
        catch (HttpRequestException exception)
            when (accountWasCreated && exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            IsCreateAccount = false;
            ClearPasswords();
            ErrorMessage = "Your account was created. Sign in with your new password to continue.";
            return LoginPageDestination.None;
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            ErrorMessage = "The email address or password is incorrect.";
            return LoginPageDestination.None;
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            ErrorMessage = "Too many attempts. Wait a moment and try again.";
            return LoginPageDestination.None;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "BillWatch could not reach the server. Check your connection and try again.";
            return LoginPageDestination.None;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LoginPageDestination.None;
        }
        catch
        {
            ErrorMessage = IsCreateAccount
                ? "BillWatch could not create your account. Please try again."
                : "BillWatch could not sign you in. Please try again.";
            return LoginPageDestination.None;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateNewPassword()
    {
        if (Password.Length < 12)
        {
            ErrorMessage = "Your password must be at least 12 characters.";
            return false;
        }

        if (!Password.Any(char.IsUpper))
        {
            ErrorMessage = "Your password needs at least one uppercase letter.";
            return false;
        }

        if (!Password.Any(char.IsLower))
        {
            ErrorMessage = "Your password needs at least one lowercase letter.";
            return false;
        }

        if (!Password.Any(char.IsDigit))
        {
            ErrorMessage = "Your password needs at least one number.";
            return false;
        }

        if (!Password.Any(character => !char.IsLetterOrDigit(character)))
        {
            ErrorMessage = "Your password needs at least one symbol.";
            return false;
        }

        if (Password.Distinct().Count() < 4)
        {
            ErrorMessage = "Use at least four different characters in your password.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ErrorMessage = "Confirm your password.";
            return false;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The passwords do not match.";
            return false;
        }

        return true;
    }

    private void ClearPasswords()
    {
        Password = string.Empty;
        ConfirmPassword = string.Empty;
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsCreateAccount));
        OnPropertyChanged(nameof(CardTitle));
        OnPropertyChanged(nameof(CardSubtitle));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(TogglePromptText));
        OnPropertyChanged(nameof(ToggleActionText));
        OnPropertyChanged(nameof(PasswordHelpText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
