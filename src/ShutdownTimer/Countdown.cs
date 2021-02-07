﻿using ShutdownTimer.Helpers;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ShutdownTimer
{
    public partial class Countdown : Form
    {
        //properties
        public TimeSpan CountdownTimeSpan { get; set; } // timespan after which the power action gets executed
        public string Action { get; set; } // defines what power action to execute (fallback to shutdown if not changed)
        public bool Graceful { get; set; } // uses a graceful shutdown which allows apps to save their work or interrupt the shutdown
        public bool PreventSystemSleep { get; set; } // tells Windows that the system should stay awake during countdown
        public bool UI { get; set; } // disables UI updates when set to false (used for running in background)
        public bool Forced { get; set; } // disables all UI controls and exit dialogs

        //private
        private FormWindowState lastStateUIFormWindowState; // used to update UI immediately after WindowState change
        private TimeSpan lastStateUITimeSpan; // used to limit UI events that should only be executed once per second instead of once per update
        private Stopwatch stopwatch; // measures exact timespan
        private bool ignoreClose; // true: cancel close events without asking | false: default behaviour (if ignoreClose == true, allowClose will be ignored)
        private bool allowClose; // true: accept close without asking | false: Ask user to confirm closing
        private bool animationSwitch = true; // used to switch warning animation colors

        public Countdown()
        {
            InitializeComponent();
        }

        #region "form events"

        // entrypoint
        private void Countdown_Load(object sender, EventArgs e)
        {
            ExceptionHandler.LogEvent("[Countdown] Start stopwatch");

            // Setup clock
            stopwatch = new Stopwatch();
            stopwatch.Start();

            ExceptionHandler.LogEvent("[Countdown] Prepare UI");

            // Set trayIcon icon to the opposite of the selected theme
            bool lighttheme;
            if (SettingsProvider.SettingsLoaded)
            {
                if (SettingsProvider.Settings.TrayIconTheme == "Light") { lighttheme = true; }
                else if (SettingsProvider.Settings.TrayIconTheme == "Dark") { lighttheme = false; }
                else { lighttheme = WindowsAPIs.GetWindowsLightTheme(); }
            }
            else
            {
                lighttheme = WindowsAPIs.GetWindowsLightTheme();
            }

            // When the dark theme is selected we are using the light icon to generate contrast (and vise versa), you wouldn't want a white icon on a white background.
            notifyIcon.Icon = lighttheme ? Properties.Resources.icon_dark : Properties.Resources.icon_light;

            // Setup UI
            titleLabel.Text = Action + " Timer";

            TopMost = !SettingsProvider.Settings.DisableAlwaysOnTop;

            if (!UI)
            {
                ignoreClose = true; // Disable close dialogs and ignore closing from form
                TopMost = false;
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                timerUIHideMenuItem.Enabled = false;
                timerUIShowMenuItem.Enabled = true;
                notifyIcon.BalloonTipText = "Timer started. The power action will be executed in " + CountdownTimeSpan.Hours + " hours, " + CountdownTimeSpan.Minutes + " minutes and " + CountdownTimeSpan.Seconds + " seconds.";
                notifyIcon.ShowBalloonTip(10000);
                Hide();
            }

            if (Forced)
            {
                ignoreClose = true;
                contextMenuStrip.Enabled = false;
            }

            ExceptionHandler.LogEvent("[Countdown] Set UI");

            UpdateUI(CountdownTimeSpan);

            if (PreventSystemSleep)
            {
                ExceptionHandler.LogEvent("[Countdown] Preventing sleep");
                ExecutionState.SetThreadExecutionState(ExecutionState.EXECUTION_STATE.ES_CONTINUOUS | ExecutionState.EXECUTION_STATE.ES_SYSTEM_REQUIRED);
            } // give the system some coffee so it stays awake when tired using some fancy EXECUTION_STATE flags
        }

        /// <summary>
        /// Checks the stopwatch and updates UI every 100ms
        /// </summary>
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan interval = CountdownTimeSpan - stopwatch.Elapsed + new TimeSpan(0, 0, 1); // Calculate countdown (add 1 second for smooth start)
            UpdateUI(interval);

            if (interval.TotalSeconds <= 0) //check if interval is negative
            {
                stopwatch.Stop();
                refreshTimer.Stop();
                ExecutePowerAction(Action);
                Application.DoEvents();
                Application.Exit();
            }
        }

        /// <summary>
        /// Logic to prevent or ignore unwanted close events and notify the user.
        /// </summary>
        private void Countdown_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ignoreClose)
            {
                e.Cancel = true; // Ignore closing event one time
            }
            else if (!allowClose)
            {
                e.Cancel = true;
                string caption = "Are you sure?";
                string message = "Do you really want to cancel the timer?";
                DialogResult question = MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (question == DialogResult.Yes) { ExitApplication(); }
            }
            // allowClose == true is not handled and if ignoreClose == false, the application will exit
        }

        #endregion

        #region "form actions / state changes"

        /// <summary>
        /// Stops the countdown, displays an exit message and closes exits the application.
        /// </summary>
        private void ExitApplication()
        {
            ExceptionHandler.LogEvent("[Countdown] Exit application");

            ignoreClose = false;
            allowClose = true;
            stopwatch.Stop();
            refreshTimer.Stop();
            ExecutionState.SetThreadExecutionState(ExecutionState.EXECUTION_STATE.ES_CONTINUOUS); // Clear EXECUTION_STATE flags to allow the system to go to sleep if it's tired
            string caption1 = "Timer canceled";
            string message1 = "Your timer was canceled successfully!\nThe application will now close.";
            MessageBox.Show(message1, caption1, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
        }

        /// <summary>
        /// Restarts the application.
        /// </summary>
        private void RestartApplication()
        {
            ExceptionHandler.LogEvent("[Countdown] Restart application");

            ignoreClose = false;
            allowClose = true;
            stopwatch.Stop();
            refreshTimer.Stop();
            ExecutionState.SetThreadExecutionState(ExecutionState.EXECUTION_STATE.ES_CONTINUOUS); // Clear EXECUTION_STATE flags to allow the system to go to sleep if it's tired
            Application.DoEvents();
            Application.Restart();
        }

        /// <summary>
        /// Restarts the countdown with initial values.
        /// </summary>
        private void RestartTimer()
        {
            ExceptionHandler.LogEvent("[Countdown] Restart timer");

            stopwatch.Stop();
            stopwatch = new Stopwatch();
            stopwatch.Start();
            UpdateUI(CountdownTimeSpan);
            if (this.WindowState == FormWindowState.Minimized) { notifyIcon.BalloonTipText = "Timer restarted. The power action will be executed in " + CountdownTimeSpan.Hours + " hours, " + CountdownTimeSpan.Minutes + " minutes and " + CountdownTimeSpan.Seconds + " seconds."; notifyIcon.ShowBalloonTip(10000); }
        }

        /// <summary>
        /// Hides countdown window
        /// </summary>
        private void HideUI()
        {
            ExceptionHandler.LogEvent("[Countdown] Hide UI");

            timerUIHideMenuItem.Enabled = false;
            timerUIShowMenuItem.Enabled = true;
            TopMost = false;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            UI = false;
            ignoreClose = true; // Prevent closing (and closing dialog) after ShowInTaskbar changed
            TimeSpan currentTimeSpan = CountdownTimeSpan - stopwatch.Elapsed + new TimeSpan(0, 0, 1); // Calculate countdown (add 1 second for smooth start)
            UpdateUI(currentTimeSpan);

            notifyIcon.BalloonTipText = "Timer has been moved to the background. Right-click the tray icon for more info.";
            notifyIcon.ShowBalloonTip(10000);
        }

        /// <summary>
        /// Shows countdown window
        /// </summary>
        private void ShowUI()
        {
            ExceptionHandler.LogEvent("[Countdown] Show UI");

            timerUIHideMenuItem.Enabled = true;
            timerUIShowMenuItem.Enabled = false;
            if (!SettingsProvider.Settings.DisableAlwaysOnTop) { TopMost = true; }
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            UI = true;
            ignoreClose = true; // Prevent closing (and closing dialog) after ShowInTaskbar changed

            // Re-Enable close question after the main thread has moved on and the close event raised from the this. ShowInTaskbar has been ignored
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                Thread.Sleep(100); // Waiting to make sure the main thread has moved on and successfully
                ignoreClose = false; // Re-Enable close question
            }).Start();

            TimeSpan currentTimeSpan = CountdownTimeSpan - stopwatch.Elapsed + new TimeSpan(0, 0, 1); // Calculate countdown (add 1 second for smooth start)
            UpdateUI(currentTimeSpan);
        }

        #endregion

        #region "tray menu events"

        /// <summary>
        /// Stop timer option in the tray menu
        /// </summary>
        private void TimerStopMenuItem_Click(object sender, EventArgs e)
        {
            ExitApplication();
        }

        /// <summary>
        /// Restart timer option in the tray menu
        /// </summary>
        private void TimerRestartMenuItem_Click(object sender, EventArgs e)
        {
            RestartTimer();
        }

        /// <summary>
        /// Restart application option in the tray menu
        /// </summary>
        private void AppRestartMenuItem_Click(object sender, EventArgs e)
        {
            RestartApplication();
        }

        /// <summary>
        /// Hide countdown window option in the tray menu
        /// </summary>
        private void TimerUIHideMenuItem_Click(object sender, EventArgs e)
        {
            HideUI();
        }

        /// <summary>
        /// Show countdown window option in the tray menu
        /// </summary>
        private void TimerUIShowMenuItem_Click(object sender, EventArgs e)
        {
            ShowUI();
        }

        #endregion

        #region "ui updates"

        /// <summary>
        /// Updates the time label with current time left and applies the corresponding background color.
        /// </summary>
        private void UpdateUI(TimeSpan ts)
        {
            if (Math.Round(lastStateUITimeSpan.TotalSeconds) != Math.Round(ts.TotalSeconds) || lastStateUITimeSpan.TotalSeconds <= 0 || lastStateUIFormWindowState != WindowState) // Only update if the seconds from the TimeSpan actually changed and when it first started
            {
                // Save current data to last state memory
                lastStateUITimeSpan = ts;
                lastStateUIFormWindowState = WindowState;

                // Update time labels
                string elapsedTime = Numerics.ConvertTimeSpanToString(ts);
                timeLabel.Text = elapsedTime;
                timeMenuItem.Text = elapsedTime;

                if (UI) // UI for countdown window
                {
                    this.Text = "Countdown";

                    // Decide which color/animation to use
                    if (!SettingsProvider.Settings.DisableAnimations)
                    {
                        if (ts.Days > 0 || ts.Hours > 0 || ts.Minutes >= 30) { BackColor = Color.ForestGreen; }
                        else if (ts.Minutes >= 10) { BackColor = Color.DarkOrange; }
                        else if (ts.Minutes >= 1) { BackColor = Color.OrangeRed; }
                        else { WarningAnimation(); }
                    }
                }
                else // UI for tray menu
                {
                    this.Text = "Countdown - " + elapsedTime;

                    // Decide which tray message to show
                    if (!SettingsProvider.Settings.DisableNotifications)
                    {
                        if (ts.Days == 0 && ts.Hours == 2 && ts.Minutes == 0 && ts.Seconds == 00) { notifyIcon.BalloonTipText = "2 hours remaining until the power action will be executed."; notifyIcon.ShowBalloonTip(5000); }
                        else if (ts.Days == 0 && ts.Hours == 1 && ts.Minutes == 0 && ts.Seconds == 00) { notifyIcon.BalloonTipText = "1 hour remaining until the power action will be executed."; notifyIcon.ShowBalloonTip(5000); }
                        else if (ts.Days == 0 && ts.Hours == 0 && ts.Minutes == 30 && ts.Seconds == 00) { notifyIcon.BalloonTipText = "30 minutes remaining until the power action will be executed."; notifyIcon.ShowBalloonTip(5000); }
                        else if (ts.Days == 0 && ts.Hours == 0 && ts.Minutes == 5 && ts.Seconds == 00) { notifyIcon.BalloonTipText = "5 minutes remaining until the power action will be executed."; notifyIcon.ShowBalloonTip(5000); }
                        else if (ts.Days == 0 && ts.Hours == 0 && ts.Minutes == 0 && ts.Seconds == 30) { notifyIcon.BalloonTipText = "30 seconds remaining until the power action will be executed."; notifyIcon.ShowBalloonTip(5000); }
                    }
                }
            }

            // Correct window states if unexpected changes occur
            if (!UI && WindowState != FormWindowState.Minimized)
            {
                // Window is visible when UI is set to background operation
                ShowUI();
            }
            else if (UI && WindowState == FormWindowState.Minimized)
            {
                // Window is hidden when UI is set to foreground operation
                HideUI();
            }

            // Update UI
            Application.DoEvents();
        }

        /// <summary>
        /// Switches from background color from red to black (and vice versa) when called.
        /// </summary>
        private void WarningAnimation()
        {
            animationSwitch = !animationSwitch; // Switch animation color

            if (animationSwitch) { BackColor = Color.Red; }
            else { BackColor = Color.Black; }
        }

        #endregion

        private void ExecutePowerAction(string choosenAction)
        {
            ExceptionHandler.LogEvent("[Countdown] Execute power action");

            ignoreClose = false; // do not ignore close event
            allowClose = true; // disable close question

            switch (choosenAction)
            {
                case "Shutdown":
                    ExitWindows.Shutdown(!Graceful);
                    break;

                case "Restart":
                    ExitWindows.Reboot(!Graceful);
                    break;

                case "Hibernate":
                    Application.SetSuspendState(PowerState.Hibernate, false, false);
                    break;

                case "Sleep":
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                    break;

                case "Logout":
                    ExitWindows.LogOff(!Graceful);
                    break;

                case "Lock":
                    ExitWindows.Lock();
                    break;
            }

            ExecutionState.SetThreadExecutionState(ExecutionState.EXECUTION_STATE.ES_CONTINUOUS); // Clear EXECUTION_STATE flags to allow the system to go to sleep if it's tired.
        }
    }
}