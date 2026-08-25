using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public sealed class LoginPageViewModel : INotifyPropertyChanged
{
    private readonly AuthenticationService _authenticationService;

    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginPageViewModel(
        AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email == value)
            {
                return;
            }

            _email = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value)
            {
                return;
            }

            _password = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public async Task<bool> LoginAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage =
                "Enter your email address and password.";

            return false;
        }

        try
        {
            IsBusy = true;

            await _authenticationService.LoginAsync(
                Email.Trim(),
                Password,
                cancellationToken);

            Password = string.Empty;

            return true;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}