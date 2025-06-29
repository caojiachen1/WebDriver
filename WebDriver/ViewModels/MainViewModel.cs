using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WebDriver.Services;

namespace WebDriver.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly WebDavClient _webDavClient;

    [ObservableProperty]
    private string _serverUrl = "https://";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private WebDavItem? _selectedItem;

    public ObservableCollection<WebDavItem> Items { get; } = new();
    public ObservableCollection<string> PathHistory { get; } = new();

    public MainViewModel()
    {
        _webDavClient = new WebDavClient();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Please enter server URL and username";
            return;
        }

        IsLoading = true;
        StatusMessage = "Connecting...";

        try
        {
            _webDavClient.SetCredentials(ServerUrl, Username, Password);
            var isConnected = await _webDavClient.TestConnectionAsync();

            if (isConnected)
            {
                IsConnected = true;
                StatusMessage = "Connected successfully";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "Failed to connect. Please check your credentials.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        IsConnected = false;
        Items.Clear();
        PathHistory.Clear();
        CurrentPath = "/";
        StatusMessage = "Disconnected";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConnected) return;

        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            var items = await _webDavClient.ListDirectoryAsync(CurrentPath);
            Items.Clear();
            
            // Add parent directory if not at root
            if (CurrentPath != "/")
            {
                Items.Add(new WebDavItem
                {
                    Name = "..",
                    FullPath = GetParentPath(CurrentPath),
                    IsDirectory = true
                });
            }

            foreach (var item in items)
            {
                Items.Add(item);
            }

            StatusMessage = $"Loaded {items.Count} items";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading directory: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToItemAsync()
    {
        if (SelectedItem == null || !SelectedItem.IsDirectory) return;

        if (SelectedItem.Name == "..")
        {
            CurrentPath = SelectedItem.FullPath;
        }
        else
        {
            PathHistory.Add(CurrentPath);
            CurrentPath = SelectedItem.FullPath;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (PathHistory.Count > 0)
        {
            CurrentPath = PathHistory[^1];
            PathHistory.RemoveAt(PathHistory.Count - 1);
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task GoHomeAsync()
    {
        CurrentPath = "/";
        PathHistory.Clear();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CreateFolderAsync(string? folderName)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(folderName)) return;

        IsLoading = true;
        StatusMessage = "Creating folder...";

        try
        {
            var newFolderPath = $"{CurrentPath.TrimEnd('/')}/{folderName}";
            await _webDavClient.CreateDirectoryAsync(newFolderPath);
            StatusMessage = "Folder created successfully";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteItemAsync()
    {
        if (!IsConnected || SelectedItem == null || SelectedItem.Name == "..") return;

        IsLoading = true;
        StatusMessage = "Deleting...";

        try
        {
            await _webDavClient.DeleteAsync(SelectedItem.FullPath);
            StatusMessage = "Item deleted successfully";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting item: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync()
    {
        if (!IsConnected || SelectedItem == null || SelectedItem.IsDirectory) return;

        IsLoading = true;
        StatusMessage = "Downloading...";

        try
        {
            using var stream = await _webDavClient.DownloadFileAsync(SelectedItem.FullPath);
            
            // For demo purposes, we'll save to Downloads folder
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var localFilePath = Path.Combine(downloadsPath, SelectedItem.Name);
            
            using var fileStream = File.Create(localFilePath);
            await stream.CopyToAsync(fileStream);
            
            StatusMessage = $"Downloaded to {localFilePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error downloading file: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UploadFileAsync(string? filePath)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        IsLoading = true;
        StatusMessage = "Uploading...";

        try
        {
            var fileName = Path.GetFileName(filePath);
            var remotePath = $"{CurrentPath.TrimEnd('/')}/{fileName}";
            
            using var fileStream = File.OpenRead(filePath);
            await _webDavClient.UploadFileAsync(remotePath, fileStream);
            
            StatusMessage = "File uploaded successfully";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error uploading file: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetParentPath(string path)
    {
        if (path == "/") return "/";
        
        var parts = path.Trim('/').Split('/');
        if (parts.Length <= 1) return "/";
        
        return "/" + string.Join("/", parts[..^1]);
    }

    public override void Dispose()
    {
        _webDavClient?.Dispose();
        base.Dispose();
    }
}
