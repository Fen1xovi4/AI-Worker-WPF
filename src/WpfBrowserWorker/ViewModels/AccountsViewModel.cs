using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfBrowserWorker.Data.Entities;
using WpfBrowserWorker.Services;
using Telegram.Bot;
using WpfBrowserWorker.Helpers;

namespace WpfBrowserWorker.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ProfileService          _profileService;
    private readonly TelegramBotService      _botService;
    private readonly TelegramListenerService _telegramListener;

    public ObservableCollection<StoredAccount>  Accounts { get; } = new();
    public ObservableCollection<BrowserProfile> Profiles { get; } = new();
    public ObservableCollection<ProfilePage>    Pages    { get; } = new();

    // ── Create account ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string _newAccountUsername = string.Empty;

    [ObservableProperty] private BrowserProfile? _newAccountProfile;
    [ObservableProperty] private string _newAccountNotes = string.Empty;

    // ── Selection ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    private StoredAccount? _selectedAccount;

    [ObservableProperty] private ProfilePage? _selectedPage;

    // ── Add page ───────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    private string _newPageUrl = string.Empty;

    [ObservableProperty] private string _newPageLabel    = string.Empty;
    [ObservableProperty] private string _newPageLanguage = "ru";

    // ── Telegram bot (for selected account) ───────────────────────────────
    [ObservableProperty] private string _newBotToken   = string.Empty;
    [ObservableProperty] private string _botUsername   = string.Empty;
    [ObservableProperty] private string _botStatus     = "Не настроен";
    [ObservableProperty] private bool   _botIsRunning;
    [ObservableProperty] private bool   _botConfigured;

    // ── Status ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusMessage = string.Empty;

    public AccountsViewModel(
        ProfileService          profileService,
        TelegramBotService      botService,
        TelegramListenerService telegramListener)
    {
        _profileService   = profileService;
        _botService       = botService;
        _telegramListener = telegramListener;
    }

    public async Task LoadAsync()
    {
        var accounts = await _profileService.GetAllAccountsAsync();
        Accounts.Clear();
        foreach (var a in accounts)
            Accounts.Add(a);

        var profiles = await _profileService.GetAllAsync();
        Profiles.Clear();
        foreach (var p in profiles)
            Profiles.Add(p);
    }

    partial void OnSelectedAccountChanged(StoredAccount? value)
    {
        _ = LoadPagesAsync(value);
        _ = LoadBotStatusAsync(value);
    }

    private async Task LoadPagesAsync(StoredAccount? account)
    {
        Pages.Clear();
        if (account is null) return;
        var items = await _profileService.GetPagesForAccountAsync(account.Id);
        foreach (var p in items)
            Pages.Add(p);
    }

    // ── Commands ───────────────────────────────────────────────────────────

    private bool CanCreateAccount() => !string.IsNullOrWhiteSpace(NewAccountUsername);

    [RelayCommand(CanExecute = nameof(CanCreateAccount))]
    private async Task CreateAccountAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            var account = await _profileService.CreateAccountAsync(
                NewAccountUsername.Trim(),
                NewAccountProfile?.Id,
                string.IsNullOrWhiteSpace(NewAccountNotes) ? null : NewAccountNotes.Trim());

            Accounts.Add(account);
            NewAccountUsername = string.Empty;
            NewAccountNotes    = string.Empty;
            NewAccountProfile  = null;
            StatusMessage = $"Account '{account.Username}' created (id={account.Id})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(StoredAccount? account)
    {
        if (account is null) return;

        var result = MessageBox.Show(
            $"Delete account \"{account.Username}\"?\nAll linked pages will also be removed.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _profileService.DeleteAccountAsync(account.Id);
        Accounts.Remove(account);
        if (SelectedAccount == account) SelectedAccount = null;
        StatusMessage = $"Account '{account.Username}' deleted.";
    }

    private bool CanAddPage() =>
        SelectedAccount is not null && !string.IsNullOrWhiteSpace(NewPageUrl);

    [RelayCommand(CanExecute = nameof(CanAddPage))]
    private async Task AddPageAsync()
    {
        if (SelectedAccount is null) return;
        try
        {
            var url = NewPageUrl.Trim();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            var page = await _profileService.AddPageAsync(
                SelectedAccount.Id, url,
                string.IsNullOrWhiteSpace(NewPageLabel) ? null : NewPageLabel.Trim(),
                NewPageLanguage);

            Pages.Add(page);
            NewPageUrl      = string.Empty;
            NewPageLabel    = string.Empty;
            NewPageLanguage = "ru";
            StatusMessage = $"Page added: [{page.Platform}] {page.Url}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    // ── Telegram bot commands ──────────────────────────────────────────────

    private async Task LoadBotStatusAsync(StoredAccount? account)
    {
        if (account is null)
        {
            BotConfigured = false;
            BotStatus     = "Не настроен";
            BotUsername   = string.Empty;
            BotIsRunning  = false;
            NewBotToken   = string.Empty;
            return;
        }

        var cfg = await _botService.GetByAccountAsync(account.Id);
        BotConfigured = cfg is not null;
        BotIsRunning  = cfg is not null && _telegramListener.IsRunning(account.Id);
        BotUsername   = cfg?.BotUsername ?? string.Empty;
        BotStatus     = BotIsRunning ? "Работает" : (BotConfigured ? "Остановлен" : "Не настроен");
        NewBotToken   = cfg?.BotToken.MaskToken() ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveBotAsync()
    {
        if (SelectedAccount is null) return;
        var token = NewBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token)) return;

        StatusMessage = "Подключаем бота…";
        try
        {
            // Verify token and get username before saving
            var tempClient = new Telegram.Bot.TelegramBotClient(token);
            var me = await tempClient.GetMe();
            var username = $"@{me.Username}";

            await _botService.SaveAsync(SelectedAccount.Id, token, username);

            // Start bot immediately
            var cfg = await _botService.GetByAccountAsync(SelectedAccount.Id);
            await _telegramListener.StartBotAsync(cfg!);

            BotUsername  = username;
            BotIsRunning = true;
            BotConfigured = true;
            BotStatus    = "Работает";
            StatusMessage = $"Бот {username} подключён и запущен.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveBotAsync()
    {
        if (SelectedAccount is null) return;
        var result = MessageBox.Show(
            "Удалить Telegram-бот для этого аккаунта?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _telegramListener.StopBot(SelectedAccount.Id);
        await _botService.RemoveAsync(SelectedAccount.Id);
        BotConfigured = false;
        BotIsRunning  = false;
        BotUsername   = string.Empty;
        BotStatus     = "Не настроен";
        NewBotToken   = string.Empty;
        StatusMessage = "Бот удалён.";
    }

    [RelayCommand]
    private async Task ToggleBotAsync()
    {
        if (SelectedAccount is null) return;
        var cfg = await _botService.GetByAccountAsync(SelectedAccount.Id);
        if (cfg is null) return;

        if (_telegramListener.IsRunning(SelectedAccount.Id))
        {
            _telegramListener.StopBot(SelectedAccount.Id);
            await _botService.SetActiveAsync(SelectedAccount.Id, false);
            BotIsRunning = false;
            BotStatus    = "Остановлен";
            StatusMessage = "Бот остановлен.";
        }
        else
        {
            await _botService.SetActiveAsync(SelectedAccount.Id, true);
            await _telegramListener.StartBotAsync(cfg);
            BotIsRunning = true;
            BotStatus    = "Работает";
            StatusMessage = $"Бот {BotUsername} запущен.";
        }
    }

    [RelayCommand]
    private async Task RemovePageAsync(ProfilePage? page)
    {
        if (page is null) return;

        var result = MessageBox.Show(
            $"Remove page \"{page.Url}\"?",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _profileService.RemovePageAsync(page.Id);
        Pages.Remove(page);
        if (SelectedPage == page) SelectedPage = null;
        StatusMessage = $"Page removed: {page.Url}";
    }
}
