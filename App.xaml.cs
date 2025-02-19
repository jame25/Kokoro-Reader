using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NLog;
using KokoroReader.Models;

namespace KokoroReader
{
    public partial class App : Application
    {
        private static readonly ILogger Logger;
        private static Settings? settings;

        static App()
        {
            try
            {
                // First create a null logger configuration
                LogManager.Configuration = new NLog.Config.LoggingConfiguration();
                Logger = LogManager.GetCurrentClassLogger();

                // Load settings without logging
                settings = Settings.Load();

                if (settings.LoggingEnabled)
                {
                    // Ensure the log directory exists
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logPath);

                    // Initialize logging
                    var config = new NLog.Config.LoggingConfiguration();
                    var logfile = new NLog.Targets.FileTarget("logfile")
                    {
                        FileName = Path.Combine(logPath, "kokororeader.log"),
                        Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${when:when=length('${exception}')>0:Inner=${exception:format=tostring}}"
                    };
                    var errorfile = new NLog.Targets.FileTarget("errorfile")
                    {
                        FileName = Path.Combine(logPath, "error.log"),
                        Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${when:when=length('${exception}')>0:Inner=${exception:format=tostring:maxInnerExceptionLevel=10}}"
                    };

                    config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);
                    config.AddRule(LogLevel.Error, LogLevel.Fatal, errorfile);
                    LogManager.Configuration = config;

                    Logger.Info("Application logging initialized successfully");
                }

                // Now that logging is properly configured, set the logger and load additional data
                settings.SetLogger(Logger);
                settings.LoadBookmarks();
                settings.LoadPronunciations();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical error initializing application: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Logger.Fatal(e.Exception, "Unhandled exception in UI thread");
                MessageBox.Show($"A critical error occurred:\n\n{e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}",
                    "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
                Shutdown(1);
            }
            catch
            {
                MessageBox.Show("A fatal error occurred and could not be logged.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                Logger.Fatal(exception, "Unhandled exception in AppDomain");
                MessageBox.Show($"A fatal error occurred:\n\n{exception?.Message}\n\nStack trace:\n{exception?.StackTrace}",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                MessageBox.Show("A fatal error occurred and could not be logged.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Shutdown(1);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Logger.Info("Application starting up");
                base.OnStartup(e);
                Logger.Info("Application startup completed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during application startup");
                MessageBox.Show($"Error during application startup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Logger.Info("Application shutting down");
                base.OnExit(e);
                LogManager.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during application shutdown: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 