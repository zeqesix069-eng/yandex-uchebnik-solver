using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace YandexSolver
{
    public partial class MainWindow : Window
    {
        private const int HotkeyId = 9001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint VkH = 0x48;
        private const int WmHotkey = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource _hwndSource;
        private static readonly Brush NeutralDot = CreateBrush("#71717A");
        private static readonly Brush LoadingDot = CreateBrush("#F59E0B");
        private static readonly Brush SuccessDot = CreateBrush("#22C55E");
        private static readonly Brush ErrorDot = CreateBrush("#EF4444");

        private readonly CdpClient _cdp = new CdpClient();
        private AppSettings _settings;
        private CancellationTokenSource _runCts;
        private bool _isKeyVisible;
        private bool _suppressUiEvents;

        public MainWindow()
        {
            InitializeComponent();
            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
            StateChanged += MainWindow_StateChanged;
            SourceInitialized += MainWindow_SourceInitialized;
            ApplySettingsToUi(AppSettings.Load());
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(handle);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
                UpdateBossHotkey();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                if (IsVisible)
                {
                    Hide();
                }
                else
                {
                    Show();
                    Activate();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void UpdateBossHotkey()
        {
            if (_hwndSource == null)
            {
                return;
            }
            IntPtr handle = _hwndSource.Handle;
            UnregisterHotKey(handle, HotkeyId);
            if (_settings != null && _settings.BossHotkeyEnabled)
            {
                RegisterHotKey(handle, HotkeyId, ModControl | ModShift, VkH);
            }
        }

        private static Brush CreateBrush(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        private static void SelectComboByContent(ComboBox combo, string value)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (string.Equals(Convert.ToString(item.Content), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static string GetSelectedComboText(ComboBox combo, string fallback)
        {
            var selected = combo.SelectedItem as ComboBoxItem;
            return selected != null ? Convert.ToString(selected.Content) : fallback;
        }

        private void ApplySettingsToUi(AppSettings loaded)
        {
            _settings = loaded;
            _suppressUiEvents = true;
            SelectComboByContent(ProviderCombo, _settings.Provider);
            SelectComboByContent(ThemeCombo, _settings.Theme);
            SelectComboByContent(AccentCombo, _settings.Accent);
            SelectComboByContent(CornerCombo, _settings.CornerStyle);
            OpacitySlider.Value = _settings.WindowOpacity;
            OpacityValueText.Text = Math.Round(_settings.WindowOpacity * 100) + "%";
            _suppressUiEvents = false;

            BaseUrlBox.Text = _settings.BaseUrl;
            ModelBox.Text = _settings.Model;
            ApiKeyPasswordBox.Password = _settings.ApiKey;
            ApiKeyPlainBox.Text = _settings.ApiKey;
            TempSlider.Value = _settings.Temperature;
            TempValueText.Text = _settings.Temperature.ToString("0.00", CultureInfo.InvariantCulture);
            SystemPromptBox.Text = _settings.CustomSystemPrompt;

            CdpPortBox.Text = _settings.CdpPort.ToString(CultureInfo.InvariantCulture);
            SubmitDelayBox.Text = _settings.SubmitDelayMs.ToString(CultureInfo.InvariantCulture);
            StepDelayBox.Text = _settings.StepDelayMs.ToString(CultureInfo.InvariantCulture);
            MaxStepsBox.Text = _settings.MaxSteps.ToString(CultureInfo.InvariantCulture);
            AutoAdvanceCheck.IsChecked = _settings.AutoAdvance;
            SkipTheoryCheck.IsChecked = _settings.SkipTheory;
            RetryOnWrongCheck.IsChecked = _settings.RetryOnWrong;
            PortableModeCheck.IsChecked = _settings.PortableMode;
            BossHotkeyCheck.IsChecked = _settings.BossHotkeyEnabled;
            SchoolCodeBox.Text = _settings.SchoolCode;
            StudentLoginBox.Text = _settings.StudentLogin;

            ThemeManager.Apply(this, _settings.Theme, _settings.Accent, _settings.CornerStyle, _settings.WindowOpacity);
            UpdateBossHotkey();
        }

        private void ReadSettingsFromUi()
        {
            _settings.Provider = GetSelectedComboText(ProviderCombo, "OpenRouter");
            _settings.Theme = GetSelectedComboText(ThemeCombo, "Dark (Slate)");
            _settings.Accent = GetSelectedComboText(AccentCombo, "Синий");
            _settings.CornerStyle = GetSelectedComboText(CornerCombo, "Стандарт");
            _settings.WindowOpacity = Math.Round(OpacitySlider.Value, 2);

            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.ApiKey = _isKeyVisible ? ApiKeyPlainBox.Text.Trim() : ApiKeyPasswordBox.Password.Trim();
            _settings.Temperature = Math.Round(TempSlider.Value, 2);
            _settings.CustomSystemPrompt = SystemPromptBox.Text.Trim();

            _settings.CdpPort = ParseClampedInt(CdpPortBox.Text, 9222, 1, 65535);
            _settings.SubmitDelayMs = ParseClampedInt(SubmitDelayBox.Text, 700, 0, 30000);
            _settings.StepDelayMs = ParseClampedInt(StepDelayBox.Text, 1500, 200, 30000);
            _settings.MaxSteps = ParseClampedInt(MaxStepsBox.Text, 30, 1, 500);
            _settings.AutoAdvance = AutoAdvanceCheck.IsChecked == true;
            _settings.SkipTheory = SkipTheoryCheck.IsChecked == true;
            _settings.RetryOnWrong = RetryOnWrongCheck.IsChecked == true;
            _settings.PortableMode = PortableModeCheck.IsChecked == true;
            _settings.BossHotkeyEnabled = BossHotkeyCheck.IsChecked == true;
            _settings.SchoolCode = SchoolCodeBox.Text.Trim();
            _settings.StudentLogin = StudentLoginBox.Text.Trim();
        }

        private static int ParseClampedInt(string text, int fallback, int min, int max)
        {
            int parsed;
            if (!int.TryParse((text ?? string.Empty).Trim(), out parsed))
            {
                return fallback;
            }
            if (parsed < min) return min;
            if (parsed > max) return max;
            return parsed;
        }

        private void Shell_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged) return;
            double width = e.NewSize.Width;
            bool compactRail = width < 970;
            RailColumn.Width = new GridLength(compactRail ? 74 : 202);
            Resources["NavLabelVisibility"] = compactRail ? Visibility.Collapsed : Visibility.Visible;

            bool wrapCommands = width < 910;
            Grid.SetColumn(RunCommands, wrapCommands ? 0 : 1);
            Grid.SetColumnSpan(RunCommands, wrapCommands ? 2 : 1);
            Grid.SetRow(RunCommands, wrapCommands ? 1 : 0);
            RunCommands.Margin = wrapCommands ? new Thickness(0, 10, 0, 0) : new Thickness(12, 4, 0, 0);

            bool stacked = width < 860;
            WorkGutter.Width = new GridLength(stacked ? 0 : 14);
            LogColumn.Width = new GridLength(stacked ? 0 : 1, GridUnitType.Star);
            Grid.SetColumn(JournalCard, stacked ? 0 : 2);
            Grid.SetRow(JournalCard, stacked ? 1 : 0);
            JournalCard.Height = stacked ? 196 : double.NaN;
            JournalCard.Margin = new Thickness(0, stacked ? 14 : 0, 0, 0);
            WorkCanvas.MinHeight = stacked ? 759 : 510;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            ShellFrame.Padding = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
            MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
            string label = maximized ? "Восстановить размер" : "Развернуть";
            MaximizeButton.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, label);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_runCts != null)
            {
                _runCts.Cancel();
            }
            if (_hwndSource != null)
            {
                UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            }
            _cdp.Dispose();
            base.OnClosed(e);
        }

        private void NavTab_Changed(object sender, RoutedEventArgs e)
        {
            if (WorkPanel == null || DatalessonPanel == null || InformaticsPanel == null || SettingsPanel == null)
            {
                return;
            }
            WorkPanel.Visibility = WorkTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DatalessonPanel.Visibility = DatalessonTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            InformaticsPanel.Visibility = InformaticsTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = SettingsTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressUiEvents || ProviderCombo.SelectedItem == null)
            {
                return;
            }
            string provider = Convert.ToString(((ComboBoxItem)ProviderCombo.SelectedItem).Content);
            KeyValuePair<string, string> defaults = AppSettings.GetDefaultsForProvider(provider);
            if (!string.IsNullOrEmpty(defaults.Key))
            {
                BaseUrlBox.Text = defaults.Key;
            }
            if (!string.IsNullOrEmpty(defaults.Value))
            {
                ModelBox.Text = defaults.Value;
            }
        }

        private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressUiEvents || ThemeCombo == null || AccentCombo == null || CornerCombo == null)
            {
                return;
            }
            ThemeManager.Apply(
                this,
                GetSelectedComboText(ThemeCombo, "Dark (Slate)"),
                GetSelectedComboText(AccentCombo, "Синий"),
                GetSelectedComboText(CornerCombo, "Стандарт"),
                OpacitySlider != null ? OpacitySlider.Value : 1.0);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUiEvents || OpacityValueText == null)
            {
                return;
            }
            OpacityValueText.Text = Math.Round(e.NewValue * 100) + "%";
            ThemeManager.Apply(
                this,
                GetSelectedComboText(ThemeCombo, "Dark (Slate)"),
                GetSelectedComboText(AccentCombo, "Синий"),
                GetSelectedComboText(CornerCombo, "Стандарт"),
                e.NewValue);
        }

        private void TempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TempValueText != null)
            {
                TempValueText.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        private void ToggleKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isKeyVisible = !_isKeyVisible;
            var btn = sender as Button;
            if (_isKeyVisible)
            {
                ApiKeyPlainBox.Text = ApiKeyPasswordBox.Password;
                ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                ApiKeyPlainBox.Visibility = Visibility.Visible;
                if (btn != null) btn.Content = "Скрыть";
            }
            else
            {
                ApiKeyPasswordBox.Password = ApiKeyPlainBox.Text;
                ApiKeyPlainBox.Visibility = Visibility.Collapsed;
                ApiKeyPasswordBox.Visibility = Visibility.Visible;
                if (btn != null) btn.Content = "Показать";
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            _settings.Save();
            ApplySettingsToUi(_settings);
            SetStatus("Настройки сохранены.", SuccessDot);
            AppendLog("Настройки сохранены.");
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            ApplySettingsToUi(new AppSettings());
            SetStatus("Настройки сброшены (нажмите «Сохранить» для записи).", NeutralDot);
        }

        private async void TestApi_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            if (string.IsNullOrEmpty(_settings.ApiKey) && _settings.Provider != "Ollama (локально)")
            {
                SetStatus("Введите API-ключ.", ErrorDot);
                return;
            }

            TestApiButton.IsEnabled = false;
            SetStatus("Проверяю API (" + _settings.Provider + ")...", LoadingDot);
            try
            {
                string reply = await Task.Run(() => NativeSolverBridge.SolveTask(
                    _settings.Provider,
                    _settings.BaseUrl,
                    _settings.ApiKey,
                    _settings.Model,
                    string.Empty,
                    "Ответь ровно одним JSON-объектом: {\"ok\":true}",
                    0.0));
                SetStatus("API работает: " + reply, SuccessDot);
                AppendLog("Тест API успешен: " + reply);
            }
            catch (Exception exc)
            {
                SetStatus("Ошибка API: " + exc.Message, ErrorDot);
                AppendLog("Тест API не прошёл: " + exc.Message);
            }
            finally
            {
                TestApiButton.IsEnabled = true;
            }
        }

        private void LaunchChrome_Click(object sender, RoutedEventArgs e)
        {
            LaunchBrowserWithCdp("Chrome", FindChromePath(), "ChromeProfile");
        }

        private void LaunchEdge_Click(object sender, RoutedEventArgs e)
        {
            LaunchBrowserWithCdp("Edge", FindEdgePath(), "EdgeProfile");
        }

        private void LaunchBrowserWithCdp(string name, string exePath, string profileFolder)
        {
            ReadSettingsFromUi();
            if (string.IsNullOrEmpty(exePath))
            {
                SetStatus(name + " не найден.", ErrorDot);
                return;
            }

            string profileDir = _settings.GetActiveProfileDir(profileFolder);
            Directory.CreateDirectory(profileDir);

            string args = string.Format(
                "--remote-debugging-port={0} --user-data-dir=\"{1}\" \"https://education.yandex.ru/kids/\"",
                _settings.CdpPort,
                profileDir);

            Process.Start(new ProcessStartInfo(exePath, args) { UseShellExecute = true });
            AppendLog("Запущен " + name + " на порту " + _settings.CdpPort + ".");
            SetStatus(name + " запущен. Войдите в аккаунт и нажмите «Подключить».", LoadingDot);
        }

        private static string FindChromePath()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };
            foreach (string path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return string.Empty;
        }

        private static string FindEdgePath()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (string path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return string.Empty;
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (_cdp.IsConnected)
            {
                return true;
            }
            ReadSettingsFromUi();
            try
            {
                string wsUrl = await Task.Run(() => CdpClient.FindTargetWebSocketUrl(_settings.CdpPort));
                await _cdp.ConnectAsync(wsUrl, CancellationToken.None);
                BrowserStatusLabel.Text = "Подключено к браузеру (порт " + _settings.CdpPort + ")";
                ConnectionDot.Fill = SuccessDot;
                ConnectionPillText.Text = "Браузер подключён · " + _settings.CdpPort;
                AppendLog("Подключено к CDP: " + wsUrl);
                return true;
            }
            catch (Exception exc)
            {
                ConnectionDot.Fill = ErrorDot;
                ConnectionPillText.Text = "Ошибка подключения";
                SetStatus("Ошибка подключения: " + exc.Message, ErrorDot);
                AppendLog("Ошибка CDP: " + exc.Message);
                return false;
            }
        }

        private async void ConnectCdp_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;
            SetStatus("Подключаюсь к CDP...", LoadingDot);
            try
            {
                if (await EnsureConnectedAsync())
                {
                    SetStatus("Подключено к браузеру.", SuccessDot);
                    await RefreshPreviewInternalAsync();
                }
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private async void RefreshTask_Click(object sender, RoutedEventArgs e)
        {
            if (!_cdp.IsConnected)
            {
                SetStatus("Сначала подключитесь к браузеру.", ErrorDot);
                return;
            }
            await RefreshPreviewInternalAsync();
        }

        private async Task RefreshPreviewInternalAsync()
        {
            try
            {
                var automation = new LessonAutomation(_cdp);
                PageTaskState state = await automation.ExtractStateAsync(CancellationToken.None);
                UpdateTaskPreview(state);
            }
            catch (Exception exc)
            {
                AppendLog("Не удалось прочитать задание: " + exc.Message);
            }
        }

        private void UpdateTaskPreview(PageTaskState state)
        {
            string preview = string.IsNullOrEmpty(state.QuestionText)
                ? "(текст задания не найден)"
                : state.QuestionText;
            TaskPreviewBox.Text = string.Format(
                "{4}\r\n\r\nЭлементов ввода: {1} · Ответить: {2} · Дальше: {3}\r\n{0}",
                state.Url,
                state.Controls.Count,
                state.HasSubmit ? "да" : "нет",
                state.HasNext ? "да" : "нет",
                preview);
        }

        private async void SolveStep_Click(object sender, RoutedEventArgs e)
        {
            await RunSolverAsync(false);
        }

        private async void AutoRun_Click(object sender, RoutedEventArgs e)
        {
            await RunSolverAsync(true);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_runCts != null)
            {
                _runCts.Cancel();
                AppendLog("Запрошена остановка.");
            }
        }

        private async Task RunSolverAsync(bool loopMode)
        {
            if (!await EnsureConnectedAsync())
            {
                return;
            }

            ReadSettingsFromUi();
            _settings.Save();

            _runCts = new CancellationTokenSource();
            SetRunningState(true);
            var automation = new LessonAutomation(_cdp);
            int solvedCount = 0;
            SolvedCounterText.Text = "0";

            try
            {
                for (int step = 1; step <= _settings.MaxSteps && !_runCts.IsCancellationRequested; step++)
                {
                    SetStatus("Читаю задание со страницы...", LoadingDot);
                    if (await automation.ClickButtonAsync(new[] { "продолжение", "ещё раз", "еще раз" }, _runCts.Token))
                    {
                        AppendLog("Нажата кнопка перехода/повтора.");
                        await Task.Delay(450, _runCts.Token);
                    }
                    PageTaskState state = await automation.ExtractStateAsync(_runCts.Token);
                    UpdateTaskPreview(state);
                    AppendLog(string.Format("[Шаг {0}] Элементов: {1} | Ответить: {2}", step, state.Controls.Count, state.HasSubmit));

                    if (state.IsCorrectBanner || (!state.HasSubmit && state.Controls.Count == 0 && state.DragItems.Count == 0))
                    {
                        if ((state.IsCorrectBanner || _settings.SkipTheory) && state.HasNext)
                        {
                            await automation.ClickButtonAsync(new[] { "дальше", "далее", "продолжить" }, _runCts.Token);
                            AppendLog("Нажато «Дальше».");
                            solvedCount++;
                            SolvedCounterText.Text = solvedCount.ToString(CultureInfo.InvariantCulture);
                            if (!loopMode) break;
                            await Task.Delay(_settings.StepDelayMs, _runCts.Token);
                            continue;
                        }
                        if (state.HasFinish)
                        {
                            await automation.ClickButtonAsync(new[] { "завершить" }, _runCts.Token);
                            AppendLog("Урок завершён.");
                            break;
                        }
                        AppendLog("Нет активных полей ввода или кнопки «Ответить».");
                        break;
                    }

                    string taskHash = AnswerCache.ComputeTaskHash(state);
                    string cachedAnswer;
                    string applied;
                    if (AnswerCache.TryGet(taskHash, _settings, out cachedAnswer))
                    {
                        SetStatus("Применяю ответ из кэша...", LoadingDot);
                        applied = await automation.ApplyAnswerJsonAsync(state, cachedAnswer, _runCts.Token);
                        AppendLog("ИИ (кэш): " + cachedAnswer);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(_settings.ApiKey) && _settings.Provider != "Ollama (локально)")
                        {
                            throw new InvalidOperationException("Укажите API-ключ во вкладке «Настройки».");
                        }

                        SetStatus("Решаю через " + _settings.Model + "...", LoadingDot);
                        if (!string.IsNullOrEmpty(state.ScreenshotBase64))
                        {
                            AppendLog("Прикреплён скриншот задания (Vision).");
                        }
                        applied = await automation.SolveAndApplyAsync(state, _settings, string.Empty, _runCts.Token);
                        AppendLog("ИИ: " + automation.LastLlmResponse);
                    }
                    AppendLog("Применено: " + applied);

                    if (state.HasSubmit)
                    {
                        if (_settings.SubmitDelayMs > 0)
                        {
                            await Task.Delay(_settings.SubmitDelayMs, _runCts.Token);
                        }
                        await automation.ClickButtonAsync(new[] { "ответить", "проверить" }, _runCts.Token);
                        AppendLog("Нажато «Ответить».");
                    }

                    PageTaskState after = await WaitForVerdictAsync(automation, _runCts.Token);
                    UpdateTaskPreview(after);

                    if (after.IsErrorBanner && _settings.RetryOnWrong && after.HasSubmit)
                    {
                        AppendLog("Ответ не принят, пробую ещё раз...");
                        string retryApplied = await automation.SolveAndApplyAsync(
                            after,
                            _settings,
                            "Предыдущий ответ был отмечен как неверный, перепроверь расчёт.",
                            _runCts.Token);
                        AppendLog("ИИ (повтор): " + automation.LastLlmResponse);
                        AppendLog("Повторный ответ: " + retryApplied);
                        if (_settings.SubmitDelayMs > 0)
                        {
                            await Task.Delay(_settings.SubmitDelayMs, _runCts.Token);
                        }
                        await automation.ClickButtonAsync(new[] { "ответить", "проверить" }, _runCts.Token);
                        await Task.Delay(1600, _runCts.Token);
                        after = await automation.ExtractStateAsync(_runCts.Token);
                    }

                    if (after.IsErrorBanner || after.HasRetry)
                    {
                        AppendLog("Ответ неверный (кнопка «Ещё раз»). Остановлено.");
                        break;
                    }

                    if (after.IsCorrectBanner || after.HasNext || after.HasFinish)
                    {
                        AnswerCache.Save(taskHash, automation.LastLlmResponse, _settings);
                    }

                    solvedCount++;
                    SolvedCounterText.Text = solvedCount.ToString(CultureInfo.InvariantCulture);

                    if (!loopMode && !_settings.AutoAdvance)
                    {
                        break;
                    }

                    if (after.HasNext && (_settings.AutoAdvance || loopMode))
                    {
                        await automation.ClickButtonAsync(new[] { "дальше", "далее", "продолжить" }, _runCts.Token);
                        AppendLog("Переход дальше.");
                        if (!loopMode) break;
                        await Task.Delay(_settings.StepDelayMs, _runCts.Token);
                    }
                    else if (after.HasFinish && loopMode)
                    {
                        await automation.ClickButtonAsync(new[] { "завершить" }, _runCts.Token);
                        AppendLog("Урок завершён.");
                        break;
                    }
                    else
                    {
                        if (loopMode)
                        {
                            AppendLog("Остановлено: нет кнопки «Дальше».");
                        }
                        break;
                    }
                }

                SetStatus("Готово.", SuccessDot);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Остановлено пользователем.", NeutralDot);
            }
            catch (Exception exc)
            {
                SetStatus("Ошибка: " + exc.Message, ErrorDot);
                AppendLog("Ошибка: " + exc.Message);
            }
            finally
            {
                SetRunningState(false);
                if (_runCts != null)
                {
                    _runCts.Dispose();
                    _runCts = null;
                }
            }
        }

        private async Task<PageTaskState> WaitForVerdictAsync(LessonAutomation automation, CancellationToken ct)
        {
            PageTaskState latest = null;
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(450, ct);
                latest = await automation.ExtractStateAsync(ct);
                if (latest.HasNext || latest.HasFinish || latest.IsErrorBanner || latest.IsCorrectBanner)
                {
                    break;
                }
            }
            return latest ?? await automation.ExtractStateAsync(ct);
        }

        private void SetRunningState(bool running)
        {
            SolveStepButton.IsEnabled = !running;
            AutoRunButton.IsEnabled = !running;
            StopButton.IsEnabled = running;
        }

        private void SetStatus(string text, Brush dot)
        {
            StatusText.Text = text;
            StatusDot.Fill = dot ?? NeutralDot;
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, line, Environment.NewLine));
            LogBox.ScrollToEnd();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }

        private async void CopyBugReport_Click(object sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== ЗАДАНИЕ ===");
            sb.AppendLine(TaskPreviewBox.Text);
            sb.AppendLine();
            sb.AppendLine("=== ЖУРНАЛ ===");
            sb.AppendLine(LogBox.Text);
            if (_cdp != null && _cdp.IsConnected)
            {
                try
                {
                    var automation = new LessonAutomation(_cdp);
                    string domJson = await automation.ExtractDebugDomAsync(CancellationToken.None);
                    sb.AppendLine();
                    sb.AppendLine("=== DOM ===");
                    sb.AppendLine(domJson);
                }
                catch (Exception ex)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== DOM ERROR ===");
                    sb.AppendLine(ex.Message);
                }
            }
            Clipboard.SetText(sb.ToString());
            AppendLog("DOM и лог скопированы в буфер обмена.");
        }

        private void DlUrlBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DlResolve_Click(sender, e);
            }
        }

        private void DlPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    DlUrlBox.Text = Clipboard.GetText().Trim();
                    DlUrlBox.CaretIndex = DlUrlBox.Text.Length;
                }
            }
            catch (Exception)
            {
            }
        }

        private async void DlResolve_Click(object sender, RoutedEventArgs e)
        {
            LessonTarget target;
            try
            {
                target = CertificateResolver.ParseLessonUrl(DlUrlBox.Text, DlSlugBox.Text);
            }
            catch (ArgumentException exc)
            {
                DlStatusText.Text = exc.Message;
                SetStatus(exc.Message, ErrorDot);
                return;
            }

            DlSlugBox.Text = target.Slug;
            DlResultBox.Text = string.Empty;
            DlResolveButton.IsEnabled = false;
            DlResolveButton.Content = "Проверяю...";
            DlCopyButton.IsEnabled = false;
            DlOpenButton.IsEnabled = false;
            DlStatusText.Text = "Проверяю страницу сертификата на сервере...";
            SetStatus("Проверяю сертификат Урок Цифры...", LoadingDot);

            try
            {
                CertificateResult result = await CertificateResolver.VerifyCertificateUrlAsync(target);
                DlResultBox.Text = result.Url;
                DlCopyButton.IsEnabled = true;
                DlOpenButton.IsEnabled = true;
                string okMsg = string.Format("Готово. Урок: {0}. Можно открыть страницу и скачать сертификат.", result.Slug);
                DlStatusText.Text = okMsg;
                SetStatus(okMsg, SuccessDot);
            }
            catch (InvalidOperationException exc)
            {
                DlStatusText.Text = exc.Message;
                SetStatus(exc.Message, ErrorDot);
            }
            finally
            {
                DlResolveButton.IsEnabled = true;
                DlResolveButton.Content = "Получить сертификат";
            }
        }

        private void DlCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(DlResultBox.Text))
            {
                return;
            }
            Clipboard.SetText(DlResultBox.Text);
            DlStatusText.Text = "Ссылка скопирована в буфер обмена.";
            SetStatus("Ссылка скопирована в буфер обмена.", SuccessDot);
        }

        private void DlOpen_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(DlResultBox.Text))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(DlResultBox.Text) { UseShellExecute = true });
        }

        private async void AutoLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!_cdp.IsConnected)
            {
                SetStatus("Сначала подключитесь к Chrome/Edge.", ErrorDot);
                return;
            }
            ReadSettingsFromUi();
            if (string.IsNullOrEmpty(_settings.SchoolCode) && string.IsNullOrEmpty(_settings.StudentLogin))
            {
                SetStatus("Укажите код школы и логин во вкладке «Настройки».", ErrorDot);
                return;
            }
            try
            {
                var automation = new LessonAutomation(_cdp);
                bool ok = await automation.FillYandexKidsLoginAsync(_settings.SchoolCode, _settings.StudentLogin, CancellationToken.None);
                if (ok)
                {
                    await Task.Delay(350);
                    await automation.ClickButtonAsync(new[] { "войти" }, CancellationToken.None);
                    SetStatus("Данные подставлены, нажато «Войти».", SuccessDot);
                    AppendLog("Автовход: поля заполнены и нажато «Войти».");
                }
                else
                {
                    SetStatus("Поля входа на текущей странице не найдены.", ErrorDot);
                }
            }
            catch (Exception exc)
            {
                SetStatus("Ошибка автовхода: " + exc.Message, ErrorDot);
            }
        }

        private void ConvertBase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string selected = GetSelectedComboText(BaseFromCombo, "10 (DEC)");
                int fromBase = 10;
                if (selected.StartsWith("2 ")) fromBase = 2;
                else if (selected.StartsWith("8 ")) fromBase = 8;
                else if (selected.StartsWith("16 ")) fromBase = 16;

                BaseOutputBox.Text = InformaticsTools.ConvertNumberBases(BaseInputBox.Text, fromBase);
            }
            catch (Exception exc)
            {
                BaseOutputBox.Text = "Ошибка: " + exc.Message;
            }
        }

        private void CalcInfoSize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double v1 = double.Parse(InfoArg1Box.Text.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
                double v2 = double.Parse(InfoArg2Box.Text.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
                double v3 = double.Parse(InfoArg3Box.Text.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
                double bits = v3 > 0 ? v1 * v2 * v3 : v1 * v2;
                InfoOutputBox.Text = InformaticsTools.FormatBitsSummary(bits);
            }
            catch (Exception exc)
            {
                InfoOutputBox.Text = "Ошибка: " + exc.Message;
            }
        }

        private void BuildTruthTable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TruthOutputBox.Text = InformaticsTools.BuildTruthTable(TruthExprBox.Text);
            }
            catch (Exception exc)
            {
                TruthOutputBox.Text = "Ошибка: " + exc.Message;
            }
        }

        private void CleanTraces_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YandexUchebnikSolver");
                if (Directory.Exists(appDataDir))
                {
                    Directory.Delete(appDataDir, true);
                }
                string portableProfiles = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");
                if (Directory.Exists(portableProfiles))
                {
                    Directory.Delete(portableProfiles, true);
                }
                string portableIni = AppSettings.GetPortableSettingsPath();
                if (File.Exists(portableIni))
                {
                    File.Delete(portableIni);
                }
                SetStatus("Следы и профили очищены.", SuccessDot);
                AppendLog("Локальные настройки и профили браузера удалены.");
            }
            catch (Exception exc)
            {
                SetStatus("Не всё удалось удалить (закройте браузер): " + exc.Message, ErrorDot);
            }
        }

        private void SelfDestruct_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirm = MessageBox.Show(
                "Полностью удалить все данные приложения, кэш ответов, профили браузера и саму программу через 5 секунд?",
                "Удаление без следа",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexUchebnikSolver");
            string nativeDir = Path.Combine(Path.GetTempPath(), "EduToolsNative");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            try { if (Directory.Exists(appDataDir)) Directory.Delete(appDataDir, true); } catch { }
            try { if (Directory.Exists(nativeDir)) Directory.Delete(nativeDir, true); } catch { }
            try
            {
                string portableProfiles = Path.Combine(baseDir, "Profiles");
                if (Directory.Exists(portableProfiles)) Directory.Delete(portableProfiles, true);
                string portableIni = AppSettings.GetPortableSettingsPath();
                if (File.Exists(portableIni)) File.Delete(portableIni);
                string portableCache = Path.Combine(baseDir, "answers_cache.json");
                if (File.Exists(portableCache)) File.Delete(portableCache);
            }
            catch { }

            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string cmdArgs = string.Format(
                "/c ping 127.0.0.1 -n 6 > nul & del /f /q \"{0}\" > nul 2>&1 & rmdir /s /q \"{1}\" > nul 2>&1 & rmdir /s /q \"{2}\" > nul 2>&1",
                exePath,
                appDataDir,
                nativeDir);

            Process.Start(new ProcessStartInfo("cmd.exe", cmdArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Current.Shutdown();
        }
    }
}
