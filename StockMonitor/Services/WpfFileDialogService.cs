using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Syncfusion.SfSkinManager;

namespace StockMonitor.Services;

/// <summary>
/// WPF implementation of <see cref="IFileDialogService"/>.
/// </summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public string? ShowOpenPortfolioFileDialog(string initialDirectory)
    {
        LocalizationService loc = LocalizationService.Instance;
        OpenFileDialog dlg = new()
        {
            Title = loc.Get("Dlg_OpenPortfolioTitle"),
            Filter = "StockMonitor Portfolio (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dlg.InitialDirectory = initialDirectory;
        }

        Window? owner = GetActiveOwnerWindow();
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public string? ShowSavePortfolioFileDialog(string initialDirectory, string defaultFileName)
    {
        LocalizationService loc = LocalizationService.Instance;
        SaveFileDialog dlg = new()
        {
            Title = loc.Get("Dlg_SavePortfolioTitle"),
            Filter = "StockMonitor Portfolio (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "portfolio.json" : defaultFileName
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dlg.InitialDirectory = initialDirectory;
        }

        Window? owner = GetActiveOwnerWindow();
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public UnsavedChangesChoice ShowUnsavedChangesPrompt(string documentName)
    {
        LocalizationService loc = LocalizationService.Instance;
        Window dialog = CreateThemedModalWindow(loc.Get("Dlg_UnsavedTitle"), width: 460);

        UnsavedChangesChoice choice = UnsavedChangesChoice.Cancel;

        StackPanel root = new() { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(loc.Get("Dlg_UnsavedMessage"), documentName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button saveBtn = new()
        {
            Content = loc.Get("Btn_Save"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        saveBtn.Click += (_, _) =>
        {
            choice = UnsavedChangesChoice.Save;
            dialog.DialogResult = true;
            dialog.Close();
        };

        Button discardBtn = new()
        {
            Content = loc.Get("Btn_DontSave"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        discardBtn.Click += (_, _) =>
        {
            choice = UnsavedChangesChoice.Discard;
            dialog.DialogResult = true;
            dialog.Close();
        };

        Button cancelBtn = new()
        {
            Content = loc.Get("Btn_Cancel"),
            Padding = new Thickness(14, 6, 14, 6),
            IsCancel = true
        };

        buttons.Children.Add(saveBtn);
        buttons.Children.Add(discardBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
        return choice;
    }

    /// <inheritdoc />
    public string? ShowEnterPasswordDialog(string fileName, bool isRetry = false)
    {
        LocalizationService loc = LocalizationService.Instance;
        Window dialog = CreateThemedModalWindow(loc.Get("Dlg_EnterPasswordTitle"), width: 420);

        StackPanel root = new() { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(loc.Get("Dlg_EnterPasswordPrompt"), fileName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        TextBlock errorText = new()
        {
            Text = isRetry ? loc.Get("Dlg_InvalidPasswordError") : string.Empty,
            Visibility = isRetry ? Visibility.Visible : Visibility.Collapsed,
            Foreground = System.Windows.Media.Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(errorText);

        PasswordBox passwordBox = new()
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        TextBox visiblePasswordBox = new()
        {
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(passwordBox);
        root.Children.Add(visiblePasswordBox);

        CheckBox showPasswordCheckBox = new()
        {
            Content = loc.Get("Dlg_ShowPassword"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        showPasswordCheckBox.Checked += (_, _) =>
        {
            visiblePasswordBox.Text = passwordBox.Password;
            passwordBox.Visibility = Visibility.Collapsed;
            visiblePasswordBox.Visibility = Visibility.Visible;
            visiblePasswordBox.Focus();
            visiblePasswordBox.CaretIndex = visiblePasswordBox.Text.Length;
        };
        showPasswordCheckBox.Unchecked += (_, _) =>
        {
            passwordBox.Password = visiblePasswordBox.Text;
            visiblePasswordBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            passwordBox.Focus();
        };
        root.Children.Add(showPasswordCheckBox);

        string GetEnteredPassword() => showPasswordCheckBox.IsChecked == true
            ? visiblePasswordBox.Text
            : passwordBox.Password;

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button okBtn = new()
        {
            Content = loc.Get("Btn_Ok"),
            Width = 90,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(GetEnteredPassword()))
            {
                errorText.Text = loc.Get("Dlg_PasswordEmptyError");
                errorText.Visibility = Visibility.Visible;
                if (showPasswordCheckBox.IsChecked == true)
                {
                    visiblePasswordBox.Focus();
                }
                else
                {
                    passwordBox.Focus();
                }

                return;
            }

            dialog.DialogResult = true;
            dialog.Close();
        };

        Button cancelBtn = new()
        {
            Content = loc.Get("Btn_Cancel"),
            Width = 90,
            Padding = new Thickness(12, 5, 12, 5),
            IsCancel = true
        };

        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Loaded += (_, _) => passwordBox.Focus();

        return dialog.ShowDialog() == true ? GetEnteredPassword() : null;
    }

    /// <inheritdoc />
    public PasswordChangeResult ShowManagePasswordDialog(bool hasExistingPassword, Func<string, bool>? verifyCurrentPassword = null)
    {
        LocalizationService loc = LocalizationService.Instance;
        Window dialog = CreateThemedModalWindow(loc.Get("Dlg_ManagePasswordTitle"), width: 450);

        PasswordChangeResult result = new(Confirmed: false, NewPassword: null);

        StackPanel root = new() { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = hasExistingPassword
                ? loc.Get("Dlg_ManagePasswordHintProtected")
                : loc.Get("Dlg_ManagePasswordHintUnprotected"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        PasswordBox? currentPasswordBox = null;
        TextBox? visibleCurrentPasswordBox = null;
        if (hasExistingPassword)
        {
            root.Children.Add(new TextBlock
            {
                Text = loc.Get("Dlg_CurrentPasswordLabel"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            currentPasswordBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
            visibleCurrentPasswordBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(currentPasswordBox);
            root.Children.Add(visibleCurrentPasswordBox);
        }

        root.Children.Add(new TextBlock
        {
            Text = loc.Get("Dlg_NewPasswordLabel"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        PasswordBox passwordBox = new() { Margin = new Thickness(0, 0, 0, 8) };
        TextBox visiblePasswordBox = new()
        {
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(passwordBox);
        root.Children.Add(visiblePasswordBox);

        CheckBox showPasswordCheckBox = new()
        {
            Content = loc.Get("Dlg_ShowPassword"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(showPasswordCheckBox);

        TextBlock confirmLabel = new()
        {
            Text = loc.Get("Dlg_ConfirmPasswordLabel"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        PasswordBox confirmBox = new() { Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(confirmLabel);
        root.Children.Add(confirmBox);

        showPasswordCheckBox.Checked += (_, _) =>
        {
            if (currentPasswordBox != null && visibleCurrentPasswordBox != null)
            {
                visibleCurrentPasswordBox.Text = currentPasswordBox.Password;
                currentPasswordBox.Visibility = Visibility.Collapsed;
                visibleCurrentPasswordBox.Visibility = Visibility.Visible;
            }

            visiblePasswordBox.Text = passwordBox.Password;
            passwordBox.Visibility = Visibility.Collapsed;
            visiblePasswordBox.Visibility = Visibility.Visible;
            confirmLabel.Visibility = Visibility.Collapsed;
            confirmBox.Visibility = Visibility.Collapsed;
            confirmBox.Clear();
            visiblePasswordBox.Focus();
            visiblePasswordBox.CaretIndex = visiblePasswordBox.Text.Length;
        };
        showPasswordCheckBox.Unchecked += (_, _) =>
        {
            if (currentPasswordBox != null && visibleCurrentPasswordBox != null)
            {
                currentPasswordBox.Password = visibleCurrentPasswordBox.Text;
                visibleCurrentPasswordBox.Visibility = Visibility.Collapsed;
                currentPasswordBox.Visibility = Visibility.Visible;
            }

            passwordBox.Password = visiblePasswordBox.Text;
            visiblePasswordBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            confirmLabel.Visibility = Visibility.Visible;
            confirmBox.Visibility = Visibility.Visible;
            passwordBox.Focus();
        };

        TextBlock errorText = new()
        {
            Foreground = System.Windows.Media.Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(errorText);

        bool ValidateCurrentPassword()
        {
            if (!hasExistingPassword)
            {
                return true;
            }

            bool showPassword = showPasswordCheckBox.IsChecked == true;
            string currentPwd = showPassword
                ? (visibleCurrentPasswordBox?.Text ?? string.Empty)
                : (currentPasswordBox?.Password ?? string.Empty);

            if (string.IsNullOrEmpty(currentPwd))
            {
                errorText.Text = loc.Get("Dlg_PasswordEmptyError");
                errorText.Visibility = Visibility.Visible;
                if (showPassword)
                {
                    visibleCurrentPasswordBox?.Focus();
                }
                else
                {
                    currentPasswordBox?.Focus();
                }

                return false;
            }

            if (verifyCurrentPassword != null && !verifyCurrentPassword(currentPwd))
            {
                errorText.Text = loc.Get("Dlg_CurrentPasswordInvalidError");
                errorText.Visibility = Visibility.Visible;
                if (showPassword)
                {
                    visibleCurrentPasswordBox?.SelectAll();
                    visibleCurrentPasswordBox?.Focus();
                }
                else
                {
                    currentPasswordBox?.SelectAll();
                    currentPasswordBox?.Focus();
                }

                return false;
            }

            return true;
        }

        DockPanel buttonBar = new() { LastChildFill = false };

        if (hasExistingPassword)
        {
            Button removeBtn = new()
            {
                Content = loc.Get("Btn_RemovePassword"),
                Padding = new Thickness(12, 5, 12, 5)
            };
            DockPanel.SetDock(removeBtn, Dock.Left);
            removeBtn.Click += (_, _) =>
            {
                if (!ValidateCurrentPassword())
                {
                    return;
                }

                result = new PasswordChangeResult(Confirmed: true, NewPassword: null);
                dialog.DialogResult = true;
                dialog.Close();
            };
            buttonBar.Children.Add(removeBtn);
        }

        StackPanel rightButtons = new() { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(rightButtons, Dock.Right);

        Button okBtn = new()
        {
            Content = loc.Get("Btn_Ok"),
            Width = 90,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) =>
        {
            if (!ValidateCurrentPassword())
            {
                return;
            }

            bool showPassword = showPasswordCheckBox.IsChecked == true;
            string pwd = showPassword ? visiblePasswordBox.Text : passwordBox.Password;

            if (string.IsNullOrEmpty(pwd))
            {
                errorText.Text = loc.Get("Dlg_PasswordEmptyError");
                errorText.Visibility = Visibility.Visible;
                if (showPassword)
                {
                    visiblePasswordBox.Focus();
                }
                else
                {
                    passwordBox.Focus();
                }

                return;
            }

            if (!showPassword && !string.Equals(pwd, confirmBox.Password, StringComparison.Ordinal))
            {
                errorText.Text = loc.Get("Dlg_PasswordMismatchError");
                errorText.Visibility = Visibility.Visible;
                confirmBox.Focus();
                return;
            }

            result = new PasswordChangeResult(Confirmed: true, NewPassword: pwd);
            dialog.DialogResult = true;
            dialog.Close();
        };

        Button cancelBtn = new()
        {
            Content = loc.Get("Btn_Cancel"),
            Width = 90,
            Padding = new Thickness(12, 5, 12, 5),
            IsCancel = true
        };

        rightButtons.Children.Add(okBtn);
        rightButtons.Children.Add(cancelBtn);
        buttonBar.Children.Add(rightButtons);

        root.Children.Add(buttonBar);
        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            if (currentPasswordBox != null)
            {
                currentPasswordBox.Focus();
            }
            else
            {
                passwordBox.Focus();
            }
        };

        dialog.ShowDialog();
        return result;
    }

    /// <inheritdoc />
    public ImportModeChoice ShowImportModeDialog(string fileName)
    {
        LocalizationService loc = LocalizationService.Instance;
        Window dialog = CreateThemedModalWindow(loc.Get("Dlg_ImportModeTitle"), width: 480);

        ImportModeChoice choice = ImportModeChoice.Cancel;

        StackPanel root = new() { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(loc.Get("Dlg_ImportModePrompt"), fileName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button replaceBtn = new()
        {
            Content = loc.Get("Btn_ImportReplace"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        replaceBtn.Click += (_, _) =>
        {
            choice = ImportModeChoice.ReplaceAll;
            dialog.DialogResult = true;
            dialog.Close();
        };

        Button appendBtn = new()
        {
            Content = loc.Get("Btn_ImportAppend"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        appendBtn.Click += (_, _) =>
        {
            choice = ImportModeChoice.Append;
            dialog.DialogResult = true;
            dialog.Close();
        };

        Button cancelBtn = new()
        {
            Content = loc.Get("Btn_Cancel"),
            Padding = new Thickness(12, 6, 12, 6),
            IsCancel = true
        };

        buttons.Children.Add(replaceBtn);
        buttons.Children.Add(appendBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
        return choice;
    }

    /// <inheritdoc />
    public string? ShowOpenXtbFileDialog()
    {
        OpenFileDialog dlg = new()
        {
            Title = "Import XTB Excel (.xlsx) or CSV Report",
            Filter = "XTB Excel & CSV Files (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|Excel Workbook (*.xlsx)|*.xlsx|CSV File (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        Window? owner = GetActiveOwnerWindow();
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public string? ShowSaveExportFileDialog(string defaultFileName)
    {
        SaveFileDialog dlg = new()
        {
            Title = "Export Portfolio Report (.xlsx or .csv)",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV File (*.csv)|*.csv",
            FileName = defaultFileName
        };

        Window? owner = GetActiveOwnerWindow();
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message)
    {
        Window? owner = GetActiveOwnerWindow();
        if (owner != null)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <inheritdoc />
    public void OpenUrlInExternalBrowser(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static Window? GetActiveOwnerWindow()
    {
        Window? main = Application.Current?.MainWindow;
        return main is { IsVisible: true } ? main : null;
    }

    private static Window CreateThemedModalWindow(string title, double width)
    {
        Window? owner = GetActiveOwnerWindow();
        Window dialog = new()
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = owner
        };

        if (owner != null)
        {
            try
            {
                Theme? activeTheme = SfSkinManager.GetTheme(owner);
                if (activeTheme != null)
                {
                    SfSkinManager.SetTheme(dialog, activeTheme);
                }
            }
            catch
            {
                // Ignore if theme cannot be copied
            }
        }

        return dialog;
    }
}
