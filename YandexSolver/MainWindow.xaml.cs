using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YandexSolver
{
    public partial class MainWindow : Window
    {
        private static readonly Brush NeutralDot = CreateBrush("#71717A");
        private static readonly Brush LoadingDot = CreateBrush("#F59E0B");
        private static readonly Brush SuccessDot = CreateBrush("#22C55E");
        private static readonly Brush ErrorDot = CreateBrush("#EF4444");

        private readonly CdpClient _cdp = new CdpClient();
        private AppSettings _settings;
        private CancellationTokenSource _runCts;
        private bool _isKeyVisible;
        private bool _suppressProviderChanged;

        public MainWindow()
        {
            InitializeComponent();
            ApplySettingsToUi(AppSettings.Load());
        }

        private static Brush CreateBrush(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        private void ApplySettingsToUi(AppSettings loaded)
        {
            _settings = loaded;
            _suppressProviderChanged = true;
            foreach (ComboBoxItem item in ProviderCombo.Items)
            {
                if (string.Equals(Convert.ToString(item.Content), _settings.Provider, StringComparison.OrdinalIgnoreCase))
                {
                    ProviderCombo.SelectedItem = item;
                    break;
                }
            }
            if (ProviderCombo.SelectedItem == null)
            {
                ProviderCombo.SelectedIndex = 0;
            }
            _suppressProviderChanged = false;

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
        }

        private void ReadSettingsFromUi()
        {
            var selected = ProviderCombo.SelectedItem as ComboBoxItem;
            _settings.Provider = selected != null ? Convert.ToString(selected.Content) : "OpenRouter";
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_runCts != null)
            {
                _runCts.Cancel();
            }
            _cdp.Dispose();
            Close();
        }

        private void NavTab_Changed(object sender, RoutedEventArgs e)
        {
            if (WorkPanel == null || SettingsPanel == null)
            {
                return;
            }
            bool showWork = WorkTabRadio.IsChecked == true;
            WorkPanel.Visibility = showWork ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = showWork ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProviderChanged || ProviderCombo.SelectedItem == null)
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

        private void LaunchEdge_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            string edgePath = FindEdgePath();
            if (string.IsNullOrEmpty(edgePath))
            {
                SetStatus("Microsoft Edge не найден.", ErrorDot);
                return;
            }

            string profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexUchebnikSolver",
                "EdgeProfile");
            Directory.CreateDirectory(profileDir);

            string args = string.Format(
                "--remote-debugging-port={0} --user-data-dir=\"{1}\" \"https://education.yandex.ru/kids/\"",
                _settings.CdpPort,
                profileDir);

            Process.Start(new ProcessStartInfo(edgePath, args) { UseShellExecute = true });
            AppendLog("Запущен Edge на порту " + _settings.CdpPort + ".");
            SetStatus("Edge запущен. Войдите в аккаунт и нажмите «Подключить».", LoadingDot);
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

        private async void ConnectCdp_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            ConnectButton.IsEnabled = false;
            SetStatus("Подключаюсь к Edge CDP...", LoadingDot);
            try
            {
                string wsUrl = await Task.Run(() => CdpClient.FindTargetWebSocketUrl(_settings.CdpPort));
                await _cdp.ConnectAsync(wsUrl, CancellationToken.None);
                BrowserStatusLabel.Text = "Подключено к вкладке Edge (порт " + _settings.CdpPort + ")";
                ConnectionDot.Fill = SuccessDot;
                ConnectionPillText.Text = "Edge: " + _settings.CdpPort;
                SetStatus("Подключено к браузеру.", SuccessDot);
                AppendLog("Подключено к CDP: " + wsUrl);
                await RefreshPreviewInternalAsync();
            }
            catch (Exception exc)
            {
                ConnectionDot.Fill = ErrorDot;
                ConnectionPillText.Text = "Edge: ошибка";
                SetStatus("Ошибка подключения: " + exc.Message, ErrorDot);
                AppendLog("Ошибка CDP: " + exc.Message);
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
                SetStatus("Сначала подключитесь к Edge.", ErrorDot);
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
                "URL: {0}\r\nЭлементов ввода: {1} | Кнопка «Ответить»: {2} | «Дальше»: {3}\r\n\r\n{4}",
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
            if (!_cdp.IsConnected)
            {
                SetStatus("Сначала подключитесь к Edge CDP.", ErrorDot);
                return;
            }

            ReadSettingsFromUi();
            _settings.Save();

            _runCts = new CancellationTokenSource();
            SetRunningState(true);
            var automation = new LessonAutomation(_cdp);
            int solvedCount = 0;
            SolvedCounterText.Text = "Шагов: 0";

            try
            {
                for (int step = 1; step <= _settings.MaxSteps && !_runCts.IsCancellationRequested; step++)
                {
                    SetStatus("Читаю задание со страницы...", LoadingDot);
                    PageTaskState state = await automation.ExtractStateAsync(_runCts.Token);
                    UpdateTaskPreview(state);
                    AppendLog(string.Format("[Шаг {0}] Элементов: {1} | Ответить: {2}", step, state.Controls.Count, state.HasSubmit));

                    if (state.IsCorrectBanner || (!state.HasSubmit && state.Controls.Count == 0))
                    {
                        if ((state.IsCorrectBanner || _settings.SkipTheory) && state.HasNext)
                        {
                            await automation.ClickButtonAsync(new[] { "дальше", "далее", "продолжить" }, _runCts.Token);
                            AppendLog("Нажато «Дальше».");
                            solvedCount++;
                            SolvedCounterText.Text = "Шагов: " + solvedCount;
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

                    if (string.IsNullOrEmpty(_settings.ApiKey) && _settings.Provider != "Ollama (локально)")
                    {
                        throw new InvalidOperationException("Укажите API-ключ во вкладке «Настройки».");
                    }

                    SetStatus("Решаю через " + _settings.Model + "...", LoadingDot);
                    string applied = await automation.SolveAndApplyAsync(state, _settings, string.Empty, _runCts.Token);
                    AppendLog("Ответ: " + applied);

                    if (state.HasSubmit)
                    {
                        if (_settings.SubmitDelayMs > 0)
                        {
                            await Task.Delay(_settings.SubmitDelayMs, _runCts.Token);
                        }
                        await automation.ClickButtonAsync(new[] { "ответить", "проверить" }, _runCts.Token);
                        AppendLog("Нажато «Ответить».");
                        await Task.Delay(1600, _runCts.Token);
                    }

                    PageTaskState after = await automation.ExtractStateAsync(_runCts.Token);
                    UpdateTaskPreview(after);

                    if (after.IsErrorBanner && _settings.RetryOnWrong && after.HasSubmit)
                    {
                        AppendLog("Ответ не принят, пробую ещё раз...");
                        string retryApplied = await automation.SolveAndApplyAsync(
                            after,
                            _settings,
                            "Предыдущий ответ был отмечен как неверный, перепроверь расчёт.",
                            _runCts.Token);
                        AppendLog("Повторный ответ: " + retryApplied);
                        if (_settings.SubmitDelayMs > 0)
                        {
                            await Task.Delay(_settings.SubmitDelayMs, _runCts.Token);
                        }
                        await automation.ClickButtonAsync(new[] { "ответить", "проверить" }, _runCts.Token);
                        await Task.Delay(1600, _runCts.Token);
                        after = await automation.ExtractStateAsync(_runCts.Token);
                    }

                    solvedCount++;
                    SolvedCounterText.Text = "Шагов: " + solvedCount;

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
    }
}
